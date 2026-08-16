# Us vs Kezyma/Morrowind-Remastered — evidence-based comparison (2026-08-16)

Studied at their HEAD `0ba2ee5` (v3.1.4, June 2026) by three research passes over the
live repo (modlist data, order/config, lifecycle). Their repo is UNLICENSED: patterns
compared, no code copied. Their v3 architecture: a Wabbajack modlist (393 archives,
~410 MO2 folders, two profiles OpenMW/MWSE, 91 OpenMW content files) + a WPF launcher
driving wabbajack-cli, MO2, and Kezyma's OpenMW Player plugin.

## Where their stability actually comes from

1. **Scope restraint, not engineering rigor.** Purist charter ("no new quests or
   landmass"): no Tamriel Rebuilt, ~4 gameplay mods, near-zero experimental Lua,
   zero user-facing options. 91 content files can be hand-ordered by one curator.
   The repo has no tests, no CI, string-matched error detection, and an unpinned
   MWSE nightly + `releases/latest` wabbajack-cli.
2. **Wabbajack's transport integrity.** Every archive pinned by fileId + xxHash64 and
   hash-verified at install. Their top field-failure class is hash mismatch — i.e.
   corruption is *detected*, never silently installed.
3. **Author-machine outputs shipped as data.** Delta/TES3Merge/MCP/MGE outputs are
   baked into the list (built once, shipped to everyone), with delta run as
   `merge --skip Cell --skip Dialogue --skip DialogueInfo`.
4. **Frozen curation.** MO2 loadorder.txt written by the author; no sorting tools, no
   master-dependency handling at all (feasible only at 91 entries).

## Scorecard

| Dimension | Kezyma | Us | Verdict |
|---|---|---|---|
| Scope | 393 archives, purist, no TR | 626 content files, TR + quests + Lua, richest | Different missions by user choice; our failure surface is ~7x, offset below |
| Archive integrity | fileId + hash verified | fileId pins, **no archive hashes** | **They win — adopt** (hash ledger backlog) |
| Load order | hand-frozen, no dep handling | machine-checked: master graph fixed point, momw alignment (0/173 inversions), moveAfter, 2 deterministic auditors incl. exact-engine dialogue simulator | We win; mandatory at our scale |
| Variants | flattened at compile into per-option folders | dataPaths picks + momw adoption + hand layer | Equivalent outcomes; ours data-driven and re-runnable |
| Delta merge | prebuilt, skips Cell/Dialogue/DialogueInfo | was full merge → **now same skips, generated per-install** (adopted 2026-08-16) | Tie after adoption; per-install generation adapts to list edits |
| Navmesh | none (2GB runtime cache) | pregenerated 383k tiles, 6GB cap | We win (their users report perf issues, #4/#8/#18) |
| openmw.cfg | regenerated per-launch by MO2 plugin; user cfg backed up/restored | composed at install, marker-verified; backup once | Tie; theirs suits MO2 layering, ours has no MO2 |
| Graphics quality | view 81920, lights 32, per-pixel, chain HBAO/DIVE/VAIO/godrays/wetworld/tonemap/SMAA, refl detail 0 (SSR) | matched view/lights/per-pixel/fog/preload/actor-range (adopted 2026-08-16); momw chain ssao_hq…hdr; refl detail 3 (real reflections) | Tie-to-us: real water reflections + normal-mapped everything + pregenerated navmesh |
| Quality presets | none (flat per-key editor) | none (single richest tier) | Tie; both single-tier |
| Updates | remote catalog poll, version compare, idempotent reinstall | **none** (list baked into exe) | **They win — adopt** (poll repo raw modlist version; engine is already idempotent) |
| Nexus auth | OAuth PKCE into Wabbajack's store | pasted API key (rotation burden) | **They win — v2 candidate** (delegate SSO to umo) |
| Disk lifecycle | prune (~10GB) + clear downloads (~40GB) buttons | nothing (22GB archives + 37GB tree kept) | **They win — adopt** (clean-downloads verb; unmounted-folder prune) |
| Failure handling | delegated to Wabbajack; manual delete-and-retry README recipe | resume at every step, disk-truth verifiers, prefetch fallback, per-mod retry/skip, redacted diagnostics bundles | We win |
| Runtime repair | none (mods run as shipped) | record fixups (2 esps) + 5 self-healing script patches, log-census triage practice | We win; necessitated by our scope |
| QA | alpha releases + Discord | 148 tests, curation CI, order/dialogue auditors, boot verification | We win |
| Platforms | Windows 10/11 only | Linux + Windows | We win |

## Adopted from this review (committed)

- delta-plugin now runs `--skip Cell --skip Dialogue --skip DialogueInfo` (their
  field-proven scope; our own dialogue incidents argue identically).
- settings uplift: viewing distance 81920, max lights 32, force per pixel lighting,
  clamp lighting off, match sunlight to sun, use distant fog + radial fog + sky
  blending, preload distance 4000 / 3 threads, actors processing range 8192.

## Backlog (ranked)

1. **Archive hash ledger** — record sha256 on first successful download (resolve-cache
   or modlist), verify before extraction on later runs. Kills the silent-corruption
   class their model catches and ours currently would not.
2. **Update check** — ship listVersion in a raw-URL manifest; app polls, compares
   embedded vs remote, offers idempotent re-run. Their best lifecycle idea, cheap here.
3. **Disk lifecycle verbs** — `clean-downloads` (archives after verified success) and
   an unmounted-variant prune report.
4. **OAuth SSO via umo** (v2) — retire the paste-and-rotate key dance.
5. Their `.omwpbackup` deploy/restore idea (protects users who also run vanilla) — nice-to-have.

## Deliberate non-adoptions

- Shipping prebuilt delta/navmesh output: ours regenerate per install and adapt to any
  list change; theirs desyncs if a user touches the list (they accept that; we don't).
- Purist scope: the entire point of this project is the maximal sheet + richest picks.
  Stability parity is pursued through machine checking instead of restraint.
- MO2/Wabbajack substrate: locked decision, and their launcher exists precisely to
  paper over MO2 path breakage (their #1 historical issue class).
