"""Home and default card: a long exposure of the pulse train along the fibre, with a far fibre and dust for depth."""
from oglib import *

c = Card()
c.glow(520, 500, 640, strength=1.3)
c.glow(1050, 120, 420, squash=.6, strength=.55, colors=(VIOLET, BLUE, BLUE))

far = Fibre([((640, -40), (820, 60), (900, 200), (1060, 190)), ((1060, 190), (1180, 180), (1220, 110), (1300, 80))],
            crest_x=1000, reach=420, wmin=4, wmax=13, seg_len=90, gap=22)
far.draw(c, opacity=.55, filt=c.blur(2.2))
dust(c, 46, 400, 230)

c.add(wordmark(88, 70, 1.55),
      f'<text x="100" y="325" fill="{TEXT}" font-size="46" font-weight="500">Fine-grained reactive computation for .NET.</text>',
      f'<text x="100" y="377" fill="{MUTED}" font-size="28">Signals, memos, effects and async boundaries in F#</text>')

f = Fibre([((-90, 660), (120, 640), (250, 470), (470, 478)), ((470, 478), (690, 486), (760, 600), (940, 580)),
           ((940, 580), (1080, 565), (1150, 470), (1300, 400))])
nodes = f.draw(c)
s = nearest(f, nodes, 270)
hops = nodes[s:s + 5]
exposure(c, f, hops, 3.3)
for d in nodes:
    f.node(c, d, "lit" if d in hops[:3] else "next" if d == hops[3] else "idle")
print(save(c, "ranvier-og"))
