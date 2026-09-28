"""Tracing card: a why chain read backwards along the fibre, each node a hop from the run to the write that caused it."""
from oglib import *

c = Card()
c.glow(640, 440, 640, strength=.9)
c.add(wordmark(88, 44, .62),
      f'<text x="100" y="170" class="mono" fill="{CYAN}" font-size="22" letter-spacing="3">GUIDE</text>',
      f'<text x="100" y="232" fill="{TEXT}" font-size="60" font-weight="600">Tracing</text>',
      f'<text x="100" y="278" fill="{MUTED}" font-size="26">Why a node ran, why it did not, and where it came from.</text>')

f = Fibre([((-90, 470), (200, 480), (380, 420), (620, 420)), ((620, 420), (860, 420), (1000, 470), (1290, 440))],
          crest_x=620, reach=820, wmin=12, wmax=28, seg_len=200, gap=44, offset=250)
nodes = f.draw(c)
s = nearest(f, nodes, 140)
steps = [("Write", "/lines"), ("Moved", "/subtotal"), ("Moved", "/total"), ("RunStart", "/banner")]
nodes = nodes[s:s + len(steps)]
# The chain is read from the run back to its cause, so the pulse travels right to left.
f.pulse(c, nodes[::-1], 2.6, lift=.36, trail=True)
for i, d in enumerate(nodes):
    x, y = f.at(d)
    f.node(c, d, "next" if i == 0 else "lit")
    kind, path = steps[i]
    c.add(f'<text x="{x:.1f}" y="560" text-anchor="middle" class="mono" fill="{MUTED}" font-size="20">{kind}</text>'
          f'<text x="{x:.1f}" y="592" text-anchor="middle" class="mono" fill="{TEXT}" font-size="22">{path}</text>')
c.add(f'<text x="{f.at(nodes[0])[0] - 30:.1f}" y="{f.at(nodes[0])[1] - 115:.1f}" text-anchor="start" class="mono" '
      f'fill="{CYAN}" font-size="20">root: user write</text>')
print(save(c, "ranvier-og-tracing"))
