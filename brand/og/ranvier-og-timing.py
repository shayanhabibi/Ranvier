"""Timing card: changed captures extending quiet time or sharing a fixed window."""
from oglib import Card, wordmark, save, CYAN, VIOLET, TEXT, MUTED

c = Card()
c.glow(850, 445, 560, strength=.9)
c.add(wordmark(88, 44, .62),
      f'<text x="100" y="170" class="mono" fill="{CYAN}" font-size="22" letter-spacing="3">GUIDE</text>',
      f'<text x="100" y="242" fill="{TEXT}" font-size="64" font-weight="600">Debounce and throttle</text>',
      f'<text x="100" y="292" fill="{MUTED}" font-size="26">Capture now. Admit after quiet, or at a fixed deadline.</text>')

for y, label, start, deadline, caption in [
    (400, "DEBOUNCE", 650, 990, "quiet period"),
    (525, "THROTTLE LAST", 410, 830, "fixed window"),
]:
    c.add(f'<text x="100" y="{y + 7}" class="mono" fill="{MUTED}" font-size="20">{label}</text>',
          f'<path d="M330 {y}H1090" stroke="{MUTED}" stroke-opacity=".3" stroke-width="2"/>',
          f'<rect x="{start}" y="{y - 25}" width="{deadline - start}" height="50" rx="12" '
          f'fill="{VIOLET}" fill-opacity=".08" stroke="{VIOLET}" stroke-width="2" stroke-dasharray="6 5"/>')
    for x in (410, 525, 650):
        c.add(f'<circle cx="{x}" cy="{y}" r="7" fill="{CYAN}"/>')
    c.add(f'<circle cx="{deadline}" cy="{y}" r="18" fill="{VIOLET}" fill-opacity=".18"/>',
          f'<circle cx="{deadline}" cy="{y}" r="9" fill="{VIOLET}"/>',
          f'<text x="{(start + deadline) / 2}" y="{y + 54}" text-anchor="middle" '
          f'class="mono" fill="{MUTED}" font-size="18">{caption}</text>')

print(save(c, "ranvier-og-timing"))
