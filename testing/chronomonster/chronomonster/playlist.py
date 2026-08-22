from __future__ import annotations

import csv
import json
import re
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import quote

from .calibration import resolved_step_range
from .certification import certification_summary
from .chronology import selected_steps
from .project import fingerprint_matches, mapping_snapshot
from .timecode import format_timecode


XSPF = "http://xspf.org/ns/0/"
VLC = "http://www.videolan.org/vlc/playlist/ns/0/"
ET.register_namespace("", XSPF)
ET.register_namespace("vlc", VLC)


def file_uri(path: str | Path) -> str:
    raw = str(path)
    if re.match(r"^[A-Za-z]:[\\/]", raw):
        return "file:///" + quote(raw.replace("\\", "/"), safe="/:~!$&'()*+,;=@")
    if raw.startswith("\\\\"):
        unc = raw.lstrip("\\").replace("\\", "/")
        host, _, rest = unc.partition("/")
        return "file://" + host + "/" + quote(rest, safe="/~!$&'()*+,;=@")
    return Path(raw).resolve().as_uri()


def active_resolved_steps(project: dict, steps: list[dict], allow_unresolved: bool = False) -> list[dict]:
    active = selected_steps(steps, project.get("scope", "all"), project.get("disabled_steps", []))
    mapping = project.get("work_map", {})
    overrides = project.get("manual_overrides", {})
    output = []
    missing = []
    unresolved = []
    for step in active:
        media = mapping.get(step["work_id"])
        if not media:
            missing.append(f"{step['work_id']} ({step['parent_title']})")
            continue
        start, end = resolved_step_range(project, step)
        override = overrides.get(str(step["watch_step"]), {})
        if override and override.get("media_fingerprint") and not fingerprint_matches(media, override["media_fingerprint"]):
            unresolved.append(f"{step['watch_step']} (mapped edition changed; reverify)")
            continue
        if step["playback_mode"] == "manual_boundary_required" and (start is None or end is None):
            unresolved.append(str(step["watch_step"]))
            continue
        item = dict(step)
        item["media_path"] = media
        item["resolved_start_seconds"] = start
        item["resolved_end_seconds"] = end
        output.append(item)
    if not allow_unresolved:
        errors = []
        if missing:
            errors.append(f"missing mappings for {len(set(missing))} active works")
        if unresolved:
            errors.append(f"unresolved manual boundaries at source steps {', '.join(unresolved)}")
        if errors:
            raise ValueError("; ".join(errors))
        if project.get("preferences", {}).get("strict_boundary_certification", True):
            certification = certification_summary(project, steps)
            if certification["unverified"]:
                raise ValueError(f"{certification['unverified']} unique boundaries remain unverified; complete boundary certification or explicitly disable strict certification")
    return output


def _friendly_title(active_ordinal: int, step: dict) -> str:
    title = f"#{active_ordinal:04d} — {step['parent_title']}"
    start = step.get("resolved_start_seconds")
    end = step.get("resolved_end_seconds")
    if start is None and end is None:
        title += " — WHOLE FILE"
    else:
        title += f" — {format_timecode(start)} → {format_timecode(end)}"
    return title + f" [source step {step['watch_step']}]"


def write_xspf(project: dict, steps: list[dict], output: str | Path, allow_unresolved: bool = False, write_sidecars: bool = True) -> dict:
    resolved = active_resolved_steps(project, steps, allow_unresolved)
    root = ET.Element(f"{{{XSPF}}}playlist", {"version": "1"})
    ET.SubElement(root, f"{{{XSPF}}}title").text = "MCU ChronoMonster"
    track_list = ET.SubElement(root, f"{{{XSPF}}}trackList")
    for ordinal, step in enumerate(resolved, 1):
        track = ET.SubElement(track_list, f"{{{XSPF}}}track")
        ET.SubElement(track, f"{{{XSPF}}}location").text = file_uri(step["media_path"])
        ET.SubElement(track, f"{{{XSPF}}}title").text = _friendly_title(ordinal, step)
        annotation = f"Work {step['work_id']}; source step {step['watch_step']}; strict ranks {step['strict_rank_start']}–{step['strict_rank_end']}"
        ET.SubElement(track, f"{{{XSPF}}}annotation").text = annotation
        extension = ET.SubElement(track, f"{{{XSPF}}}extension", {"application": "http://www.videolan.org/vlc/playlist/0"})
        ET.SubElement(extension, f"{{{VLC}}}id").text = str(ordinal - 1)
        if step["resolved_start_seconds"] is not None:
            ET.SubElement(extension, f"{{{VLC}}}option").text = f"start-time={step['resolved_start_seconds']:g}"
        if step["resolved_end_seconds"] is not None:
            ET.SubElement(extension, f"{{{VLC}}}option").text = f"stop-time={step['resolved_end_seconds']:g}"
    target = Path(output)
    target.parent.mkdir(parents=True, exist_ok=True)
    ET.ElementTree(root).write(target, encoding="utf-8", xml_declaration=True)
    report = {
        "format": "chronomonster-vlc-build-report-v1",
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "certified": not allow_unresolved,
        "entry_count": len(resolved),
        "source_step_count": len(steps),
        "chronology_sha256": project["chronology"]["sha256"],
        "scope": project.get("scope", "all"),
        "disabled_steps": project.get("disabled_steps", []),
        "playlist": str(target.resolve()),
    }
    if write_sidecars:
        base = target.with_suffix("")
        Path(str(base) + ".build-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        Path(str(base) + ".mapping.json").write_text(json.dumps(mapping_snapshot(project), indent=2), encoding="utf-8")
        Path(str(base) + ".txt").write_text("\n".join(_friendly_title(i, s) for i, s in enumerate(resolved, 1)) + "\n", encoding="utf-8")
    return report


def _ffconcat_escape(path: str | Path) -> str:
    return str(path).replace("\\", "/").replace("'", "'\\''")


def write_ffconcat(project: dict, steps: list[dict], output: str | Path, allow_unresolved: bool = False) -> int:
    resolved = active_resolved_steps(project, steps, allow_unresolved)
    lines = ["ffconcat version 1.0"]
    for step in resolved:
        lines.extend(["", f"file '{_ffconcat_escape(step['media_path'])}'"])
        if step["resolved_start_seconds"] is not None:
            lines.append(f"inpoint {step['resolved_start_seconds']:g}")
        if step["resolved_end_seconds"] is not None:
            lines.append(f"outpoint {step['resolved_end_seconds']:g}")
    target = Path(output)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return len(resolved)


def write_companion_csv(project: dict, steps: list[dict], output: str | Path, allow_unresolved: bool = True) -> int:
    resolved = active_resolved_steps(project, steps, allow_unresolved)
    target = Path(output)
    target.parent.mkdir(parents=True, exist_ok=True)
    fields = ["active_ordinal", "source_step", "work_id", "title", "mode", "start", "end", "media"]
    with target.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for ordinal, step in enumerate(resolved, 1):
            writer.writerow({
                "active_ordinal": ordinal, "source_step": step["watch_step"], "work_id": step["work_id"],
                "title": step["parent_title"], "mode": step["playback_mode"],
                "start": format_timecode(step["resolved_start_seconds"], True),
                "end": format_timecode(step["resolved_end_seconds"], True), "media": step["media_path"],
            })
    return len(resolved)
