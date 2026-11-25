"""Zeichnet das Fundus-Logo und schreibt fundus.ico + logo.png (python make_logo.py)."""
from pathlib import Path
from PIL import Image, ImageDraw

OUT = Path(__file__).parent
S = 2048  # Arbeitsgröße, wird runterskaliert (= Kantenglättung)

TOP, BOTTOM = (79, 139, 255), (30, 78, 214)
WHITE = (246, 249, 255)
BLUE = (47, 111, 235)


def gradient_square():
    grad = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        d.line([(0, y), (S, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(TOP, BOTTOM)) + (255,))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((80, 80, S - 80, S - 80), radius=430, fill=255)
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(grad, (0, 0), mask)
    return out


def draw(detailed: bool):
    img = gradient_square()
    d = ImageDraw.Draw(img)
    k = S / 1024

    def r(x0, y0, x1, y1, rad, fill):
        d.rounded_rectangle((x0 * k, y0 * k, x1 * k, y1 * k), radius=rad * k, fill=fill)

    # Ordner: Reiter + Körper
    r(200, 245, 480, 370, 42, WHITE)
    r(200, 310, 824, 790, 64, WHITE)

    # Spalten-Trenner (Miller Columns)
    w = 24 if detailed else 58
    for x in (408, 616):
        r(x - w / 2, 380, x + w / 2, 735, w / 2, BLUE)

    if detailed:
        light = (190, 208, 245)
        for col_x0, col_x1 in ((250, 380), (444, 588)):
            for i, y in enumerate((400, 470, 540, 610)):
                r(col_x0, y, col_x1 - (40 if i % 2 else 0), y + 28, 14, light)
        r(646, 455, 780, 505, 22, BLUE)  # markierte Zeile in der letzten Spalte
        for y in (400, 540, 610):
            r(652, y, 770, y + 28, 14, light)
    return img


big, small = draw(True), draw(False)
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
frames = [(small if s <= 32 else big).resize((s, s), Image.LANCZOS) for s in sizes]
frames[-1].save(OUT / "fundus.ico", format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
big.resize((256, 256), Image.LANCZOS).save(OUT / "logo.png")

# Vorschau aller Größen nebeneinander (zum Prüfen)
prev = Image.new("RGBA", (sum(sizes) + 10 * len(sizes) + 10, 276), (32, 32, 36, 255))
x = 10
for s, f in zip(sizes, frames):
    prev.paste(f, (x, 266 - s), f)
    x += s + 10
prev.save(OUT / "preview.png")
print("ok")
