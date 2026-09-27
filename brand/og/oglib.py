"""Shared drawing for the og cards: the tapered myelinated fibre, its pulse train and the card frame."""
import re, math, bisect, os, subprocess, tempfile, time

HERE = os.path.dirname(os.path.abspath(__file__))
CANVAS = "#0D1118"
TEXT, MUTED = "#EBF3F7", "#9BAAB5"
CYAN, BLUE, VIOLET = "#64D9EE", "#6D9FFB", "#9D8CFF"
FONT = "@import url('https://fonts.googleapis.com/css2?family=Geist:wght@400..700&amp;family=Geist+Mono:wght@400..600&amp;display=swap');"

_wm = open(os.path.join(HERE, "..", "wordmark", "ranvier-wordmark-dark.svg"), encoding="utf-8").read()
WORDMARK = re.search(r"<svg[^>]*>(.*)</svg>", _wm, re.S).group(1)


def wordmark(x, y, scale):
    return f'<g transform="translate({x} {y}) scale({scale})">{WORDMARK}</g>'


def state_mark(name):
    """The inner shapes and style of a brand state mark, in its 96-unit box."""
    s = open(os.path.join(HERE, "..", "states", f"{name}-dark.svg"), encoding="utf-8").read()
    return re.search(r"<style>(.*?)</style>", s).group(1), re.search(r"</style>(.*)</svg>", s, re.S).group(1)


def mix(c0, c1, t):
    a = [int(c0[i:i + 2], 16) for i in (1, 3, 5)]
    b = [int(c1[i:i + 2], 16) for i in (1, 3, 5)]
    t = min(1, max(0, t))
    return "#%02X%02X%02X" % tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def grad(t):
    """The brand accent gradient: cyan at 0, blue at .53, violet at 1."""
    return mix(CYAN, BLUE, t / .53) if t <= .53 else mix(BLUE, VIOLET, (t - .53) / .47)


class Card:
    def __init__(self):
        self.defs, self.body, self._id = [], [], 0

    def uid(self, prefix):
        self._id += 1
        return f"{prefix}{self._id}"

    def lin(self, p0, p1, c0, c1):
        i = self.uid("l")
        self.defs.append(f'<linearGradient id="{i}" gradientUnits="userSpaceOnUse" x1="{p0[0]:.1f}" y1="{p0[1]:.1f}" '
                         f'x2="{p1[0]:.1f}" y2="{p1[1]:.1f}"><stop offset="0" stop-color="{c0}"/><stop offset="1" stop-color="{c1}"/></linearGradient>')
        return f"url(#{i})"

    def glow(self, cx, cy, r, squash=.5, strength=1.0, colors=(CYAN, BLUE, VIOLET)):
        i = self.uid("g")
        self.defs.append(f'<radialGradient id="{i}" cx="{cx}" cy="{cy}" r="{r}" gradientUnits="userSpaceOnUse" '
                         f'gradientTransform="translate({cx} {cy}) scale(1 {squash}) translate({-cx} {-cy})">'
                         f'<stop offset="0" stop-color="{colors[0]}" stop-opacity="{.16 * strength:.3f}"/>'
                         f'<stop offset=".45" stop-color="{colors[1]}" stop-opacity="{.07 * strength:.3f}"/>'
                         f'<stop offset="1" stop-color="{colors[2]}" stop-opacity="0"/></radialGradient>')
        self.body.append(f'<rect width="1200" height="630" fill="url(#{i})"/>')

    def blur(self, sd):
        i = self.uid("b")
        self.defs.append(f'<filter id="{i}" x="-20%" y="-50%" width="140%" height="200%"><feGaussianBlur stdDeviation="{sd}"/></filter>')
        return f"url(#{i})"

    def add(self, *parts):
        self.body.extend(parts)

    def svg(self, extra_style=""):
        return (f'<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="630" viewBox="0 0 1200 630">\n'
                f'<defs>{"".join(self.defs)}<style>{FONT} text{{font-family:Geist,system-ui,sans-serif}} '
                f'.mono{{font-family:"Geist Mono",ui-monospace,monospace}} {extra_style}</style></defs>\n'
                f'<rect width="1200" height="630" fill="{CANVAS}"/>\n' + "\n".join(self.body) + "\n</svg>")


class Fibre:
    """A myelinated fibre along a cubic spline: thick (near) at the crest, thin (far) at the ends."""

    def __init__(self, segs, crest_x=560, reach=760, wmin=10, wmax=34, seg_len=178, gap=40, offset=30):
        self.pts = [self._bez(p, i / 1500) for p in segs for i in range(1501)]
        self.acc = [0.0]
        for a, b in zip(self.pts, self.pts[1:]):
            self.acc.append(self.acc[-1] + math.dist(a, b))
        self.total = self.acc[-1]
        self.crest_x, self.reach, self.wmin, self.wmax = crest_x, reach, wmin, wmax
        self.L, self.G, self.off = seg_len, gap, offset

    @staticmethod
    def _bez(p, t):
        (x0, y0), (x1, y1), (x2, y2), (x3, y3) = p
        u = 1 - t
        return (u ** 3 * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t ** 3 * x3,
                u ** 3 * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t ** 3 * y3)

    def at(self, d):
        return self.pts[min(bisect.bisect_left(self.acc, max(d, 0)), len(self.pts) - 1)]

    def normal(self, d):
        a, b = self.at(d - 2), self.at(d + 2)
        dx, dy = b[0] - a[0], b[1] - a[1]
        n = math.hypot(dx, dy) or 1
        return dy / n, -dx / n

    def width(self, d):
        c = 1 - min(1, abs(self.at(d)[0] - self.crest_x) / self.reach)
        return self.wmin + (self.wmax - self.wmin) * c ** 1.3

    def depth(self, d):
        return (self.width(d) - self.wmin) / (self.wmax - self.wmin)

    def hue(self, d):
        return grad(1 - self.depth(d))

    def fillc(self, d):
        return mix(CANVAS, self.hue(d), .06 + .34 * self.depth(d) ** 1.2)

    def edgec(self, d):
        return mix(CANVAS, self.hue(d), .18 + .72 * self.depth(d))

    def draw(self, card, opacity=1.0, filt=None):
        """Axon and myelin segments; returns the node positions (arc lengths) visible on the card."""
        g = [f'<g opacity="{opacity}"' + (f' filter="{filt}"' if filt else "") + ">"]
        pts, acc = self.pts, self.acc
        for i in range(0, len(pts) - 12, 12):
            (x0, y0), (x1, y1) = pts[i], pts[i + 12]
            d = acc[i]
            g.append(f'<line x1="{x0:.1f}" y1="{y0:.1f}" x2="{x1:.1f}" y2="{y1:.1f}" '
                     f'stroke="{mix(CANVAS, self.hue(d), .2 + .5 * self.depth(d))}" '
                     f'stroke-width="{max(1.5, self.width(d) * .1):.2f}" stroke-linecap="round"/>')
        nodes, k = [], 0
        while True:
            d0 = k * (self.L + self.G) - self.off
            d1 = d0 + self.L
            if d0 > self.total:
                break
            a, b = max(d0, 0), min(d1, self.total)
            if b - a > 4:
                s1, s2 = [], []
                for j in range(121):
                    d = a + (b - a) * j / 120
                    x, y = self.at(d)
                    nx, ny = self.normal(d)
                    r = self.width(d) / 2
                    e = min(1, (d - d0) / r, (d1 - d) / r)
                    if e < 1:
                        r *= math.sqrt(max(0, 1 - (1 - e) ** 2))
                    s1.append((x + nx * r, y + ny * r))
                    s2.append((x - nx * r, y - ny * r))
                pa, pb = self.at(a), self.at(b)
                g.append('<path d="M' + " L".join(f"{x:.1f} {y:.1f}" for x, y in s1 + s2[::-1]) +
                         f'Z" fill="{card.lin(pa, pb, self.fillc(a), self.fillc(b))}" '
                         f'stroke="{card.lin(pa, pb, self.edgec(a), self.edgec(b))}" stroke-width="2" stroke-linejoin="round"/>')
            dn = d1 + self.G / 2
            if 0 < dn < self.total and -40 < self.at(dn)[0] < 1240:
                nodes.append(dn)
            k += 1
        g.append("</g>")
        card.add(*g)
        return nodes

    def node(self, card, d, state="idle"):
        x, y = self.at(d)
        s = self.width(d) / 30
        if state == "lit":
            card.add(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{22 * s:.1f}" fill="{CYAN}" opacity=".14"/>'
                     f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{9 * s:.1f}" fill="{CYAN}"/>')
        elif state == "next":
            card.add(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{9 * s:.1f}" fill="{CANVAS}" stroke="{CYAN}" stroke-width="2.5"/>')
        else:
            card.add(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{max(2.5, 6 * s):.1f}" fill="{self.edgec(d)}"/>')

    def hop(self, a, b, lift=.55):
        (x0, y0), (x1, y1) = self.at(a), self.at(b)
        nx, ny = self.normal((a + b) / 2)
        if ny > 0:
            nx, ny = -nx, -ny
        h = lift * math.dist((x0, y0), (x1, y1))
        return (x0, y0), ((x0 + x1) / 2 + nx * h, (y0 + y1) / 2 + ny * h), (x1, y1)

    def pulse(self, card, hops, head, period=.5, packets=4, levels=(1, .55, .32, .18), trail=True,
              particles=20, size=1.0, filt=None, lift=.55):
        """A pulse train jumping node to node along `hops`, its lead packet at `head` hop units."""
        curves = [self.hop(a, b, lift) for a, b in zip(hops, hops[1:])]

        def q(c, t):
            (x0, y0), (cx, cy), (x1, y1) = c
            u = 1 - t
            return u * u * x0 + 2 * u * t * cx + t * t * x1, u * u * y0 + 2 * u * t * cy + t * t * y1

        def along(u):
            h = min(int(u), len(curves) - 1)
            t = u - h
            return h, t, q(curves[h], t)

        def colour(h, t):
            return self.hue(hops[h] + t * (hops[h + 1] - hops[h]))

        if trail:
            for c in curves:
                for i in range(1, 30):
                    x, y = q(c, i / 30)
                    card.add(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="1.6" fill="#3A4855"/>')
        g = ["<g" + (f' filter="{filt}"' if filt else "") + ">"]
        for k in range(packets):
            h0 = head - k * period
            if h0 <= 0:
                break
            level = levels[min(k, len(levels) - 1)]
            for i in range(particles):
                f = i / (particles - 1)
                u = h0 - .3 * (1 - f) ** 1.3
                if u < 0:
                    continue
                h, t, (x, y) = along(u)
                _, _, (xa, ya) = along(max(u - .01, 0))
                _, _, (xb, yb) = along(u + .01)
                dx, dy = xb - xa, yb - ya
                m = math.hypot(dx, dy) or 1
                jit = math.sin(i * 12.9898 + k * 78.233) * 43758.5453 % 1 - .5
                spread = (1 - f) * 20
                x += -dy / m * jit * spread
                y += dx / m * jit * spread
                r = (2.6 + 6.4 * f ** 2) * size
                g.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r:.2f}" fill="{colour(h, t)}" opacity="{(.15 + .85 * f ** 2) * level:.2f}"/>')
            h, t, (x, y) = along(h0)
            g.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{26 * level ** .5 * size:.1f}" fill="{colour(h, t)}" opacity="{.16 * level:.2f}"/>')
            if k == 0:
                g.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{4.5 * size:.1f}" fill="{TEXT}"/>')
        g.append("</g>")
        card.add(*g)
        return along


def exposure(card, fibre, hops, head, levels=(1, .6, .35, .2), period=.5, length=1.1):
    """A long exposure of the pulse train: each packet drawn out into a ribbon that brightens toward its head."""
    along = fibre.pulse(card, hops, head, packets=0, trail=False)

    def streak(u0, u1, width, level, filt=None):
        g = ["<g" + (f' filter="{filt}"' if filt else "") + ' stroke-linecap="round">']
        n = 90
        prev = along(max(u0, 0))[2]
        for i in range(1, n + 1):
            u = u0 + (u1 - u0) * i / n
            if u <= 0:
                continue
            h, t, p = along(u)
            k = i / n
            col = fibre.hue(hops[h] + t * (hops[h + 1] - hops[h]))
            g.append(f'<line x1="{prev[0]:.1f}" y1="{prev[1]:.1f}" x2="{p[0]:.1f}" y2="{p[1]:.1f}" stroke="{col}" '
                     f'stroke-width="{width * (.25 + .75 * k):.2f}" opacity="{level * k ** 2:.2f}"/>')
            prev = p
        g.append("</g>")
        card.add(*g)

    bloom = card.blur(8)
    for k, level in enumerate(levels):
        h0 = head - k * period
        if h0 <= 0:
            break
        streak(h0 - length, h0, 22, .35 * level, bloom)
        streak(h0 - length, h0, 5, level)
        streak(h0 - length * .55, h0, 1.6, level)
    x, y = along(head)[2]
    card.add(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="26" fill="{CYAN}" opacity=".18"/>',
             f'<circle cx="{x:.1f}" cy="{y:.1f}" r="5" fill="{TEXT}"/>')


def axon(card, cx, cy, r, depth, sd=0, lit=False):
    """An axon in cross-section: a core inside concentric myelin lamellae, brighter and sharper when near.
    A lit axon carries the signal as a bright core with a horizontal flare."""
    g = [f'<g opacity="{.35 + .65 * depth:.2f}"' + (f' filter="{card.blur(sd)}"' if sd else "") + ">"]
    core = r * .56
    g.append(f'<circle cx="{cx}" cy="{cy}" r="{r:.1f}" fill="{mix(CANVAS, VIOLET, .05 + .1 * depth)}"/>')
    n = 7
    for i in range(n):
        t = i / (n - 1)
        rr = core + (r - core) * (i + .5) / n
        col = mix(CANVAS, grad(.15 + .85 * t), .25 + .6 * depth * (1 - .5 * t))
        g.append(f'<circle cx="{cx}" cy="{cy}" r="{rr:.1f}" fill="none" stroke="{col}" stroke-width="{(r - core) / n * .55:.2f}"/>')
    g.append(f'<circle cx="{cx}" cy="{cy}" r="{core:.1f}" fill="{mix(CANVAS, CYAN, .05 + .08 * depth)}" '
             f'stroke="{mix(CANVAS, CYAN, .3 + .6 * depth)}" stroke-width="2"/>')
    g.append("</g>")
    card.add(*g)
    if lit:
        i = card.uid("r")
        card.defs.append(f'<radialGradient id="{i}"><stop offset="0" stop-color="{TEXT}" stop-opacity=".95"/>'
                         f'<stop offset=".25" stop-color="{CYAN}" stop-opacity=".6"/><stop offset="1" stop-color="{BLUE}" stop-opacity="0"/></radialGradient>')
        card.add(f'<circle cx="{cx}" cy="{cy}" r="{core * .95:.1f}" fill="url(#{i})"/>',
                 f'<ellipse cx="{cx}" cy="{cy}" rx="{r * 2.5:.0f}" ry="3" fill="{CYAN}" opacity=".35" filter="{card.blur(3)}"/>',
                 f'<ellipse cx="{cx}" cy="{cy}" rx="{r * 1.6:.0f}" ry="1.2" fill="{TEXT}" opacity=".5"/>',
                 f'<circle cx="{cx}" cy="{cy}" r="6" fill="{TEXT}"/>')


def dust(card, n, y0, h, sd=3, seed=0):
    """Soft out-of-focus particles in the band `y0` to `y0 + h`, placed deterministically."""
    soft = card.blur(sd)
    dots = []
    for i in range(seed, seed + n):
        x = (math.sin(i * 91.7) * 43758.5453 % 1) * 1200
        y = y0 + (math.sin(i * 17.3) * 23421.631 % 1) * h
        r = 1.5 + (math.sin(i * 5.1) * 9173.1 % 1) * 5
        col = grad(math.sin(i * 3.3) * 777.7 % 1)
        dots.append(f'<circle cx="{x:.0f}" cy="{y:.0f}" r="{r:.1f}" fill="{col}" opacity="{.08 + .18 * (r / 6.5):.2f}"/>')
    card.add(f'<g filter="{soft}">', *dots, "</g>")


def nearest(fibre, nodes, x):
    return min(range(len(nodes)), key=lambda i: abs(fibre.at(nodes[i])[0] - x))


def render(name):
    """Render <name>.svg beside this file to <name>.png at 1200x630 with headless Edge."""
    edge = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
    svg, png = os.path.join(HERE, name + ".svg"), os.path.join(HERE, name + ".png")
    if os.path.exists(png):
        os.remove(png)
    # Edge can return before the screenshot lands, and keeps its profile locked a while after.
    for attempt in range(6):
        profile = tempfile.mkdtemp(prefix="og-edge-")
        subprocess.run([edge, "--headless", "--disable-gpu", "--hide-scrollbars", f"--user-data-dir={profile}",
                        "--window-size=1200,630", "--virtual-time-budget=4000", f"--screenshot={png}",
                        "file:///" + svg.replace("\\", "/")], capture_output=True, timeout=90)
        for _ in range(40):
            if os.path.exists(png):
                return png
            time.sleep(.25)
    raise RuntimeError(f"no screenshot for {name}")


def save(card, name, extra_style=""):
    open(os.path.join(HERE, name + ".svg"), "w", encoding="utf-8").write(card.svg(extra_style))
    return render(name)
