#!/usr/bin/env python3
"""Generate the Nerevarine icon: an original moon-and-star roundel in the
launcher's parchment palette (crescent + eight-pointed star are universal
celestial motifs drawn from scratch — no game art involved).

Outputs:
  src/Mri.App/Assets/nerevarine.png   (256px, window/desktop icon)
  src/Mri.App/Assets/nerevarine.ico   (16/24/32/48/64/128/256)
"""
import math
from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent.parent / "src/Mri.App/Assets"

INK = (46, 32, 19, 255)          # #2E2013
FIELD = (122, 46, 29, 255)       # #7A2E1D deep redware
FIELD_EDGE = (90, 33, 21, 255)   # #5A2115
GOLD = (176, 141, 62, 255)       # #B08D3E
PARCH = (240, 228, 192, 255)     # #F0E4C0


def star_points(cx, cy, r_out, r_in, points=8, rot=-90):
    pts = []
    for i in range(points * 2):
        r = r_out if i % 2 == 0 else r_in
        a = math.radians(rot + i * (360 / (points * 2)))
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def render(size=1024):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    s = size / 256.0
    cx = cy = size / 2

    # Redware field with double gold ring (echoes the launcher's scroll frame).
    d.ellipse([12 * s, 12 * s, size - 12 * s, size - 12 * s], fill=FIELD_EDGE)
    d.ellipse([18 * s, 18 * s, size - 18 * s, size - 18 * s], fill=FIELD)
    d.ellipse([12 * s, 12 * s, size - 12 * s, size - 12 * s], outline=GOLD, width=int(7 * s))
    d.ellipse([28 * s, 28 * s, size - 28 * s, size - 28 * s], outline=GOLD, width=int(3 * s))

    # Crescent: parchment disc minus offset disc, tilted open to upper-right.
    moon = Image.new("L", (size, size), 0)
    dm = ImageDraw.Draw(moon)
    dm.ellipse([cx - 78 * s, cy - 78 * s, cx + 78 * s, cy + 78 * s], fill=255)
    dm.ellipse([cx - 78 * s + 34 * s, cy - 78 * s - 26 * s,
                cx + 78 * s + 34 * s, cy + 78 * s - 26 * s], fill=0)
    img.paste(Image.new("RGBA", (size, size), PARCH), (0, 0), moon)

    # Eight-pointed star nested in the crescent's embrace.
    star_cx, star_cy = cx + 26 * s, cy - 22 * s
    d = ImageDraw.Draw(img)
    d.polygon(star_points(star_cx, star_cy, 46 * s, 17 * s), fill=GOLD)
    d.polygon(star_points(star_cx, star_cy, 30 * s, 11 * s), fill=PARCH)

    return img


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    big = render(1024)
    png256 = big.resize((256, 256), Image.LANCZOS)
    png256.save(OUT / "nerevarine.png")
    big.resize((256, 256), Image.LANCZOS).save(
        OUT / "nerevarine.ico",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print(f"wrote {OUT}/nerevarine.png + .ico")


if __name__ == "__main__":
    main()
