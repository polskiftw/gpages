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
7. the number of unique official tag names equals the reported total;
8. every tag's parsed synonym count equals its reported synonym count.

Anything else is incomplete. The CLI must say so explicitly.

Do not silently discard malformed rows, duplicate IDs/names, synonym mismatches, HTTP errors, or authentication failures.

## Authentication and privacy

Tag Gremlin reads the user's Firefox `cookies.sqlite` directly and loads only cookies eligible for the target host into an in-memory CookieJar.

Rules:

- cookie values are never printed;
- cookie values are never written to the Tag Gremlin SQLite database;
- no cookie-export file is created;
- the target hostname is not committed to this repository;
- the user may override Firefox profile discovery with `--firefox-profile`.

## Network behavior

The crawler is resumable and intentionally conservative.

- start at low concurrency;
- use bounded concurrency;
- honor `Retry-After` on HTTP 429 when present;
- retry transient failures with exponential backoff plus jitter;
- reduce pressure after retries, HTTP 429/5xx, or high latency;
- increase concurrency only after sustained clean/fast batches;
- never refetch a page already committed with status `ok` unless the user starts a new database.

Local CPU and RAM may be used freely for parsing, validation, indexing, and exports. Network aggressiveness is not a performance goal.

## Database

The SQLite database is the canonical lossless local result.

### `meta`

Key/value metadata including schema version, source URL, reported total, final page, rows-per-page baseline, and timestamps.

### `pages`

One row per page:

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

### `tags`

One row per official tag:

- TagID
- official name
- uses
- positive votes
- negative votes
- reported synonym count
- source page
- raw synonym-detail text
- synonym parse status

Tag IDs and official names are both unique invariants.

### `tag_synonyms`

Lossless mapping from official TagID to each synonym string observed in that tag's hidden detail row.

The raw relationship is preserved. Do not assume synonym relationships are symmetric or transitive.

## Synonym semantics

The site calls the values synonyms, but Tag Gremlin stores the observed mapping rather than imposing a graph model during harvest.

Derived views/exports may later calculate:

- synonym -> official tag reverse mappings;
- aliases shared by multiple official tags;
- official names that also appear as synonyms;
- connected components when justified by the data.

Those are derived products, not harvest truth.

## Exports

The CLI exports:

- `tags.txt` - official tag names, one per line;
- `tags.tsv` - TagID, name, uses, votes, reported synonym count;
- `synonyms.tsv` - TagID, official tag, synonym;
- `synonym-map.tsv` - synonym first, then TagID and official tag for convenient reverse lookup.

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

Synonyms are taken from link text inside the associated hidden detail row. Raw detail text is also stored so parser improvements can be audited later.

If parsed unique synonym link text does not equal the site's reported synonym count for a tag, the page is not considered complete.

## Commands

```
python3 tag_gremlin.py harvest --url https://SITE/tags.php
python3 tag_gremlin.py status
python3 tag_gremlin.py verify
python3 tag_gremlin.py export
```

The default database is `./tag-gremlin.sqlite3`. Existing databases resume automatically.

## Testing

The project uses only the Python standard library.

GitHub Actions tests run on `windows-latest` and cover:

- HTML parsing;
- page-total/final-page discovery;
- hidden synonym-row association;
- synonym-count mismatch detection;
- deterministic exports;
- SQLite completeness verification.

No live target-site requests are made in CI.
