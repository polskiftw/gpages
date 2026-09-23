# Fourteen Seed Problem

The **fourteen seed problem** means:

Use **one seed number** across **all fourteen world sizes**, generate all fourteen worlds, render them as PNGs, and produce **one final comparison image** showing all fourteen results.

## Critical loading requirement

**Make sure ExpandedWorlds is fully loaded and Harmony-patched before Terraria queues or enters the world-load callback.**

Do **not** initialize ExpandedWorlds from inside `WorldGen.serverLoadWorldCallBack` immediately before `WorldFile.LoadWorld`. That is too late and can deadlock while Harmony patches world/server code that is already active.

For runner-based generation, bootstrap ExpandedWorlds **before** the callback is queued — in practice, initialize the mod immediately before the `serverLoadWorld` code creates/references the `serverLoadWorldCallBack` delegate. Only start world generation after ExpandedWorlds initialization and `Harmony.PatchAll(...)` have completed successfully.

In short: **ExpandedWorlds first, world-load callback second, world generation third.**

## Default mode

If Claire does **not** provide a seed:

1. Pick one random seed number.
2. Use that exact same seed for all fourteen world sizes.
3. Generate all fourteen worlds fresh.
4. Render all fourteen worlds to PNG.
5. Combine the fourteen renders into one comparison image.

## Supplied-seed mode

If Claire provides a seed:

1. Use that exact seed for all fourteen world sizes.
2. Generate all fourteen worlds fresh.
3. Render all fourteen worlds to PNG.
4. Combine the fourteen renders into one comparison image.

If Claire also specifies Terraria special-seed or secret-seed options, apply the same selected options to **every one of the fourteen worlds**. Keep the supplied number as the base numeric seed; for Terraria 1.4.5 secret seeds, use Terraria's native copied-seed payload rather than substituting a different seed string.

## Rules

- The same seed is used for every size.
- The same requested special/secret-seed modifiers are used for every size.
- Do not search for a better seed.
- Do not try to make the layouts match.
- Do not treat biome/layout differences between sizes as failures.
- Do not edit generated worlds to force similarity.
- The purpose is simply to see what one seed produces at every world size.

## Required world sizes

Generate exactly these fourteen sizes:

1. **Small** — 4200 x 1200
2. **Medium** — 6400 x 1800
3. **Large** — 8400 x 2400
4. **THICC** — 10600 x 3000
5. **THICC 2** — 12600 x 3600
6. **THICC 3** — 14800 x 4200
7. **THICC 4** — 16800 x 4800
8. **THICC 5** — 19000 x 5400
9. **THICC 6** — 21000 x 6000
10. **THICC 7** — 23200 x 6600
11. **THICC 8** — 25200 x 7200
12. **THICC 9** — 27400 x 7800
13. **THICC 10** — 29400 x 8400
14. **THICC 11** — 31600 x 9000

## Queue ordering for multi-run batches

When launching multiple fourteen-seed families at once — for example several secret seeds, several numeric seeds, or both — queue the generation jobs **by world size across the entire batch**, smallest first.

Preferred order:

1. all Small jobs
2. all Medium jobs
3. all Large jobs
4. all THICC jobs
5. all THICC 2 jobs
6. all THICC 3 jobs
7. all THICC 4 jobs
8. all THICC 5 jobs
9. all THICC 6 jobs
10. all THICC 7 jobs
11. all THICC 8 jobs
12. all THICC 9 jobs
13. all THICC 10 jobs
14. all THICC 11 jobs

Within each size tier, the seed or secret-seed variants may be in any stable order. For a four-secret-seed batch, for example, queue all four Small jobs before any Medium job, then all four Medium jobs before any Large job, and so on.

For GitHub Actions, prefer an explicit `matrix.include` list in that size-first order when queue order matters. Do not rely on the expansion order of a multi-axis Cartesian matrix to produce the desired size-first sequence.

This does not make an individual world generate faster. It improves throughput when runner concurrency is lower than the total job count: short Small/Medium jobs finish first and free runner slots sooner, basic workflow/seed-encoding failures surface earlier, and the queue reaches the long THICC tiers with less avoidable blocking.

**Size-first is a queue-ordering policy, not a synchronization barrier.** Do not make every Small job finish before Medium jobs are allowed to become runnable, and do not make one arbitrary matrix shard finish before the next shard may start. The purpose is to present cheaper jobs to the runner pool first while still keeping every available runner busy.

## Parallelism and matrix sharding for multi-run batches

For large batches, let GitHub Actions consume the full runner capacity available to the repository/account.

- **Omit `strategy.max-parallel` by default.** It is an optional throttle, not a request for more runners. Without it, GitHub starts as many runnable matrix jobs as the available runner/concurrency limits permit.
- Set `max-parallel` only when there is a deliberate reason to run **below** the available runner capacity, such as protecting an external API/service, limiting expensive self-hosted hardware, or avoiding contention for a shared resource.
- Do not pick an arbitrary high `max-parallel` merely to mean "run as fast as possible." Omitting it expresses that intent directly and automatically benefits from any future increase in runner concurrency.
- A single GitHub Actions matrix is limited to **256 generated jobs**. If the batch exceeds that, split it into multiple matrix jobs/shards, each at or below the matrix limit.
- **Balance shards as evenly as practical.** Divide seed families among the required shards so their family counts differ by at most one whenever possible. Because every family contributes exactly fourteen generation jobs, balancing family counts also balances job counts. Example: a 46-family batch requiring three shards should be split **16 / 15 / 15 families**, producing **224 / 210 / 210 jobs**. Do not use an arbitrarily skewed partition when an even one fits the matrix limits.
- **Matrix shards are queue partitions, not sequential phases.** After their common prerequisites are ready, all generation shards should become runnable together. Do not make `generate_b` depend on `generate_a`, or `generate_c` depend on `generate_b`, unless there is a real data dependency between them.
- When sharding is required, partition the seed families across shards and keep each shard's explicit `matrix.include` list in the same size-first order: all assigned Small jobs, then all assigned Medium jobs, and so on through THICC 11. Starting all shards together then gives GitHub a large runnable pool without introducing artificial end-of-shard idle time.
- The only normal shared prerequisite should be whatever genuinely must exist first, such as the plan and prepared runtime artifact. Once that prerequisite succeeds, every generation shard should be free to enter the queue.

Why this matters: a sequential A -> B -> C arrangement can strand runners at the tail of a shard. If one pathological world is still generating while the rest of that shard has finished, every otherwise-free runner sits idle even though hundreds of later jobs are ready in principle. Independent shards let those free runners immediately start work from the remaining queue.

For a batch containing `n` fourteen-seed families, there are `14 * n` generation jobs. The ideal workflow-level parallelism is therefore simply "all of them may be runnable"; the actual number executing at once should be left to GitHub's runner/concurrency limit unless an intentional lower throttle is required.

## Proven runner workflow

This is the known-good path from the successful seed `1337420` run. Prefer this path instead of rediscovering the Windows/XNA dead ends.

1. Start from the current ExpandedWorlds code, including relevant unmerged branches/PRs when "latest/current" is requested.
2. Download the official Terraria **1.4.5.8 dedicated-server package**.
3. Use the package's **Linux** server tree, specifically its native `TerrariaServer.bin.x86_64` launcher and bundled **FNA** stack.
4. Build the current ExpandedWorlds dedicated-server bootstrap against that Linux/FNA server bundle.
5. Inject the bootstrap call into `Terraria.WorldGen.serverLoadWorld()` immediately **before** the code takes/references `serverLoadWorldCallBack` (the `ldftn` site used by the proven patcher).
6. Run one isolated GitHub Actions job per world size so each generator gets its own runner memory budget.
7. Pass the seed literally through Terraria's server config. When secret-seed options are requested, serialize the native copied-seed payload with the numeric seed and the requested secret flags. For expanded sizes, set `GLOADER_EXPANDED_WORLD` to `THICC`, `THICC2`, `THICC3`, `THICC4`, `THICC5`, `THICC6`, `THICC7`, `THICC8`, `THICC9`, `THICC10`, or `THICC11`; leave it unset for Small/Medium/Large.
8. Require proof that ExpandedWorlds loaded early and `Environment.Is64BitProcess` is `True`.
9. Require the expanded generation completion log for THICC through THICC 11 at the exact expected dimensions. Do not accept a run merely because a `.wld` file exists.
10. Upload each fresh `.wld` separately.
11. In the compose job, download all fourteen worlds and independently verify every world header/dimension before rendering.
12. Render with the existing **pinned TEdit render-only compositor** using the full pinned TEdit palette.
13. Produce the fourteen individual PNGs plus one final combined comparison PNG.

## Compose / render process waiting

The pinned `world-family-renderer` project is a Windows `WinExe`. In PowerShell/GitHub Actions, launching that GUI-subsystem executable with the call operator (`&`) can return control **before the renderer has actually finished**.

Seed `54321` with Remix, Error World, Care Bears, and Zenith proved this failure mode: all **56 generation jobs** completed successfully, but all four compose jobs immediately checked for the final comparison PNG while the renderer was still running and therefore false-failed.

For fourteen-seed compose jobs:

- Prefer converting the temporary render-only project from `<OutputType>WinExe</OutputType>` to `<OutputType>Exe</OutputType>` before publishing.
- Launch the renderer with `Start-Process -Wait -PassThru`, check the returned process `ExitCode`, and only then verify the final PNG exists.
- Do **not** use `& $exe ...` followed immediately by an output-file existence check for this pinned renderer.
- Keep a short post-process output check/poll as a final guard, but process completion is the primary synchronization mechanism.

If world generation has already succeeded and the uploaded `.wld` artifacts are intact, a compose failure **does not justify regenerating the worlds**. Run a compose-only recovery job against the existing world artifacts while they are still within the artifact retention window.

For cross-run recovery, do not assume a wildcard `actions/download-artifact` request saw the complete source-run artifact set. Large fourteen-seed batches can exceed one GitHub API artifact page: the `54321` four-secret-seed run had **113 artifacts**, and a recovery wildcard saw only the first **100**, silently omitting two Remix worlds. When the source run may have more than 100 artifacts, enumerate the run-artifacts API **page by page**, resolve each expected `world-<suite>-<size>` artifact by exact name/ID, download those exact artifacts, and assert that all fourteen `.wld` files are present before rendering.

## Timeout budget

The largest ExpandedWorlds tiers can legitimately take **multiple hours** to finish world generation.

For every fourteen-seed generation run:

- Set the GitHub Actions generation-job timeout to **at least five hours** (`timeout-minutes: 300` or higher).
- Any separate shell/harness/watchdog deadline must also allow **at least five hours**. Prefer removing the extra watchdog entirely when the Actions job timeout already provides the hard stop.
- Check for both timeout layers before launching. A generous Actions timeout does not help if an internal harness deadline kills Terraria first.

Seed `343434` proved why this matters: the old `6600`-second harness deadline killed THICC 10 and THICC 11 while Terraria was still actively generating, and the old `timeout-minutes: 120` job ceiling was also too low for those tiers.

In short: **fourteen-seed generation timeouts are 5+ hours, not ~2 hours.**

## Do not repeat these dead ends

The successful run established several things that should be treated as settled unless the underlying Terraria runtime changes:

- **Do not use the ordinary Windows dedicated-server path for fourteen-seed generation.** A normal Windows launch can still be 32-bit (`Is64BitProcess=False`).
- Clearing the Windows executable's 32-bit CLR preference is **not enough**. Microsoft XNA 4 then becomes the next 32-bit/runtime blocker.
- Do not spend time retargeting Terraria's Windows XNA references to FNA or trying to fake strong-named XNA assembly binding. The official Linux package already provides the clean **x86_64 + FNA** route.
- Do not load ExpandedWorlds late from inside the world-load callback. It must be patched in **before Terraria queues worldgen**.
- Do not use file existence alone as success. A truncated, wrong-size, or vanilla-size world is a failed fourteen-seed result.
- Do not restore the old `6600`-second / `120`-minute timeout limits. Generation jobs and any internal watchdogs must allow **at least five hours**.
- Do not treat a compose-only failure as a reason to regenerate already-valid world artifacts.
- Do not launch the pinned `WinExe` compositor with `&` and immediately check for output; explicitly wait for the renderer process to exit.
- Do not trust an unpaginated cross-run artifact wildcard when the source run has more than 100 artifacts; enumerate pages and require all fourteen expected world artifacts.
- Do not use `max-parallel` as a "go fast" setting. Omit it unless intentionally throttling below available runner capacity.
- Do not serialize matrix shards with A -> B -> C `needs` dependencies merely because the batch had to be split around GitHub's matrix-size limit. Queue all independent generation shards together.

## Required validation gates

A fourteen-seed job is accepted only when all of these are true:

- The exact requested/random seed was used for all fourteen worlds.
- Any requested special/secret-seed modifiers were applied identically to all fourteen worlds.
- All fourteen worlds were generated fresh.
- ExpandedWorlds reports successful early load.
- The generator process reports **64-bit**.
- Small is 4200 x 1200.
- Medium is 6400 x 1800.
- Large is 8400 x 2400.
- THICC is 10600 x 3000.
- THICC 2 is 12600 x 3600.
- THICC 3 is 14800 x 4200.
- THICC 4 is 16800 x 4800.
- THICC 5 is 19000 x 5400.
- THICC 6 is 21000 x 6000.
- THICC 7 is 23200 x 6600.
- THICC 8 is 25200 x 7200.
- THICC 9 is 27400 x 7800.
- THICC 10 is 29400 x 8400.
- THICC 11 is 31600 x 9000.
- The compositor independently verifies all fourteen world dimensions before rendering.
- All fourteen renders use the pinned TEdit palette implementation.
- The final combined PNG is produced successfully.

## Reference success

The seed `1337420` run proved this workflow end to end for the original six-size ladder:

- official Terraria 1.4.5.8 Linux x64 server path
- FNA instead of Windows XNA
- early ExpandedWorlds injection
- six isolated generation jobs
- all six dimension gates passing through the then-current THICC
- final pinned-TEdit render/composite job passing

The current definition keeps that exact process and extends only the size count to the full fourteen-size ladder.

## Short version

**Fourteen seed problem = pick one seed, load ExpandedWorlds before Terraria queues worldgen, give generation 5+ hour timeouts, run all fourteen sizes on the official Linux x64/FNA server path with any requested special/secret-seed modifiers applied identically, queue every independent matrix shard together without an artificial `max-parallel` throttle or A -> B -> C barrier, verify every dimension, render with pinned TEdit, make the picture.**
