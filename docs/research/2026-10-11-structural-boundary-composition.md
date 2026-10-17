# Structural selection from a core boundary

Based on `34dec3d`, six new checks compose existing primitives: a core computation boundary returns a keyed presentation value, and a singleton `Keyed.list` mounts its selected loading, error or content view. Ordinary text still reads strings. This adds proof coverage without changing the runtime or its public surface.

The shared computation reads two async sources. Settling only one keeps the loading instance. Once both settle, the content instance displays both values. Same-key content updates preserve the element and both text nodes; repeated failures update the retained error instance. A changed selection key disposes the previous instance. Returning to content after loading/error creates a fresh content instance; hidden content is not retained.

The four DOM checks cover Tree/Program and synchronous/queued text commits. They verify replacement cleanup, stale queued writes cancelled when a row is removed, stopped reconstruction after disposal, and exact setup/cleanup counts. The two portable checks serialize loading/content/error snapshots with HTML escaping. The DOM cases are included in both the Node binding runner and Chromium range harness.

Structural reconciliation remains synchronous even when text commits are queued. This proof does not establish coordinated reveal across independent regions, retained hidden views, descendant effect-error capture, or recovery from arbitrary DOM setter failures. Those require separately defined contracts. The singleton list also uses the existing list machinery and adapters; it establishes behavior, not an optimized branch cost.

Validation: all 162 Node cases and 18 Chromium tests pass. Release DomTests builds with zero warnings/errors, fresh scoped FCS is clean, the six-group portable executable passes, and the reloaded SageFs session passes all 18 portable binding cases. XML audit has zero findings. Review corrected a test expectation: owned descendant text nodes are removed during disposal, so stale-write assertions inspect the captured text nodes rather than their emptied element.

Next: define a dedicated branch's identity and scheduling contract before choosing whether to specialize this composition. Structural error boundaries must account for the current core's computation-only error capture; adding outcome policy to text would not solve that contract.
