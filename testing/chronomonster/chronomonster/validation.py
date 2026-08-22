from __future__ import annotations

from collections import Counter, defaultdict
from dataclasses import dataclass, asdict
from pathlib import Path

from .chronology import selected_steps, step_range
from .media import probe_with_cache
from .project import fingerprint_matches


@dataclass
class Issue:
    severity: str
    code: str
    message: str
    work_id: str = ""
    watch_step: int | None = None


def validate_project(project: dict, steps: list[dict], catalog: list[dict], probe: bool = True) -> dict:
    active = selected_steps(steps, project.get("scope", "all"), project.get("disabled_steps", []))
    mapping = project.get("work_map", {})
    overrides = project.get("manual_overrides", {})
    cache = project.setdefault("probe_cache", {})
    ffprobe = project.get("executables", {}).get("ffprobe", "")
    issues: list[Issue] = []
    probes: dict[str, dict] = {}
    needed_work_ids = {s["work_id"] for s in active}
    for work_id in sorted(needed_work_ids):
        path = mapping.get(work_id)
        if not path:
            issues.append(Issue("red", "missing_media", "No media file is mapped", work_id))
            continue
        if not Path(path).is_file():
            issues.append(Issue("red", "missing_file", f"Mapped file does not exist: {path}", work_id))
            continue
        if probe:
            try:
                media_probe, _ = probe_with_cache(path, cache, ffprobe)
                probes[work_id] = media_probe
                if not media_probe.get("video"):
                    issues.append(Issue("red", "no_video", "Mapped media has no video stream", work_id))
                if not media_probe.get("audio"):
                    issues.append(Issue("yellow", "no_audio", "Mapped media has no audio stream; monster mode requires one", work_id))
            except Exception as exc:
                issues.append(Issue("red", "probe_failed", str(exc), work_id))
    for step in active:
        work_id = step["work_id"]
        start, end = step_range(step, overrides)
        override = overrides.get(str(step["watch_step"]), {})
        mapped_path = mapping.get(work_id)
        if override and override.get("media_fingerprint") and mapped_path and not fingerprint_matches(mapped_path, override["media_fingerprint"]):
            issues.append(Issue("red", "boundary_reverify", "Saved manual boundary belongs to a different file identity; reverify it", work_id, int(step["watch_step"])))
        if step["playback_mode"] == "manual_boundary_required" and (start is None or end is None):
            issues.append(Issue("red", "manual_boundary", "Manual scene start/end must be resolved or this step disabled", work_id, int(step["watch_step"])))
            continue
        if (start is None) != (end is None):
            issues.append(Issue("red", "partial_range", "Range has only one endpoint", work_id, int(step["watch_step"])))
        if start is not None and end is not None and start >= end:
            issues.append(Issue("red", "invalid_range", "Start must be earlier than end", work_id, int(step["watch_step"])))
        duration = probes.get(work_id, {}).get("duration")
        if duration and end is not None and end > duration + 1.0:
            issues.append(Issue("red", "outpoint_past_eof", f"Required outpoint {end:.3f}s exceeds file duration {duration:.3f}s", work_id, int(step["watch_step"])))
    reverse: dict[str, list[str]] = defaultdict(list)
    for work_id, path in mapping.items():
        if work_id in needed_work_ids and path:
            reverse[str(Path(path)).lower()].append(work_id)
    for path, ids in reverse.items():
        if len(ids) > 1:
            issues.append(Issue("yellow", "duplicate_mapping", f"One file is mapped to multiple works: {', '.join(ids)} ({path})"))
    counts = Counter(issue.severity for issue in issues)
    return {
        "certified": counts["red"] == 0,
        "active_steps": len(active),
        "active_works": len(needed_work_ids),
        "issues": [asdict(i) for i in issues],
        "counts": {"red": counts["red"], "yellow": counts["yellow"], "green": max(0, len(needed_work_ids) - counts["red"] - counts["yellow"])},
        "probes": probes,
    }
