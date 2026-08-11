#!/usr/bin/env python3
"""Validate our emitted umo modlist against umo's OWN Pydantic model.

Usage:
    validate-moddesc.py --umo-src <path-to-umo-src-dir> --list <umo-list.json>

The umo source dir must contain momw_types.py and nodeps.py (clone
https://gitlab.com/modding-openmw/umo at the tag pinned in data/tools.json's
momw-tools-pack entry and point at its src/). Nothing from umo is vendored
into this repo; the model is imported at check time only.

Checks, in order:
  1. Every entry validates against ModDesc (all errors reported, not just the first).
  2. Every entry is claimable by a download handler — umo ignores our
     `handler` field and re-derives it (nexus URL regex, gitlab/github URL
     prefix, direct_download presence). Unclaimed mods land in umo's silent
     "manual" pile and never download.
  3. `dir` values are unique — umo's cache is keyed by dir; a collision
     silently overwrites a mod.
  4. No entry uses a category umo filters out (settings/Tools/FirstSteps).

Requires: pip install pydantic
"""

import argparse
import json
import re
import sys
import tempfile
import textwrap
from pathlib import Path

NEXUS_URL_RE = re.compile(r"https://www.nexusmods.com/(.*?)/.*/([0-9]+)")
FILTERED_CATEGORIES = {"settings", "Tools", "FirstSteps"}


def import_moddesc(umo_src: Path):
    """Import umo's momw_types with its logging shimmed out."""
    shim_dir = Path(tempfile.mkdtemp(prefix="umo-shim-"))
    (shim_dir / "log.py").write_text(textwrap.dedent("""\
        import logging

        def get_logger():
            return logging.getLogger("umo-validate")
    """))
    sys.path.insert(0, str(shim_dir))
    sys.path.insert(1, str(umo_src))
    import momw_types  # noqa: E402

    return momw_types.ModDesc


def handler_claims(mod: dict) -> str | None:
    """Faithful replica of umo handlers' can() dispatch (handlers.py)."""
    url = mod.get("url") or ""
    download_info = mod.get("download_info") or [{}]
    if NEXUS_URL_RE.match(url):
        return "nexus"
    if url.startswith("https://gitlab.com/"):
        return "raw-gitlab"
    if url.startswith("https://github.com/"):
        return "github"
    if download_info and download_info[0].get("direct_download"):
        return "direct"
    return None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--umo-src", required=True, type=Path)
    parser.add_argument("--list", required=True, type=Path)
    args = parser.parse_args()

    ModDesc = import_moddesc(args.umo_src)
    mods = json.loads(args.list.read_text())

    errors: list[str] = []
    warnings: list[str] = []
    dirs_seen: dict[str, str] = {}

    for i, mod in enumerate(mods):
        label = f"[{i}] {mod.get('slug') or mod.get('name') or '?'}"

        try:
            ModDesc.model_validate(mod)
        except Exception as e:  # pydantic.ValidationError
            errors.append(f"{label}: ModDesc validation failed:\n"
                          + textwrap.indent(str(e), "    "))
            continue

        handler = handler_claims(mod)
        if handler is None:
            errors.append(
                f"{label}: NO handler claims this mod (url={mod.get('url')!r}, "
                f"direct_download={ (mod.get('download_info') or [{}])[0].get('direct_download')!r}) "
                "— umo would divert it to the never-downloaded manual pile")

        d = mod.get("dir", "")
        if d in dirs_seen:
            errors.append(f"{label}: dir '{d}' already used by {dirs_seen[d]} "
                          "— umo's cache would silently overwrite one of them")
        else:
            dirs_seen[d] = label

        if mod.get("category") in FILTERED_CATEGORIES:
            warnings.append(f"{label}: category '{mod['category']}' is filtered out by umo")

    for w in warnings:
        print(f"WARN  {w}")
    for e in errors:
        print(f"ERROR {e}")

    print(f"\n{len(mods)} mods checked: {len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
