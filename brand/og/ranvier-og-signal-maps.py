"""Signal maps card: the fibre as a traced graph drawn live; a write travels from the signal to the effect, each node a map mark."""
from oglib import *

c = Card()
c.glow(700, 430, 640, strength=.9)
c.add(wordmark(88, 44, .62),
      f'<text x="100" y="170" class="mono" fill="{CYAN}" font-size="22" letter-spacing="3">GUIDE</text>',
      f'<text x="100" y="232" fill="{TEXT}" font-size="60" font-weight="600">Signal maps</text>',
      f'<text x="100" y="278" fill="{MUTED}" font-size="26">The traced graph behind an example, drawn live as it runs.</text>')

f = Fibre([((-90, 470), (200, 480), (380, 420), (620, 420)), ((620, 420), (860, 420), (1000, 470), (1290, 440))],
          crest_x=700, reach=820, wmin=12, wmax=28, seg_len=200, gap=44, offset=250)
nodes = f.draw(c)
s = nearest(f, nodes, 140)
steps = [("write", "lines", "[4M; 4M]"), ("moved", "subtotal", "8M"), ("moved", "total", "13M"), ("run", "effect", "")]
nodes = nodes[s:s + len(steps)]
f.pulse(c, nodes, 2.6, lift=.36, trail=True)
for i, d in enumerate(nodes):
    x, y = f.at(d)
    f.node(c, d, "next" if i == len(nodes) - 1 else "lit")
    kind, name, value = steps[i]
    c.add(f'<text x="{x:.1f}" y="{y - 58:.1f}" text-anchor="middle" class="mono" fill="{CYAN}" font-size="20">{value}</text>',
          f'<text x="{x:.1f}" y="560" text-anchor="middle" class="mono" fill="{MUTED}" font-size="20">{kind}</text>'
          f'<text x="{x:.1f}" y="592" text-anchor="middle" class="mono" fill="{TEXT}" font-size="22">{name}</text>')
print(save(c, "ranvier-og-signal-maps"))
