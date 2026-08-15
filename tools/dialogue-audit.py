#!/usr/bin/env python3
"""Merged-dialogue-chain auditor using OpenMW 0.51's EXACT semantics
(verified against components/esm3/infoorder.hpp, loaddial.cpp,
apps/openmw/mwdialogue/filter.cpp on the openmw-51 branch):

  - INFO insertion per topic: same id + same PNAM -> replace in place;
    PNAM empty -> FRONT; PNAM found -> immediately after it; PNAM not
    found -> BACK; same id + different PNAM -> replace + move (splice).
    DELE'd infos anchor during load, removed at the end. Ids are
    case-insensitive. INFOs attach to the most recently read DIAL.
  - Greeting selection: topics scanned in case-insensitive lexicographic
    id order ("Greeting 0".."Greeting 9"); within a topic, chain order;
    the FIRST info whose actor/player/select/disposition gates all pass
    fires immediately.

Hazard report: for each Greeting topic, every info reachable by an
ORDINARY NPC — i.e. with no actor/class/faction/cell/pc-faction filter,
no Local/NotLocal select (script-var gates like NoLore), no Journal
select — sitting ABOVE the first vanilla (Morrowind.esm) info of the
same unfiltered kind. Those hijack vanilla greeting flow for everyone
(the Sload-riddle / chargen class of bug). Global/Item/Dead selects are
kept as "soft" (they can pass for anyone); disposition <= 50 counts as
passable for a neutral NPC.
"""
import struct
import sys
from collections import defaultdict
from pathlib import Path

CFG = Path.home() / ".config/openmw/openmw.cfg"
OUT = Path(__file__).resolve().parent.parent / "build/dialogue-audit.md"


def parse_cfg():
    data_dirs, content = [], []
    for line in CFG.read_text().splitlines():
        if line.startswith("data="):
            data_dirs.append(Path(line[5:].strip().strip('"').replace('&"', '"').replace("&&", "&")))
        elif line.startswith("content="):
            content.append(line[8:].strip())
    return data_dirs, content


def build_vfs(data_dirs):
    vfs = {}
    for d in data_dirs:
        if not d.is_dir():
            continue
        for p in d.rglob("*"):
            if p.is_file():
                vfs[str(p.relative_to(d)).replace("\\", "/").lower()] = p
    return vfs


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
        yield tag.decode("ascii", "replace"), payload[pos + 8:pos + 8 + size]
        pos += 8 + size


def s(b):
    return b.split(b"\0")[0].decode("cp1252", "replace")


class Info:
    __slots__ = ("id", "prev", "src", "text", "actor", "race", "cls", "faction",
                 "cell", "pcfaction", "disp", "selects", "deleted")


def parse_info(payload, src):
    inf = Info()
    inf.src = src
    inf.id = inf.prev = ""
    inf.text = inf.actor = inf.race = inf.cls = inf.faction = ""
    inf.cell = inf.pcfaction = ""
    inf.disp = 0
    inf.selects = []
    inf.deleted = False
    for tag, sub in subrecords(payload):
        if tag == "INAM":
            inf.id = s(sub).lower()
        elif tag == "PNAM":
            inf.prev = s(sub).lower()
        elif tag == "DATA" and len(sub) >= 8:
            inf.disp = struct.unpack_from("<i", sub, 4)[0]
        elif tag == "ONAM":
            inf.actor = s(sub)
        elif tag == "RNAM":
            inf.race = s(sub)
        elif tag == "CNAM":
            inf.cls = s(sub)
        elif tag == "FNAM":
            inf.faction = s(sub)
        elif tag == "ANAM":
            inf.cell = s(sub)
        elif tag == "DNAM":
            inf.pcfaction = s(sub)
        elif tag == "NAME":
            inf.text = s(sub)
        elif tag == "SCVR":
            inf.selects.append([sub.decode("cp1252", "replace"), None])
        elif tag in ("INTV", "FLTV") and inf.selects and inf.selects[-1][1] is None:
            inf.selects[-1][1] = (struct.unpack("<i", sub)[0] if tag == "INTV"
                                  else round(struct.unpack("<f", sub)[0], 3))
        elif tag == "DELE":
            inf.deleted = True
    return inf


def hard_gated(inf):
    """True if an ordinary mainland NPC can never hit this info."""
    if inf.actor or inf.cls or inf.faction or inf.cell or inf.pcfaction or inf.race:
        return True
    if inf.disp > 50:
        return True
    for sc, _val in inf.selects:
        if len(sc) < 5:
            continue
        stype = sc[1]      # 1 Function 2 Global 3 Local 4 Journal 5 Item 6 Dead 7.. Not*
        if stype in "34789ABC":   # Local, Journal, NotId/Faction/Class/Race/Cell, NotLocal
            return True
        if stype == "1":          # engine Function (choice, pc stats, ...) — treat as gate
            return True
        if stype == "2" and "random100" in sc[5:].lower():
            op, val = sc[4], _val
            if (op == "4" and (val is not None and val <= 0)) or \
               (op == "2" and (val is not None and val >= 99)):
                return True    # statically impossible (Random100 is 0..99)
    return False


def main():
    data_dirs, content = parse_cfg()
    vfs = build_vfs(data_dirs)
    game_dir = None
    for line in CFG.read_text().splitlines():
        if line.startswith("data=") and "steamapps" in line.lower():
            game_dir = Path(line[5:].strip().strip('"'))
    chains = defaultdict(list)          # topic(lower) -> list[Info]
    positions = defaultdict(dict)       # topic -> id -> index resolution via list scan
    topic_names = {}

    def resolve(name):
        cand = vfs.get(name.lower())
        if cand:
            return cand
        if game_dir and (game_dir / name).exists():
            return game_dir / name
        return None

    for c in content:
        p = resolve(c)
        if p is None:
            print(f"WARN: cannot resolve {c}", file=sys.stderr)
            continue
        cur_topic = None
        for tag, payload in records(p.read_bytes()):
            t = tag.decode("ascii", "replace")
            if t == "DIAL":
                nm = ""
                for st, sub in subrecords(payload):
                    if st == "NAME":
                        nm = s(sub)
                        break
                cur_topic = nm.lower()
                topic_names.setdefault(cur_topic, nm)
            elif t == "INFO" and cur_topic is not None:
                inf = parse_info(payload, c)
                chain = chains[cur_topic]
                idx = positions[cur_topic]
                # exact InfoOrder::insertInfo semantics
                if inf.id in idx and chain[idx[inf.id]].prev == inf.prev:
                    chain[idx[inf.id]] = inf
                    continue
                if not inf.prev:
                    before = 0
                elif inf.prev in idx:
                    before = idx[inf.prev] + 1
                else:
                    before = len(chain)
                if inf.id in idx:
                    old = idx[inf.id]
                    chain.pop(old)
                    if old < before:
                        before -= 1
                chain.insert(before, inf)
                positions[cur_topic] = {x.id: i for i, x in enumerate(chain)}

    report = ["# Dialogue chain audit (exact OpenMW 0.51 semantics)\n"]
    findings = 0
    greet_topics = sorted((t for t in chains if t.startswith("greeting")),
                          key=str.lower)
    for t in greet_topics:
        chain = [i for i in chains[t] if not i.deleted]
        vanilla_first = next((i for i, inf in enumerate(chain)
                              if inf.src.lower() == "morrowind.esm"), len(chain))
        hazards = [(i, inf) for i, inf in enumerate(chain)
                   if not hard_gated(inf) and inf.src.lower() not in
                   ("morrowind.esm", "tribunal.esm", "bloodmoon.esm")]
        report.append(f"\n## {topic_names[t]} — chain {len(chain)}, first vanilla entry at #{vanilla_first}\n")
        if not hazards:
            report.append("- OK: no ungated mod entries in this topic")
            continue
        for i, inf in hazards:
            findings += 1
            sev = "SEVERE(above all vanilla)" if i < vanilla_first else "flavor-injection"
            sel = "; ".join(f"{sc[1:6]}…{sc[5:]}{'=' if v is None else ''}{v if v is not None else ''}"
                            for sc, v in inf.selects)
            report.append(f"- #{i}/{len(chain)} [{sev}] `{inf.src}` disp<={inf.disp} "
                          f"[{sel}]: \"{inf.text[:60]}\"")
    OUT.write_text("\n".join(report) + "\n")
    print(f"{findings} greeting hazards -> {OUT}")


if __name__ == "__main__":
    main()
