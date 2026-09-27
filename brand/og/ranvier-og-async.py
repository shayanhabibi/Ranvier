"""Async card: the fibre as a timeline; each node is a boundary state, each hop the call that moves it."""
from oglib import *

c = Card()
c.glow(560, 420, 620, strength=.9)
c.add(wordmark(88, 44, .62),
      f'<text x="100" y="170" class="mono" fill="{CYAN}" font-size="22" letter-spacing="3">GUIDE</text>',
      f'<text x="100" y="232" fill="{TEXT}" font-size="60" font-weight="600">Async and pending</text>',
      f'<text x="100" y="278" fill="{MUTED}" font-size="26">A boundary shows a fallback while its inputs are in flight.</text>')

f = Fibre([((-90, 470), (200, 480), (380, 420), (620, 420)), ((620, 420), (860, 420), (1000, 470), (1290, 440))],
          crest_x=560, reach=820, wmin=12, wmax=28, seg_len=200, gap=44, offset=250)
nodes = f.draw(c)
s = nearest(f, nodes, 140)
states = [("pending", "Pending"), ("fallback", "Fallback"), ("ready", "Ready"), ("failed", "Failed"), ("recovered", "Recovered")]
nodes = nodes[s:s + len(states)]
along = f.pulse(c, nodes[:4], 1.55, lift=.36, trail=True)
calls = ["createAsyncSource ()", "price.Settle 4m", "price.Fail exn"]
style = state_mark("ready")[0]
for i, d in enumerate(nodes):
    x, y = f.at(d)
    f.node(c, d, "lit" if i < 2 else "next" if i == 2 else "idle")
    key, label = states[i]
    dim = "" if i <= 2 else ' opacity=".45"'
    c.add(f'<g{dim}><g transform="translate({x - 33:.1f} 498) scale(.6875)">{state_mark(key)[1]}</g>'
          f'<text x="{x:.1f}" y="600" text-anchor="middle" class="mono" fill="{MUTED}" font-size="22">{label}</text></g>')
for i, call in enumerate(calls):
    x = (f.at(nodes[i])[0] + f.at(nodes[i + 1])[0]) / 2
    y = min(f.at(nodes[i])[1], f.at(nodes[i + 1])[1]) - 62
    op = 1 if i < 2 else .45
    c.add(f'<text x="{x:.1f}" y="{y:.1f}" text-anchor="middle" class="mono" fill="{TEXT}" opacity="{op}" font-size="20">{call}</text>')
print(save(c, "ranvier-og-async", style))
