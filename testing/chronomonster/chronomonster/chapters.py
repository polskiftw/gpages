from __future__ import annotations

import html
import json
from pathlib import Path

from .timecode import ffmeta_timebase_ms, format_timecode


def chapter_rows(resolved_steps: list[dict], duration_lookup: dict[str, float]) -> list[dict]:
    chapters = []
    cursor = 0.0
    for ordinal, step in enumerate(resolved_steps, 1):
        start = step.get("resolved_start_seconds")
        end = step.get("resolved_end_seconds")
        duration = (end - start) if start is not None and end is not None else duration_lookup.get(step["work_id"])
        if duration is None or duration <= 0:
            raise ValueError(f"No positive duration available for source step {step['watch_step']}")
        title = f"#{ordinal:04d} — {step['parent_title']} — source step {step['watch_step']}"
        chapters.append({
            "active_ordinal": ordinal, "source_step": int(step["watch_step"]), "work_id": step["work_id"],
            "title": title, "start_seconds": cursor, "end_seconds": cursor + duration,
            "source_start_seconds": start, "source_end_seconds": end,
            "source_path": step["media_path"], "era": (step.get("era_labels") or [""])[0],
        })
        cursor += duration
    return chapters


def write_ffmetadata(chapters: list[dict], output: str | Path) -> Path:
    lines = [";FFMETADATA1", "title=MCU: THE MOVIE — ChronoMonster"]
    for chapter in chapters:
        title = chapter["title"].replace("\\", "\\\\").replace("=", "\\=").replace(";", "\\;").replace("#", "\\#")
        lines.extend([
            "", "[CHAPTER]", "TIMEBASE=1/1000", f"START={ffmeta_timebase_ms(chapter['start_seconds'])}",
            f"END={ffmeta_timebase_ms(chapter['end_seconds'])}", f"title={title}",
        ])
    target = Path(output)
    target.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return target


def write_chapter_sidecars(chapters: list[dict], base: str | Path) -> tuple[Path, Path, Path]:
    base = Path(base)
    metadata = write_ffmetadata(chapters, base.with_suffix(".chapters.ffmeta"))
    json_path = base.with_suffix(".chapters.json")
    json_path.write_text(json.dumps(chapters, indent=2, ensure_ascii=False), encoding="utf-8")
    html_path = base.with_suffix(".chapters.html")
    rows = []
    for c in chapters:
        rows.append(
            f"<tr><td>{c['active_ordinal']}</td><td>{format_timecode(c['start_seconds'])}</td>"
            f"<td>{html.escape(c['title'])}</td><td>{html.escape(c['work_id'])}</td>"
            f"<td>{format_timecode(c.get('source_start_seconds'))} → {format_timecode(c.get('source_end_seconds'))}</td></tr>"
        )
    html_path.write_text(
        "<!doctype html><meta charset='utf-8'><title>MCU ChronoMonster chapters</title>"
        "<style>body{font:16px system-ui;background:#111;color:#eee;margin:2rem}table{border-collapse:collapse;width:100%}"
        "th,td{padding:.55rem;border-bottom:1px solid #444;text-align:left}th{position:sticky;top:0;background:#173b2c}"
        "tr:hover{background:#263}</style><h1>MCU: THE MOVIE — Chapter Browser</h1>"
        "<table><thead><tr><th>#</th><th>Monster time</th><th>Chapter</th><th>Work</th><th>Source range</th></tr></thead>"
        f"<tbody>{''.join(rows)}</tbody></table>", encoding="utf-8"
    )
    return metadata, json_path, html_path

