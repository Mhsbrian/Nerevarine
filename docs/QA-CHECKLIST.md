# QA checklist

## M1 — micro end-to-end smoke (Windows machine/VM) — THE gate before deep curation

Goal: prove the external CLI contracts (umo, NSIS silent install, delta-plugin, iniimporter)
with a hand-written 5-mod list before investing in curating 610 mods.

Setup:
- [ ] Windows 10/11 with real Morrowind GOTY (Steam install preferred, non-default library ideal)
- [ ] Nexus Premium account API key
- [ ] Build `MorrowindRemakeInstaller.exe` (`ci.yml` artifact or local publish)
- [ ] Swap the embedded modlist for a 5-mod smoke list (2 Nexus + 2 direct + 1 with a remove
      action; e.g. Patch for Purists, Expansion Delay, a Dropbox-hosted mod, a GitLab artifact)

Checks, in order:
- [ ] Steam auto-detection lists the right folder with a "Steam" badge (test: also a GOG install,
      also "Browse" to a fake folder → clear failure text)
- [ ] Tool acquisition: pack lands in `tools/momw-tools` FIRST, then OpenMW is 7z-extracted into
      `tools/openmw` (no installer window may ever appear; `$PLUGINSDIR`/uninstaller cleaned up;
      `openmw.exe` + `openmw-iniimporter.exe` + `openmw-navmeshtool.exe` present),
      `umo --version` prints. Re-test specifically with an install path containing a space.
- [ ] `umo-conf/config.json` accepted by umo — **verify key names** by diffing against a real
      `umo setup` output; fix `UmoConfigWriter` if they drifted
- [ ] `umo list add` accepts our emitted ModDesc JSON (adjust `ModlistCompiler` on validation errors)
- [ ] `umo install` downloads all 5 mods; **capture full stdout into
      `tests/Mri.Core.Tests/Fixtures/umo/`** and tighten `UmoProgressParser` against it
- [ ] Remove-action mod: the targeted file is really gone post-install
- [ ] `openmw.cfg` generated in `Documents\My Games\OpenMW`: fallback lines present, data order
      = list order, old cfg backed up
- [ ] delta-merge produced `mods/delta-merged/delta-merged.omwaddon`; phase-2 cfg references it last
- [ ] **OpenMW boots to the main menu and a new game starts with the 5 mods visibly active**
- [ ] Kill the app mid-download → relaunch → resumes at the right step, no duplicate work
- [ ] Delete `umo.exe` (simulate AV) → relaunch → clear "antivirus" guidance, re-extract works

## M4 — full-list hardening (after M2 curation)

- [ ] Full 610-mod install on a clean VM: duration, disk, peak network logged
- [ ] Spot-check 20 mods across categories in-game (textures, quests, lua UI, groundcover)
- [ ] `openmw-navmeshtool` completes; first game start does not rebuild navmeshes
- [ ] Retry/skip flow: block one mod's download (hosts file), verify skip produces a warning list
      and a cfg without the skipped mod's lines
- [ ] settings.cfg overlay preserves a user's pre-existing custom settings
- [ ] Re-running the finished installer = all steps "already done" in seconds
- [ ] Long-path stress: install dir ~70 chars deep
- [ ] Non-GOTY (missing expansions) install → warning shown on the game screen

## Release

- [ ] SmartScreen behavior on the unsigned exe documented (or signing cert acquired)
- [ ] Windows Defender scan of the full install dir — record any false positives + exclusion docs
- [ ] README quick-start screenshots
