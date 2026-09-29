"""Section cards: eyebrow, headline and line over the fibre side-on, or the nerve end-on as a bundle of axons."""
from oglib import *

SECTIONS = [
    ("guide", "GUIDE", "Build with signals", "Installation, collections, async boundaries and troubleshooting.", "side"),
    ("concepts", "CONCEPTS", "How the graph works", "Signals, memos, effects and the async graph behind them.", "end"),
    ("fable", "FABLE", "The JavaScript target", "The same reactive graph under Fable. Planned.", "side"),
    ("benchmarks", "BENCHMARKS", "Measured per commit", "BenchmarkDotNet suites and instruction counts.", "side"),
]


def side(c, bracket):
    c.glow(760, 490, 600, strength=1.1)
    f = Fibre([((-90, 590), (260, 610), (420, 490), (700, 480)), ((700, 480), (960, 470), (1080, 410), (1300, 370))],
              crest_x=780, reach=700, wmin=8, wmax=30)
    nodes = f.draw(c)
    s = nearest(f, nodes, 420)
    hops = nodes[s:s + 4]
    exposure(c, f, hops, 2.3)
    for d in nodes:
        f.node(c, d, "lit" if d in hops[:2] else "next" if d == hops[2] else "idle")
    if bracket:
        (x0, y0), (x1, y1) = f.at(hops[0]), f.at(hops[2])
        yb = max(y0, y1) + 58
        c.add(f'<path d="M{x0:.1f} {yb - 8:.1f}V{yb:.1f}H{x1:.1f}V{yb - 8:.1f}" fill="none" stroke="{MUTED}" stroke-width="1.5" opacity=".7"/>',
              f'<text x="{x0:.1f}" y="{yb + 30:.1f}" text-anchor="middle" class="mono" fill="{MUTED}" font-size="20">m(N)</text>',
              f'<text x="{x1:.1f}" y="{yb + 30:.1f}" text-anchor="middle" class="mono" fill="{MUTED}" font-size="20">m(2N)</text>')


def end(c):
    c.glow(1000, 470, 520, squash=.8, strength=1.3)
    c.glow(1080, 60, 420, squash=.6, strength=.5, colors=(VIOLET, BLUE, BLUE))
    for cx, cy, r, depth, sd in [(560, 610, 26, .15, 6), (760, 560, 40, .25, 4), (1190, 360, 44, .3, 4),
                                 (1150, 60, 64, .45, 2.2), (960, 40, 38, .3, 3.5), (830, 470, 58, .45, 2),
                                 (1150, 600, 70, .5, 1.6)]:
        axon(c, cx, cy, r, depth, sd)
    axon(c, 1010, 450, 110, 1, lit=True)


for key, section, title, line, view in SECTIONS:
    c = Card()
    if view == "side":
        side(c, key == "benchmarks")
    else:
        end(c)
    dust(c, 30, 380, 250, seed=len(key))
    c.add(wordmark(88, 48, .62),
          f'<text x="100" y="200" class="mono" fill="{CYAN}" font-size="22" letter-spacing="3">{section}</text>',
          f'<text x="100" y="278" fill="{TEXT}" font-size="72" font-weight="600">{title}</text>',
          f'<text x="100" y="330" fill="{MUTED}" font-size="28">{line}</text>')
    print(save(c, f"ranvier-og-{key}"))
