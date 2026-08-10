# Morrowind Remake Installer

A Windows installer that turns a clean **Morrowind (Game of the Year Edition)** install into a
fully modded **OpenMW** setup — 600+ curated mods (based on the
["Morrowind in 2025" modlist](https://docs.google.com/spreadsheets/u/0/d/e/2PACX-1vTWfHcdX5HXdpZj9R9KOcRYo0V80aGIAVd8tbrqTzC-J4R7ZeBdMslgdDlBGdTmvyF874qqaVY8V9VN/pubhtml)),
fully automated: tool download, mod download, configuration, leveled-list merging and navmesh
pre-generation.

## What it does

1. **Auto-detects Morrowind** — Steam (registry → `libraryfolders.vdf` → `appmanifest_22320.acf`),
   GOG, the Bethesda registry key, and well-known default paths; manual browse as fallback.
   Validation is the same ground truth OpenMW's wizard uses (`Morrowind.esm` + `Morrowind.bsa`).
2. **Downloads all tooling** — OpenMW 0.51 and the [MOMW tools pack](https://modding-openmw.gitlab.io/momw-tools-pack/)
   (umo, delta-plugin, tes3cmd, navmeshtool, RAR-capable 7z…), sha256-pinned.
3. **Downloads and installs every mod** via [umo](https://modding-openmw.gitlab.io/umo/) with our
   custom modlist (Nexus Premium required in v1 — the Nexus API only issues automated download
   links to Premium accounts).
4. **Generates the OpenMW configuration** — imports `Morrowind.ini`, composes `openmw.cfg`
   (data/content/groundcover order is baked into the curated list), applies tuned `settings.cfg`
   values non-destructively, runs the delta-plugin merge and pre-builds navmeshes.

Everything is **resumable**: each pipeline step verifies its real on-disk artifacts, so closing the
app (or a crash, or a network drop) never loses progress.

## Repository layout

| Path | What |
|---|---|
| `src/Mri.App` | Avalonia wizard GUI (ships as one self-contained `MorrowindRemakeInstaller.exe`) |
| `src/Mri.Core` | All logic: detection, tools, umo, cfg generation, install pipeline |
| `src/Mri.Curation` | Spreadsheet → canonical modlist pipeline (runs on Linux/CI) |
| `data/` | Source CSV snapshot, overrides, resolve cache, emitted `modlist.json`, tool manifest, config templates |
| `tests/` | xUnit suites (all runnable on Linux) |
| `docs/` | Architecture, curation guide, QA checklist |

## Building

```sh
dotnet build            # everything
dotnet test             # 100 tests, no Windows needed
dotnet run --project src/Mri.App          # run the wizard (Linux dev works)

# Windows single-file exe (works from Linux too):
dotnet publish src/Mri.App -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o artifacts/win-x64
```

## Status

- ✅ M0 skeleton, core library, curation pipeline, wizard UI, CI — done
- ⏳ **M1**: end-to-end smoke on a Windows machine with a 5-mod list (validates the umo CLI
  contract before deep curation) — see `docs/QA-CHECKLIST.md`
- ⏳ **M2**: full curation of the 610-mod list (`dotnet run --project src/Mri.Curation -- emit …`
  prints the burn-down report; needs a `NEXUS_APIKEY` for the one-time resolve pass)

See `docs/ARCHITECTURE.md` for design details and credits/licensing notes.
