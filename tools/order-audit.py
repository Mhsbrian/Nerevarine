#!/usr/bin/env python3
"""Deterministic load-order conflict auditor. Reads the FINAL openmw.cfg
(ground truth), the emitted modlist (sheet intent via provenance.csvRow),
and every mounted plugin's bytes. No heuristics about intent beyond the
sheet order; every finding carries evidence.

Checks:
  A masters:    every content plugin's MAST entries load earlier than it
  B inversions: plugin pairs that SHARE edited record IDs and whose cfg
                order is inverted vs sheet order (last-wins hazards);
                pairs consistent with the embedded MOMW reference order
                are tagged SANCTIONED, dialogue-block members INTENTIONAL
  C interfaces: Lua interface providers must precede consumers in content
  D shadowing:  same VFS path shipped by multiple MODS (scripts = always
                reported; other files summarized, inversions highlighted)
  E hygiene:    plugins both in content= and groundcover=; duplicate lines
"""
import json
import re
import struct
import sys
from collections import defaultdict
from pathlib import Path

CFG = Path.home() / ".config/openmw/openmw.cfg"
REPO = Path(__file__).resolve().parent.parent
MODLIST = REPO / "data/modlist.json"
MOMW_ORDER = REPO / "data/momw-content-order.txt"
OUT = REPO / "build/order-audit.md"

PLUGIN_EXTS = {".esm", ".esp", ".omwaddon", ".omwgame"}
BUILTIN_INTERFACES = {
    "AI", "Activation", "AnimationController", "Camera", "Combat", "Controls",
    "GamepadControls", "ItemUsage", "MWUI", "Music", "SettingsMenu", "Settings",
    "SkillProgression", "UI", "Crimes",
}


def parse_cfg():
    data_dirs, content, groundcover = [], [], []
    for line in CFG.read_text().splitlines():
        if line.startswith("data="):
            data_dirs.append(Path(line[5:].strip().strip('"').replace('&"', '"').replace("&&", "&")))
        elif line.startswith("content="):
            content.append(line[8:].strip())
        elif line.startswith("groundcover="):
            groundcover.append(line[12:].strip())
    return data_dirs, content, groundcover


def build_vfs(data_dirs):
    """path(lower, /) -> list of (mount_index, real_path); later wins."""
    vfs = defaultdict(list)
    for i, d in enumerate(data_dirs):
        if not d.is_dir():
            continue
        for p in d.rglob("*"):
            if p.is_file():
                rel = str(p.relative_to(d)).replace("\\", "/").lower()
                vfs[rel].append((i, p))
    return vfs


def resolve_plugin(vfs, name):
    hits = vfs.get(name.lower())
    return hits[-1][1] if hits else None


def records(data):
    pos, n = 0, len(data)
    while pos + 16 <= n:
        tag = data[pos:pos + 4]
        size = struct.unpack_from("<I", data, pos + 4)[0]
        yield tag, data[pos + 16:pos + 16 + size]
        pos += 16 + size


def subrecords(payload):
    pos, n = 0, len(payload)
    while pos + 8 <= n:
        tag = payload[pos:pos + 4]
        size = struct.unpack_from("<I", payload, pos + 4)[0]
        yield tag, payload[pos + 8:pos + 8 + size]
        pos += 8 + size


def first_sub(payload, want):
    for tag, sub in subrecords(payload):
        if tag == want:
            return sub
    return None


def plugin_inventory(path):
    """Returns (masters, {(rtype, rid)}). rid normalized lowercase."""
    data = path.read_bytes()
    masters, ids = [], set()
    for tag, payload in records(data):
        t = tag.decode("ascii", "replace")
        if t == "TES3":
            for st, sub in subrecords(payload):
                if st == b"MAST":
                    masters.append(sub.split(b"\0")[0].decode("cp1252", "replace"))
            continue
        rid = None
        if t == "SCPT":
            schd = first_sub(payload, b"SCHD")
            if schd:
                rid = schd[:32].split(b"\0")[0].decode("cp1252", "replace")
        elif t == "INFO":
            inam = first_sub(payload, b"INAM")
            if inam:
                rid = inam.rstrip(b"\0").decode("cp1252", "replace")
        elif t == "CELL":
            name = first_sub(payload, b"NAME")
            nm = name.rstrip(b"\0").decode("cp1252", "replace") if name else ""
            if nm:
                rid = nm
            else:
                d = first_sub(payload, b"DATA")
                if d and len(d) >= 12:
                    _, x, y = struct.unpack_from("<iii", d)
                    rid = f"ext({x},{y})"
        elif t == "LAND":
            d = first_sub(payload, b"INTV")
            if d and len(d) >= 8:
                x, y = struct.unpack_from("<ii", d)
                rid = f"land({x},{y})"
        else:
            name = first_sub(payload, b"NAME")
            if name is not None:
                rid = name.rstrip(b"\0").decode("cp1252", "replace")
        if rid:
            ids.add((t, rid.lower()))
    return masters, ids


def main():
    data_dirs, content, groundcover = parse_cfg()
    modlist = json.loads(MODLIST.read_text())["mods"]
    vfs = build_vfs(data_dirs)

    plugin_mod = {}          # plugin lower -> (mod id, csvRow)
    dialogue_block = set()   # mods we intentionally moved late
    for m in modlist:
        row = (m.get("provenance") or {}).get("csvRow", 0)
        for c in m.get("content", []):
            plugin_mod[c["file"].lower()] = (m["id"], row)
        if m.get("category") == "Dialogue":
            dialogue_block.add(m["id"])

    momw_pos = {}
    if MOMW_ORDER.exists():
        for i, line in enumerate(MOMW_ORDER.read_text().splitlines()):
            momw_pos[line.strip().lower()] = i

    pos = {c.lower(): i for i, c in enumerate(content)}
    report = ["# Load-order audit\n"]
    problems = 0

    # Parse every content plugin once.
    inv = {}
    unresolved = []
    for c in content:
        p = resolve_plugin(vfs, c)
        if p is None:
            unresolved.append(c)
            continue
        try:
            inv[c] = plugin_inventory(p)
        except Exception as e:
            unresolved.append(f"{c} (parse error: {e})")

    # A: masters
    report.append("## A. Master ordering\n")
    bad = []
    for c, (masters, _) in inv.items():
        for mast in masters:
            mp = pos.get(mast.lower())
            if mp is None:
                bad.append(f"- `{c}` requires ABSENT master `{mast}`")
            elif mp > pos[c.lower()]:
                bad.append(f"- `{c}` (#{pos[c.lower()]}) loads BEFORE its master `{mast}` (#{mp})")
    report += (bad or ["- OK: all masters precede their dependents\n"])
    problems += len(bad)

    # B: override inversions vs sheet
    report.append("\n## B. Same-record override pairs inverted vs sheet order\n")
    owner = defaultdict(list)   # (rtype, rid) -> [plugin, ...] in cfg order
    for c in sorted(inv, key=lambda c: pos[c.lower()]):
        for key in inv[c][1]:
            owner[key].append(c)
    pair_hits = defaultdict(lambda: defaultdict(int))
    for key, plugs in owner.items():
        if len(plugs) < 2:
            continue
        for i in range(len(plugs)):
            for j in range(i + 1, len(plugs)):
                a, b = plugs[i], plugs[j]     # a before b in cfg; b wins
                ma = plugin_mod.get(a.lower())
                mb = plugin_mod.get(b.lower())
                if not ma or not mb or ma[0] == mb[0]:
                    continue
                if ma[1] and mb[1] and mb[1] < ma[1]:   # sheet said b before a
                    pair_hits[(a, b)][key[0]] += 1
    rows = []
    for (a, b), types in sorted(pair_hits.items(),
                                key=lambda kv: -sum(kv[1].values())):
        ma, mb = plugin_mod[a.lower()][0], plugin_mod[b.lower()][0]
        tags = []
        if a.lower() in momw_pos and b.lower() in momw_pos:
            tags.append("SANCTIONED(momw)" if momw_pos[a.lower()] < momw_pos[b.lower()]
                        else "MOMW-DISAGREES-TOO")
        if ma in dialogue_block or mb in dialogue_block:
            tags.append("INTENTIONAL(dialogue-block)")
        n = sum(types.values())
        rows.append(f"- `{b}` now OVERRIDES `{a}` on {n} records "
                    f"({', '.join(f'{k}:{v}' for k, v in sorted(types.items()))}) "
                    f"— sheet wanted the reverse [{' '.join(tags) or 'UNSANCTIONED'}]")
    unsanc = [r for r in rows if "UNSANCTIONED" in r]
    report += ([f"{len(rows)} inverted overriding pairs total; "
                f"{len(unsanc)} unsanctioned:\n"] + rows[:80]) if rows else ["- OK: none\n"]
    problems += len(unsanc)

    # C: lua interfaces
    report.append("\n## C. Lua interface provider/consumer order\n")
    script_owner = {}
    provides, consumes = defaultdict(set), defaultdict(set)
    for c in content:
        if not c.lower().endswith(".omwscripts"):
            continue
        p = resolve_plugin(vfs, c)
        if not p:
            continue
        mod_scripts = []
        for line in p.read_text(errors="replace").splitlines():
            line = line.split("#")[0].strip()
            if ":" in line:
                mod_scripts.append(line.split(":", 1)[1].strip().replace("\\", "/").lower())
        for s in mod_scripts:
            script_owner.setdefault(s, c)
            hit = vfs.get(s)
            if not hit:
                continue
            try:
                text = hit[-1][1].read_text(errors="replace")
            except Exception:
                continue
            for m in re.finditer(r'interfaceName\s*=\s*["\']([\w]+)["\']', text):
                provides[m.group(1)].add(c)
            for m in re.finditer(r'\bI\.([A-Z]\w+)', text):
                consumes[m.group(1)].add(c)
            for m in re.finditer(r'\binterfaces\.([A-Z]\w+)', text):
                consumes[m.group(1)].add(c)
    bad = []
    for iface, users in sorted(consumes.items()):
        if iface in BUILTIN_INTERFACES:
            continue
        provs = provides.get(iface)
        if not provs:
            for u in users:
                if u not in provides.get(iface, set()):
                    bad.append(f"- `{u}` consumes interface `{iface}` with NO provider in load")
            continue
        pmin = min(pos[p.lower()] for p in provs)
        for u in users:
            if u in provs:
                continue
            if pos[u.lower()] < pmin:
                bad.append(f"- `{u}` (#{pos[u.lower()]}) consumes `{iface}` provided by "
                           f"{sorted(provs)} (first at #{pmin}) — CONSUMER FIRST")
    report += (bad or ["- OK: all providers precede consumers\n"])
    problems += len(bad)

    # D: cross-mod VFS shadowing (scripts always; others summarized)
    report.append("\n## D. Cross-mod VFS shadowing\n")
    dir_mod = {}
    for m in modlist:
        for i, d in enumerate(data_dirs):
            s = str(d).lower()
            for dp in m.get("dataPaths", []):
                if s.endswith("/" + dp.lower()):
                    dir_mod[i] = m["id"]
    script_shadow, other = [], defaultdict(int)
    for rel, hits in vfs.items():
        if len(hits) < 2:
            continue
        mods = [(dir_mod.get(i), i) for i, _ in hits]
        distinct = {m for m, _ in mods if m}
        if len(distinct) < 2:
            continue
        if rel.endswith((".lua", ".omwscripts")):
            chain = " -> ".join(f"{m or f'dir{i}'}" for m, i in mods)
            script_shadow.append(f"- `{rel}`: {chain} (LAST wins)")
        else:
            key = tuple(sorted(distinct))
            other[key] += 1
    report.append(f"{len(script_shadow)} script files shadowed across mods:\n")
    report += script_shadow[:40] or ["- OK: none\n"]
    report.append(f"\nNon-script cross-mod overlaps (mod-set: files) — top 15:\n")
    for key, n in sorted(other.items(), key=lambda kv: -kv[1])[:15]:
        report.append(f"- {' + '.join(key)}: {n}")
    problems += len(script_shadow)

    # E: hygiene
    report.append("\n## E. Hygiene\n")
    both = set(c.lower() for c in content) & set(g.lower() for g in groundcover)
    dup = [c for c in set(x.lower() for x in content) if content and
           sum(1 for x in content if x.lower() == c) > 1]
    lines = []
    for b in both:
        lines.append(f"- `{b}` is in BOTH content= and groundcover= (double grass)")
    for d in dup:
        lines.append(f"- duplicate content line: `{d}`")
    for u in unresolved:
        lines.append(f"- content file not resolvable on disk: {u}")
    report += (lines or ["- OK\n"])
    problems += len(lines)

    OUT.parent.mkdir(exist_ok=True)
    OUT.write_text("\n".join(report) + "\n")
    print(f"{problems} findings -> {OUT}")


if __name__ == "__main__":
    main()
