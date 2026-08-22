from __future__ import annotations

import csv
import hashlib
import json
from dataclasses import dataclass
from importlib.resources import files
from pathlib import Path
from typing import Iterable


DATA = files("chronomonster").joinpath("data")


def default_steps_path() -> Path:
    return Path(str(DATA.joinpath("watch_steps_continuous.json")))


def default_catalog_path() -> Path:
    return Path(str(DATA.joinpath("work_catalog.csv")))


def load_steps(path: str | Path | None = None) -> list[dict]:
    target = Path(path) if path else default_steps_path()
    result = json.loads(target.read_text(encoding="utf-8"))
    if not isinstance(result, list):
        raise ValueError("Chronology JSON must contain an array")
    return result


def load_catalog(path: str | Path | None = None) -> list[dict]:
    target = Path(path) if path else default_catalog_path()
    with target.open(encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def chronology_hash(path: str | Path | None = None) -> str:
    target = Path(path) if path else default_steps_path()
    return hashlib.sha256(target.read_bytes()).hexdigest()


def selected_steps(
    steps: Iterable[dict],
    scope: str = "all",
    disabled_steps: Iterable[int | str] = (),
) -> list[dict]:
    disabled = {int(x) for x in disabled_steps}
    allowed = {
        "all": {"Core", "Completionist", "Optional"},
        "core": {"Core"},
        "core_completionist": {"Core", "Completionist"},
    }.get(scope, {"Core", "Completionist", "Optional"})
    return [
        step
        for step in steps
        if int(step["watch_step"]) not in disabled
        and step.get("recommended_scope", "Core") in allowed
    ]


def step_range(step: dict, overrides: dict[str, dict] | None = None) -> tuple[float | None, float | None]:
    override = (overrides or {}).get(str(step["watch_step"]), {})
    start = override.get("start_seconds", step.get("start_seconds"))
    end = override.get("end_seconds", step.get("end_seconds"))
    return (None if start is None else float(start), None if end is None else float(end))


def step_duration(step: dict, overrides: dict[str, dict] | None = None, media_duration: float | None = None) -> float | None:
    start, end = step_range(step, overrides)
    if start is not None and end is not None:
        return max(0.0, end - start)
    if step.get("playback_mode") == "whole_file" and media_duration is not None:
        return max(0.0, media_duration)
    return None


def assert_default_invariants(steps: list[dict], catalog: list[dict]) -> None:
    summary = json.loads(Path(str(DATA.joinpath("derived_summary.json"))).read_text(encoding="utf-8"))
    assert summary["source_fragment_rows"] == 5456
    assert summary["continuous_watch_steps"] == 2622
    assert summary["redundant_contiguous_fragments_collapsed"] == 2834
    assert len(steps) == 2622, f"expected 2622 playback steps, found {len(steps)}"
    assert len(catalog) == 663, f"expected 663 works, found {len(catalog)}"
    assert sum(s["playback_mode"] == "manual_boundary_required" for s in steps) == 9
    assert sum(s["playback_mode"] == "whole_file" for s in steps) == 45
    wakanda = next(s for s in steps if s["work_id"] == "W0001")
    assert (wakanda["source_start_tc"], wakanda["source_end_tc"]) == ("00:00:00", "00:27:20")
    assert (wakanda["strict_rank_start"], wakanda["strict_rank_end"], wakanda["collapsed_fragment_count"]) == (8, 19, 12)


def merge_continuous_rows(rows: Iterable[dict]) -> list[list[dict]]:
    """The exact source-to-playback merge rule, retained for audits/tests."""
    groups: list[list[dict]] = []
    for row in rows:
        if groups:
            previous = groups[-1][-1]
            if (
                row["Work ID"] == previous["Work ID"]
                and previous.get("Source End TC")
                and row.get("Source Start TC")
                and previous["Source End TC"].strip() == row["Source Start TC"].strip()
            ):
                groups[-1].append(row)
                continue
        groups.append([row])
    return groups
