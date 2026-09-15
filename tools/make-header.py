"""Compose the Nexus page header (1300x372) from the app icon and a real screenshot.

    python tools/make-screenshots.py report.html <shots>          (renders the gallery images first)
    python tools/make-header.py <shots>/01-diagnosis-vram.png docs/screenshots/00-header.png

Nothing in it is generated imagery: the picture on the right is a crop of the app's own diagnosis card from a real
scan, the mark is src/app.ico, and the rest is type and a trace line in the Neon theme's colours. Fonts are the
ones the app itself uses (Bahnschrift for display, Segoe UI for body, Consolas for the mono line), all shipped
with Windows.
"""
import os, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H = 1300, 372
PAPER, CARD, INK, STEEL = (10, 9, 13), (20, 13, 18), (255, 230, 223), (201, 149, 146)
RED, CYAN, YELLOW, LINE = (255, 92, 92), (94, 246, 255), (243, 230, 0), (58, 26, 32)
FONTS = r"C:\Windows\Fonts"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def font(name, size, variation=None):
    f = ImageFont.truetype(os.path.join(FONTS, name), size)
    if variation:
        try:
            f.set_variation_by_name(variation)
        except Exception:
            pass
    return f


def glow(base, layer, radius, strength=1.0):
    """Add a blurred copy of an RGBA layer under itself, the way the theme lights its headings."""
    blurred = layer.filter(ImageFilter.GaussianBlur(radius))
    if strength != 1.0:
        a = blurred.split()[3].point(lambda v: min(255, int(v * strength)))
        blurred.putalpha(a)
    base.alpha_composite(blurred)
    base.alpha_composite(layer)


def radial(size, centre, rx, ry, colour, alpha):
    """A soft elliptical glow, drawn small and scaled up so it is smooth."""
    small = Image.new("RGBA", (200, 200), (0, 0, 0, 0))
    d = ImageDraw.Draw(small)
    for i in range(60, 0, -1):
        a = int(alpha * (1 - i / 60.0) ** 2)
        d.ellipse([100 - i * 1.6, 100 - i * 1.6, 100 + i * 1.6, 100 + i * 1.6], fill=colour + (a,))
    small = small.filter(ImageFilter.GaussianBlur(6)).resize((rx * 2, ry * 2), Image.LANCZOS)
    out = Image.new("RGBA", size, (0, 0, 0, 0))
    out.alpha_composite(small, (centre[0] - rx, centre[1] - ry))
    return out


def cut_corners(size, cut):
    """Mask with the top-right and bottom-left corners cut away, the Neon theme's shape."""
    w, h = size
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon([(0, 0), (w - cut, 0), (w, cut), (w, h), (cut, h), (0, h - cut)], fill=255)
    return m


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    shot_path, out = argv[0], argv[1]
    img = Image.new("RGBA", (W, H), PAPER + (255,))

    # the theme's background: a red glow top-right, a cyan one bottom-left, a faint grid
    img.alpha_composite(radial((W, H), (int(W * 0.86), -20), 620, 330, RED, 70))
    img.alpha_composite(radial((W, H), (-60, H + 40), 520, 300, CYAN, 34))
    grid = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    g = ImageDraw.Draw(grid)
    for x in range(0, W, 40):
        g.line([(x, 0), (x, H)], fill=RED + (14,))
    for y in range(0, H, 40):
        g.line([(0, y), (W, y)], fill=RED + (14,))
    img.alpha_composite(grid)

    # the diagnosis card from a real scan, corners cut, thin cyan edge, sitting on the right
    shot = Image.open(shot_path).convert("RGBA")
    card = shot.crop((224, 105, 1026, 425))                     # the verdict card in a 1920x1080 render
    cw = 556                                                   # narrow enough to leave the words their own space
    card = card.resize((cw, int(card.height * cw / card.width)), Image.LANCZOS)
    ch = card.height
    cx, cy = W - cw - 34, (H - ch) // 2
    mask = cut_corners(card.size, 22)
    frame = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    fd = ImageDraw.Draw(frame)
    fd.polygon([(cx - 1, cy - 1), (cx + card.width - 22, cy - 1), (cx + card.width, cy + 22), (cx + card.width, cy + ch),
                (cx + 22, cy + ch), (cx - 1, cy + ch - 22)], outline=CYAN + (150,), width=1)
    glow(img, frame, 10, 0.8)
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    shadow.paste((0, 0, 0, 170), (cx + 6, cy + 10, cx + card.width + 6, cy + ch + 10))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(14)))
    img.paste(card, (cx, cy), mask)

    # the mark and the words
    icon = Image.open(os.path.join(ROOT, "src", "app.ico"))
    icon.size = (256, 256)
    icon.load()
    icon = icon.convert("RGBA").resize((128, 128), Image.LANCZOS)
    img.alpha_composite(icon, (54, 66))

    text = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    t = ImageDraw.Draw(text)
    title = font("bahnschrift.ttf", 62, "SemiBold")
    t.text((206, 56), "CRASH DOCTOR", font=title, fill=RED)
    glow(img, text, 16, 0.9)

    d = ImageDraw.Draw(img)
    d.text((208, 126), "for Cyberpunk 2077", font=font("consola.ttf", 21), fill=CYAN)
    body = font("segoeui.ttf", 21)
    d.text((208, 166), "Reads your crash dumps and mod logs,", font=body, fill=INK)
    d.text((208, 194), "and tells you what actually happened.", font=body, fill=INK)
    d.text((208, 236), "You do not have 30 crashes. You have three problems.", font=font("segoeuii.ttf", 17), fill=STEEL)

    # the vital-signs trace from the icon, run along the foot of the banner
    trace = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    td = ImageDraw.Draw(trace)
    y0 = 306
    pts = [(54, y0), (560, y0), (576, y0 - 5), (590, y0 + 26), (606, y0 - 36), (622, y0 + 10), (636, y0), (cx - 36, y0)]
    td.line(pts, fill=CYAN + (230,), width=3, joint="curve")
    td.line([(590, y0 + 26), (606, y0 - 36)], fill=RED + (255,), width=3)
    glow(img, trace, 8, 0.7)
    d.text((54, 336), "free  \u00b7  reads only, changes nothing unless you ask  \u00b7  0.6 early release", font=font("consola.ttf", 14), fill=STEEL)

    img.convert("RGB").save(out, "PNG", optimize=True)
    print("%s  %dx%d  %.0f KB" % (out, W, H, os.path.getsize(out) / 1024.0))
    return 0


sys.exit(main(sys.argv[1:]))
