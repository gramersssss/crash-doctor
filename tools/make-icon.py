"""Draw the Crash Doctor application icon into src/app.ico.

    python tools/make-icon.py                 # the shipped icon
    python tools/make-icon.py --sheet         # every palette side by side in test/icon-sheet.png, nothing written

The mark is a vital-signs trace that runs flat, spikes once and carries on: an instrument reading a fault. It is
drawn in the game's colours - Night City yellow, near-black and hot red - on a tile with one corner cut away, the
same cut the pause-menu skin uses. No game art, logo or typeface is involved; the colours and a cut corner are the
whole of the reference.

Each size is drawn separately at 8x and downsampled, and sizes below 48 px use a simplified trace with a heavier
stroke, because the detailed one turns to mush at 16 px - which is the size Explorer and the taskbar actually use.
"""
import io, os, struct, sys
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src")
SHEET = os.path.join(ROOT, "test")   # gitignored; the contact sheet is for looking at, not for shipping
SS = 8                                   # supersample factor
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

# tile, line, spike
PALETTES = {
    "night-city": ((252, 238, 10, 255), (10, 10, 12, 255), (255, 0, 60, 255)),     # shipped: yellow tile, black trace, red spike
    "pause": ((10, 9, 13, 255), (252, 238, 10, 255), (255, 60, 70, 255)),          # black tile, yellow trace, red spike
    "terminal": ((4, 7, 11, 255), (94, 246, 255, 255), (255, 79, 90, 255)),        # black tile, cyan trace, red spike
    "instrument": ((16, 20, 26, 255), (237, 240, 243, 255), (217, 58, 58, 255)),  # the original
}
SHIPPED = "night-city"

BASE = 0.56
DETAIL = [
    ("line", [(0.10, BASE), (0.24, BASE), (0.29, 0.47), (0.34, BASE), (0.42, BASE)]),
    ("spike", [(0.42, BASE), (0.50, 0.17), (0.585, 0.87), (0.655, BASE)]),
    ("line", [(0.655, BASE), (0.90, BASE)]),
]
SIMPLE = [
    ("line", [(0.13, BASE), (0.38, BASE)]),
    ("spike", [(0.38, BASE), (0.49, 0.27), (0.60, 0.80), (0.68, BASE)]),
    ("line", [(0.68, BASE), (0.87, BASE)]),
]


def draw(size, palette):
    tile, line, spike = PALETTES[palette]
    n = size * SS
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # square tile with the top-right corner cut off; the cut is proportionally a little bigger at small sizes so it
    # survives downsampling instead of becoming a single grey pixel
    pad = round(n * 0.03)
    cut = round(n * (0.30 if size < 48 else 0.24))
    x0, y0, x1, y1 = pad, pad, n - 1 - pad, n - 1 - pad
    d.polygon([(x0, y0), (x1 - cut, y0), (x1, y0 + cut), (x1, y1), (x0, y1)], fill=tile)
    if size >= 128:
        # a thin bar along the bottom edge in the spike colour, like the rule under a menu heading
        bh = max(SS, round(n * 0.035))
        d.rectangle([x0, y1 - bh, x0 + round((x1 - x0) * 0.34), y1], fill=spike)

    small = size < 48
    strokes = SIMPLE if small else DETAIL
    w = max(SS, round(n * (0.11 if small else 0.066)))
    colours = {"line": line, "spike": spike}
    for which, pts in strokes:
        c = colours[which]
        xy = [(p[0] * n, p[1] * n) for p in pts]
        d.line(xy, fill=c, width=w, joint="curve")
        for x, y in xy:   # round the ends; PIL's joint="curve" only rounds interior corners
            d.ellipse([x - w / 2.0, y - w / 2.0, x + w / 2.0, y + w / 2.0], fill=c)

    return img.resize((size, size), Image.LANCZOS)


def write_ico(path, images):
    """Write a multi-size .ico with PNG-compressed entries (Windows Vista and later)."""
    blobs = []
    for im in images:
        b = io.BytesIO()
        im.save(b, format="PNG", optimize=True)
        blobs.append(b.getvalue())
    offset = 6 + 16 * len(blobs)
    out = [struct.pack("<HHH", 0, 1, len(blobs))]
    for im, blob in zip(images, blobs):
        w = 0 if im.width >= 256 else im.width
        h = 0 if im.height >= 256 else im.height
        out.append(struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(blob), offset))
        offset += len(blob)
    out.extend(blobs)
    open(path, "wb").write(b"".join(out))


def sheet(palettes):
    """Every size of every palette, on light and dark backgrounds, to judge the small sizes honestly."""
    row_h = 300
    img = Image.new("RGBA", (1060, row_h * len(palettes)), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    for r, pal in enumerate(palettes):
        top = r * row_h
        d.rectangle([0, top + 150, 1060, top + row_h], fill=(40, 44, 52, 255))
        d.text((16, top + 8), pal + ("  (shipped)" if pal == SHIPPED else ""), fill=(60, 60, 60, 255))
        x = 16
        for s in SIZES:
            im = draw(s, pal)
            img.paste(im, (x, top + 150 - 16 - s if s < 128 else top + 22), im)
            img.paste(im, (x, top + 150 + 16 if s < 128 else top + 150 + 6), im)
            x += s + 22
    os.makedirs(SHEET, exist_ok=True)
    path = os.path.join(SHEET, "icon-sheet.png")
    img.save(path)
    return path


def main():
    if "--sheet" in sys.argv:
        print("contact sheet:", sheet(list(PALETTES)))
        return
    images = [draw(s, SHIPPED) for s in SIZES]
    ico = os.path.join(OUT, "app.ico")   # src/app.ico, referenced by <ApplicationIcon> in CrashDoctor.csproj
    write_ico(ico, images)
    print("app.ico  %.1f KB  sizes %s  palette %s" % (os.path.getsize(ico) / 1024.0, SIZES, SHIPPED))
    print("contact sheet:", sheet([SHIPPED]))


main()
