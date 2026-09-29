"""The NuGet package icon: the interim `r` lettermark of docs/static/favicon.svg, dark scheme, as a 128px PNG."""
import os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
SIZE, SS = 128, 16
K = SIZE * SS / 32
CANVAS, TEXT, CONTOUR = "#0D1118", "#EBF3F7", "#64D9EE"


def cubic(p0, p1, p2, p3, n=200):
    for i in range(n + 1):
        t = i / n
        u = 1 - t
        yield tuple(u ** 3 * a + 3 * u * u * t * b + 3 * u * t * t * c + t ** 3 * d for a, b, c, d in zip(p0, p1, p2, p3))


def stroke(draw, points, width, fill):
    points = [(x * K, y * K) for x, y in points]
    r = width * K / 2
    draw.line(points, fill=fill, width=round(width * K))
    for x, y in points:
        draw.ellipse((x - r, y - r, x + r, y + r), fill=fill)


img = Image.new("RGBA", (SIZE * SS, SIZE * SS), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)
draw.rounded_rectangle((0, 0, SIZE * SS - 1, SIZE * SS - 1), radius=7 * K, fill=CANVAS)
stroke(draw, [(10.5, 24), (10.5, 13.5)], 3.6, TEXT)
stroke(draw, list(cubic((10.5, 17.5), (10.5, 14.3), (12.9, 12.5), (17, 12.5))), 3.6, TEXT)
draw.ellipse(((22 - 2.4) * K, (22.5 - 2.4) * K, (22 + 2.4) * K, (22.5 + 2.4) * K), fill=CONTOUR)
img.resize((SIZE, SIZE), Image.LANCZOS).save(os.path.join(HERE, "ranvier-icon.png"), optimize=True)
