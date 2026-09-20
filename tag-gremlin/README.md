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
- synonym/alias strings already embedded in the page's hidden detail row

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

Be logged into the target site in Firefox before starting. By default, Tag Gremlin closes Firefox cleanly, reads the matching cookies from `cookies.sqlite`, then reopens the same Firefox profile automatically. Use `--no-close-firefox` or `--no-reopen-firefox` only if you intentionally want to override that behavior. The cookie values are only kept in memory and are not written to the Tag Gremlin database.

## Resume and later refreshes

Just run the same harvest command again.

If the current crawl is incomplete, pages already committed with status `ok` are skipped and that crawl resumes. If the database already has a COMPLETE crawl, running harvest again automatically starts a new refresh generation and re-fetches the whole current tag index.

Tags are keyed only by TagID, not by the page where they appeared. If a growing site pushes TagID 123 from page 32 to page 47, the refresh updates the existing TagID 123 row; it cannot create a second row because TagID is the SQLite primary key.

After a new generation verifies COMPLETE, old TagIDs that were not seen anywhere in that generation are removed. Synonym rows remain simple aliases attached to their owning official TagID. Interrupted refreshes do not purge old rows.

Ctrl-C is safe: completed pages have already been committed.

## Check progress

```bash
python3 tag_gremlin.py --db ~/tag-gremlin.sqlite3 status
```

Strict verification:

```bash
python3 tag_gremlin.py --db ~/tag-gremlin.sqlite3 verify
```

A harvest is not reported COMPLETE unless every expected page is present, unique TagIDs equal the sum of harvested page rows, unique names equal unique TagIDs, the observed corpus is not smaller than the site's displayed counter, and every synonym count reconciles. The displayed counter is shown for comparison but may lag the live pageable index.

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
- `synonyms.tsv` - official TagID/name -> alias string
- `synonym-map.tsv` - alias string -> official TagID/name

Partial export is deliberately blocked. For debugging only, `export --allow-incomplete` overrides that protection.

## Network behavior

Harvesting uses a fixed continuous worker pool. Use `-8` for eight workers or the long form `--workers 8`:

```bash
python3 tag_gremlin.py \
  --db ~/tag-gremlin.sqlite3 \
  harvest \
  --url 'https://SITE/tags.php' \
  -8
```

All workers share one central pending-page iterator, so page assignments cannot overlap. As soon as a worker finishes one page, it takes the next unused page.

A timeout, 429, or transient server/network error backs off and retries only that worker's current request. The other workers continue at full speed. HTTP 429 honors `Retry-After` when supplied.

## Old Gremlin

The old `tag-harvester/` bookmarklet/autocomplete implementations and the v10 scheduler lab remain in the repository as historical/reference work. They are not imported into a new desktop harvest.

See [DGD.md](DGD.md) for the canonical design, data model, and completeness rules.


## Synonym semantics

The site's "synonyms" are alternate spellings, typos, or aliases for an official tag. They do not represent separate tag entities with their own IDs. Tag Gremlin stores each alias against the official TagID it belongs to.

Example:

```text
official TagID 1234 ("tag5")
alias "tagg5"
```

The database stores `1234 -> "tagg5"`. The reverse export makes the same relationship easy to use as `"tagg5" -> 1234 ("tag5")`.

## Growing source during a crawl

Because the live tag list keeps growing, Tag Gremlin rechecks page 1 after a long crawl. If the source total changed but still fits on the same final page, only that final page is refreshed to catch the newly appended tags; already-fetched interior pages are not rescanned. If growth created one or more new page numbers, those pages remain pending and the same command resumes only those missing pages.


## If the site's displayed total is stale

Tag Gremlin keeps the page-1 displayed total, but it does not blindly trust it over a fully consistent corpus. If all expected pages are OK and the database contains more unique TagIDs than the displayed counter, Tag Gremlin refreshes the live final page once to confirm the tail. If the unique-ID count still exactly matches the sum of page rows, the crawl can complete and the verification report shows the positive displayed/observed delta.

A displayed total that is higher than the observed corpus still prevents completion.
