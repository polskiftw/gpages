from __future__ import annotations

import json
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

from .certification import boundary_descriptors, certification_valid
from .playlist import VLC, XSPF, file_uri
from .project import media_fingerprint
from .timecode import format_timecode


def write_audit_xspf(project: dict, steps: list[dict], output: str | Path, context_seconds: float = 4.0, include_verified: bool = False) -> dict:
    context = max(1.0, float(context_seconds))
    root = ET.Element(f"{{{XSPF}}}playlist", {"version": "1"})
    ET.SubElement(root, f"{{{XSPF}}}title").text = "MCU ChronoMonster exhaustive boundary audit"
    track_list = ET.SubElement(root, f"{{{XSPF}}}trackList")
    included = []
    for boundary in boundary_descriptors(project, steps):
        valid, _ = certification_valid(project, boundary)
        if valid and not include_verified:
            continue
        media = project.get("work_map", {}).get(boundary["work_id"])
        if not media or not Path(media).is_file():
            continue
        local = float(boundary["local_seconds"])
        start, end = max(0.0, local - context), local + context
        track = ET.SubElement(track_list, f"{{{XSPF}}}track")
        ET.SubElement(track, f"{{{XSPF}}}location").text = file_uri(media)
        ET.SubElement(track, f"{{{XSPF}}}title").text = f"{boundary['key']} — {boundary['title']} — boundary {format_timecode(local, True)}"
        ET.SubElement(track, f"{{{XSPF}}}annotation").text = f"Verify both sides of {boundary['key']}; source steps {boundary['watch_steps']}"
        extension = ET.SubElement(track, f"{{{XSPF}}}extension", {"application": "http://www.videolan.org/vlc/playlist/0"})
        ET.SubElement(extension, f"{{{VLC}}}id").text = str(len(included))
        ET.SubElement(extension, f"{{{VLC}}}option").text = f"start-time={start:g}"
        ET.SubElement(extension, f"{{{VLC}}}option").text = f"stop-time={end:g}"
        included.append(dict(boundary, preview_start=start, preview_end=end))
    target = Path(output)
    target.parent.mkdir(parents=True, exist_ok=True)
    ET.ElementTree(root).write(target, encoding="utf-8", xml_declaration=True)
    report = {
        "format": "chronomonster-boundary-audit-v1", "created_at": datetime.now(timezone.utc).isoformat(),
        "playlist": str(target.resolve()), "context_seconds_each_side": context,
        "boundary_count": len(included), "boundaries": included,
    }
    report_path = target.with_suffix(".audit.json")
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    project["last_boundary_audit"] = {
        "playlist": str(target.resolve()), "report": str(report_path.resolve()), "created_at": report["created_at"],
        "items": [
            {"key": b["key"], "local_seconds": b["local_seconds"], "media_fingerprint": media_fingerprint(project["work_map"][b["work_id"]])}
            for b in included
        ],
    }
    return report
