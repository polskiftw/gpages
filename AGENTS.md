# The First Commandment

> **Thou shalt seek the newest truth before acting upon the old.**

Whenever Claire asks for the **latest**, **newest**, **most recent**, **current**, **up-to-date**, **bleeding-edge**, or equivalent version of anything in this repository, do not assume the default branch or latest release is authoritative.

Before choosing what to use, inspect all relevant sources of newer work, including:

- the default branch;
- other branches;
- open pull requests and their head branches;
- releases and tags when relevant.

Use the newest relevant state of the work, even when it is unmerged, unless Claire explicitly names a specific release, tag, branch, commit, or other fixed version.

**Thus is the law: "latest" means latest across the whole repository, not merely latest merged.**

# License Compatibility and Code Reuse

The repository-wide default license is the **PolyForm Noncommercial License 1.0.0** in the root `LICENSE` file. It applies to all repository content that does not declare its own separate license.

For any game mod or other self-contained subproject in this repository, Claire grants standing authorization to change, replace, or add that mod/subproject's license **without asking for confirmation first** when doing so is necessary to lawfully incorporate, adapt, port, or otherwise reuse code from another project.

When exercising this authority:

- choose a license that satisfies the upstream code's actual license obligations and is compatible with the intended reuse;
- scope the license change to the affected mod/subproject when practical rather than relicensing unrelated repository content;
- preserve all required copyright notices, attribution, license text, source-availability obligations, and other upstream conditions;
- do not claim the right to relicense third-party code beyond what its original license permits;
- if no lawful license-compatible path exists, do not copy the code merely by changing this repository's license.

**No additional approval from Claire is required for a license change made under this rule.**


# Jotunn Compatibility Layer: Generality Invariant

The `valheim-jotunn-compat` project is a **general-purpose drop-in compatibility layer for Jotunn**, not a compatibility patch for any individual mod.

Individual Jotunn-dependent mods may be selected as compatibility targets, canaries, regression tests, benchmarks, or API-coverage examples. Those targets are used to discover which **general Jotunn contracts and behaviors** the compatibility layer must implement correctly.

Runtime code in `valheim-jotunn-compat` must therefore:

- implement or optimize Jotunn APIs and behaviors generically;
- preserve compatibility for any mod using the same Jotunn contract;
- never branch on, probe for, hardcode, or special-case a target mod's GUID, assembly name, plugin name, prefab names, configuration keys, or other mod-specific identifiers merely to make that target pass;
- never require a target mod to be present;
- keep target-specific knowledge in tests, compatibility matrices, benchmarks, fixtures, or documentation rather than production runtime logic.

**MoreWorldLocations_All is the first compatibility target/canary, not a runtime dependency or special case.**

When a target exposes a missing Jotunn behavior, fix the underlying generic compatibility layer so that target and other mods using the same behavior benefit automatically.


# Hosted Artifact Timestamp Integrity

When a gpages download page displays a **hosted build**, **published build**, **last updated**, or equivalent timestamp for a generated/downloadable artifact, that timestamp must describe the artifact itself, not merely the latest CI run, packaging pass, page deployment, or unrelated repository change.

Therefore:

- only advance the displayed artifact timestamp when the actual hosted artifact meaningfully changes;
- prefer comparing the primary built binary or another stable content hash, rather than ZIP/container bytes whose metadata may change between otherwise identical builds;
- do not advance an artifact timestamp because CI reran, GitHub Pages redeployed, an unrelated artifact was published, or an archive was recreated with different internal file timestamps;
- when a package contains a primary binary plus support files, use the primary binary as the default change detector unless the package's support-file changes are independently meaningful to users;
- keep the displayed timestamp tied to the exact artifact currently served by the page;
- use an unambiguous machine-readable UTC value in the page/source, and it may be rendered in the viewer's local timezone for readability.

**Thus is the law: a hosted-build timestamp changes when the hosted build changes, not when automation happens to run.**
