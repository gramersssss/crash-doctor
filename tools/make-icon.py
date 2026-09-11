"""Draw the Crash Doctor application icon into src/app.ico.

    python tools/make-icon.py

The mark is the app's own brand dot taken one step further: a vital-signs trace that runs flat, spikes
once in the signal red, and carries on. It says "instrument reading a fault" rather than "Cyberpunk fan
art", which is the line DESIGN.md draws.

Each size is drawn at 8x and downsampled, and sizes below 48 px use a simplified trace with a heavier
stroke, because the detailed one turns to mush at 16 px.
"""
import io, os, struct, sys
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src")
SHEET = os.path.join(ROOT, "test")   # gitignored; the contact sheet is for looking at, not for shipping
SS = 8                                   # supersample factor

INK = (16, 20, 26, 255)                  # --ink   #10141A
PAPER = (237, 240, 243, 255)             # --paper #EDF0F3
SIGNAL = (217, 58, 58, 255)              # --signal #D93A3A

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

BASE = 0.56

DETAIL = [
    (PAPER, [(0.10, BASE), (0.24, BASE), (0.29, 0.47), (0.34, BASE), (0.42, BASE)]),
    (SIGNAL, [(0.42, BASE), (0.50, 0.17), (0.585, 0.87), (0.655, BASE)]),
    (PAPER, [(0.655, BASE), (0.90, BASE)]),
]
SIMPLE = [
    (PAPER, [(0.13, BASE), (0.38, BASE)]),
    (SIGNAL, [(0.38, BASE), (0.49, 0.27), (0.60, 0.80), (0.68, BASE)]),
    (PAPER, [(0.68, BASE), (0.87, BASE)]),
]


def draw(size):
    n = size * SS
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    pad = round(n * 0.02)
    d.rounded_rectangle([pad, pad, n - 1 - pad, n - 1 - pad], radius=round(n * 0.22), fill=INK)

    small = size < 48
    strokes = SIMPLE if small else DETAIL
    w = max(SS, round(n * (0.105 if small else 0.062)))

    for colour, pts in strokes:
        xy = [(p[0] * n, p[1] * n) for p in pts]
        d.line(xy, fill=colour, width=w, joint="curve")
        # round the ends and joints; PIL's joint="curve" only rounds interior corners
        for x, y in xy:
            d.ellipse([x - w / 2.0, y - w / 2.0, x + w / 2.0, y + w / 2.0], fill=colour)

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


def main():
    images = [draw(s) for s in SIZES]
    ico = os.path.join(OUT, "app.ico")   # src/app.ico, referenced by <ApplicationIcon> in CrashDoctor.csproj
    write_ico(ico, images)
    print("app.ico  %.1f KB  sizes %s" % (os.path.getsize(ico) / 1024.0, SIZES))

    # contact sheet on both backgrounds, to judge the small sizes honestly
    sheet = Image.new("RGBA", (1060, 300), (255, 255, 255, 255))
    sd = ImageDraw.Draw(sheet)
    sd.rectangle([0, 150, 1060, 300], fill=(40, 44, 52, 255))
    x = 16
    for s in SIZES:
        im = images[SIZES.index(s)]
        sheet.paste(im, (x, 150 - 16 - s), im)
        sheet.paste(im, (x, 150 + 16), im)
        sd.text((x, 150 - 12), str(s), fill=(60, 60, 60, 255))
        x += s + 22
    if not os.path.isdir(SHEET): os.makedirs(SHEET)
    sheet.save(os.path.join(SHEET, "icon-sheet.png"))
    print("contact sheet: test/icon-sheet.png")


main()
