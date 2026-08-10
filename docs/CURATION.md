# Curation guide

The curation pipeline turns the source spreadsheet (`data/modlist.source.csv`) into the canonical
`data/modlist.json` the installer ships. It runs anywhere .NET runs — no Windows needed.

## The loop

```sh
# 1. One-time (and after sheet refreshes): resolve Nexus metadata into the committed cache.
#    ~1130 API calls for the full list — fits the 2500/day budget; resumable; checkpointed.
NEXUS_APIKEY=<your key> dotnet run --project src/Mri.Curation -- resolve \
  --csv data/modlist.source.csv

# 2. Emit + read the burn-down report.
dotnet run --project src/Mri.Curation -- emit --csv data/modlist.source.csv \
  --list-version 2026.08.0

less build/report.md        # every open problem, grouped by category

# 3. Encode rules in data/overrides/overrides.yaml, re-run emit (seconds, cache-hot), repeat.
```

`emit` exits non-zero on **constraint violations** (load-order assertions), and with `--strict`
on any open problem. CI (`curation-check.yml`) re-emits from the committed cache and fails if
`data/modlist.json` is stale.

## What the report flags

| Problem | Fix |
|---|---|
| `comment not encoded: "…"` | Translate the prose into a rule (`actions`, `set`, `constraints`, `splitInto`…). Any matching rule clears the flag. |
| `plugin list unknown` | Set `content:` (and real `dataPaths:`) via override — from the mod page's file listing or archive inspection. |
| `low-confidence file pick` | Confirm/pin `nexusFileId` via `set:`, or document the choice with `pickFile:`. |
| `pinned file_id N not found` | The sheet's link had a typo'd/outdated id — pin the correct one. |
| `github/gitlab source — verify URL` | Point `set.directUrl` at the actual release artifact. |
| `mod is hidden/removed on Nexus` | Find a mirror (`directUrl`) or `skip:` it with a reason. |

See the header comment in `data/overrides/overrides.yaml` for the full rule vocabulary, and the
existing rules for worked examples (workflow-row skips, remove-actions, a load-order constraint).

## Data-path conventions

`extractTo`/`dataPaths` are relative to the installer's `mods/` root. umo extracts each archive
into `extractTo`; `dataPaths` are the directories that become `data=` lines — for simple mods
that's the extract dir itself, for BAIN-style archives it's subfolders like `Mod/00 Core`.

## Future: archive inspection (planned M2 tooling)

Plugin lists (`content:`) can't come from the Nexus API — it lists archives, not their contents.
The planned `inspect` verb downloads each archive once (umo's Linux build + a Premium key),
lists contents with 7z, and auto-fills `dataPaths`/`content` drafts for human review. Until then,
overrides are the mechanism.
