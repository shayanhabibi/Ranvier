# Component and typed prop proof

The experimental component helper now expands to the direct component operation in Release Fable output. Both Tree and Program pass the component semantics proof. This is framework Task 4 evidence, not a production API freeze or an overall performance claim.

## Authoring and lifetime

`Components.create ops setup props` builds an inert, reusable definition. Browser setup executes once per mounted instance, untracked, in the caller graph and a child owner. Allocating a signal inside setup gives each instance independent state; a signal captured outside setup remains shared. The compiled `independentState` example in `examples/Fable.Ranvier.Playground/tests/ComponentChecks.fs` demonstrates both in one typed `StateProps` record.

`Prop.read` reads now in the caller tracking context; it creates no observer or cache. `ops.Text (Prop.read prop)` inside browser setup is a snapshot; `Prop.text ops (=) prop` selects static Text for Constant and a live Bind for Reactive. Signal and memo adapters defer their reads. Memo caching is supplied by the explicitly created memo. `adapters` demonstrates all three live sources and a snapshot, including their behavior after disposal. Retained prop adapters still read their source after a mount is disposed; adapters do not own the source or extend the mount lifetime. Constant reference values preserve normal reference mutability.

The compiler-checked `PanelProps<'view>` and `panel` example in `experiments/Ranvier.View.Foundation/ComponentPortableChecks.fs` uses ordinary typed records, callbacks and reusable child views. The browser `typedChildren` example adds listener ownership and proves independent child instances. No props proxy, plugin, implicit graph or automatic memo is involved. Callbacks express writes explicitly; these read adapters expose no setter, but they do not make referenced objects deeply immutable.

Failure tests prove that nested listeners, bindings, component cleanups and generated DOM are released without removing existing host siblings. Expired browser and server setup cleanup capabilities reject registration. The experimental HTML interpreter invokes setup directly: it does not create a component owner or untrack reads. Rendering inside an active effect can subscribe that caller, and effects created by server setup are not automatically released by this interpreter. Full request-local server ownership remains Task 9. The server guard fixes previously silently lost registrations after render completion; existing failure aggregation remains intact.

## Allocation evidence

Workload: retain 100,000 inert component definitions per sample, with one shared typed props record and one setup function outside the definition loop. Static and reactive props are separate workloads, but neither is evaluated during definition construction. Array storage is included; shared props and setup contribute a negligible fixed amount. Mounted resources are measured separately by the behavioral tests.

Node 26.7.0 / V8 14.6.202.34-node.28, Fable 5.18.0 Release: three independent child processes per representation/prop/helper combination, seven samples each after a 10,000-definition warmup. Forced GC brackets the retained heap measurement. Before inlining, direct definitions retained about 160 bytes each and helper definitions about 208 bytes: an extra 48-byte closure environment at the helper call boundary. The raw pre-optimization data is retained in `view-components/node-before-inline.json`.

With `create` inline, direct and helper medians are both about 160.01 bytes per definition for both representations and both prop kinds. The generated benchmark branches now both call `ops.Component(context => setup(context, props))`; the helper call boundary disappears. Raw final data: `view-components/node.json`. These are retained V8 heap samples, not allocation traffic or browser mount measurements; the fixed process order and GC noise limit inference.

.NET 10.0.12: optimized FSI driver referencing the Release assemblies, seven samples per combination after a 1,000-definition warmup, measuring `GC.GetAllocatedBytesForCurrentThread`. Direct and helper allocate 72.00072 bytes per Tree definition and 88.00072 per Program definition, including the retained array and tiny shared setup cost. Raw output, including six portable test successes: `view-components/dotnet.txt`. These .NET figures cannot be treated as browser allocation figures.

Resource tests use the unchanged Browser baseline: a component has two owners and one setup; Constant text adds zero binding effects/writes, Reactive text one effect/initial write. Prop construction still allocates a DU and its fields array in JavaScript; Signal/Memo adapters also allocate a reader closure. `Prop.text` passes the reader directly to Bind, but the generated comparer uses Fable's `curry2` adapter. None of those observations supports an allocation-free claim.

## Reproduction and acceptance

Run from the DOM worktree:

```powershell
rtk proxy npm --prefix examples/Fable.Ranvier.Playground run compile:tests
rtk proxy node examples/Fable.Ranvier.Playground/benchmarks/view-components.mjs
rtk proxy dotnet build examples/Fable.Ranvier.Playground/tests/DomTests.fsproj -c Release
rtk proxy dotnet fsi --optimize+ experiments/Ranvier.View.Foundation/ComponentProof.fsx
rtk proxy npm --prefix examples/Fable.Ranvier.Playground test
rtk proxy dotnet fsi experiments/Ranvier.View.Foundation/RepresentationChecks.fsx
```

The whole DOM acceptance suite is 106 Node and 15 Chromium tests. Release DomTests builds with zero warnings/errors; the scoped FCS check is clean. Six pure component cases also passed in the rebuilt SageFs session. Its HTTP client reported an SSE BrokenResourceError during teardown after structured evaluation success; CLI acceptance remains the gate.

Framework Task 4 is proven experimentally. Status/error/pending policies, keyed regions, production representation selection and integrated allocation/timing budgets remain open. Efficient component sugar has a demonstrated direct expansion here; full Oxpecker/Feliz-style wrappers still require their own equivalence and performance proof.
