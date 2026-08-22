from __future__ import annotations

import hashlib
import json


def calibration_digest(project: dict, work_id: str) -> str:
    payload = {
        "calibration": project.get("edition_calibration", {}).get(work_id, {}),
        "nudges": {
            key: value for key, value in project.get("boundary_nudges", {}).items()
            if key.startswith(f"{work_id}@")
        },
    }
    return hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest()


def reference_boundary_key(work_id: str, seconds: float) -> str:
    return f"{work_id}@ref:{float(seconds):.6f}"


def manual_boundary_key(work_id: str, watch_step: int, side: str) -> str:
    return f"{work_id}@manual:{int(watch_step)}:{side}"


def _anchors(project: dict, work_id: str) -> list[tuple[float, float]]:
    calibration = project.get("edition_calibration", {}).get(work_id, {})
    result = []
    for anchor in calibration.get("anchors", []):
        try:
            result.append((float(anchor["reference_seconds"]), float(anchor["local_seconds"])))
        except (KeyError, TypeError, ValueError):
            continue
    return sorted(set(result))


def transform_time(project: dict, work_id: str, reference_seconds: float | None) -> float | None:
    if reference_seconds is None:
        return None
    value = float(reference_seconds)
    anchors = _anchors(project, work_id)
    if not anchors:
        return value
    if len(anchors) == 1:
        reference, local = anchors[0]
        return value + local - reference
    mode = project.get("edition_calibration", {}).get(work_id, {}).get("mode", "affine")
    if mode != "piecewise":
        first, last = anchors[0], anchors[-1]
        span = last[0] - first[0]
        if abs(span) < 1e-9:
            return value + first[1] - first[0]
        scale = (last[1] - first[1]) / span
        return first[1] + (value - first[0]) * scale
    left, right = anchors[0], anchors[1]
    if value >= anchors[-1][0]:
        left, right = anchors[-2], anchors[-1]
    else:
        for candidate_left, candidate_right in zip(anchors, anchors[1:]):
            if candidate_left[0] <= value <= candidate_right[0]:
                left, right = candidate_left, candidate_right
                break
    span = right[0] - left[0]
    scale = 1.0 if abs(span) < 1e-9 else (right[1] - left[1]) / span
    return left[1] + (value - left[0]) * scale


def resolved_step_range(project: dict, step: dict) -> tuple[float | None, float | None]:
    override = project.get("manual_overrides", {}).get(str(step["watch_step"]), {})
    if "start_seconds" in override or "end_seconds" in override:
        start = override.get("start_seconds", step.get("start_seconds"))
        end = override.get("end_seconds", step.get("end_seconds"))
        return (None if start is None else float(start), None if end is None else float(end))
    work_id = step["work_id"]
    start_ref, end_ref = step.get("start_seconds"), step.get("end_seconds")
    start = transform_time(project, work_id, start_ref)
    end = transform_time(project, work_id, end_ref)
    nudges = project.get("boundary_nudges", {})
    if start_ref is not None:
        start = nudges.get(reference_boundary_key(work_id, start_ref), start)
    if end_ref is not None:
        end = nudges.get(reference_boundary_key(work_id, end_ref), end)
    return (None if start is None else float(start), None if end is None else float(end))


def set_calibration(project: dict, work_id: str, anchors: list[dict], mode: str = "affine") -> None:
    cleaned = sorted(
        ({"reference_seconds": float(a["reference_seconds"]), "local_seconds": float(a["local_seconds"])} for a in anchors),
        key=lambda a: a["reference_seconds"],
    )
    if not cleaned:
        project.setdefault("edition_calibration", {}).pop(work_id, None)
        return
    if mode not in {"affine", "piecewise"}:
        raise ValueError("Calibration mode must be affine or piecewise")
    project.setdefault("edition_calibration", {})[work_id] = {"mode": mode, "anchors": cleaned}

