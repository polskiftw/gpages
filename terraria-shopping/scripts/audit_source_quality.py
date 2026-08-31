#!/usr/bin/env python3
"""Audit acquisition-source priority for canonical Terraria shopping leaves.

Drops Cargo and Itemsource can surface valid but secondary methods before the
ordinary way a player gets a material. Terraria 1.4.5 resource slimes and Shimmer
routes make this especially visible. Direct-source priorities are therefore a
published invariant, while remaining slime/Shimmer-backed rows are logged for
future review.
"""
from __future__ import annotations

import json
import pathlib

from apply_source_priorities import DIRECT_SOURCE_PRIORITIES

ROOT = pathlib.Path(__file__).resolve().parents[1]
AVAILABILITY = ROOT / "availability.generated.js"
PREFIX = "window.TERRARIA_GENERATED_AVAILABILITY="


def load() -> dict[str, list[object]]:
    text = AVAILABILITY.read_text(encoding="utf-8").strip()
    if not text.startswith(PREFIX) or not text.endswith(";"):
        raise RuntimeError("availability.generated.js has an unexpected wrapper")
    payload = json.loads(text[len(PREFIX):-1])
    if not isinstance(payload, dict):
        raise RuntimeError("availability payload is not an object")
    return payload


def report(label: str, rows: list[tuple[str, str]]) -> None:
    rows.sort(key=lambda pair: pair[0].casefold())
    print(f"{label}: {len(rows)}")
    for name, source in rows:
        print(f"  {name}: {source}")


def main() -> int:
    rows = load()

    wrong_priorities: list[tuple[str, str, str]] = []
    for name, expected in DIRECT_SOURCE_PRIORITIES.items():
        row = rows.get(name)
        actual = str(row[2] if isinstance(row, list) and len(row) > 2 else "")
        if actual != expected:
            wrong_priorities.append((name, expected, actual))
    if wrong_priorities:
        print("Direct acquisition source priorities regressed:")
        for name, expected, actual in wrong_priorities:
            print(f"  {name}: expected {expected!r}; got {actual!r}")
        raise RuntimeError(f"{len(wrong_priorities)} direct acquisition source priorities regressed")
    print(f"Direct acquisition source priorities clean: {len(DIRECT_SOURCE_PRIORITIES)} canonical leaves")

    slime_rows: list[tuple[str, str]] = []
    shimmer_rows: list[tuple[str, str]] = []
    for name, row in rows.items():
        source = str(row[2] if isinstance(row, list) and len(row) > 2 else "")
        if "Slime" in source:
            slime_rows.append((name, source))
        if source.startswith("Shimmer transmutation:"):
            shimmer_rows.append((name, source))
    report("Remaining slime-backed canonical source badges", slime_rows)
    report("Remaining Shimmer-primary canonical source badges", shimmer_rows)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
