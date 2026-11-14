# gpages

Small static browser tools hosted with GitHub Pages.

## Structure

- `/index.html` — landing page
- `/maze-simulator/` — mobile-friendly reproducible multi-floor mathematical maze previews
- `/terraria-shopping/` — Terraria Shopping acquisition planner
- `/boolean-search/` — Boolean Search Builder
- `/bubblewrap-launcher/` — client-side Bubblewrap sandbox `start-isolated.sh` generator (mounts, tools, GPU/CUDA, network)
- `/boink-local/` — manually-run local Reddit media archiver with SQLite/content dedupe
- `/tag-harvester/` — legacy browser/bookmarklet Tag Gremlin
- `/tag-gremlin/` — desktop Tag Gremlin CLI, SQLite harvester, and synonym-map exporter

Each browser tool should live in its own folder with its own `index.html`; non-browser utilities should likewise stay self-contained in their own folders.

## Licensing

Original gpages content is licensed under **Apache License 2.0** by default; see [LICENSE](LICENSE). Self-contained subprojects and third-party components can have their own licensing requirements. In particular, [`valheim-hammer-everything/`](valheim-hammer-everything/) remains **GPL-3.0-only** because it incorporates GPLv3 upstream material; [`valheim-jotunn-compat/`](valheim-jotunn-compat/) uses Apache-2.0 for its original code while preserving Jotunn's upstream MIT notice; and [`gloader-windows/`](gloader-windows/) uses Apache-2.0 for its original code while preserving its third-party notices and special Re-Logic grant. No upstream or third-party rights are replaced by the repository default license.
