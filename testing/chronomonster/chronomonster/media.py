from __future__ import annotations

import json
import re
import shutil
import subprocess
import unicodedata
from dataclasses import dataclass, asdict
from difflib import SequenceMatcher
from pathlib import Path
from typing import Callable, Iterable


VIDEO_EXTENSIONS = {
    ".mkv", ".mp4", ".m4v", ".mov", ".avi", ".webm", ".ts", ".m2ts",
    ".mpg", ".mpeg", ".wmv", ".flv", ".vob", ".ogv",
}

NOISE = {
    "1080p", "2160p", "720p", "480p", "bluray", "brrip", "webrip", "webdl",
    "web", "dl", "hdr", "hdr10", "dv", "dolby", "vision", "x264", "x265",
    "h264", "h265", "hevc", "av1", "aac", "ac3", "eac3", "dts", "atmos",
    "remux", "proper", "repack", "extended", "uhd", "multi", "dual", "audio",
}


def find_executable(name: str, configured: str = "") -> str | None:
    if configured and Path(configured).is_file():
        return configured
    found = shutil.which(name)
    if found:
        return found
    if name in {"ffmpeg", "ffprobe"}:
        candidates = [
            Path("C:/ffmpeg/bin") / f"{name}.exe",
            Path("C:/Program Files/ffmpeg/bin") / f"{name}.exe",
        ]
    elif name == "vlc":
        candidates = [Path("C:/Program Files/VideoLAN/VLC/vlc.exe"), Path("C:/Program Files (x86)/VideoLAN/VLC/vlc.exe")]
    else:
        candidates = []
    return next((str(p) for p in candidates if p.is_file()), None)


def scan_media(roots: Iterable[str | Path]) -> list[Path]:
    found: dict[str, Path] = {}
    for root in roots:
        path = Path(root)
        if path.is_file() and path.suffix.lower() in VIDEO_EXTENSIONS:
            found[str(path.resolve()).lower()] = path.resolve()
        elif path.is_dir():
            for candidate in path.rglob("*"):
                if candidate.is_file() and candidate.suffix.lower() in VIDEO_EXTENSIONS:
                    found[str(candidate.resolve()).lower()] = candidate.resolve()
    return sorted(found.values(), key=lambda p: str(p).lower())


def normalize_title(value: str) -> str:
    value = unicodedata.normalize("NFKD", value).encode("ascii", "ignore").decode("ascii")
    value = value.lower().replace("&", " and ")
    value = re.sub(r"[\[\(\{].*?[\]\)\}]", " ", value)
    value = re.sub(r"s(\d{1,2})[ ._-]*e(\d{1,3})", r" s\1e\2 ", value)
    value = re.sub(r"(\d{1,2})x(\d{1,3})", r" s\1e\2 ", value)
    tokens = re.findall(r"[a-z0-9]+", value)
    tokens = [t for t in tokens if t not in NOISE and not re.fullmatch(r"\d{3,4}p", t)]
    return " ".join(tokens)


def episode_codes(work: dict) -> set[str]:
    season = str(work.get("season", "")).strip()
    episode = str(work.get("episode", "")).strip()
    if not season or not episode or not season.isdigit() or not episode.isdigit():
        return set()
    s, e = int(season), int(episode)
    return {f"s{s:02d}e{e:02d}", f"s{s}e{e}", f"{s}x{e:02d}", f"{s}x{e}"}


def score_candidate(work: dict, path: str | Path, duration: float | None = None) -> float:
    stem = normalize_title(Path(path).stem)
    title = normalize_title(work.get("watch_item") or work.get("parent_title") or "")
    series = normalize_title(work.get("series_film") or "")
    title_score = SequenceMatcher(None, title, stem).ratio()
    series_score = SequenceMatcher(None, series, stem).ratio() if series else 0.0
    score = max(title_score, series_score * 0.92)
    codes = episode_codes(work)
    if codes:
        raw = re.sub(r"[^a-z0-9]", "", Path(path).stem.lower())
        if any(code.replace("x", "x") in raw for code in codes):
            score = max(score, 0.74) + 0.18
        else:
            score -= 0.12
    required = float(work.get("required_min_duration_seconds") or 0)
    if duration is not None and required:
        if duration + 2 < required:
            score -= min(0.4, 0.15 + (required - duration) / max(required, 1))
        else:
            score += 0.03
    return max(0.0, min(1.0, score))


def match_catalog(catalog: list[dict], media: list[Path], probes: dict[str, dict] | None = None) -> dict[str, dict]:
    probes = probes or {}
    result: dict[str, dict] = {}
    for work in catalog:
        scored = []
        for path in media:
            probe = probes.get(str(path.resolve()), {})
            score = score_candidate(work, path, probe.get("duration"))
            if score >= 0.30:
                scored.append({"path": str(path), "score": round(score, 4)})
        scored.sort(key=lambda x: (-x["score"], x["path"].lower()))
        best = scored[0]["score"] if scored else 0.0
        margin = best - (scored[1]["score"] if len(scored) > 1 else 0.0)
        status = "green" if best >= 0.76 and margin >= 0.06 else "yellow" if best >= 0.48 else "red"
        result[work["work_id"]] = {"status": status, "candidates": scored[:8], "margin": round(margin, 4)}
    return result


def _probe_key(path: Path) -> str:
    stat = path.stat()
    return f"{path.resolve()}|{stat.st_size}|{stat.st_mtime_ns}"


def probe_media(path: str | Path, ffprobe: str | None = None) -> dict:
    target = Path(path)
    executable = find_executable("ffprobe", ffprobe or "")
    if not executable:
        raise FileNotFoundError("ffprobe was not found. Install FFmpeg or select ffprobe.exe.")
    command = [
        executable, "-v", "error", "-show_format", "-show_streams", "-show_chapters",
        "-of", "json", str(target),
    ]
    completed = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if completed.returncode:
        raise RuntimeError(completed.stderr.strip() or f"ffprobe failed for {target}")
    raw = json.loads(completed.stdout)
    streams = raw.get("streams", [])
    fmt = raw.get("format", {})
    video = []
    audio = []
    subtitles = []
    for stream in streams:
        compact = {
            "index": stream.get("index"),
            "codec": stream.get("codec_name"),
            "language": (stream.get("tags") or {}).get("language", ""),
            "default": bool((stream.get("disposition") or {}).get("default")),
        }
        if stream.get("codec_type") == "video":
            compact.update({
                "width": stream.get("width"), "height": stream.get("height"),
                "pix_fmt": stream.get("pix_fmt"), "frame_rate": stream.get("avg_frame_rate"),
            })
            video.append(compact)
        elif stream.get("codec_type") == "audio":
            compact.update({
                "channels": stream.get("channels"), "channel_layout": stream.get("channel_layout"),
                "sample_rate": stream.get("sample_rate"),
            })
            audio.append(compact)
        elif stream.get("codec_type") == "subtitle":
            subtitles.append(compact)
    return {
        "path": str(target.resolve()),
        "cache_key": _probe_key(target),
        "size": target.stat().st_size,
        "mtime_ns": target.stat().st_mtime_ns,
        "duration": float(fmt.get("duration") or 0),
        "format": fmt.get("format_name", ""),
        "video": video,
        "audio": audio,
        "subtitles": subtitles,
        "chapters": len(raw.get("chapters", [])),
    }


def probe_with_cache(path: str | Path, cache: dict, ffprobe: str | None = None) -> tuple[dict, bool]:
    target = Path(path)
    key = _probe_key(target)
    cached = cache.get(key)
    if cached:
        return cached, True
    probed = probe_media(target, ffprobe)
    cache[key] = probed
    return probed, False


def probe_paths(
    paths: Iterable[str | Path],
    cache: dict,
    ffprobe: str | None = None,
    progress: Callable[[int, int, str], None] | None = None,
) -> dict[str, dict]:
    paths = list(paths)
    output: dict[str, dict] = {}
    for index, path in enumerate(paths, 1):
        try:
            probe, _ = probe_with_cache(path, cache, ffprobe)
            output[str(Path(path).resolve())] = probe
        except Exception as exc:
            output[str(Path(path).resolve())] = {"path": str(path), "error": str(exc), "duration": 0, "video": [], "audio": []}
        if progress:
            progress(index, len(paths), str(path))
    return output


def choose_audio_stream(probe: dict, language: str = "eng", preference: str = "default") -> int | None:
    streams = probe.get("audio", [])
    if not streams:
        return None
    language = language.lower()
    same_language = [s for s in streams if str(s.get("language", "")).lower() == language]
    candidates = same_language or streams
    if preference == "default":
        default = next((s for s in candidates if s.get("default")), None)
        if default:
            return int(default["index"])
    return int(candidates[0]["index"])

