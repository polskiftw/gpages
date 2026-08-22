from __future__ import annotations

import re
import subprocess
from pathlib import Path

from .certification import boundary_descriptors
from .media import find_executable, probe_with_cache


TIMING = re.compile(r"(?P<a>\d{1,2}:\d{2}:\d{2}[,.]\d{3})\s+-->\s+(?P<b>\d{1,2}:\d{2}:\d{2}[,.]\d{3})")


def _seconds(value: str) -> float:
    h, m, tail = value.replace(",", ".").split(":")
    return int(h) * 3600 + int(m) * 60 + float(tail)


def parse_srt_intervals(text: str) -> list[tuple[float, float]]:
    intervals = []
    for match in TIMING.finditer(text):
        start, end = _seconds(match.group("a")), _seconds(match.group("b"))
        if end > start:
            intervals.append((start, end))
    return sorted(intervals)


def subtitle_gaps(intervals: list[tuple[float, float]], minimum_seconds: float = 4.0) -> list[dict]:
    if not intervals:
        return []
    merged = []
    for start, end in intervals:
        if merged and start <= merged[-1][1]:
            merged[-1] = (merged[-1][0], max(merged[-1][1], end))
        else:
            merged.append((start, end))
    gaps = []
    for left, right in zip(merged, merged[1:]):
        duration = right[0] - left[1]
        if duration >= minimum_seconds:
            gaps.append({"start": left[1], "end": right[0], "duration": duration, "midpoint": (left[1] + right[0]) / 2})
    return gaps


def extract_srt(media: str | Path, project: dict, work_id: str) -> tuple[str, dict]:
    ffmpeg = find_executable("ffmpeg", project.get("executables", {}).get("ffmpeg", ""))
    if not ffmpeg:
        raise FileNotFoundError("FFmpeg is required for subtitle matching")
    probe, _ = probe_with_cache(media, project.setdefault("probe_cache", {}), project.get("executables", {}).get("ffprobe", ""))
    subtitles = probe.get("subtitles", [])
    language = project.get("preferences", {}).get("audio_language", "eng").lower()
    text_codecs = {"subrip", "srt", "ass", "ssa", "webvtt", "mov_text"}
    candidates = [s for s in subtitles if s.get("codec") in text_codecs]
    preferred = [s for s in candidates if str(s.get("language", "")).lower() == language]
    stream = next((s for s in preferred if s.get("default")), None) or (preferred[0] if preferred else None)
    stream = stream or next((s for s in candidates if s.get("default")), None) or (candidates[0] if candidates else None)
    if not stream:
        external = next((p for p in Path(media).parent.glob(Path(media).stem + "*.srt") if p.is_file()), None)
        if external:
            return external.read_text(encoding="utf-8-sig", errors="replace"), {"source": str(external), "stream_index": None}
        raise ValueError(f"{work_id} has no supported text subtitle stream or adjacent SRT")
    command = [ffmpeg, "-v", "error", "-i", str(media), "-map", f"0:{stream['index']}", "-f", "srt", "-"]
    completed = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if completed.returncode or not completed.stdout.strip():
        raise RuntimeError(completed.stderr.strip() or "Subtitle extraction produced no SRT text")
    return completed.stdout, {"source": str(media), "stream_index": int(stream["index"]), "codec": stream.get("codec"), "language": stream.get("language", "")}


def propose_subtitle_matches(project: dict, steps: list[dict], work_id: str, minimum_gap: float = 4.0) -> dict:
    media = project.get("work_map", {}).get(work_id)
    if not media:
        raise ValueError("Map this work before attempting subtitle matching")
    text, source = extract_srt(media, project, work_id)
    intervals = parse_srt_intervals(text)
    gaps = subtitle_gaps(intervals, minimum_gap)
    proposals = []
    for boundary in boundary_descriptors(project, steps, work_id):
        if not boundary.get("subtitle_eligible"):
            continue
        expected = float(boundary["local_seconds"])
        containing = [g for g in gaps if g["start"] <= expected <= g["end"]]
        if len(containing) == 1:
            gap = containing[0]
            proposals.append({
                "boundary_key": boundary["key"], "expected_seconds": expected,
                "proposed_seconds": gap["midpoint"], "gap_start": gap["start"], "gap_end": gap["end"],
                "gap_duration": gap["duration"], "rule": f"expected timestamp lies inside one subtitle gap >= {minimum_gap:.3f}s",
                "rule_pass": True,
            })
    result = {
        "work_id": work_id, "source": source, "minimum_gap_seconds": float(minimum_gap),
        "cue_count": len(intervals), "gap_count": len(gaps), "eligible_boundaries": sum(b.get("subtitle_eligible", False) for b in boundary_descriptors(project, steps, work_id)),
        "proposals": proposals,
    }
    project.setdefault("subtitle_match_runs", {})[work_id] = result
    project.setdefault("subtitle_proposals", {}).update({p["boundary_key"]: p for p in proposals})
    return result

