# Published catalogue integration and five-tag generation

The generation prerequisite is now available. Published Cloudsmith `Xantham.Fable.Core.TS` 0.2.1 contains `xantham/catalogue.json` and `xantham/declarations.json.br`. The descriptor identifies the packaged native assembly and payload by hash. The earlier 0.2.0 investigation remains historical evidence, not current availability.

The [narrow generator](../../tools/Ranvier.Dom.Generate/README.md) consumes restored published packages and pinned TypeScript inputs without a sibling checkout. It validates release hashes, descriptor/schema/compatibility fields, compiler revision and declaration input hashes. Wire 0.3.0 supplies tag maps, event maps, inheritance, member identities and readonly facts. Normalized compiler declaration handles resolve through the catalogue to existing native bindings; reflection supplies their actual F# member representations. Missing, ambiguous or unsupported mappings fail. This is a pinned adapter, not Xantham's complete catalogue authentication/application engine.

Generated [Html.fs](../../experiments/Ranvier.View.Foundation/Generated/Html.fs) covers `div`, `button`, `input`, `select` and `option`. It provides inherited title helpers, applicable value helpers, input checked helpers and mapped `InputEvent` listeners. Input files stays `FileList option`; form stays `HTMLFormElement option`. Form is readonly upstream. Files is writable upstream, but its setter is intentionally omitted by Ranvier policy.

Generated helpers call handwritten `Typed` operations. Static properties write during setup; reactive properties use one owned effect and the mount's existing synchronous/queued binding path. Text continues using its ordinary string reader; sharing the binding implementation adds no outcome policy or extra owner to text nodes. Listeners capture the graph/owner, untrack handler reads and unregister on disposal. They introduce no implicit event batch. Live property writes do not become HTML attributes. Select modifiers run after options are built so initial selection is preserved.

The consumer pins Core 0.2.0 and Core.TS 0.2.1 and adds the public Cloudsmith feed in repository NuGet configuration. Generator dependencies and npm compiler inputs remain tooling-only. Compiler-tested modifier types reject input-only modifiers on a div and incorrect property values. Readonly setters and the policy-excluded files setter are also rejected.

Verification passes:

- Complete scoped suite: 176 Node cases and 18 Chromium tests, including generated/handwritten equivalence across Tree/Program and synchronous/queued mounts, independent mounts, form booleans, typed listeners, cleanup and queued cancellation.
- Release DomTests build and fresh scoped FCS check: zero errors/warnings. Portable .NET representation executable: six groups passed. XML documentation audit: zero findings.
- Generator Release build, deterministic regeneration and corrupt-payload rejection; rejected inputs preserve the previous generated output.
- Positive generated usage and four negative compiler checks via `check-types.py`.
- Release emitted JS uses direct native property assignments and the same handwritten runtime helpers. Production bundle: 81.57 kB JavaScript / 22.70 kB gzip. Generated helpers are compiled but unused by the playground application, so this bundle figure is not their full retained contribution. No new throughput experiment or speed ranking was run.

Generation covers the selected primitive/optional/native-reference shapes. Generic declarations, arbitrary unions, callbacks/arrays beyond this subset, broad HTML/SVG coverage, portable property serialization, generated attribute helpers, application migration and performance budgets remain open. Browser-only modifiers require the explicit HTML interpreter policy. Descendant error boundaries and coordinated reveal are separate runtime tasks.

Tool friction: SageFs evaluated declaration identity and unsupported native-type probes. Cleanup of tool-created sessions `e8da4c30` and `21711239` was rejected: “the session is owned by another bound MCP connection; use the connection that created it or stop it through a privileged daemon control”. The reconnecting MCP helper cannot retain that connection. Both sessions remain; the daemon and the existing user session were left running. CLI builds/tests provide the final gates.
