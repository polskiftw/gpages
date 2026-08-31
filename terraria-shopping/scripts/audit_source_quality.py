#!/usr/bin/env python3
"""Audit acquisition badges for sources likely to displace ordinary acquisition.

Terraria 1.4.5 added many resource-carrying slime variants. Drops Cargo is useful
for discovering those alternatives, but a new mob drop must not silently replace
a more ordinary terrain/harvest/vendor source in the shopping UI. This audit
prints every canonical leaf whose final badge contains a slime source so changes
in that family are visible during the refresh workflow.
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


def main() -> int:
    rows = load()
    slime_rows: list[tuple[str, str]] = []
    for name, row in rows.items():
        source = str(row[2] if isinstance(row, list) and len(row) > 2 else "")
        if "Slime" in source:
            slime_rows.append((name, source))
    slime_rows.sort(key=lambda pair: pair[0].casefold())
    print(f"Slime-backed canonical source badges: {len(slime_rows)}")
    for name, source in slime_rows:
        print(f"  {name}: {source}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
