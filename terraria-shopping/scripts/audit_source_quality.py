#!/usr/bin/env python3
"""Audit acquisition badges for sources likely to displace ordinary acquisition.

Terraria 1.4.5 added many resource-carrying slime variants. Drops Cargo is useful
for discovering those alternatives, but a new mob drop must not silently replace
a more ordinary terrain/harvest/vendor source in the shopping UI. Shimmer can
cause the same presentation problem when a transmutation exists for an otherwise
ordinary world material. This audit keeps both families visible in refresh logs.
"""
from __future__ import annotations

import json
import pathlib

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
    slime_rows: list[tuple[str, str]] = []
    shimmer_rows: list[tuple[str, str]] = []
    for name, row in rows.items():
        source = str(row[2] if isinstance(row, list) and len(row) > 2 else "")
        if "Slime" in source:
            slime_rows.append((name, source))
        if source.startswith("Shimmer transmutation:"):
            shimmer_rows.append((name, source))
    report("Slime-backed canonical source badges", slime_rows)
    report("Shimmer-primary canonical source badges", shimmer_rows)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
