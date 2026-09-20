# Tag Gremlin Desktop

A local, resumable harvester for the site's full `tags.php` index.

This replaces the old bookmarklet/autocomplete approach for full-corpus collection. It uses the site's directly pageable tag browser, reads the logged-in Firefox session locally, stores the result in SQLite, and exports a simple tag list plus synonym maps.

No third-party Python packages are required.

## What it collects

For every official tag:

- TagID
- official tag name
- uses
- positive votes
- negative votes
- reported synonym count
- synonym strings already embedded in the page's hidden detail row

The crawler never prints cookie values and never writes Firefox cookies into its database.

## Quick start on Gentoo

Be logged into the target site in Firefox, then from a clone of this repository:

```bash
cd tag-gremlin

python3 tag_gremlin.py \
  --db ~/tag-gremlin.sqlite3 \
  harvest \
  --url 'https://SITE/tags.php'
```

The target hostname is deliberately not stored in this repository. Replace `SITE` with the real host locally.

Tag Gremlin auto-detects the default Firefox profile. If needed:

```bash
python3 tag_gremlin.py \
  --db ~/tag-gremlin.sqlite3 \
  harvest \
  --url 'https://SITE/tags.php' \
  --firefox-profile ~/.mozilla/firefox/<profile>.default-esr
```

Firefox may remain open. The cookie database is opened read-only.

## Resume

Just run the same harvest command again. Pages already committed with status `ok` are skipped.

Ctrl-C is safe: completed pages have already been committed.

## Check progress

```bash
python3 tag_gremlin.py --db ~/tag-gremlin.sqlite3 status
```

Strict verification:

```bash
python3 tag_gremlin.py --db ~/tag-gremlin.sqlite3 verify
```

A harvest is not reported COMPLETE unless every expected page is present, tag counts match the site's reported total, tag IDs/names are unique, and every synonym count reconciles.

## Export

After verification succeeds:

```bash
python3 tag_gremlin.py \
  --db ~/tag-gremlin.sqlite3 \
  export \
  --out-dir ~/tag-gremlin-export
```

Outputs:

- `tags.txt` - one official tag per line; intended as the simple corpus input for search/regex tooling
- `tags.tsv` - tag metadata
- `synonyms.tsv` - official tag -> synonym mapping
- `synonym-map.tsv` - synonym -> official tag reverse mapping

Partial export is deliberately blocked. For debugging only, `export --allow-incomplete` overrides that protection.

## Network behavior

The default starts at 2 concurrent requests and can rise to at most 4 after sustained clean, fast batches. It backs off when latency rises or retries occur.

HTTP 429 honors `Retry-After` when the server provides it. Transient network/5xx failures use bounded exponential backoff and jitter.

You can lower the ceiling if desired:

```bash
python3 tag_gremlin.py \
  --db ~/tag-gremlin.sqlite3 \
  harvest \
  --url 'https://SITE/tags.php' \
  --max-workers 2
```

## Old Gremlin

The old `tag-harvester/` bookmarklet/autocomplete implementations and the v10 scheduler lab remain in the repository as historical/reference work. They are not imported into a new desktop harvest.

See [DGD.md](DGD.md) for the canonical design, data model, and completeness rules.
