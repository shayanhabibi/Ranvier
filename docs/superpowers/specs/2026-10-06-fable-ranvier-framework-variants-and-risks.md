# Fable.Ranvier variants, performance and gotchas

Date: 2026-10-06. Companion to the [brief](2026-10-06-fable-ranvier-framework-brief.md) and [plan](../plans/2026-10-06-fable-ranvier-framework.md). Recommendations below are proposed mechanisms, not additional agreed defaults.

Labels: **Gain** identifies an opportunity to measure; **Cost** identifies potential overhead; **Gotcha** identifies behavior requiring a contract and a test. Framework comparison lines describe an analogous solution, not proof that its full semantics match Ranvier. Sources are official documentation checked for this design; stable Solid documentation is not a claim about unreleased Solid 2 behavior.

## Implementation variants

### V01 — View representation

- **A: Portable instruction tree.** An opaque DU/object tree represents elements, text, composition and bindings; backend interpreters create DOM or HTML. Easiest to inspect and serialize conceptually, but closures and live reactive state themselves are not serializable. **Cost:** definition allocations, dispatch and retained descriptions.
- **B: Backend-neutral build program.** An opaque executable definition receives explicit renderer capabilities. **Gain:** possible reduction in intermediate tree allocation. **Cost:** closure allocation, indirect calls and less static visibility. Browser-specific signatures can accidentally destroy portability.
- **C: Browser factory with a separate server implementation.** Simple browser hot path, but duplicates application structure and makes hydration agreement harder. Useful only as an explicit browser-only escape hatch.

**Proposal:** prototype A and B behind the same public opaque `View`; choose from measured output and a server proof. Do not select C as the sole portable core. Neither A nor B requires update-time virtual DOM diffing: definitions are instantiated once, and bindings update their own targets.

### V02 — Props and readers

- Use the agreed `Prop<'T>` DU at authoring boundaries and normalize once during setup.
- A `unit -> 'T`-only surface is smaller but cannot distinguish static intent from dynamic intent without separate functions.
- A read-only source interface can expose stronger capability information, but virtual calls and extra wrappers need justification. Introduce it only when it provides more than read access.

**Gain:** specialize constants to avoid effects entirely. **Cost:** repeated DU matching, adapter closures and automatic memos would penalize tiny bindings. Keep caching explicit.

### V03 — Ownership implementation

- Renderer-managed child registry over Ranvier owners could impose child-first teardown without changing graph-wide contracts.
- A core lifecycle extension could expose the required disposal phase directly, but changes public API and potentially unrelated consumers.
- A detached root per component simplifies some isolation cases but increases bookkeeping and breaks natural parent disposal unless manually reconnected.

**Proposal:** test the registry adapter first, including disposal initiated by the graph and by effect reruns. If it cannot guarantee ordering, stop and review a core extension. Do not ship a partially correct adapter. Use lightweight child ownership, not a new graph per component.

### V04 — Lists and keys

- Snapshot keyed reconciliation is the baseline; optimize unchanged prefixes/suffixes and retain ranges.
- An incremental collection adapter avoids rescanning untouched entries when its producer supplies valid deltas.
- A positional variant favors stable positions and clearly documents state following position.
- An LIS move optimizer may reduce DOM moves on complex reorder, but adds arrays and computation to small lists.

**Gain:** retained instances avoid setup, listener creation and DOM replacement. **Cost:** an equality-only custom comparer can force quadratic lookup; a hash-based implementation needs consistent equality and hashing. JS `Map` identity is not automatically F# structural equality. Do not change the user's key semantics to gain speed.

### V05 — Scheduling and event dispatch

- Synchronous commits minimize deferred bookkeeping and make selected timing straightforward.
- Microtask queues can coalesce repeated expensive writes, at the cost of slots, callbacks, invalidation tokens and latency.
- Animation-frame scheduling may suit animation-specific integrations later; it is not a substitute for the other strategies.
- Direct listeners are the baseline. Delegation can reduce listener count but changes handling of propagation, non-bubbling events and event context.

**Proposal:** preserve explicit scheduler selection; start with direct listeners. No scheduler or delegation choice becomes an invisible optimization if it changes observable timing or event behavior.

### V06 — DSL, compiler and server delivery

Start with ordinary functions and typed modifiers. Add an optional CE for composition after core contracts settle. A Fable plugin can specialize static structure and binding setup, but must preserve the same explicit operations. Compiler transformation cannot rescue code rejected earlier by F# parsing or type checking.

For server delivery, static HTML generation is the first milestone; hydration follows once stable identity, escaping and portable property semantics exist. A separate SSR DSL is possible but makes shared structure and hydration consistency more expensive to maintain.

**Gain:** cached static templates, constant binding elimination and specialized setters may reduce startup work. **Cost:** generated-code size, code splitting boundaries and duplicated client/server logic. [Vue's compiler-informed rendering](https://vuejs.org/guide/extras/rendering-mechanism.html) demonstrates static caching and compile-time hints, although Vue's runtime architecture differs.

## Performance evidence and priorities

The existing [PoC measurements](../../.ai/benchmarks/dom-scheduling/README.md), dated 2026-10-02, favor synchronous batching for several measured workloads: counter 5.4 ms versus queued batching 6.2 ms; dashboard bursts 25.3 versus 36.9 ms; single updates 47.3 versus 68.9 ms. In the costly-setter workload, queue-only improves over unbatched synchronous writes (57.2 versus 272.4 ms), but synchronous batching is still faster than queued batching (27.6 versus 35.3 ms).

These runs exclude mount/disposal/paint, use one browser/environment, and use independent row graphs rather than a shared-graph dashboard. They do not establish Solid parity or a universal scheduler winner.

Prioritize measurement in this order:

1. **Gain:** eliminate effects for constants; avoid component reruns; keep reads tracked and writes untracked.
2. **Gain:** reduce redundant writes with explicit batching/equality; retain keyed instances and ranges.
3. **Cost:** per-binding memos, closures, queue entries, owner records and Fable generic equality helpers. Inspect Release JS and allocation profiles.
4. **Gain:** optimize list scans/moves and static creation only after identifying hot paths.
5. **Cost:** layout thrashing, input latency, long synchronous waves and retained subscriptions may dominate small runtime savings.

Benchmark mount, steady updates, list reorder, disposal, retained memory, bundle size and interaction latency separately. Include one shared graph with many components, many independent mounts, cheap and expensive setters, and mostly-static views. Compare equivalent behavior against pinned Solid versions using the same browser, data, warmup and production settings. Record medians and tail latency across multiple process runs; no parity claim from a single microbenchmark.

## Gotcha register

### G01 — Snapshot props and repeated readers

**Gotcha:** reading a prop before creating a binding captures its current value; a getter may also repeat expensive work on each call. Keep constant/reactive operations distinct and make memoization explicit. **Cost:** accidental repeated computation or excessive memo nodes.

Framework comparison: [Solid preserves reactive props through property access/getters and warns that destructuring can lose reactivity](https://docs.solidjs.com/concepts/components/props).

### G02 — Read-only access and accidental sharing

**Gotcha:** read-only props do not freeze nested objects, and reusable view closures can intentionally or accidentally share state. Allocate instance state in setup and request state inside the request scope; never infer cloning from intended usage.

Framework comparison: [Vue makes prop bindings read-only while documenting that nested object/array values can still be mutated](https://vuejs.org/guide/components/props.html).

### G03 — Cleanup order and failure

**Gotcha:** core parent-first cleanup conflicts with the renderer's child-first, attached-DOM contract. Disposal must also finish when a cleanup throws. Prove renderer and graph-initiated disposal, partial setup failure and reentrant disposal before building structural APIs.

Framework comparison: [Solid associates cleanup with the current owner and runs it on disposal or reactive-scope refresh](https://docs.solidjs.com/reference/lifecycle/on-cleanup); that API alone does not establish Ranvier's proposed cleanup order or failure aggregation.

### G04 — External DOM removal

**Gotcha:** removing a native node does not notify the reactive owner. Require explicit disposal at integration boundaries; do not rely on eventual mutation observation to establish lifecycle correctness.

Framework comparison: [React requires `root.unmount()` when another system removes a root or its ancestor so subscriptions and other resources are released](https://react.dev/reference/react-dom/client/createRoot).

### G05 — Keys encode instance identity

**Gotcha:** stable keys retain state even when the data changes; captured initial values become stale. Pass a current-value reader to retained instances. Include the variant discriminator when changing component shape must reset state; reject duplicate keys before mutating the mounted list.

Framework comparison: [React preserves state by tree identity and uses changing keys to deliberately reset a subtree](https://react.dev/learn/preserving-and-resetting-state).

### G06 — Memos are not subscriptions with free caching

**Gotcha:** source equality, memo equality and committed DOM equality are different. A failed setter must not advance a proposed last-successful-write cache; mutable values can invalidate equality assumptions. **Cost:** deep equality can exceed the cost of the avoided write.

Framework comparison: [Solid memos cache derived values and expose an equality option, including disabling equality suppression](https://docs.solidjs.com/reference/basic-reactivity/create-memo).

### G07 — Reentrancy and queue invalidation

**Gotcha:** a setter can synchronously trigger events, updates or disposal. Check liveness before every queued action, not just at snapshot creation; invalidate tickets when manually flushing or disposing. Define remaining-entry handling after fail-fast. **Cost:** endlessly generated waves can starve input; any diagnostic limit must be an explicit policy.

Framework comparison: [Vue batches watcher callbacks and exposes pre/post/synchronous flush modes, warning that synchronous watchers are unbatched](https://vuejs.org/guide/essentials/watchers.html).

### G08 — Async ownership and stale completions

**Gotcha:** cancellation is cooperative; a completed request can target a disposed or replaced instance. Owned task helpers need cancellation plus a lifetime/generation check, and must not depend on owner context surviving an await.

Framework comparison: [Vue provides watcher cleanup for invalidating stale async work and documents synchronous registration constraints for `onWatcherCleanup`](https://vuejs.org/guide/essentials/watchers.html).

### G09 — Pending and errors are rendering decisions

**Gotcha:** a core effect that skips its action on a pending/error read can accidentally preserve old DOM. A status adapter must expose changes in status, not merely successful values. Specify initial versus later failure and unhandled outcomes explicitly.

Framework comparison: [Svelte's await blocks explicitly declare pending, fulfilled and rejected branches](https://svelte.dev/docs/svelte/await).

### G10 — Form properties, composition and focus

**Gotcha:** attributes are not interchangeable with live `value`/`checked` properties. Repeated writes can disturb selection or IME composition; keyed moves can affect focus and browser-managed state. Test typing and reorder in real browsers, not only jsdom.

Framework comparison: [Vue's `v-model` selects control-specific properties/events and treats IME composition specially](https://vuejs.org/guide/essentials/forms.html).

### G11 — Setup versus attachment

**Gotcha:** setup cannot safely measure a node that is not attached. Mounted callbacks may create resources and fail halfway through; register cleanup before exposing the instance and define rollback of partial mounting. Browser hooks must not execute during SSR.

Framework comparison: [React effects run only on the client, with development Strict Mode exercising an extra setup/cleanup cycle](https://react.dev/reference/react/useEffect).

### G12 — Portable properties and hydration

**Gotcha:** an arbitrary browser setter has no automatic HTML meaning. Portable property tokens need explicit server serialization; deterministic IDs, escaping, state and branch selection must agree across server/client. .NET graph threading policies do not imply safe concurrent request sharing.

Framework comparison: [React hydration expects matching server/client output and treats mismatches as bugs rather than guaranteeing arbitrary repair](https://react.dev/reference/react-dom/client/hydrateRoot).

### G13 — Escape hatches and HTML injection

**Gotcha:** raw HTML bypasses text escaping and can invalidate owned ranges or introduce script injection. Keep it explicitly named, isolate its owned region, and require callers to supply trusted/sanitized content under a documented contract.

Framework comparison: [React exposes raw markup through the explicitly named `dangerouslySetInnerHTML` and warns about untrusted HTML](https://react.dev/reference/react-dom/components/common#dangerously-setting-the-inner-html).

### G14 — Sugar changing lifecycle

**Gotcha:** wrapping ordinary branching in a CE does not by itself make it reactive or create the right nested owners. Plugin optimizations must preserve branch identity, setup count, cleanup and scheduling, including early exits and nested composition.

Framework comparison: [Svelte exposes keyed blocks explicitly to destroy and recreate their contents when the key changes](https://svelte.dev/docs/svelte/key).

### G15 — Moves, borrowing and double ownership

**Gotcha:** one mounted range cannot belong to two owners. Reparenting owned nodes outside the framework can make later disposal remove content from an unexpected location. Require explicit transfer or a clearly scoped borrowed integration; do not infer ownership from the current DOM parent.

Framework comparison: [React warns against changing DOM nodes it manages because manual mutations can conflict with its updates](https://react.dev/learn/manipulating-the-dom-with-refs).
