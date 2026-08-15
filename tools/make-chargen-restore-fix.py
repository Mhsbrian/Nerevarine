#!/usr/bin/env python3
"""Generate data/fixups/MRI_ChargenVanillaScripts.esp.

Quest Voice Greetings overrides CharGenClassNPC and CharGenRaceNPC to add
voice lines; its class script hard-references its own prop
("vd_misc_lw_platter"->OnActivate). Field failure 2026-08-15: that instance
was absent, the script crashed ("Failed to find an instance of object"),
OpenMW disabled it, and the census-office chargen state machine died
(no race/class/sign menus, insult greetings via broken NoLore gating,
default Acrobat class on paper pickup).

This patch byte-copies the two VANILLA script records from Morrowind.esm;
loading last (mri-fixups), they win and chargen runs on stock code. QVG's
voice INFOs everywhere else are untouched.
"""
import struct
import sys
from pathlib import Path

ESM = Path.home() / ".local/share/Steam/steamapps/common/Morrowind/Data Files/Morrowind.esm"
QVG = Path.home() / ("MorrowindRemake/mods/morrowind-remake/Dialogue/QuestVoiceGreetings/"
                     "Quest Voice Greetings 3.82/Quest Voice Greetings.ESP")
OUT = Path(__file__).resolve().parent.parent / "data/fixups/MRI_ChargenVanillaScripts.esp"
TARGETS = {b"chargenclassnpc", b"chargenracenpc"}


def records(data):
    pos = 0
    while pos < len(data):
        tag = data[pos:pos + 4]
        size = struct.unpack_from("<I", data, pos + 4)[0]
        yield tag, data[pos:pos + 16 + size], data[pos + 16:pos + 16 + size]
        pos += 16 + size


def script_id(payload):
    # SCPT: first subrecord SCHD, header starts with the 32-byte script name.
    if payload[:4] != b"SCHD":
        return b""
    return payload[8:8 + 32].split(b"\0")[0].strip().lower()


def build_record(tag, subs):
    payload = b"".join(t + struct.pack("<I", len(p)) + p for t, p in subs)
    return tag + struct.pack("<III", len(payload), 0, 0) + payload


def main():
    found = {}
    for tag, raw, payload in records(ESM.read_bytes()):
        if tag == b"SCPT" and script_id(payload) in TARGETS:
            found[script_id(payload)] = raw
    missing = TARGETS - found.keys()
    if missing:
        sys.exit(f"vanilla scripts not found in {ESM}: {missing}")

    hedr = struct.pack("<fI", 1.3, 0)
    hedr += b"MRI installer".ljust(32, b"\0")
    hedr += (b"Restores vanilla CharGenClassNPC/CharGenRaceNPC over Quest Voice"
             b" Greetings (see repo tools/make-chargen-restore-fix.py)").ljust(256, b"\0")
    hedr += struct.pack("<I", len(found))
    masters = []
    for name, path in ((b"Morrowind.esm", ESM), (b"Quest Voice Greetings.ESP", QVG)):
        masters.append((b"MAST", name + b"\0"))
        masters.append((b"DATA", struct.pack("<Q", path.stat().st_size)))
    tes3 = build_record(b"TES3", [(b"HEDR", hedr)] + masters)

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_bytes(tes3 + b"".join(found[k] for k in sorted(found)))
    print(f"wrote {OUT} ({OUT.stat().st_size} bytes, {len(found)} vanilla scripts)")


if __name__ == "__main__":
    main()
