# Tag Gremlin Desktop - Design and Development Guide

## Purpose

Tag Gremlin Desktop is the canonical local harvester for the site's tag index.

The old browser/autocomplete Gremlin remains historical reference material. The desktop implementation does not depend on bookmarklets, DOM automation, simulated typing, autocomplete-frontier exploration, or the old v7/v8/v9 state.

The primary source is the directly pageable tag index:

- page 1: `tags.php`
- page N: `tags.php?page=N`
- the page reports the authoritative total number of official tags
- each result page contains the official tag rows and adjacent hidden synonym-detail rows

The target site hostname is intentionally not hard-coded. The user supplies the complete `tags.php` URL at runtime.

## Invariants

A harvest is COMPLETE only when all of the following are true:

1. the site-reported tag total was discovered;
2. the final page number was discovered;
3. every page from 1 through the final page has status `ok`;
4. there are no failed or unresolved pages;
5. the sum of parsed tag rows equals the reported total;
6. the number of unique tag IDs equals the reported total;
7. every tag's parsed synonym count equals its reported synonym count.

TagID is the identity invariant. Official names are data, not identity; a rename updates the row for that TagID rather than creating a new row.

Anything else is incomplete. The CLI must say so explicitly.

Do not silently discard malformed rows, duplicate TagIDs encountered across fetched pages, synonym mismatches, HTTP errors, or authentication failures. The SQLite primary key makes duplicate stored TagID rows impossible; overlapping TagIDs discovered during a crawl still make completeness verification fail because the unique TagID count cannot match the sum of page rows.

## Authentication and privacy

Tag Gremlin reads the user's Firefox `cookies.sqlite` directly and loads only cookies eligible for the target host into an in-memory CookieJar.

Rules:

- cookie values are never printed;
- cookie values are never written to the Tag Gremlin SQLite database;
- no cookie-export file is created;
- the target hostname is not committed to this repository;
- the user may override Firefox profile discovery with `--firefox-profile`.

## Network behavior

The crawler is resumable and uses a fixed continuous worker pool.

- the user chooses a fixed worker count, for example `-8` / `--workers 8`;
- all workers draw from one central pending-page iterator, so two workers cannot be assigned the same page;
- when one worker finishes a page it immediately receives the next unused page;
- retries/backoff are local to the worker/request that hit a timeout, HTTP 429, or transient failure;
- other workers keep moving and the global worker count does not automatically ratchet downward;
- honor `Retry-After` on HTTP 429 when present;
- retry transient failures with bounded exponential backoff plus jitter;
- within an incomplete crawl, pages already committed with status `ok` are skipped;
- after a long crawl, page 1 is rechecked so a source-total increase during the run can be reconciled without rescanning already-completed pages; if the growth created new page numbers, those pages remain pending for the next resume.

Local CPU and RAM may be used freely for parsing, validation, indexing, and exports.

## Database

The SQLite database is the canonical lossless local result.

### `meta`

Key/value metadata including schema version, source URL, reported total, final page, rows-per-page baseline, crawl generation, and timestamps.

### `pages`

One row per page for the current crawl generation:

- page number
- status
- attempts
- last HTTP status
- latency
- parsed tag count
- parsed synonym count
- response SHA-256
- fetched timestamp
- error text

The pages table is progress bookkeeping only. It does not own tags.

### `tags`

One row per official TagID:

- TagID (primary key)
- official name
- uses
- positive votes
- negative votes
- reported synonym count
- raw synonym-detail text
- synonym parse status
- last-seen crawl generation

Tags are never owned by the page where they were discovered. A tag moving from page 32 to page 47 still updates the same TagID row.

The `tag_id INTEGER PRIMARY KEY` constraint makes duplicate stored TagID rows impossible. Refreshes use an UPSERT keyed only by TagID, then replace that TagID's synonym rows with the newly observed mapping.

### `tag_synonyms`

Lossless mapping from official TagID to each synonym string observed in that tag's hidden detail row.

Each row stores only:

- the owning official TagID;
- the synonym/alias string exactly as normalized by the parser.

On this site, these synonym values are alternate spellings, typos, or aliases for the owning official tag. They intentionally do not have their own TagIDs. A row such as `1234 -> "tagg6"` means that alias points back to official TagID 1234; it does not imply another official tag entity.

## Refresh generations

A completed database can be refreshed in place by running the normal harvest command again.

- If the current generation is incomplete, the command resumes it and skips its already-OK pages.
- If the current generation is COMPLETE, the next harvest automatically starts a new generation and re-fetches every current page.
- Existing tag rows remain in place while the refresh runs.
- Each observed tag is UPSERTed by TagID and marked with the new generation.
- Each observed source TagID's raw synonym rows are replaced with the newly observed set.
- After the new generation passes strict completeness verification, tags not seen in that generation are removed. This handles source-side deletions without tying any tag to a page.
- If a refresh is interrupted, old rows are not purged. The same generation simply resumes later.

This design tolerates new tags pushing existing tags onto different pages and cannot create a second stored row for an already-known TagID.

## Synonym semantics

The site's "synonyms" are aliases/alternate spellings/typos that point to the owning official tag. Tag Gremlin therefore stores the direct alias -> official TagID relationship and does not try to resolve aliases into separate tag entities.

## Exports

The CLI exports:

- `tags.txt` - official tag names, one per line;
- `tags.tsv` - TagID, name, uses, votes, reported synonym count;
- `synonyms.tsv` - official TagID/name and alias text;
- `synonym-map.tsv` - alias text first, then the official TagID/name it points to.

Exports are deterministic and sorted.

## Parser contract

The current page shape observed in Firefox is:

- two tag tables per normal page;
- each table contains a structural/header row;
- 50 six-cell official-tag rows;
- each official-tag row is followed by a one-cell hidden synonym-detail row;
- the detail cell spans five columns;
- normal pages therefore contain 100 official tags total.

The parser must not depend on the table index, visual side, CSS class, or hidden-row style. It recognizes official rows by field shape and numeric TagID/uses/vote/synonym metadata, then associates the immediately following detail row.

Synonyms may be rendered as explicit links or as plain text separated by inline nodes, line breaks, commas, semicolons, or pipes. A parsing strategy is accepted only when the parsed count exactly matches the site's reported synonym count. Raw detail text is also stored so parser improvements can be audited later.

If no supported interpretation reconciles to the reported synonym count, the page is not considered complete.

## Commands

```
python3 tag_gremlin.py harvest --url https://SITE/tags.php -8
python3 tag_gremlin.py status
python3 tag_gremlin.py verify
python3 tag_gremlin.py export
```

The default database is `./tag-gremlin.sqlite3`. Incomplete generations resume automatically; running harvest again after a COMPLETE generation starts a full in-place refresh generation. Older databases are migrated in place to the current page-independent TagID + alias model.

## Testing

The project uses only the Python standard library.

GitHub Actions tests run on `windows-latest` and cover:

- HTML parsing;
- page-total/final-page discovery;
- hidden synonym-row association;
- synonym-count mismatch detection;
- deterministic exports;
- SQLite completeness verification;
- page-independent TagID UPSERT behavior;
- completed-refresh stale-tag cleanup;
- raw alias -> official TagID preservation;
- live end-of-crawl total reconciliation when the source grows during a long run;
- schema-v1/v2/v3 to schema-v4 migration.

No live target-site requests are made in CI.
