# Architecture

## The one-sentence version

A thin Avalonia wizard drives a **verifier-based step pipeline** that shells out to specialist
CLIs (umo, delta-plugin, openmw-iniimporter, openmw-navmeshtool) and generates OpenMW's
configuration deterministically from a **single canonical modlist** compiled at curation time.

## Canonical modlist → two projections

`data/modlist.json` is the single source of truth. **Array order is load order** — later `data=`
directories win file conflicts in OpenMW, which is exactly how the spreadsheet's
"NEEDS TO OVERWRITE" section is expressed (those mods simply sit last).

`ModlistCompiler` (in `Mri.Core.Modlist`) projects it into:

1. **umo ModDesc[] JSON** — the exact schema modding-openmw.com serves from `/api/lists/<slug>`
   and `umo list add` ingests (verified against live API output, including the
   `{"action": "remove"|"rename"|"copy"|"clean"}` shapes).
2. **LoadOrderPlan** — flat ordered `data=` / `content=` / `groundcover=` / `fallback-archive=`
   inputs for the cfg composer.

One source, two projections — they can never drift.

## The install pipeline (`Mri.Core.Pipeline`)

```
AcquireTools → WriteUmoConfig → RegisterModlist → InstallMods (umo)
→ ImportIni → GenerateOpenMwCfg (phase 1) → GenerateSettings
→ DeltaMerge (then phase-2 cfg rewrite) → Navmesh → Validate (optional)
```

Every step implements `Verify(ctx)` against **real disk artifacts** (never a stored flag):
tools = version marker + exe probe, cfg = header hash match, delta = merged `.omwaddon` exists +
phase-2 cfg current. The engine skips verified steps, which *is* the resume story — kill the app
at any point, relaunch, and it continues from the first unverified step. `state.json` carries only
what disk can't: failed/skipped mods, harvested `fallback=` lines, the list version.

**Two-phase delta:** phase-1 `openmw.cfg` includes `deltaOnly` plugins so delta-plugin can read
and merge them; the phase-2 rewrite drops them and appends `delta-merged/` +
`delta-merged.omwaddon` last. The composer regenerates the entire file deterministically, so both
phases are idempotent and verifiable by hash.

## Key seams

- `IProcessRunner` — every child CLI goes through this; streams stdout line-by-line into
  progress parsers. Faked in tests with captured output.
- `IRegistryReader` — registry access behind an interface; `NullRegistryReader` on Linux,
  fakes in tests. VDF/ACF parsing is pure C# (`VdfParser` handles both libraryfolders formats).
- `UMO_CONF_DIR` — umo always runs with its config redirected into the install dir, so the
  user's own `%APPDATA%\umomwd` is never touched.

## Nexus rules (why Premium-only v1)

`download_link.json` without a key/expires pair returns 403 for non-Premium accounts — there is
no legitimate programmatic path for free users beyond one manual click per file, and autoclickers
violate Nexus ToS §10/§11 (ban risk; never bundle one). v1 therefore hard-gates on Premium
(validated via `/v1/users/validate.json` before anything downloads). A guided click-through queue
for free users is the designed v2 feature. Per the API AUP we send
`Application-Name`/`Application-Version` on every request and keys never leave the machine
(DPAPI-encrypted at rest).

## Known M1-verification points

These external contracts were researched but must be confirmed against real binaries during the
M1 Windows smoke run (all isolated in one place each):

1. `umo` config.json key names (`UmoConfigWriter`) and `list add` / `install` flag behavior.
2. OpenMW NSIS installer silent switches `/S /D=` (`ToolAcquisitionService.ExtractAsync`).
3. delta-plugin's `OPENMW_CONFIG` env resolution (`DeltaPluginService`).
4. umo stdout format → refine `UmoProgressParser` with captured fixtures.
5. `shaders.yaml` expected location next to `settings.cfg` (`OpenMwUserPaths`).

## Reference project & licensing

Architecture patterns (config-as-data, verifier-driven idempotent steps, section-aware INI
editing, overwrite-order-as-data) were studied from
[Kezyma/Morrowind-Remastered](https://github.com/Kezyma/Morrowind-Remastered), which has **no
license** — nothing was copied; everything here is an independent implementation. umo and the
MOMW tools are AGPL/GPL and are invoked strictly as separate processes, never linked.
