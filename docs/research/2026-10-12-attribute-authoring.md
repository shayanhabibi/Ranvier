# Generated common attribute authoring

The HTML generator now emits 34 named static/reactive attributes and generic data-/aria- helpers under `Html.Attrs`. A versioned Ranvier policy maps names and semantics to native members validated through the published Xantham catalogue, Wire and binding assembly. Its SHA-256 is recorded in generated coverage. Native subtype constraints prevent input-only attributes on unrelated elements.

The shared runtime supports optional and reactive string attributes. None removes an attribute, HTML booleans use presence/removal, and ARIA booleans preserve literal false. Reactive writes reuse the existing effect, committed equality, queue and disposal path. No per-attribute roots or error policy were added. HTML snapshots evaluate readers, escape values and preserve duplicate removal/reinsertion ordering.

Input value/checked and option selected attributes represent defaults rather than live properties. A regression verifies that updating the value attribute preserves a dirty input's live value. Live property helpers remain separate. Unsafe event-handler/srcdoc names are rejected. This is a curated common-authoring subset; SVG, complete portable property metadata, hydration and descendant error/reveal work remain open.

Foundation's playground uses generated IDs, class, reactive row-count and ARIA busy state. Acceptance passes 188 Node cases and 18 Chromium tests. Generation is byte-identical with deterministic coverage; malformed names and corrupt catalogue payloads fail without overwriting output. Compiler checks include one positive and twelve negative examples. Release compilation, the portable .NET representation executable, scoped FCS and XML checks also pass. The production bundle builds at 83.43 kB JavaScript / 23.35 kB gzip. No renderer throughput benchmark or performance ranking is claimed.

Review found that policy validation accepted empty or non-letter-leading names that the runtime rejected. Validation now matches the runtime's ASCII name grammar; rejection tests cover empty, digit/hyphen-leading and Unicode-digit names.
