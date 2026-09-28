# Privacy boundary and threat model

The goal is stronger than “tell Codex not to open the database.” The final local machine should make plaintext database access unavailable to Codex by construction.

## Assets that must stay private

- tag names from the real local database;
- popularity/count values attached to individual named tags;
- query transcripts when they contain literal tag/query strings derived from the private corpus;
- raw autocomplete response lists.

Aggregate numeric statistics are intended to leave the private side: total requests, completeness, SAT/CLOSED counts, bucketed histograms, runtimes, frontier sizes, and similar metrics that do not contain literal database strings.

## Phase-2 process split

Planned Windows layout:

```text
C:\TagGremlinPrivate\   # denied to the Codex identities
    private database
    trusted oracle/benchmark

C:\TagGremlinLab\       # Codex workspace
    candidate source/specs
    sanitized results
    research notes
```

The trusted benchmark is prepared and audited before Codex automation begins. Codex may create candidate schedulers and submit them for evaluation, but it may not modify the trusted private oracle or its filesystem permissions.

## Important leakage paths to block

A candidate scheduler legitimately needs to observe autocomplete responses while an episode is running. Therefore simply hiding the database file is insufficient. Candidate execution must also be prevented from exporting those responses through files, stdout/stderr, network access, child processes, crash dumps, or other arbitrary channels.

The benchmark supervisor, not candidate code, owns the final result file. Candidate-provided free-form logs are discarded/disabled. The result schema allows only predefined fields and bounded numeric histograms plus non-secret candidate IDs.

## What phase 1 proves / does not prove

Phase 1 proves the search semantics, index correctness, completeness accounting, deterministic test harness, and safe result schema on fake data. It does **not** yet claim that arbitrary Codex-generated native code can safely be run next to the private oracle. That isolation layer is a separate deliverable and must be tested before the real DB is selected.
