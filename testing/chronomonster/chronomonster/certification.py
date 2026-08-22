from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path

from .calibration import calibration_digest, manual_boundary_key, reference_boundary_key, resolved_step_range
from .chronology import selected_steps
from .project import fingerprint_matches, media_fingerprint


def _subtitle_eligible(step: dict) -> bool:
    values = step.get("research_resolutions", []) + [step.get("source_edition", "")]
    return any("subtitle" in str(value).lower() for value in values)


def boundary_descriptors(project: dict, steps: list[dict], work_id: str | None = None) -> list[dict]:
    active = selected_steps(steps, project.get("scope", "all"), project.get("disabled_steps", []))
    grouped: dict[str, dict] = {}
    for step in active:
        if work_id and step["work_id"] != work_id:
            continue
        if step.get("playback_mode") == "whole_file":
            continue
        start, end = resolved_step_range(project, step)
        for side, reference, local in (
            ("start", step.get("start_seconds"), start),
            ("end", step.get("end_seconds"), end),
        ):
            if local is None:
                continue
            key = (reference_boundary_key(step["work_id"], reference) if reference is not None
                   else manual_boundary_key(step["work_id"], int(step["watch_step"]), side))
            record = grouped.setdefault(key, {
                "key": key, "work_id": step["work_id"], "title": step["parent_title"],
                "reference_seconds": None if reference is None else float(reference),
                "local_seconds": float(local), "watch_steps": [], "sides": [],
                "subtitle_eligible": False,
            })
            record["watch_steps"].append(int(step["watch_step"]))
            record["sides"].append(side)
            record["subtitle_eligible"] = record["subtitle_eligible"] or _subtitle_eligible(step)
    return sorted(grouped.values(), key=lambda x: (x["work_id"], x["local_seconds"], x["key"]))


def intrinsic_reason(boundary: dict, probe: dict | None = None) -> str | None:
    local = float(boundary["local_seconds"])
    if local <= 0.001 and "start" in boundary["sides"]:
        return "file_start"
    duration = float((probe or {}).get("duration") or 0)
    if duration and abs(local - duration) <= 0.250 and "end" in boundary["sides"]:
        return "probed_eof"
    return None


def cached_probe_for_work(project: dict, work_id: str) -> dict:
    media = project.get("work_map", {}).get(work_id)
    if not media:
        return {}
    resolved = str(Path(media).resolve()).lower()
    return next((value for value in project.get("probe_cache", {}).values() if str(value.get("path", "")).lower() == resolved), {})


def certification_valid(project: dict, boundary: dict, probe: dict | None = None) -> tuple[bool, str]:
    probe = probe or cached_probe_for_work(project, boundary["work_id"])
    intrinsic = intrinsic_reason(boundary, probe)
    if intrinsic:
        return True, intrinsic
    certificate = project.get("boundary_certifications", {}).get(boundary["key"])
    if not certificate:
        return False, "unverified"
    if abs(float(certificate.get("local_seconds", -1)) - float(boundary["local_seconds"])) > 0.001:
        return False, "timestamp_changed"
    if certificate.get("calibration_digest") != calibration_digest(project, boundary["work_id"]):
        return False, "calibration_changed"
    media = project.get("work_map", {}).get(boundary["work_id"])
    if not media or not fingerprint_matches(media, certificate.get("media_fingerprint")):
        return False, "media_changed"
    return True, str(certificate.get("method", "verified"))


def certify_boundary(project: dict, boundary: dict, method: str, evidence: dict | None = None, probe: dict | None = None) -> dict:
    if method not in {"human", "human_audit", "subtitle_experimental", "reference_fingerprint"}:
        raise ValueError("Unsupported certification method")
    media = project.get("work_map", {}).get(boundary["work_id"])
    if not media or not Path(media).is_file():
        raise ValueError("Map an existing media file before certifying this boundary")
    certificate = {
        "work_id": boundary["work_id"], "reference_seconds": boundary.get("reference_seconds"),
        "local_seconds": float(boundary["local_seconds"]), "method": method,
        "verified_at": datetime.now(timezone.utc).isoformat(),
        "media_fingerprint": media_fingerprint(media, probe),
        "calibration_digest": calibration_digest(project, boundary["work_id"]),
        "evidence": evidence or {},
    }
    project.setdefault("boundary_certifications", {})[boundary["key"]] = certificate
    return certificate


def certification_summary(project: dict, steps: list[dict], probes: dict[str, dict] | None = None) -> dict:
    probes = dict(probes or {})
    for work_id, media in project.get("work_map", {}).items():
        if work_id in probes or not media:
            continue
        resolved = str(Path(media).resolve()).lower()
        cached = next((value for value in project.get("probe_cache", {}).values() if str(value.get("path", "")).lower() == resolved), None)
        if cached:
            probes[work_id] = cached
    boundaries = boundary_descriptors(project, steps)
    methods: dict[str, int] = {}
    unverified = []
    for boundary in boundaries:
        valid, reason = certification_valid(project, boundary, probes.get(boundary["work_id"]))
        methods[reason] = methods.get(reason, 0) + 1
        if not valid:
            unverified.append(dict(boundary, reason=reason))
    return {
        "required": len(boundaries), "verified": len(boundaries) - len(unverified),
        "unverified": len(unverified), "methods": methods,
        "unverified_boundaries": unverified,
    }


def certify_batch(project: dict, steps: list[dict], keys: list[str], method: str, evidence: dict | None = None, probes: dict[str, dict] | None = None) -> int:
    wanted = set(keys)
    count = 0
    for boundary in boundary_descriptors(project, steps):
        if boundary["key"] in wanted:
            certify_boundary(project, boundary, method, evidence, (probes or {}).get(boundary["work_id"]))
            count += 1
    return count


def certify_audit_batch(project: dict, steps: list[dict], batch: dict) -> tuple[int, int]:
    items = batch.get("items", [])
    if not items:
        raise ValueError("The audit batch predates exact timestamp/file binding; export and watch a new audit playlist")
    current = {boundary["key"]: boundary for boundary in boundary_descriptors(project, steps)}
    accepted = skipped = 0
    for item in items:
        boundary = current.get(item.get("key", ""))
        media = project.get("work_map", {}).get(boundary["work_id"]) if boundary else None
        unchanged = bool(
            boundary and media
            and abs(float(boundary["local_seconds"]) - float(item.get("local_seconds", -1))) <= 0.001
            and fingerprint_matches(media, item.get("media_fingerprint"))
        )
        if not unchanged:
            skipped += 1
            continue
        certify_boundary(project, boundary, "human_audit", {
            "audit_playlist": batch.get("playlist"), "audit_created_at": batch.get("created_at"),
            "audited_local_seconds": item["local_seconds"], "user_attested_complete_review": True,
        })
        accepted += 1
    return accepted, skipped
