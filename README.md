<p align="center">
  <img src="src/Mri.App/Assets/nerevarine.png" width="96" alt="Nerevarine moon-and-star roundel">
</p>

<h1 align="center">Nerevarine</h1>

<p align="center">
  One installer that turns a clean <strong>Morrowind (GOTY)</strong> into a fully modded
  <strong>OpenMW</strong> — <strong>599 curated mods</strong>, zero manual steps.
  <br>
  <a href="https://github.com/Mhsbrian/Nerevarine/actions/workflows/ci.yml"><img src="https://github.com/Mhsbrian/Nerevarine/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://github.com/Mhsbrian/Nerevarine/releases/latest"><img src="https://img.shields.io/github/v/release/Mhsbrian/Nerevarine" alt="Latest release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT license"></a>
</p>

Modding Morrowind to a modern standard normally means days of hand-installing hundreds of
archives, resolving conflicts, and sorting load order. Nerevarine does all of it in one run:
point it at your Morrowind install, sign in to Nexus, and come back to a playable,
pre-optimized world. A recent full install completed in **35 minutes** with **0 failed mods**.

## What you get

- **599 mods**, curated and conflict-resolved: HD assets and normal maps everywhere, Tamriel Rebuilt
  and major quest mods, overhauled cities, dungeons and lighting, voiced dialogue, grass,
  post-processing shaders, and modern gameplay/QoL Lua.
- **OpenMW 0.51** installed and configured automatically — load order, groundcover,
  leveled-list delta merge and tuned settings are generated, not hand-edited.
- **Pre-generated navmesh** (383,494 tiles) so AI pathfinding never stutters on first play.
- **A launcher** for day-to-day play: three quality tiers (with hardware auto-detection),
  per-mod engine switches, and one-click updates when the mod list evolves.

## Requirements

| | |
|---|---|
| Game | Morrowind **Game of the Year Edition** (Steam, GOG or disc — auto-detected) |
| Nexus | **Nexus Mods Premium** (the Nexus API only issues automated downloads to Premium accounts) |
| Disk | ~82 GB free during install (~57 GB after) |
| OS | Windows 10/11 x64 · Linux x64 |

## Quick start

1. Download the binary for your platform from the [latest release](https://github.com/Mhsbrian/Nerevarine/releases/latest).
2. Run it. The wizard detects your Morrowind install, asks where to put the modded setup,
   and validates your Nexus API key.
3. Wait. Every step is **resumable** — close the app, lose the network, even reboot: relaunch
   and it continues exactly where it left off.
4. Play from the Nerevarine launcher it installs at the end.

## How it works

```
curated source list ──► curation pipeline ──► canonical modlist.json (order = load order)
                                              │
                     ┌────────────────────────┴──────────────────────┐
                     ▼                                               ▼
              umo download plan                            openmw.cfg composition
                     │                                               │
   tools → downloads → fixups → ini import → cfg → settings → delta merge → navmesh → validate
```

- A **curation pipeline** compiles the curated source list in `data/` plus a layered rule set (variant
  picks, load-order constraints, dependency additions, script repairs) into one canonical
  `data/modlist.json`. Array order *is* load order; CI re-verifies it on every change.
- The installer is a **verifier-driven step pipeline**: every step proves completion against
  real on-disk artifacts (hashes, probes, markers), which is what makes any interruption
  safely resumable.
- Downloads and extraction run through [umo](https://modding-openmw.gitlab.io/umo/); merging
  and pre-generation use the [MOMW tools pack](https://modding-openmw.gitlab.io/momw-tools-pack/)
  (delta-plugin, navmeshtool) — all sha256-pinned, all invoked as separate processes.
- Known upstream mod bugs are repaired at install time by small record-fix plugins and
  targeted Lua patches (documented in `data/fixups/`), so the shipped setup boots clean.

## Building from source

```sh
dotnet test                                # 169 tests, no Windows needed
dotnet run --project src/Mri.App           # run the wizard (Linux dev works)

# Single-file binaries (also produced by CI):
dotnet publish src/Mri.App -c Release -r win-x64   -p:PublishSingleFile=true -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
dotnet publish src/Mri.App -c Release -r linux-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/linux-x64
```

| Path | What |
|---|---|
| `src/Mri.App` | Avalonia wizard + Nerevarine launcher (ships as one self-contained binary) |
| `src/Mri.Core` | Detection, tools, umo orchestration, cfg generation, install pipeline |
| `src/Mri.Curation` | Spreadsheet → canonical modlist pipeline (CLI, runs anywhere) |
| `data/` | Modlist, curation rules, resolve cache, fixups, tool manifest |
| `docs/` | [Architecture](docs/ARCHITECTURE.md) · [Curation guide](docs/CURATION.md) · [QA checklist](docs/QA-CHECKLIST.md) |

## Credits & licensing

This project is MIT-licensed **installer code only**. It downloads mods from their original
sources at install time and redistributes **no game assets and no mod content**. You need your
own legitimate copy of Morrowind GOTY.

Full credit to the mod authors whose work makes the setup what it is, to the
[OpenMW](https://openmw.org) team, and to the
[Modding-OpenMW](https://modding-openmw.com) community for umo, the tools pack and their
field-tested load-order data. Architectural patterns were studied from
[Kezyma/Morrowind-Remastered](https://github.com/Kezyma/Morrowind-Remastered); no code was
copied. umo and the MOMW tools are AGPL/GPL and are invoked strictly as separate processes.
