#!/usr/bin/env python3
"""Generate data/fixups/MRI_AbeceanGreetingFix.esp.

The Abecean Isles (xOTM_01.ESP, upstream dead since 2024-04) ships two
Greeting 5 INFO records with no speaker filter — one gated only by a
Random100 > 50 coin flip, one with no conditions at all. Any NPC anywhere
whose greeting lookup falls through the conditioned records above lands on
a Sload riddle (field report: Seyda Neen census NPCs and shopkeepers).
The author filtered the other 192 dialogue records (ONAM/FNAM/RNAM/ANAM);
no recoverable intended scope exists for this pair (no Sload NPC record,
no coherent cell naming), so this patch overrides both records verbatim
plus an added never-true condition (Global Random100 < 0) — the least
invasive neutralization, reversible by dropping the patch.

Reads the installed mod, emits the patch esp. Byte-level copy: text,
ids and chain links stay identical to the source records.
"""
import struct
import sys
from pathlib import Path

SRC = Path.home() / "MorrowindRemake/mods/morrowind-remake/NewMods/TheAbeceanIsles/xOTM_01.ESP"
OUT = Path(__file__).resolve().parent.parent / "data/fixups/MRI_AbeceanGreetingFix.esp"
TARGETS = {b"304635424997631748", b"25186850513767963"}


def records(data):
    pos = 0
    while pos < len(data):
        tag = data[pos:pos + 4]
        size = struct.unpack_from("<I", data, pos + 4)[0]
        yield tag, data[pos:pos + 16 + size], data[pos + 16:pos + 16 + size]
        pos += 16 + size


def subrecords(payload):
    pos = 0
    while pos < len(payload):
        tag = payload[pos:pos + 4]
        size = struct.unpack_from("<I", payload, pos + 4)[0]
        yield tag, payload[pos + 8:pos + 8 + size]
        pos += 8 + size


def build_record(tag, subs):
    payload = b"".join(t + struct.pack("<I", len(p)) + p for t, p in subs)
    return tag + struct.pack("<III", len(payload), 0, 0) + payload


def main():
    data = SRC.read_bytes()
    dial_raw = None
    picked = {}      # inam -> (dial_raw, [(tag, payload), ...])
    scvr_template = None

    for tag, raw, payload in records(data):
        if tag == b"DIAL":
            dial_raw = raw
        elif tag == b"INFO":
            subs = list(subrecords(payload))
            inam = next((p.rstrip(b"\0") for t, p in subs if t == b"INAM"), b"")
            if inam in TARGETS:
                picked[inam] = (dial_raw, subs)
                for t, p in subs:
                    if t == b"SCVR" and b"Random100" in p:
                        scvr_template = bytearray(p)

    missing = TARGETS - picked.keys()
    if missing:
        sys.exit(f"records not found in {SRC}: {missing} — mod version changed?")
    if scvr_template is None:
        sys.exit("Random100 SCVR template not found — mod version changed?")
    dials = {id(d[0]): d[0] for d in picked.values()}
    if len(dials) != 1:
        sys.exit("targets span multiple DIAL topics — layout assumption broken")

    # SCVR layout: index(1) type(1) function(2) operator(1) name(...).
    # Template is "Global Random100 >"; flip operator to '<' ('4'); INTV 0.
    def never_true(index_char):
        scvr = bytearray(scvr_template)
        scvr[0] = ord(index_char)
        scvr[4] = ord("4")
        return [(b"SCVR", bytes(scvr)), (b"INTV", struct.pack("<i", 0))]

    out_records = []
    for inam in sorted(picked, key=lambda k: 0 if k == b"304635424997631748" else 1):
        _, subs = picked[inam]
        existing = sum(1 for t, _ in subs if t == b"SCVR")
        insert_at = next(i for i, (t, _) in enumerate(subs) if t == b"BNAM")
        new_subs = subs[:insert_at] + never_true(str(existing)) + subs[insert_at:]
        out_records.append(build_record(b"INFO", new_subs))

    dial_bytes = next(iter(dials.values()))

    hedr = struct.pack("<fI", 1.3, 0)
    hedr += b"MRI installer".ljust(32, b"\0")
    hedr += b"Adds the speaker conditions the author forgot on two Abecean" \
            b" Isles fallback greetings (see repo tools/make-abecean-greeting-fix.py)".ljust(256, b"\0")
    hedr += struct.pack("<I", 1 + len(out_records))
    masters = []
    for name, path in ((b"Morrowind.esm", Path.home() /
                        ".local/share/Steam/steamapps/common/Morrowind/Data Files/Morrowind.esm"),
                       (b"xOTM_01.ESP", SRC)):
        masters.append((b"MAST", name + b"\0"))
        masters.append((b"DATA", struct.pack("<Q", path.stat().st_size)))
    tes3 = build_record(b"TES3", [(b"HEDR", hedr)] + masters)

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_bytes(tes3 + dial_bytes + b"".join(out_records))
    print(f"wrote {OUT} ({OUT.stat().st_size} bytes, "
          f"{1 + len(out_records)} records + header)")


if __name__ == "__main__":
    main()
