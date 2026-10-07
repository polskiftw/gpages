# gpages

Small static browser tools hosted with GitHub Pages.

## Structure

- `/index.html` — landing page
- `/terraria-shopping/` — Terraria Shopping acquisition planner
- `/boolean-search/` — Boolean Search Builder
- `/boink-local/` — manually-run local Reddit media archiver with SQLite/content dedupe
- `/tag-harvester/` — legacy browser/bookmarklet Tag Gremlin
- `/tag-gremlin/` — desktop Tag Gremlin CLI, SQLite harvester, and synonym-map exporter

Each browser tool should live in its own folder with its own `index.html`; non-browser utilities should likewise stay self-contained in their own folders.
