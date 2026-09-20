#!/usr/bin/env python3
"""
Tag Gremlin Desktop.

Stdlib-only, resumable harvester for a pageable tags.php index. It reads the
active Firefox profile's cookies.sqlite directly, keeps cookie values only in
memory, stores parsed tag/synonym data in SQLite, and verifies completeness
before export.
"""

from __future__ import annotations

import argparse
import configparser
import csv
from collections import Counter
import hashlib
import http.cookiejar
import json
import math
import os
import random
import re
import shutil
import signal
import sqlite3
import subprocess
import statistics
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass, field
from datetime import datetime, timezone
from html.parser import HTMLParser
from pathlib import Path
from typing import Iterable

SCHEMA_VERSION = "1"
DEFAULT_DB = "tag-gremlin.sqlite3"
DEFAULT_INITIAL_WORKERS = 2
DEFAULT_MAX_WORKERS = 4
MAX_RESPONSE_BYTES = 8 * 1024 * 1024
USER_AGENT = "Mozilla/5.0 (X11; Linux x86_64; rv:140.0) Gecko/20100101 Firefox/140.0"

_INT_RE = re.compile(r"[-+]?\d[\d,]*")
_TOTAL_RE = re.compile(r"\b(\d[\d,]{3,})\s+tags?\b", re.IGNORECASE)


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def clean_space(value: str) -> str:
    return re.sub(r"\s+", " ", value or "").strip()


def parse_int(value: str, *, absolute: bool = False) -> int | None:
    match = _INT_RE.search(value or "")
    if not match:
        return None
    number = int(match.group(0).replace(",", ""))
    return abs(number) if absolute else number


@dataclass
class LinkCapture:
    href: str
    text: str


@dataclass
class CellCapture:
    text: str = ""
    colspan: int = 1
    links: list[LinkCapture] = field(default_factory=list)
    data_parts: list[str] = field(default_factory=list)
    child_tags: list[str] = field(default_factory=list)


@dataclass
class RowCapture:
    cells: list[CellCapture] = field(default_factory=list)


@dataclass
class TagRecord:
    tag_id: int
    name: str
    uses: int
    upvotes: int
    downvotes: int
    reported_synonym_count: int
    synonyms: list[str]
    synonym_raw_text: str
    synonym_parse_ok: bool


@dataclass
class ParsedPage:
    reported_total: int | None
    final_page: int | None
    tags: list[TagRecord]
    synonym_mismatches: int


class TagsHTMLParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.tables: list[list[RowCapture]] = []
        self.hrefs: list[str] = []
        self.all_text: list[str] = []

        self._table_depth = 0
        self._current_table: list[RowCapture] | None = None
        self._current_row: RowCapture | None = None
        self._current_cell: CellCapture | None = None
        self._current_link_href: str | None = None
        self._current_link_text: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        attrs_d = {k.lower(): (v or "") for k, v in attrs}
        tag = tag.lower()

        if tag == "table":
            self._table_depth += 1
            if self._table_depth == 1:
                self._current_table = []
                self.tables.append(self._current_table)
            return

        if self._table_depth < 1:
            if tag == "a":
                href = attrs_d.get("href", "")
                if href:
                    self.hrefs.append(href)
            return

        if tag == "tr":
            self._current_row = RowCapture()
            return

        if tag in ("td", "th") and self._current_row is not None:
            try:
                colspan = max(1, int(attrs_d.get("colspan", "1") or "1"))
            except ValueError:
                colspan = 1
            self._current_cell = CellCapture(colspan=colspan)
            self._current_row.cells.append(self._current_cell)
            return

        if self._current_cell is not None and tag not in ("td", "th"):
            self._current_cell.child_tags.append(tag)

        if tag == "a":
            href = attrs_d.get("href", "")
            if href:
                self.hrefs.append(href)
            if self._current_cell is not None:
                self._current_link_href = href
                self._current_link_text = []

    def handle_endtag(self, tag: str) -> None:
        tag = tag.lower()

        if tag == "a" and self._current_link_href is not None:
            text = clean_space("".join(self._current_link_text))
            self._current_cell.links.append(
                LinkCapture(href=self._current_link_href, text=text)
            )
            self._current_link_href = None
            self._current_link_text = []
            return

        if tag in ("td", "th"):
            if self._current_cell is not None:
                self._current_cell.text = clean_space(self._current_cell.text)
            self._current_cell = None
            return

        if tag == "tr":
            if self._current_table is not None and self._current_row is not None:
                self._current_table.append(self._current_row)
            self._current_row = None
            self._current_cell = None
            return

        if tag == "table":
            if self._table_depth > 0:
                self._table_depth -= 1
            if self._table_depth == 0:
                self._current_table = None

    def handle_data(self, data: str) -> None:
        self.all_text.append(data)
        if self._current_cell is not None:
            self._current_cell.text += data
            self._current_cell.data_parts.append(data)
        if self._current_link_href is not None:
            self._current_link_text.append(data)


def _looks_like_tag_row(row: RowCapture) -> bool:
    if len(row.cells) != 6:
        return False
    return (
        parse_int(row.cells[0].text) is not None
        and bool(clean_space(row.cells[1].text))
        and parse_int(row.cells[2].text) is not None
        and parse_int(row.cells[5].text) is not None
    )


def _looks_like_detail_row(row: RowCapture) -> bool:
    return len(row.cells) == 1 and row.cells[0].colspan >= 5


def _safe_parser_diagnostic(html: str) -> dict[str, object]:
    """Describe parser shape without emitting tag names, IDs, cookie data, or raw HTML."""
    parser = TagsHTMLParser()
    parser.feed(html)
    parser.close()

    rows = [row for table in parser.tables for row in table]
    six = [row for row in rows if len(row.cells) == 6]
    details = [row for row in rows if _looks_like_detail_row(row)]

    reject = Counter()
    for row in six:
        if parse_int(row.cells[0].text) is None:
            reject["tag_id_not_numeric"] += 1
        if not clean_space(row.cells[1].text):
            reject["name_empty"] += 1
        if parse_int(row.cells[2].text) is None:
            reject["uses_not_numeric"] += 1
        if parse_int(row.cells[5].text) is None:
            reject["synonym_count_not_numeric_or_blank"] += 1

    link_dist = Counter(len(row.cells[0].links) for row in details)
    nonempty_parts_dist = Counter(
        sum(1 for part in row.cells[0].data_parts if clean_space(part))
        for row in details
    )
    tag_pattern_dist = Counter(
        ",".join(row.cells[0].child_tags) or "(none)"
        for row in details
    )

    samples: list[dict[str, object]] = []
    for table in parser.tables:
        previous_six: RowCapture | None = None
        for row in table:
            if len(row.cells) == 6:
                previous_six = row
                continue
            if not _looks_like_detail_row(row):
                continue
            cell = row.cells[0]
            raw = "".join(cell.data_parts)
            if not clean_space(raw):
                continue
            reported = (
                parse_int(previous_six.cells[5].text)
                if previous_six is not None
                else None
            )
            parts = [clean_space(x) for x in cell.data_parts if clean_space(x)]
            samples.append(
                {
                    "reported_synonyms": reported,
                    "text_characters": len(clean_space(raw)),
                    "link_count": len(cell.links),
                    "nonempty_text_nodes": len(parts),
                    "text_node_lengths": [len(x) for x in parts[:12]],
                    "child_tag_counts": dict(Counter(cell.child_tags)),
                    "punctuation_counts": {
                        "comma": raw.count(","),
                        "semicolon": raw.count(";"),
                        "pipe": raw.count("|"),
                        "newline": raw.count("\n"),
                    },
                }
            )
            if len(samples) >= 5:
                break
        if len(samples) >= 5:
            break

    return {
        "tables": len(parser.tables),
        "rows_total": len(rows),
        "six_cell_rows": len(six),
        "recognized_tag_rows": sum(1 for row in six if _looks_like_tag_row(row)),
        "six_cell_rejection_reasons": dict(reject),
        "detail_rows": len(details),
        "detail_rows_with_text": sum(
            1 for row in details if clean_space(row.cells[0].text)
        ),
        "detail_link_count_distribution": dict(sorted(link_dist.items())),
        "detail_text_node_count_distribution": dict(
            sorted(nonempty_parts_dist.items())
        ),
        "detail_child_tag_patterns_top5": [
            {"pattern": pattern, "count": count}
            for pattern, count in tag_pattern_dist.most_common(5)
        ],
        "detail_samples": samples,
    }


def _synonyms_from_detail(cell: CellCapture | None) -> tuple[list[str], str]:
    if cell is None:
        return [], ""

    raw = clean_space(cell.text)
    values: list[str] = []
    seen: set[str] = set()

    # Prefer explicit links when the site supplies them. The real site may instead
    # render synonym names as plain text; the canary diagnostic below detects that
    # shape before any bulk crawl is allowed.
    for link in cell.links:
        text = clean_space(link.text)
        if not text:
            continue
        if re.fullmatch(r"[\[\](){}+\-\s]+", text):
            continue
        key = text.casefold()
        if key in seen:
            continue
        seen.add(key)
        values.append(text)

    return values, raw


def parse_tags_page(html: str) -> ParsedPage:
    parser = TagsHTMLParser()
    parser.feed(html)
    parser.close()

    page_text = clean_space(" ".join(parser.all_text))
    totals = [
        int(match.group(1).replace(",", ""))
        for match in _TOTAL_RE.finditer(page_text)
    ]
    reported_total = max(totals) if totals else None

    page_numbers: list[int] = []
    for href in parser.hrefs:
        try:
            query = urllib.parse.parse_qs(urllib.parse.urlsplit(href).query)
            for raw in query.get("page", []):
                if str(raw).isdigit():
                    page_numbers.append(int(raw))
        except ValueError:
            pass
    final_page = max(page_numbers) if page_numbers else None

    tags: list[TagRecord] = []
    mismatches = 0

    for table in parser.tables:
        i = 0
        while i < len(table):
            row = table[i]
            if not _looks_like_tag_row(row):
                i += 1
                continue

            cells = row.cells
            tag_id = parse_int(cells[0].text)
            uses = parse_int(cells[2].text)
            upvotes = parse_int(cells[3].text, absolute=True)
            downvotes = parse_int(cells[4].text, absolute=True)
            synonym_count = parse_int(cells[5].text)

            if tag_id is None or uses is None or synonym_count is None:
                i += 1
                continue

            name = clean_space(cells[1].text)
            detail: CellCapture | None = None
            if i + 1 < len(table) and _looks_like_detail_row(table[i + 1]):
                detail = table[i + 1].cells[0]
                i += 1

            synonyms, raw_detail = _synonyms_from_detail(detail)
            parse_ok = len(synonyms) == synonym_count
            if not parse_ok:
                mismatches += 1

            tags.append(
                TagRecord(
                    tag_id=tag_id,
                    name=name,
                    uses=uses,
                    upvotes=upvotes or 0,
                    downvotes=downvotes or 0,
                    reported_synonym_count=synonym_count,
                    synonyms=synonyms,
                    synonym_raw_text=raw_detail,
                    synonym_parse_ok=parse_ok,
                )
            )
            i += 1

    return ParsedPage(
        reported_total=reported_total,
        final_page=final_page,
        tags=tags,
        synonym_mismatches=mismatches,
    )


def open_db(path: Path) -> sqlite3.Connection:
    path.parent.mkdir(parents=True, exist_ok=True)
    db: sqlite3.Connection | None = None
    try:
        db = sqlite3.connect(path, timeout=30.0)
        db.execute("PRAGMA busy_timeout=30000")
        db.execute("PRAGMA foreign_keys=ON")
        db.execute("PRAGMA journal_mode=WAL")
        db.execute("PRAGMA synchronous=NORMAL")
        db.executescript(
        """
        CREATE TABLE IF NOT EXISTS meta (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS pages (
            page INTEGER PRIMARY KEY,
            status TEXT NOT NULL,
            attempts INTEGER NOT NULL DEFAULT 0,
            http_status INTEGER,
            latency_ms INTEGER,
            tag_count INTEGER,
            synonym_count INTEGER,
            body_sha256 TEXT,
            fetched_at TEXT,
            error TEXT
        );

        CREATE TABLE IF NOT EXISTS tags (
            tag_id INTEGER PRIMARY KEY,
            name TEXT NOT NULL UNIQUE,
            uses INTEGER NOT NULL,
            upvotes INTEGER NOT NULL,
            downvotes INTEGER NOT NULL,
            reported_synonym_count INTEGER NOT NULL,
            source_page INTEGER NOT NULL,
            synonym_raw_text TEXT NOT NULL,
            synonym_parse_ok INTEGER NOT NULL CHECK (synonym_parse_ok IN (0,1))
        );

        CREATE INDEX IF NOT EXISTS tags_name_nocase
            ON tags(name COLLATE NOCASE);
        CREATE INDEX IF NOT EXISTS tags_source_page
            ON tags(source_page);

        CREATE TABLE IF NOT EXISTS tag_synonyms (
            tag_id INTEGER NOT NULL REFERENCES tags(tag_id) ON DELETE CASCADE,
            synonym TEXT NOT NULL,
            PRIMARY KEY (tag_id, synonym)
        );

        CREATE INDEX IF NOT EXISTS synonyms_name_nocase
            ON tag_synonyms(synonym COLLATE NOCASE);
        """
        )
        set_meta(db, "schema_version", SCHEMA_VERSION)
        db.commit()
        return db
    except sqlite3.OperationalError as exc:
        if db is not None:
            db.close()
        raise RuntimeError(f"Could not open Tag Gremlin database {path}: {exc}") from exc


def get_meta(db: sqlite3.Connection, key: str) -> str | None:
    row = db.execute("SELECT value FROM meta WHERE key=?", (key,)).fetchone()
    return row[0] if row else None


def set_meta(db: sqlite3.Connection, key: str, value: object) -> None:
    db.execute(
        "INSERT INTO meta(key,value) VALUES(?,?) "
        "ON CONFLICT(key) DO UPDATE SET value=excluded.value",
        (key, str(value)),
    )


def page_status(db: sqlite3.Connection, page: int) -> str | None:
    row = db.execute("SELECT status FROM pages WHERE page=?", (page,)).fetchone()
    return row[0] if row else None


def prior_attempts(db: sqlite3.Connection, page: int) -> int:
    row = db.execute("SELECT attempts FROM pages WHERE page=?", (page,)).fetchone()
    return int(row[0]) if row else 0


def write_page(
    db: sqlite3.Connection,
    *,
    page: int,
    parsed: ParsedPage,
    attempts: int,
    http_status: int,
    latency_ms: int,
    body_sha256: str,
) -> None:
    status = "ok" if parsed.synonym_mismatches == 0 else "parse_error"
    error = (
        None
        if status == "ok"
        else f"{parsed.synonym_mismatches} tag(s) had synonym-count mismatches"
    )
    synonym_total = sum(len(tag.synonyms) for tag in parsed.tags)

    try:
        db.execute("BEGIN")
        old_ids = [
            row[0]
            for row in db.execute(
                "SELECT tag_id FROM tags WHERE source_page=?", (page,)
            ).fetchall()
        ]
        if old_ids:
            db.executemany(
                "DELETE FROM tag_synonyms WHERE tag_id=?",
                ((tag_id,) for tag_id in old_ids),
            )
            db.execute("DELETE FROM tags WHERE source_page=?", (page,))

        for tag in parsed.tags:
            db.execute(
                """
                INSERT INTO tags(
                    tag_id,name,uses,upvotes,downvotes,reported_synonym_count,
                    source_page,synonym_raw_text,synonym_parse_ok
                ) VALUES(?,?,?,?,?,?,?,?,?)
                """,
                (
                    tag.tag_id,
                    tag.name,
                    tag.uses,
                    tag.upvotes,
                    tag.downvotes,
                    tag.reported_synonym_count,
                    page,
                    tag.synonym_raw_text,
                    int(tag.synonym_parse_ok),
                ),
            )
            db.executemany(
                "INSERT INTO tag_synonyms(tag_id,synonym) VALUES(?,?)",
                ((tag.tag_id, synonym) for synonym in tag.synonyms),
            )

        db.execute(
            """
            INSERT INTO pages(
                page,status,attempts,http_status,latency_ms,tag_count,
                synonym_count,body_sha256,fetched_at,error
            ) VALUES(?,?,?,?,?,?,?,?,?,?)
            ON CONFLICT(page) DO UPDATE SET
                status=excluded.status,
                attempts=pages.attempts + excluded.attempts,
                http_status=excluded.http_status,
                latency_ms=excluded.latency_ms,
                tag_count=excluded.tag_count,
                synonym_count=excluded.synonym_count,
                body_sha256=excluded.body_sha256,
                fetched_at=excluded.fetched_at,
                error=excluded.error
            """,
            (
                page,
                status,
                attempts,
                http_status,
                latency_ms,
                len(parsed.tags),
                synonym_total,
                body_sha256,
                now_iso(),
                error,
            ),
        )
        set_meta(db, "updated_at", now_iso())
        db.commit()
    except Exception:
        db.rollback()
        raise


def record_page_failure(
    db: sqlite3.Connection,
    *,
    page: int,
    attempts: int,
    http_status: int | None,
    error: str,
) -> None:
    db.execute(
        """
        INSERT INTO pages(page,status,attempts,http_status,fetched_at,error)
        VALUES(?,?,?,?,?,?)
        ON CONFLICT(page) DO UPDATE SET
            status='failed',
            attempts=pages.attempts + excluded.attempts,
            http_status=excluded.http_status,
            fetched_at=excluded.fetched_at,
            error=excluded.error
        """,
        (page, "failed", attempts, http_status, now_iso(), error[:1000]),
    )
    set_meta(db, "updated_at", now_iso())
    db.commit()


def _profile_from_profiles_ini(root: Path) -> Path:
    ini = root / "profiles.ini"
    if not ini.exists():
        raise RuntimeError(f"Firefox profiles.ini not found at {ini}")

    cfg = configparser.ConfigParser()
    cfg.read(ini)

    candidates: list[tuple[int, str, Path]] = []
    for section in cfg.sections():
        if not section.lower().startswith("profile"):
            continue
        path_value = cfg.get(section, "Path", fallback="")
        if not path_value:
            continue
        relative = cfg.getboolean(section, "IsRelative", fallback=True)
        profile_path = (root / path_value) if relative else Path(path_value)
        name = cfg.get(section, "Name", fallback=section)
        score = 0
        if cfg.getboolean(section, "Default", fallback=False):
            score += 100
        if "default-esr" in path_value.lower() or "default-esr" in name.lower():
            score += 20
        if "default" in path_value.lower() or "default" in name.lower():
            score += 10
        if (profile_path / "cookies.sqlite").exists():
            score += 5
        candidates.append((score, name, profile_path))

    if not candidates:
        raise RuntimeError("No Firefox profiles were found in profiles.ini")

    candidates.sort(key=lambda item: item[0], reverse=True)
    best_score = candidates[0][0]
    best = [item for item in candidates if item[0] == best_score]
    if len(best) > 1 and best_score < 100:
        details = "\n".join(f"  {name}: {path}" for _, name, path in best)
        raise RuntimeError(
            "Firefox profile selection is ambiguous. Use --firefox-profile:\n"
            + details
        )
    return best[0][2].expanduser().resolve()


def discover_firefox_profile(override: str | None) -> Path:
    if override:
        path = Path(override).expanduser().resolve()
        if path.is_file() and path.name == "cookies.sqlite":
            path = path.parent
        if not (path / "cookies.sqlite").exists():
            raise RuntimeError(f"No cookies.sqlite found in {path}")
        return path

    root = Path.home() / ".mozilla" / "firefox"
    return _profile_from_profiles_ini(root)


def _domain_matches(hostname: str, cookie_host: str) -> bool:
    domain = cookie_host.lstrip(".").casefold()
    host = hostname.casefold()
    return host == domain or host.endswith("." + domain)


def _firefox_pids() -> list[int]:
    proc = Path("/proc")
    if not proc.exists():
        return []

    try:
        uid = os.getuid()
    except AttributeError:
        uid = None

    pids: list[int] = []
    for entry in proc.iterdir():
        if not entry.name.isdigit():
            continue
        try:
            if uid is not None and entry.stat().st_uid != uid:
                continue
            comm = (entry / "comm").read_text(encoding="utf-8").strip().casefold()
        except (OSError, UnicodeError):
            continue
        if comm in {"firefox", "firefox-bin"}:
            pids.append(int(entry.name))
    return sorted(pids)


def _firefox_running() -> bool:
    return bool(_firefox_pids())


def _close_firefox(timeout: float = 15.0) -> None:
    pids = _firefox_pids()
    if not pids:
        print("Firefox is already closed.", flush=True)
        return

    print(
        f"Closing Firefox cleanly ({len(pids)} process(es)) so cookies.sqlite can be read...",
        flush=True,
    )

    for pid in pids:
        try:
            os.kill(pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
        except PermissionError as exc:
            raise RuntimeError(f"Could not close Firefox process {pid}: {exc}") from exc

    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if not _firefox_running():
            print("Firefox closed.", flush=True)
            return
        time.sleep(0.10)

    remaining = _firefox_pids()
    raise RuntimeError(
        "Firefox did not close cleanly within "
        f"{timeout:.0f}s (remaining PID(s): {', '.join(map(str, remaining))}). "
        "Tag Gremlin will not force-kill it; close Firefox manually and rerun."
    )


def _reopen_firefox(profile: Path) -> None:
    if _firefox_running():
        return

    exe = shutil.which("firefox")
    if not exe:
        print(
            "Firefox is closed, but the 'firefox' executable was not found in PATH; "
            "open Firefox manually.",
            flush=True,
        )
        return

    try:
        subprocess.Popen(
            [exe, "--profile", str(profile)],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            start_new_session=True,
            close_fds=True,
        )
        print("Firefox reopened automatically.", flush=True)
    except OSError as exc:
        print(f"Could not reopen Firefox automatically: {exc}", flush=True)


def _read_firefox_cookie_rows(
    cookie_db: Path, *, max_attempts: int = 8
) -> tuple[set[str], list[tuple[object, ...]]]:
    """Read Firefox cookies directly with bounded retries; never wait forever."""
    uri = "file:" + urllib.parse.quote(str(cookie_db.resolve())) + "?mode=ro"
    last_error: sqlite3.OperationalError | None = None

    for attempt in range(1, max_attempts + 1):
        db: sqlite3.Connection | None = None
        try:
            db = sqlite3.connect(uri, uri=True, timeout=2.0)
            db.execute("PRAGMA query_only=ON")
            db.execute("PRAGMA busy_timeout=2000")

            columns = {row[1] for row in db.execute("PRAGMA table_info(moz_cookies)")}
            required = {"host", "path", "isSecure", "expiry", "name", "value"}
            missing = required - columns
            if missing:
                raise RuntimeError(
                    "Firefox cookie database is missing expected columns: "
                    + ", ".join(sorted(missing))
                )

            where = ""
            if "originAttributes" in columns:
                where = " WHERE originAttributes=''"

            rows = db.execute(
                "SELECT host,path,isSecure,expiry,name,value FROM moz_cookies" + where
            ).fetchall()
            return columns, rows
        except sqlite3.OperationalError as exc:
            last_error = exc
            message = str(exc).lower()
            if "locked" not in message and "busy" not in message:
                raise RuntimeError(
                    f"Could not read Firefox cookies.sqlite: {exc}"
                ) from exc

            if attempt >= max_attempts:
                break

            delay = min(2.0, 0.15 * (2 ** (attempt - 1)))
            print(
                f"Firefox cookies.sqlite is busy; retrying "
                f"({attempt}/{max_attempts}) in {delay:.2f}s...",
                flush=True,
            )
            time.sleep(delay)
        finally:
            if db is not None:
                db.close()

    raise RuntimeError(
        "Firefox cookies.sqlite stayed locked after bounded retries. "
        "Close Firefox for a few seconds, run Tag Gremlin again, then reopen Firefox. "
        f"Last SQLite error: {last_error}"
    )


def load_firefox_cookiejar(profile: Path, target_url: str) -> tuple[http.cookiejar.CookieJar, int]:
    hostname = urllib.parse.urlsplit(target_url).hostname
    if not hostname:
        raise RuntimeError("Target URL has no hostname")

    cookie_db = profile / "cookies.sqlite"
    _, rows = _read_firefox_cookie_rows(cookie_db)

    jar = http.cookiejar.CookieJar()
    now = int(time.time())
    loaded = 0

    for host, path, secure, expiry, name, value in rows:
        if not host or not name or not _domain_matches(hostname, host):
            continue
        expiry_i = int(expiry or 0)
        if expiry_i and expiry_i < now:
            continue

        initial_dot = str(host).startswith(".")
        cookie = http.cookiejar.Cookie(
            version=0,
            name=str(name),
            value=str(value),
            port=None,
            port_specified=False,
            domain=str(host),
            domain_specified=initial_dot,
            domain_initial_dot=initial_dot,
            path=str(path or "/"),
            path_specified=True,
            secure=bool(secure),
            expires=expiry_i or None,
            discard=not bool(expiry_i),
            comment=None,
            comment_url=None,
            rest={},
            rfc2109=False,
        )
        jar.set_cookie(cookie)
        loaded += 1

    return jar, loaded


class FetchFailure(RuntimeError):
    def __init__(self, message: str, *, attempts: int, status: int | None = None):
        super().__init__(message)
        self.attempts = attempts
        self.status = status


@dataclass
class FetchResult:
    page: int
    html: str
    status: int
    attempts: int
    latency_ms: int
    had_retry: bool
    body_sha256: str


class HarvesterHTTP:
    def __init__(self, base_url: str, jar: http.cookiejar.CookieJar, timeout: float):
        self.base_url = base_url
        self.timeout = timeout
        self._jar = jar
        self._lock = threading.Lock()

    def page_url(self, page: int) -> str:
        split = urllib.parse.urlsplit(self.base_url)
        query = urllib.parse.parse_qsl(split.query, keep_blank_values=True)
        query = [(k, v) for k, v in query if k != "page"]
        if page > 1:
            query.append(("page", str(page)))
        return urllib.parse.urlunsplit(
            (
                split.scheme,
                split.netloc,
                split.path,
                urllib.parse.urlencode(query),
                split.fragment,
            )
        )

    @staticmethod
    def _retry_after(headers) -> float | None:
        raw = headers.get("Retry-After") if headers else None
        if not raw:
            return None
        try:
            return min(300.0, max(0.0, float(raw)))
        except ValueError:
            return None

    def fetch_page(self, page: int, max_attempts: int) -> FetchResult:
        url = self.page_url(page)
        last_error: Exception | None = None
        last_status: int | None = None
        total_started = time.monotonic()

        for attempt in range(1, max_attempts + 1):
            request = urllib.request.Request(
                url,
                headers={
                    "User-Agent": USER_AGENT,
                    "Accept": "text/html,application/xhtml+xml",
                    "Accept-Language": "en-US,en;q=0.5",
                    "Cache-Control": "no-cache",
                    "Referer": self.base_url,
                },
                method="GET",
            )
            started = time.monotonic()
            try:
                # CookieJar mutation/selection is serialized, but the network request is not.
                # This keeps concurrent fetches safe without exporting cookie values.
                with self._lock:
                    self._jar.add_cookie_header(request)
                opener = urllib.request.build_opener()
                with opener.open(request, timeout=self.timeout) as response:
                    with self._lock:
                        self._jar.extract_cookies(response, request)
                    status = int(getattr(response, "status", 200))
                    last_status = status
                    body = response.read(MAX_RESPONSE_BYTES + 1)
                    if len(body) > MAX_RESPONSE_BYTES:
                        raise FetchFailure(
                            f"page {page} exceeded {MAX_RESPONSE_BYTES} bytes",
                            attempts=attempt,
                            status=status,
                        )
                    charset = response.headers.get_content_charset() or "utf-8"
                    html = body.decode(charset, errors="replace")
                    elapsed_ms = round((time.monotonic() - started) * 1000)
                    return FetchResult(
                        page=page,
                        html=html,
                        status=status,
                        attempts=attempt,
                        latency_ms=elapsed_ms,
                        had_retry=attempt > 1,
                        body_sha256=hashlib.sha256(body).hexdigest(),
                    )
            except urllib.error.HTTPError as exc:
                last_error = exc
                last_status = exc.code
                if exc.code in (401, 403):
                    raise FetchFailure(
                        f"page {page}: HTTP {exc.code}; Firefox login may have expired",
                        attempts=attempt,
                        status=exc.code,
                    ) from exc
                transient = exc.code == 429 or 500 <= exc.code <= 599
                if not transient or attempt >= max_attempts:
                    break
                delay = self._retry_after(exc.headers)
                if delay is None:
                    delay = min(60.0, 2.0 ** (attempt - 1)) + random.uniform(0, 0.75)
                time.sleep(delay)
            except FetchFailure:
                raise
            except (urllib.error.URLError, TimeoutError, OSError) as exc:
                last_error = exc
                if attempt >= max_attempts:
                    break
                delay = min(60.0, 2.0 ** (attempt - 1)) + random.uniform(0, 0.75)
                time.sleep(delay)

        elapsed = round((time.monotonic() - total_started) * 1000)
        message = f"page {page} failed after {max_attempts} attempt(s)"
        if last_status is not None:
            message += f" (last HTTP {last_status})"
        if last_error is not None:
            message += f": {last_error}"
        raise FetchFailure(message + f" [{elapsed} ms]", attempts=max_attempts, status=last_status)


def validate_source_url(url: str) -> str:
    split = urllib.parse.urlsplit(url)
    if split.scheme not in ("http", "https") or not split.netloc:
        raise RuntimeError("--url must be a complete http(s) URL")
    if not split.path.lower().endswith("tags.php"):
        raise RuntimeError("--url must point to the site's tags.php page")
    query = [(k, v) for k, v in urllib.parse.parse_qsl(split.query) if k != "page"]
    return urllib.parse.urlunsplit(
        (split.scheme, split.netloc, split.path, urllib.parse.urlencode(query), "")
    )


def inspect_first_page(parsed: ParsedPage) -> tuple[int, int, int]:
    if not parsed.tags:
        raise RuntimeError(
            "No official tag rows were parsed from page 1. "
            "The Firefox session may not be authenticated, or the site layout changed."
        )
    if not parsed.reported_total:
        raise RuntimeError("Could not find the site-reported '<number> tags' total")

    rows_per_page = len(parsed.tags)
    final_page = parsed.final_page
    if final_page is None:
        final_page = math.ceil(parsed.reported_total / rows_per_page)
    if final_page < 1:
        raise RuntimeError("Could not determine the final page")

    return parsed.reported_total, final_page, rows_per_page


def print_progress(db: sqlite3.Connection, final_page: int) -> None:
    row = db.execute(
        """
        SELECT
            SUM(CASE WHEN status='ok' THEN 1 ELSE 0 END),
            SUM(CASE WHEN status='failed' THEN 1 ELSE 0 END),
            SUM(CASE WHEN status='parse_error' THEN 1 ELSE 0 END)
        FROM pages
        """
    ).fetchone()
    ok, failed, parse_error = (int(x or 0) for x in row)
    tags = db.execute("SELECT COUNT(*) FROM tags").fetchone()[0]
    print(
        f"pages {ok:,}/{final_page:,} ok"
        f" | failed {failed:,}"
        f" | parse-error {parse_error:,}"
        f" | tags {tags:,}",
        flush=True,
    )


def harvest(args: argparse.Namespace) -> int:
    source_url = validate_source_url(args.url)
    db_path = Path(args.db).expanduser().resolve()

    print(f"Opening Tag Gremlin database: {db_path}", flush=True)
    db = open_db(db_path)
    print("Harvest database ready.", flush=True)

    existing_source = get_meta(db, "source_url")
    if existing_source and existing_source != source_url:
        raise RuntimeError(
            f"{db_path} belongs to a different source URL. "
            "Use a different --db for a fresh harvest."
        )

    print("Finding Firefox profile...", flush=True)
    profile = discover_firefox_profile(args.firefox_profile)
    print(f"Firefox profile: {profile}", flush=True)
    if args.close_firefox:
        _close_firefox()
    print("Reading matching Firefox cookies...", flush=True)
    jar, cookie_count = load_firefox_cookiejar(profile, source_url)
    print(
        f"Loaded {cookie_count} matching cookie(s) for the target host "
        "(values not shown).",
        flush=True,
    )
    if args.reopen_firefox:
        _reopen_firefox(profile)

    http = HarvesterHTTP(source_url, jar, timeout=args.timeout)

    print("Fetching and parsing page 1...", flush=True)
    first_fetch = http.fetch_page(1, args.max_attempts)
    first_parsed = parse_tags_page(first_fetch.html)
    total, final_page, rows_per_page = inspect_first_page(first_parsed)

    inferred_page_capacity = (
        math.ceil(total / final_page) if final_page and total else rows_per_page
    )
    canary_problem = (
        first_parsed.synonym_mismatches > 0
        or rows_per_page != inferred_page_capacity
    )
    if canary_problem:
        print("")
        print("PARSER DIAGNOSTIC (privacy-safe; no tag names/IDs/raw HTML)")
        print("--------------------------------------------------------")
        print(json.dumps(_safe_parser_diagnostic(first_fetch.html), indent=2))
        print("")
        raise RuntimeError(
            "Page 1 parser canary failed: "
            f"parsed {rows_per_page} tag rows, expected about "
            f"{inferred_page_capacity}, with "
            f"{first_parsed.synonym_mismatches} synonym mismatch(es). "
            "Bulk crawl was not started."
        )

    set_meta(db, "source_url", source_url)
    set_meta(db, "reported_total", total)
    set_meta(db, "final_page", final_page)
    set_meta(db, "rows_per_page", rows_per_page)
    if not get_meta(db, "created_at"):
        set_meta(db, "created_at", now_iso())
    set_meta(db, "updated_at", now_iso())
    db.commit()

    if page_status(db, 1) != "ok":
        write_page(
            db,
            page=1,
            parsed=first_parsed,
            attempts=first_fetch.attempts,
            http_status=first_fetch.status,
            latency_ms=first_fetch.latency_ms,
            body_sha256=first_fetch.body_sha256,
        )

    print(
        f"Site reports {total:,} official tags across {final_page:,} page(s); "
        f"page 1 contains {rows_per_page} tag rows."
    )

    ok_pages = {
        row[0]
        for row in db.execute("SELECT page FROM pages WHERE status='ok'").fetchall()
    }
    pending = [page for page in range(1, final_page + 1) if page not in ok_pages]
    if not pending:
        print("All pages are already harvested.")
        report = verify_db(db)
        print_verify_report(report)
        return 0 if report["complete"] else 2

    current_workers = max(1, min(args.initial_workers, args.max_workers))
    clean_batches = 0

    print(
        f"Resuming with {len(pending):,} page(s) pending; "
        f"adaptive concurrency {current_workers}..{args.max_workers}."
    )

    fatal_parse_error = False

    while pending and not fatal_parse_error:
        batch_size = max(current_workers, current_workers * 4)
        batch = pending[:batch_size]
        pending = pending[batch_size:]

        latencies: list[int] = []
        stressed = False

        with ThreadPoolExecutor(max_workers=current_workers) as pool:
            futures = {
                pool.submit(http.fetch_page, page, args.max_attempts): page
                for page in batch
            }
            for future in as_completed(futures):
                page = futures[future]
                try:
                    result = future.result()
                    parsed = parse_tags_page(result.html)

                    if not parsed.tags:
                        raise FetchFailure(
                            f"page {page}: HTTP 200 but no official tag rows parsed",
                            attempts=result.attempts,
                            status=result.status,
                        )

                    if page < final_page and len(parsed.tags) != rows_per_page:
                        raise FetchFailure(
                            f"page {page}: expected {rows_per_page} tag rows, "
                            f"parsed {len(parsed.tags)}",
                            attempts=result.attempts,
                            status=result.status,
                        )

                    write_page(
                        db,
                        page=page,
                        parsed=parsed,
                        attempts=result.attempts,
                        http_status=result.status,
                        latency_ms=result.latency_ms,
                        body_sha256=result.body_sha256,
                    )
                    latencies.append(result.latency_ms)
                    stressed = stressed or result.had_retry
                    if parsed.synonym_mismatches:
                        stressed = True
                        fatal_parse_error = True
                        print(
                            f"page {page}: {parsed.synonym_mismatches} "
                            "synonym mismatch(es); bulk crawl will stop after this batch"
                        )
                except FetchFailure as exc:
                    stressed = True
                    record_page_failure(
                        db,
                        page=page,
                        attempts=exc.attempts,
                        http_status=exc.status,
                        error=str(exc),
                    )
                    print(str(exc), file=sys.stderr)
                except sqlite3.IntegrityError as exc:
                    stressed = True
                    record_page_failure(
                        db,
                        page=page,
                        attempts=1,
                        http_status=200,
                        error=f"database uniqueness failure: {exc}",
                    )
                    print(
                        f"page {page}: database uniqueness failure: {exc}",
                        file=sys.stderr,
                    )
                except Exception as exc:
                    stressed = True
                    record_page_failure(
                        db,
                        page=page,
                        attempts=1,
                        http_status=None,
                        error=f"unexpected error: {exc}",
                    )
                    print(f"page {page}: {exc}", file=sys.stderr)

        print_progress(db, final_page)

        if fatal_parse_error:
            print(
                "Stopping because synonym parsing no longer matches the site. "
                "Saved OK pages remain resumable."
            )
            break

        median_ms = statistics.median(latencies) if latencies else None
        if stressed or (median_ms is not None and median_ms > args.slow_ms):
            clean_batches = 0
            if current_workers > 1:
                current_workers -= 1
                print(f"Backing off to {current_workers} worker(s).")
        else:
            clean_batches += 1
            if (
                clean_batches >= 2
                and median_ms is not None
                and median_ms < args.fast_ms
                and current_workers < args.max_workers
            ):
                current_workers += 1
                clean_batches = 0
                print(f"Clean/fast batches; increasing to {current_workers} worker(s).")

    report = verify_db(db)
    print_verify_report(report)
    return 0 if report["complete"] else 2


def verify_db(db: sqlite3.Connection) -> dict[str, object]:
    total_raw = get_meta(db, "reported_total")
    final_raw = get_meta(db, "final_page")
    total = int(total_raw) if total_raw and total_raw.isdigit() else None
    final_page = int(final_raw) if final_raw and final_raw.isdigit() else None

    ok_pages = db.execute(
        "SELECT COUNT(*) FROM pages WHERE status='ok'"
    ).fetchone()[0]
    failed_pages = db.execute(
        "SELECT COUNT(*) FROM pages WHERE status='failed'"
    ).fetchone()[0]
    parse_error_pages = db.execute(
        "SELECT COUNT(*) FROM pages WHERE status='parse_error'"
    ).fetchone()[0]
    tag_count = db.execute("SELECT COUNT(*) FROM tags").fetchone()[0]
    unique_names = db.execute("SELECT COUNT(DISTINCT name) FROM tags").fetchone()[0]
    page_tag_sum = db.execute(
        "SELECT COALESCE(SUM(tag_count),0) FROM pages WHERE status IN ('ok','parse_error')"
    ).fetchone()[0]
    synonym_mismatches = db.execute(
        "SELECT COUNT(*) FROM tags WHERE synonym_parse_ok=0"
    ).fetchone()[0]
    edge_mismatches = db.execute(
        """
        SELECT COUNT(*) FROM (
            SELECT t.tag_id
            FROM tags t
            LEFT JOIN tag_synonyms s ON s.tag_id=t.tag_id
            GROUP BY t.tag_id, t.reported_synonym_count
            HAVING COUNT(s.synonym) != t.reported_synonym_count
        )
        """
    ).fetchone()[0]

    missing_pages: list[int] = []
    if final_page:
        present = {
            row[0]
            for row in db.execute(
                "SELECT page FROM pages WHERE status='ok'"
            ).fetchall()
        }
        missing_pages = [
            page for page in range(1, final_page + 1) if page not in present
        ]

    complete = all(
        (
            total is not None,
            final_page is not None,
            final_page is not None and ok_pages == final_page,
            failed_pages == 0,
            parse_error_pages == 0,
            total is not None and tag_count == total,
            total is not None and unique_names == total,
            total is not None and page_tag_sum == total,
            synonym_mismatches == 0,
            edge_mismatches == 0,
            len(missing_pages) == 0,
        )
    )

    return {
        "complete": complete,
        "reported_total": total,
        "final_page": final_page,
        "ok_pages": ok_pages,
        "failed_pages": failed_pages,
        "parse_error_pages": parse_error_pages,
        "tag_count": tag_count,
        "unique_names": unique_names,
        "page_tag_sum": page_tag_sum,
        "synonym_mismatches": synonym_mismatches,
        "edge_mismatches": edge_mismatches,
        "missing_pages": missing_pages,
    }


def print_verify_report(report: dict[str, object]) -> None:
    print("")
    print("Tag Gremlin verification")
    print("------------------------")
    print(f"Reported tags:            {report['reported_total']}")
    print(f"Final page:               {report['final_page']}")
    print(f"Pages OK:                 {report['ok_pages']}")
    print(f"Failed pages:             {report['failed_pages']}")
    print(f"Parse-error pages:        {report['parse_error_pages']}")
    print(f"Stored unique TagIDs:     {report['tag_count']}")
    print(f"Stored unique names:      {report['unique_names']}")
    print(f"Sum of page tag counts:   {report['page_tag_sum']}")
    print(f"Synonym parse mismatches: {report['synonym_mismatches']}")
    print(f"Synonym edge mismatches:  {report['edge_mismatches']}")
    missing = report["missing_pages"]
    if isinstance(missing, list) and missing:
        preview = ", ".join(str(x) for x in missing[:20])
        suffix = " ..." if len(missing) > 20 else ""
        print(f"Missing/non-OK pages:     {preview}{suffix}")
    print(f"COMPLETE:                 {'YES' if report['complete'] else 'NO'}")


def status_command(args: argparse.Namespace) -> int:
    db_path = Path(args.db).expanduser().resolve()
    if not db_path.exists():
        raise RuntimeError(f"Database does not exist: {db_path}")
    db = open_db(db_path)
    report = verify_db(db)
    print_verify_report(report)

    attempts = db.execute(
        "SELECT COALESCE(SUM(attempts),0) FROM pages"
    ).fetchone()[0]
    synonyms = db.execute("SELECT COUNT(*) FROM tag_synonyms").fetchone()[0]
    print(f"Network attempts recorded: {attempts}")
    print(f"Synonym mappings stored:   {synonyms}")
    db.close()
    return 0


def verify_command(args: argparse.Namespace) -> int:
    db_path = Path(args.db).expanduser().resolve()
    if not db_path.exists():
        raise RuntimeError(f"Database does not exist: {db_path}")
    db = open_db(db_path)
    report = verify_db(db)
    print_verify_report(report)
    db.close()
    return 0 if report["complete"] else 2


def _write_tsv(path: Path, header: list[str], rows: Iterable[tuple[object, ...]]) -> None:
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle, delimiter="\t", lineterminator="\n")
        writer.writerow(header)
        writer.writerows(rows)


def export_command(args: argparse.Namespace) -> int:
    db_path = Path(args.db).expanduser().resolve()
    if not db_path.exists():
        raise RuntimeError(f"Database does not exist: {db_path}")
    db = open_db(db_path)
    report = verify_db(db)
    if not report["complete"] and not args.allow_incomplete:
        print_verify_report(report)
        raise RuntimeError(
            "Refusing to export an incomplete harvest. "
            "Use --allow-incomplete only if you intentionally want partial data."
        )

    out = Path(args.out_dir).expanduser().resolve()
    out.mkdir(parents=True, exist_ok=True)

    tag_rows = db.execute(
        """
        SELECT tag_id,name,uses,upvotes,downvotes,reported_synonym_count
        FROM tags
        ORDER BY name COLLATE NOCASE, tag_id
        """
    ).fetchall()

    with (out / "tags.txt").open("w", encoding="utf-8", newline="\n") as handle:
        for _, name, *_ in tag_rows:
            handle.write(str(name) + "\n")

    _write_tsv(
        out / "tags.tsv",
        ["tag_id", "tag", "uses", "upvotes", "downvotes", "synonym_count"],
        tag_rows,
    )

    synonym_rows = db.execute(
        """
        SELECT t.tag_id,t.name,s.synonym
        FROM tag_synonyms s
        JOIN tags t ON t.tag_id=s.tag_id
        ORDER BY t.name COLLATE NOCASE, s.synonym COLLATE NOCASE
        """
    ).fetchall()
    _write_tsv(
        out / "synonyms.tsv",
        ["tag_id", "official_tag", "synonym"],
        synonym_rows,
    )

    reverse_rows = db.execute(
        """
        SELECT s.synonym,t.tag_id,t.name
        FROM tag_synonyms s
        JOIN tags t ON t.tag_id=s.tag_id
        ORDER BY s.synonym COLLATE NOCASE, t.name COLLATE NOCASE
        """
    ).fetchall()
    db.close()

    _write_tsv(
        out / "synonym-map.tsv",
        ["synonym", "tag_id", "official_tag"],
        reverse_rows,
    )

    print(f"Exported {len(tag_rows):,} tags to {out / 'tags.txt'}")
    print(f"Exported metadata to {out / 'tags.tsv'}")
    print(f"Exported {len(synonym_rows):,} synonym mappings to {out / 'synonyms.tsv'}")
    print(f"Exported reverse synonym map to {out / 'synonym-map.tsv'}")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="tag-gremlin",
        description="Resumable Firefox-authenticated tags.php harvester.",
    )
    parser.add_argument(
        "--db",
        default=DEFAULT_DB,
        help=f"SQLite database path (default: ./{DEFAULT_DB})",
    )

    sub = parser.add_subparsers(dest="command", required=True)

    p_harvest = sub.add_parser("harvest", help="Harvest or resume the tag index")
    p_harvest.add_argument("--url", required=True, help="Complete https://SITE/tags.php URL")
    p_harvest.add_argument(
        "--firefox-profile",
        help="Firefox profile directory or cookies.sqlite path; default is auto-detect",
    )
    p_harvest.add_argument(
        "--no-close-firefox",
        dest="close_firefox",
        action="store_false",
        help="Do not close Firefox automatically before reading cookies.sqlite",
    )
    p_harvest.add_argument(
        "--no-reopen-firefox",
        dest="reopen_firefox",
        action="store_false",
        help="Do not reopen Firefox automatically after its cookies have been loaded",
    )
    p_harvest.set_defaults(close_firefox=True, reopen_firefox=True)
    p_harvest.add_argument(
        "--initial-workers",
        type=int,
        default=DEFAULT_INITIAL_WORKERS,
        help=f"Initial concurrent requests (default: {DEFAULT_INITIAL_WORKERS})",
    )
    p_harvest.add_argument(
        "--max-workers",
        type=int,
        default=DEFAULT_MAX_WORKERS,
        help=f"Maximum adaptive concurrency (default: {DEFAULT_MAX_WORKERS})",
    )
    p_harvest.add_argument(
        "--max-attempts",
        type=int,
        default=5,
        help="Maximum network attempts per page in one run (default: 5)",
    )
    p_harvest.add_argument(
        "--timeout",
        type=float,
        default=45.0,
        help="Per-request timeout in seconds (default: 45)",
    )
    p_harvest.add_argument(
        "--fast-ms",
        type=int,
        default=2500,
        help="Median latency below which concurrency may increase (default: 2500)",
    )
    p_harvest.add_argument(
        "--slow-ms",
        type=int,
        default=8000,
        help="Median latency above which concurrency decreases (default: 8000)",
    )
    p_harvest.set_defaults(func=harvest)

    p_status = sub.add_parser("status", help="Show saved harvest status")
    p_status.set_defaults(func=status_command)

    p_verify = sub.add_parser("verify", help="Run strict completeness checks")
    p_verify.set_defaults(func=verify_command)

    p_export = sub.add_parser("export", help="Export tags and synonym maps")
    p_export.add_argument(
        "--out-dir",
        default="tag-gremlin-export",
        help="Output directory (default: ./tag-gremlin-export)",
    )
    p_export.add_argument(
        "--allow-incomplete",
        action="store_true",
        help="Allow partial exports even when strict verification fails",
    )
    p_export.set_defaults(func=export_command)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    if hasattr(args, "initial_workers"):
        if args.initial_workers < 1 or args.max_workers < 1:
            parser.error("worker counts must be >= 1")
        if args.initial_workers > args.max_workers:
            parser.error("--initial-workers cannot exceed --max-workers")
        if args.max_attempts < 1:
            parser.error("--max-attempts must be >= 1")
        if args.timeout <= 0:
            parser.error("--timeout must be > 0")

    try:
        return int(args.func(args))
    except KeyboardInterrupt:
        print("\nInterrupted. Completed pages are already committed; rerun harvest to resume.")
        return 130
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
