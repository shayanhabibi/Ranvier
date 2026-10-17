# Type-directed optimisation candidates

F# types state facts that JavaScript frameworks either discover at runtime or derive with a compiler: element types, slot value types, union cases, key types, and which values are signals. The [component templates design](../superpowers/specs/2026-10-13-component-templates-design.md) uses five of them: direct bindings from signal accessors, typed slot kinds, specialised materialisers, one event property per node, and `T.matchWith` arms. This note records the candidates that belong to later designs. None is implemented or measured.

## Keyed lists by key type

Keyed reconciliation compares boxed keys through `obj -> obj -> bool` and checks duplicates quadratically. With `int` or `string` keys, reconciliation can use a native `Map` keyed by the primitive and a typed array of positions, and detect duplicates in one pass. Belongs to the keyed-row design, measured on a list benchmark against Solid 2.

## Server rendering on .NET

- **Precompiled UTF-8 markup.** Encode each template's static markup to UTF-8 once. Rendering writes the precompiled chunks and slot values into a pooled `IBufferWriter<byte>` or `PipeWriter`, with no intermediate strings and no allocation for static content.
- **Escaping by type.** `int`, `bool`, enum and union-case slots need no HTML escaping and format directly as UTF-8 digits or names. Only `string` slots pass through a vectorised escaper built on `SearchValues`.
- **Evaluation without the reactive graph.** A server render evaluates each reader once, untracked. A dedicated evaluation mode reads signal values directly and creates no effects, owners or observer edges.
- **Compiled render delegates.** Compile each template once into a typed delegate through `System.Linq.Expressions` or IL emission, giving straight-line rendering without an interpreter or boxing. NativeAOT forbids runtime IL emission, so an interpreted renderer remains the fallback there.

These belong to a server-rendering design, measured as requests per second and allocations per render.

## Templates as F# quotations

A template written as `<@ fun r -> ... @>` exposes its expression tree. From it, a renderer can prove which readers touch no signal and evaluate them once per instance, infer each reader's dependencies and bind them directly without `T.from`, and generate rendering code. Fable's quotation support is limited, so this is a server-side or build-step option. It is the closest the explicit API can come to a compiler without a Fable plugin.
