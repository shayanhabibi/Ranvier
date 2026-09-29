# Ranvier brand

This file is the brand reference for the repository. It supersedes the v1.0 brand pack and brief,
which are not kept here.

## Kept

- **Colour tokens**: `tokens/ranvier-brand-tokens.css` and `.json` (v1.1). Dark is the default
  scheme; light is a full companion. Use the named tokens instead of adding near-duplicate shades.
- **Accent gradient** (`--rv-gradient-accent`): one accent moment per page, such as the primary
  landing action. Never on body text, small icons or status colours.
- **State marks**: `states/{ready,pending,retained,fallback,failed,recovered}-{dark,light,mono}.svg`.
  They label what a reader observes, always next to a text label. Pending, value and error stay
  flat semantic colours.
- **Voice**: precise, calm, technical. Say what the API does; no benchmark superiority claims, and
  no claims of instantaneous propagation or universally minimal updates.

## Retired

Do not reintroduce these:

- The F2 "arrival pulse" logo, wordmark, icon and gradient lockups. At small sizes the open arc
  and dot read as a loading spinner and blur with the Pending state mark.
- The outlined DejaVu Sans wordmark, and its font notice.
- The system-sans-only type direction.
- The full-bleed feature gradient (`--rv-gradient-feature`) behind heroes and editorial surfaces.
- The "Built to conduct change." tagline.

## Current direction

- **Name**: write **Ranvier** in running text. Until a new mark exists, the identity is the
  lowercase word `ranvier` set in Geist 600. The favicon is an interim `r` lettermark. The NuGet package icon, `icon/ranvier-icon.png`, is the same lettermark in the dark scheme,
  drawn by `icon/ranvier-icon.py`.
- **Type**: Geist for headings, navigation and the wordmark; the system sans stack for body
  copy; Geist Mono for code.
- **Motif**: a short contour line in `--rv-contour`, used for section rules, the active navigation
  item and code block edges.
- **Landing**: lead with the behaviour: a boundary moving through its states next to the code
  that drives it, then the state vocabulary.
