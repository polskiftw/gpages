# Tag Gremlin private benchmark — phase 1

This directory is a clean-room benchmark prototype for the real Tag Gremlin search problem. It intentionally does **not** reuse the v9 scheduler logic. v9 is relevant later only as documentation for the private database's storage format.

## Real search contract implemented here

The benchmark treats tag/search strings as lowercase `a-z`, digits `0-9`, and `.` only. `.` is an ordinary character: leading periods, trailing periods, repeated periods, and strings such as `...` are legal. Minimum query length is one character. Uppercase is outside this universe.

For query `q`, the simulated site returns at most 40 tags in two groups:

1. all tags whose names start with `q`, ordered by popularity descending;
2. if fewer than 40 prefix matches were returned, tags containing `q` elsewhere, also ordered by popularity descending, until the result has at most 40 entries.

A 40-item response is ambiguous: it means only that at least 40 matches were visible. The scheduler is not told whether the true count is exactly 40 or larger.

For otherwise equal popularity the phase-1 benchmark uses tag name ascending as a deterministic tie fallback. Candidate schedulers must not rely on ties. We will instrument cutoff ties before the private run.

## What exists now

`tagbench.cpp` contains an indexed oracle, a deliberately stupid exhaustive-query-tree baseline, a numeric-only result printer, and differential self-tests against a naive reference oracle. The optimized oracle indexes 1–4 character substring postings and uses a lexical range + segment tree for prefix top-k selection; long substring searches filter the rarest 4-gram posting list.

`tagbench-audit` is a reachability preflight. It checks whether every tag in a database can appear in at least one legal autocomplete response at all. This matters because the 40-result prefix-first ranking can make a very short, low-popularity tag mathematically unobservable: if its only possible queries always bury it below the cap, no scheduler can discover it. The private DB will be audited numerically before it is ever used as a win state. An unreachable count of zero means the whole DB is a valid completeness target. A nonzero count means we stop and resolve the definition before benchmarking; we do not silently weaken completeness.

`make_fake_db.py` generates synthetic *fake* data specifically for CI and development. It intentionally includes leading/trailing/repeated periods and lots of popularity ties.

The current input format is a temporary canonical TSV used only during phase 1:

```text
popularity<TAB>tag
```

The private v9 database adapter will be added only after the benchmark/oracle boundary is stable. No real tags belong in this repository, Actions artifacts, issue comments, or test fixtures.

## Local development

```powershell
cmake -S . -B build
cmake --build build --config Release
ctest --test-dir build -C Release --output-on-failure
python make_fake_db.py fake.tsv --count 800
.\build\Release\tagbench-audit.exe fake.tsv
.\build\Release\tagbench.exe fake.tsv exhaustive-prefix 500000
```

Linux/macOS builds use the same CMake project; executable paths differ in the usual way.

## Privacy boundary planned for phase 2

The final local setup will separate the trusted private oracle from Codex-generated candidate code. Codex will not receive filesystem access to the private database. Candidate execution will be isolated so it cannot write raw search responses to the Codex workspace or network. Only benchmark-owned aggregate metrics will be released.

The candidate isolation layer is deliberately **not** implemented yet. We should not pretend an in-process C++ test strategy is a security boundary. See `PRIVACY.md`.
