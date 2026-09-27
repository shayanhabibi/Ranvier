import re, math, bisect
wm=open('../wordmark/ranvier-wordmark-dark.svg',encoding='utf-8').read()
wm_inner=re.search(r'<svg[^>]*>(.*)</svg>',wm,re.S).group(1)
FONT="@import url('https://fonts.googleapis.com/css2?family=Geist:wght@400..700&amp;display=swap');"

# Fibre centreline: rises from the lower left, crests, dips, climbs off the right edge.
segs=[((-90,660),(120,640),(250,470),(470,478)),
      ((470,478),(690,486),(760,600),(940,580)),
      ((940,580),(1080,565),(1150,470),(1300,400))]
def bez(p,t):
    (x0,y0),(x1,y1),(x2,y2),(x3,y3)=p; u=1-t
    return (u**3*x0+3*u*u*t*x1+3*u*t*t*x2+t**3*x3, u**3*y0+3*u*u*t*y1+3*u*t*t*y2+t**3*y3)
pts=[bez(p,i/1500) for p in segs for i in range(1501)]
acc=[0.0]
for a,b in zip(pts,pts[1:]): acc.append(acc[-1]+math.dist(a,b))
total=acc[-1]
def at(d): return pts[min(bisect.bisect_left(acc,max(d,0)),len(pts)-1)]
def normal(d):
    a,b=at(d-2),at(d+2); dx,dy=b[0]-a[0],b[1]-a[1]; n=math.hypot(dx,dy) or 1; return (dy/n,-dx/n)
def width(d):
    x=at(d)[0]; c=1-min(1,abs(x-560)/760)          # thickest near the crest, thinning to the edges
    return 10+24*c**1.3
stops=[(0,(0x64,0xD9,0xEE)),(.53,(0x6D,0x9F,0xFB)),(1,(0x9D,0x8C,0xFF))]
def grad(t):
    for (t0,c0),(t1,c1) in zip(stops,stops[1:]):
        if t<=t1:
            u=(t-t0)/(t1-t0); return "#%02X%02X%02X"%tuple(round(c0[i]+(c1[i]-c0[i])*u) for i in range(3))
    return "#9D8CFF"

def depth(d): return (width(d)-10)/24          # 0 far (thin) .. 1 near (thick)
def mix(c0,c1,t):
    a=[int(c0[i:i+2],16) for i in (1,3,5)]; b=[int(c1[i:i+2],16) for i in (1,3,5)]
    return "#%02X%02X%02X"%tuple(round(a[i]+(b[i]-a[i])*t) for i in range(3))
gid=[0]
def lin(p0,p1,c0,c1):
    gid[0]+=1; i=f"d{gid[0]}"
    defs.append(f'<linearGradient id="{i}" gradientUnits="userSpaceOnUse" x1="{p0[0]:.1f}" y1="{p0[1]:.1f}" x2="{p1[0]:.1f}" y2="{p1[1]:.1f}"><stop offset="0" stop-color="{c0}"/><stop offset="1" stop-color="{c1}"/></linearGradient>')
    return f"url(#{i})"
defs=[]
CANVAS="#0D1118"
def hue(d): return grad(1-depth(d))      # violet where the fibre is far, blue between, cyan at the thick crest
def fillc(d): return mix(CANVAS,hue(d),.06+.34*depth(d)**1.2)
def edgec(d): return mix(CANVAS,hue(d),.18+.72*depth(d))
cl="M"+" L".join(f"{x:.1f} {y:.1f}" for x,y in pts[::6])

L,G,off=178,40,30
out=[]
def axon_w(d): return max(1.5,width(d)*0.1)
# axon: short pieces so it tapers too
for i in range(0,len(pts)-12,12):
    (x0,y0),(x1,y1)=pts[i],pts[i+12]
    out.append(f'<line x1="{x0:.1f}" y1="{y0:.1f}" x2="{x1:.1f}" y2="{y1:.1f}" stroke="{mix(CANVAS,hue(acc[i]),.2+.5*depth(acc[i]))}" stroke-width="{axon_w(acc[i]):.2f}" stroke-linecap="round"/>')
nodes=[]
k=0
while True:
    d0=k*(L+G)-off; d1=d0+L
    if d0>total: break
    a,b=max(d0,0),min(d1,total)
    if b-a>4:
        side1=[];side2=[]
        n=120
        for j in range(n+1):
            d=a+(b-a)*j/n; (x,y)=at(d); nx,ny=normal(d)
            r=width(d)/2
            # round the capsule ends
            e=min(1,(d-d0)/r,(d1-d)/r) if r>0 else 1
            r*=math.sqrt(max(0,1-(1-e)**2)) if e<1 else 1
            side1.append((x+nx*r,y+ny*r)); side2.append((x-nx*r,y-ny*r))
        poly=side1+side2[::-1]
        pa,pb=at(a),at(b); da,db=depth(a),depth(b)
        fill=lin(pa,pb,fillc(a),fillc(b))
        edge=lin(pa,pb,edgec(a),edgec(b))
        out.append('<path d="M'+" L".join(f"{x:.1f} {y:.1f}" for x,y in poly)+f'Z" fill="{fill}" stroke="{edge}" stroke-width="2" stroke-linejoin="round"/>')
    dn=d1+G/2
    if 0<dn<total and 0<at(dn)[0]<1200: nodes.append(dn)
    k+=1

xs=[at(d)[0] for d in nodes]
start=min(range(len(nodes)),key=lambda i:abs(xs[i]-270))
hops=nodes[start:start+4]        # three hops: passed, in flight, ahead
def hop_curve(a,b):
    (x0,y0),(x1,y1)=at(a),at(b); nx,ny=normal((a+b)/2)
    if ny>0: nx,ny=-nx,-ny
    h=0.55*math.dist((x0,y0),(x1,y1))
    return (x0,y0),((x0+x1)/2+nx*h,(y0+y1)/2+ny*h),(x1,y1)
def q(c,t):
    (x0,y0),(cx,cy),(x1,y1)=c; u=1-t
    return (u*u*x0+2*u*t*cx+t*t*x1, u*u*y0+2*u*t*cy+t*t*y1)

curves=[hop_curve(a,b) for a,b in zip(hops,hops[1:])]
for c in curves:
    for i in range(1,30):
        x,y=q(c,i/30)
        out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="1.6" fill="#3A4855"/>')
def along(u):                      # u in hop units: hop index + position within it
    h=min(int(u),len(curves)-1); t=u-h
    return h,t,q(curves[h],t)
HEAD,PERIOD=1.62,0.5               # a pulse train: packets one half-hop apart, fading behind the head
for k in range(4):
    head=HEAD-k*PERIOD
    if head<=0: break
    level=[1,.55,.32,.18][k]
    n=20
    for i in range(n):
        f=i/(n-1)
        u=head-0.3*(1-f)**1.3
        if u<0: continue
        h,t,(x,y)=along(u)
        _,_,(xa,ya)=along(max(u-.01,0)); _,_,(xb,yb)=along(u+.01)
        dx,dy=xb-xa,yb-ya; m=math.hypot(dx,dy) or 1
        jit=math.sin(i*12.9898+k*78.233)*43758.5453%1-0.5
        spread=(1-f)*20
        x+=-dy/m*jit*spread; y+=dx/m*jit*spread
        r=2.6+6.4*f**2
        op=(0.15+0.85*f**2)*level
        out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r:.2f}" fill="{hue(hops[h]+t*(hops[h+1]-hops[h]))}" opacity="{op:.2f}"/>')
    h,t,(x,y)=along(head)
    out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{26*level**.5:.1f}" fill="{hue(hops[h]+t*(hops[h+1]-hops[h]))}" opacity="{.16*level:.2f}"/>')
    if k==0: out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="4.5" fill="#EBF3F7"/>')

for i,d in enumerate(nodes):
    x,y=at(d); s=width(d)/30
    if d in hops[:2]:   # reached
        out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{22*s:.1f}" fill="#64D9EE" opacity=".14"/><circle cx="{x:.1f}" cy="{y:.1f}" r="{9*s:.1f}" fill="#64D9EE"/>')
    elif d==hops[2]:    # about to be reached
        out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{9*s:.1f}" fill="#0D1118" stroke="#64D9EE" stroke-width="2.5"/>')
    else:
        out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{6*s:.1f}" fill="{edgec(d)}"/>')

svg=f'''<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="630" viewBox="0 0 1200 630">
<defs><radialGradient id="glow" cx="520" cy="500" r="560" gradientUnits="userSpaceOnUse" gradientTransform="translate(520 500) scale(1 .5) translate(-520 -500)"><stop offset="0" stop-color="#64D9EE" stop-opacity=".16"/><stop offset=".45" stop-color="#6D9FFB" stop-opacity=".07"/><stop offset="1" stop-color="#9D8CFF" stop-opacity="0"/></radialGradient>{''.join(defs)}<style>{FONT} text{{font-family:Geist,system-ui,sans-serif}}</style></defs>
<rect width="1200" height="630" fill="#0D1118"/>
<rect width="1200" height="630" fill="url(#glow)"/>
<g transform="translate(88 70) scale(1.55)">{wm_inner}</g>
<text x="100" y="325" fill="#EBF3F7" font-size="46" font-weight="500">Fine-grained reactive computation for .NET.</text>
<text x="100" y="377" fill="#9BAAB5" font-size="28">Signals, memos, effects and async boundaries in F#</text>
{chr(10).join(out)}
</svg>'''
open('ranvier-og.svg','w',encoding='utf-8').write(svg)
