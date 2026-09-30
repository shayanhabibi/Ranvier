# AOT and trim analysis: design

**Status:** implemented on worktree-wf_c46b5816-3af-1 (no benchmark gate: no hot-path code changes). See §11 for what
landed and how it differs from this note. Line references in §§2–8 are to `c631f23`. All numbers were measured in a scratch worktree on
a 4-core Linux container with .NET SDK 10.0.112 and ILCompiler 10.0.12, restored from nuget.org. §7 names the CI case
that keeps them current.

## 1. Goal

Research §10 asks for an AOT and trim check in CI while the C# surface is still small. The target state:

- A NativeAOT app that uses Ranvier and Ranvier.CSharp publishes with **zero** IL2xxx/IL3xxx warnings.
- Every exception Ranvier throws keeps its type under NativeAOT for primitive and `string` keys (§3.1 lists the text
  changes). A key of a user record or union type still reaches FSharp.Core's formatter through its own generated
  `ToString`, unless the key's assembly is also compiled reflection-free.
- CI fails on any regression.

## 2. Findings on `master`

### 2.1 The SDK properties do no checking for F#

`<IsTrimmable>`, `<IsAotCompatible>`, `<EnableTrimAnalyzer>` and `<EnableAotAnalyzer>` enable `ILLink.RoslynAnalyzer`, which
ships only under `analyzers/dotnet/cs`. Measured on `src/Ranvier.CSharp` built with `-p:IsAotCompatible=true -p:IsTrimmable=true`:
the `Analyzer` item list is empty, the build reports 0 warnings, and the build takes 18 s either way. The only effect is
`[AssemblyMetadata("IsTrimmable"/"IsAotCompatible", "True")]` on the net10.0 and net8.0 outputs; netstandard2.1 gets
nothing. For F#, the properties make a claim and do not check it. The checks come from ILC at publish time (§2.2) and
from the F# compiler's `--reflectionfree` flag (§3).

### 2.2 NativeAOT publish: every warning with its file and line

A console app (`PublishAot`, `TrimmerSingleWarn=false`) creates a signal, memo, effect and projection. The "rooted"
variant adds `<TrimmerRootAssembly>` for both assemblies, so ILC analyses the whole public surface.

| Variant | Distinct warnings | Size |
| --- | --- | --- |
| F# app, FSharp.Core only (baseline) | 0 | 2,762,992 B |
| App using Ranvier | 46 | 3,944,712 B |
| Same, both assemblies rooted | 49 | 5,058,984 B |

ILC attributes **none** of the warnings to Ranvier source. They are all in FSharp.Core, reached from Ranvier:

- `sformat.fs` (the `%A` formatter): 481, 482, 537, 538, 546, 547, 555, 556, 582 (IL2067); 515, 516, 529 (IL2072);
  600, 1421, 1425, 1461 (IL2070); 1100 (IL2080); 1340, 1369, 1378 (IL2075).
- `printf.fs`: 1005, 1119, 1125, 1132, 1163 (IL2060 + IL3050); 1107, 1283, 1291 (IL3050).
- `reflect.fs`: 73, 76, 371, 375, 428, 542, 837, 839, 956 (IL2070); 432 (IL2055 + IL3050); 508 (IL2070 + IL2075).
- `prim-types.fs`: 2688 (IL2070); 2824, 2827 (IL2090). Rooted variant only.

The ILC dependency graph (`IlcGenerateDgmlFile`) traces them to three sources in Ranvier:

1. **Printf-format strings.** `%A` in `Projections.fs:182, 1015, 1363, 1417` and `Combinators.fs:914` (and the Fable-only
   `Projections.fs:1411`), and `%d` in `Core.fs:1530`. `Bindings.fs:362` has only a `%s` hole on a string and already
   compiles to `String.Concat`. A typed F# interpolated string compiles to `PrintfFormat`
   (graph edges from `Graph.AssertOnGraphThread` and `Projection.GetRow`). An untyped hole on a generic value (`{key}`)
   also compiles to `PrintfFormat`.
2. **Compiler-generated `ToString`** on the public unions, records and exception: `ThreadAffinity` (`Types.fs:19`),
   `FlightPolicy` (`Types.fs:34`), `NotReadyException` (`Types.fs:90`, its `Message`), `GraphOptions` (`Types.fs:257`),
   `Reading<'T>` (`Types.fs:304`). Each one calls `sprintf "%+A"`. The internal `PositionalChange<'T>` (`Positional.fs:5`),
   `FlightOutcome<'T>` (`Platform.fs:87`) and, in Ranvier.CSharp, `Notifications` (`Bindings.fs:82`, the actual printf
   edge from Ranvier.CSharp) do the same in `ToString` and `__DebugDisplay`.
3. **`Projection.sumBy`** (`Combinators.fs:1183`) is `inline` with SRTP. Its compiled non-inline body calls
   `AdditionDynamic`/`GenericZeroDynamic`, which accounts for the three `prim-types.fs` warnings. Call sites use the
   inlined body and never reach it.

### 2.3 Failures at runtime under NativeAOT (measured)

| Call | JIT | NativeAOT |
| --- | --- | --- |
| `proj.Get 99`, missing `int` key | `KeyNotFoundException` | `NotSupportedException` (MakeGenericMethod) |
| `proj.Get "nope"`, missing `string` key | `KeyNotFoundException` | `KeyNotFoundException` |
| Duplicate `int` key | `InvalidOperationException` | `NotSupportedException` |
| Off-thread write (`Core.fs:1530`) | `InvalidOperationException` | same, correct text |
| `(Ready 3).ToString()`, `(Ready "x").ToString()` | `Ready 3`, `Ready "x"` | `NotSupportedException` |
| `Guarded.ToString()` | `Guarded` | `KeyNotFoundException` |
| `GraphOptions.Default.ToString()` | record text | `"Error: An index satisfying the predicate..."` |

With a value-type key, `%A` replaces the documented exception with an unrelated one. The five generated `ToString`
members fail or return garbage.

### 2.4 Other items from §10

- **Static initializers that start threads:** none. The src tree contains no `Thread`, `Timer`, `ThreadPool` or `Task.Run`.
  Static state: `JsComparer<'T>.Instance` (`Types.fs:224-225`), `Core.fs:3180`, the `AsyncLocal` at `Platform.fs:267`, and
  `[<ThreadStatic>]` fields at `Core.fs:1053` and `Trace.fs:436`. With no synchronisation context,
  `Platform.defaultDispatcher` returns `ManualDispatcher` (`Platform.fs:426-432`). Blazor WASM therefore meets FDA#96's
  constraint.
- **Reflection:** `typeof` tests at `Types.fs:206-212`, `Api.fs:23,30` and `Platform.fs:316`, and `GetType().Name` at
  `Core.fs:3694` and `Projections.fs:1197, 1859, 1891`. ILC reports none of them.
- **`WeakReference`** (`Trace.fs:90, 402, 405-413`): compiled only in traced builds, and Fable gets a strong list
  (`Trace.fs:88-89`). No warning.
- **Traced build** (`-p:RanvierTrace=true`, rooted): the same FSharp.Core set plus one Ranvier warning,
  `TraceSite.fs:17` IL2026 (`StackFrame.GetMethod`). `assemblyOf` already handles the `null` it returns when metadata
  is trimmed (`TraceSite.fs:17-18`). ILC reports nothing for the `JsonDocument` reads (`TraceModel.fs:1683, 1706, 1782`).
  The traced `%A` sites are `TraceApi.fs:157, 165` and `TraceModel.fs:263`.
- **`Interlocked`** (`Counters.fs:42, 50`): compiled only with `RANVIER_COUNTERS`.

## 3. Proposed change

```xml
<!-- Ranvier.fsproj and Ranvier.CSharp.fsproj -->
<OtherFlags>$(OtherFlags) --reflectionfree</OtherFlags>
<IsAotCompatible Condition="$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net8.0'))">true</IsAotCompatible>
```

1. **`--reflectionfree`** is the F# compiler's substitute for the missing analyzer. It turns `%A` into error FS3376
   (measured on the traced sites) and stops generating `%+A`-based `ToString`.
2. **Replace the seven format-string sites** (six on .NET plus the Fable-only 1411), plus the three traced sites, with
   concatenation and `string key`.
3. **Hand-write `ToString`** on `ThreadAffinity`, `FlightPolicy`, `GraphOptions` and `Reading<'T>`, and write the
   `Message` of `NotReadyException`, with the text shown in the JIT column of §2.3 for `int` and `string` payloads.
   `Reading` quotes a `string` payload the way `%A` does and uses `string v` for any other payload, so other payloads
   change as in §3.1 (`Ready 3.0` becomes `Ready 3`, `Ready (Some 1)` becomes `Ready Some(1)`).
4. **`[<NoDynamicInvocation>]` on `sumBy`**. The compiled body then throws in place of the dynamic operator call.
5. **Traced build:** `[<UnconditionalSuppressMessage("Trimming", "IL2026")>]` on `TraceSite.assemblyOf`, or
   `DiagnosticMethodInfo.Create` (.NET 9+) under `#if NET9_0_OR_GREATER`.

Measured on steps 1, 2 and 4 applied to the untraced build: **0 warnings** in both the plain and the rooted variant. All
four exceptions in §2.3 keep their JIT type; key text changes as in §3.1. Steps 3 and 5 were unmeasured when this note
was written; §11 records the measurement with all five steps. The app shrinks from 3,944,712 B to 2,957,448 B (−25%); the rooted
app from 5,058,984 B to 4,162,808 B. `Ranvier.Tests` passes 755/755 (net10.0, Release).

### 3.1 Message text changes

Measured with `sprintf "%A"` against `string`:

| Key | `%A` today | `string key` |
| --- | --- | --- |
| `99`, `1.5`, a `Guid`, `[1; 2; 3]`, `set [1; 2]`, a user union | same | same |
| `3.0`, `1L`, `'c'` | `3.0`, `1L`, `'c'` | `3`, `1`, `c` |
| `"nope"` | `"nope"` | `nope` |
| `(1, "a")` | `(1, "a")` | `(1, a)` |
| `Some 1` | `Some 1` | `Some(1)` |
| `[\|1; 2\|]` | `[\|1; 2\|]` | `System.Int32[]` |
| user record | multi-line | same |

`docs/content/guide/troubleshooting.md:217, 246, 282` write the key as `<k>` and need no change. No F# test asserts on
the key text. Traced `valueText` keeps `%A` (§11: the traced build is not compiled `--reflectionfree`).

Without step 3, `--reflectionfree` changes observable text: `Ranvier.CSharp.Tests` fails 1 of 43 at
`AsyncTests.cs:113`, which expects `Ready "Loading…"` and gets `Ranvier.Reading` + "`1[System.String]". Step 3 keeps that
test passing.

## 4. Cost model

- **Write, recompute, flush:** no change. Every edited site is on a throw path or in a `ToString`.
- **Per-node memory and allocations:** no change. A throw builds its message with one `String.Concat` in place of a
  printf parse. On .NET the printf path caches the parsed format per call site; concatenation needs no cache.
- **Code that does not use the feature:** the property adds only assembly metadata. In the untraced build,
  `--reflectionfree` changes the members listed in §3 and removes the generated `ToString` and `__DebugDisplay` of the
  internal `PositionalChange`, `FlightOutcome` and `Notifications` types: no hot-path effect, but their debugger views
  change. The traced build is compiled without the flag (§11), so its public trace records and unions keep their
  generated `ToString` text.
- **Build:** the net8.0 target now restores `Microsoft.NET.ILLink.Tasks` 8.0.31 (5 MB). Library build time is unchanged
  (18 s either way; no analyzer runs).
- **Consumers:** trimmed and AOT apps shrink (§3). The `IsTrimmable` metadata lets a `TrimMode=partial` consumer trim
  Ranvier.

## 5. Fable

All edits are plain F#: `string key` compiles to `String(x)`, and a hand-written `ToString` is an ordinary override.
The Fable-only branch at `Projections.fs:1409-1414` takes the same concatenation. Measured: `dotnet fable
fable/Ranvier.Tests.Fable` compiles the patched tree in 32 s with no new warnings. It is not established whether Fable
forwards `OtherFlags`. The Fable build compiles either way, because the Fable sources contain no `%A` after step 2.

## 6. Breaking?

- **Binary and source compatibility:** unchanged. The four `ToString()` lines in `docs/.ai/public-api-baseline.txt`
  (82, 117, 204, 234) and `NotReadyException get_Message()` (160) stay, because step 3 declares them by hand. Without
  step 3 those five lines disappear.
- **Behaviour:** the key text in five exception messages changes as in §3.1. The package is at `0.1.0-preview.1`.

## 7. CI

New project `tests/Ranvier.AotSmoke` (not packed): `PublishAot`, `TrimmerSingleWarn=false`, `TreatWarningsAsErrors=true`,
and `TrimmerRootAssembly` for Ranvier and Ranvier.CSharp. Its `main` exits non-zero when an exception type differs from
§2.3's JIT column. `TreatWarningsAsErrors` reaches ILC: on `master` it turns the 49 warnings into errors and fails the
publish (measured).

```yaml
aot:
  runs-on: ubuntu-latest
  steps: [checkout, setup-dotnet,
          dotnet publish tests/Ranvier.AotSmoke -c Release -r linux-x64 && ./…/AotSmoke]
```

Decision 2 (*untraced*) drops the traced publish this note first proposed.

**Time:** a clean publish including the library build took 25 to 36 s here with a warm NuGet cache; the traced variant
took 33 s. On CI, add the 13.8 MB `runtime.linux-x64.Microsoft.DotNet.ILCompiler` download and job setup. Estimate:
2 to 3 minutes of compute in a parallel job, with no added wall-clock time while it finishes before the `.NET tests`
job. `ubuntu-latest` ships clang, which the NativeAOT linker needs.

No hot-path code changes (§4), so no benchmark is needed.

## 8. Alternatives

- **Properties only (§2.1).** Advertises AOT compatibility while §2.3's failures ship. Rejected: ReactiveUI#4243 shows
  that annotating without checking fails the same way.
- **Format-string fixes without `--reflectionfree`.** Measured: 46 warnings remain, because the generated `ToString`
  keeps printf reachable, and nothing stops a new `%A` from being added.
- **Suppress FSharp.Core's warnings in the smoke project.** Hides §2.3's runtime failures.
- **Rely on FSharp.Core to become trim-safe.** Out of Ranvier's control, and a later FSharp.Core fix would not repair
  generated `ToString` in Ranvier's own assembly.

## 9. Recommendation

**Do now.** §2.3 is a correctness bug for any NativeAOT user, and the fix changes no hot path. Land steps 1 to 5 and the
CI job together, so the job starts green and blocks regressions. The 0-warning measurement in §3 covers steps 1, 2
and 4; steps 3 and 5 were unmeasured when this was written (§11 measures all five).

## 10. Questions for the maintainer

1. Keep the current `ToString` text by hand-writing it (step 3), or accept the type-name default? (*keep* / *drop*)
2. Should the traced build also be AOT-checked in CI, or untraced only? (*both* / *untraced*)
3. Is `string key` acceptable text for the exception messages, dropping the quotes around string keys? (*yes* / *no*)

Decided (`wave-b/decisions.md`): 1 *keep*, 2 *untraced*, 3 *yes*.

## 11. Implementation

Landed on `worktree-wf_c46b5816-3af-1`:

- Steps 1–5 as in §3. Both `.fsproj` files set `RanvierAotClean=true`; `Directory.Build.targets` then passes
  `--reflectionfree` and sets `IsAotCompatible` (net8.0 and later) **only when `RanvierTrace` is not `true`**. The
  condition lives in the targets file because the Debug default for `RanvierTrace` is set there, after the project body.
- `tests/Ranvier.AotSmoke` (in `Ranvier.slnx`, not packed): §7's settings, also compiled `--reflectionfree`. Its `main`
  checks the §2.3 exception types and messages (missing `int` key, missing `string` key through
  `Ranvier.CSharp.Reactive.Projection`, duplicate key, off-thread write, pending read) and the hand-written `ToString`
  text.
- CI job `aot` in `.github/workflows/ci.yml`: publish `-c Release -r linux-x64`, then run the binary. Untraced only.
- Tests: `tests/Ranvier.Tests/Texts.fs` (the `ToString` text and the key text in two messages; the `NotReadyException`
  message test is .NET only, because Fable gives an F# exception an empty message) and three `valueText` cases in
  `Tracing.fs`.
- Docs: `guide/installation.md` gains "Native AOT and trimming", with the claim scoped to primitive and `string` keys;
  `concepts/roadmap.md` moves the item to Shipped.

Measured locally (SDK 10.0.112, ILCompiler 10.0.x from nuget.org):

| Publish | IL2xxx/IL3xxx warnings | Binary | Smoke checks |
| --- | --- | --- | --- |
| Untraced, both assemblies rooted | 0 | 4,204,192 B | 12/12 |
| Traced (`-p:RanvierTrace=true`), rooted, compiled `--reflectionfree` (superseded, see below) | 0 | — | 12/12 |
| Untraced plus one `sprintf "%d"` in the smoke app (gate check) | fails: IL2055, IL2060, IL2067, IL2070, IL2072, IL2075, IL2080, IL3050 as errors | — | — |

A clean publish including the library build took 25 s. An IL scan of the built assemblies (untraced and traced, both
libraries) finds no call to `PrintfFormat`, `PrintfModule` or `PrintFormatToString`.

Deviations from the note:

- **Two more traced sites.** `TraceApi.origin` (`{id}`) and `TraceModel.whyDepth` (`{node}`, `{run}`) used untyped `int`
  holes, which compile to `PrintfFormat`; both now concatenate. The `reconcile` lines keep their `%A` shape by hand
  (`[|1; 2|]`, `[1; 2]`, `set [1; 2]`).
- **The traced build is not reflection-free.** Compiled `--reflectionfree`, the traced build turned the `ToString` of
  its 16 public trace records and unions (`WhyRoot`, `WhyStep`, `Why`, `WhyNotReason`, `TraceRun`, `TraceHistory`,
  `TraceFlightState`, `TraceFlight`, `TraceWaiting`, `TraceNodeStatus`, `TraceSnapshotNode`, `TraceSnapshotOwner`,
  `TraceSnapshot`, `TraceDump`, `TraceEvent`, `TraceOrigin`) into bare type names (`Ranvier.WhyNotReason+Disposed` for
  `Disposed 3`), against decision 1. The flag is therefore untraced-only, and the traced build also drops
  `IsAotCompatible`: a NativeAOT publish of `Ranvier.Traced` warns again. Decision 2 already leaves the traced build
  unchecked. Hand-writing those 16 `ToString` overrides is the alternative; a `FOR-REVIEW` tag in
  `Directory.Build.targets` leaves the choice to the maintainer. C# is unaffected: `Tracing` returns rendered text.
- **Traced `valueText`** keeps `sprintf "%A"` on both targets, so its text is unchanged from `master`. Fable compiles
  `%A` (it does not apply `--reflectionfree`, which settles §5's open question).
- **`Reading.ToString` parentheses.** A `Ready` payload whose text contains a space and opens with neither a bracket
  nor a quote is wrapped in parentheses, as `%A` does: `Ready (Ready 3)`, `Ready (1, a)`. Remaining differences from
  `%A`, beyond §3.1's: `Ready [|1; 2|]` reads `Ready System.Int32[]`, `Ready (1, "a")` reads `Ready (1, a)`,
  `Ready 'c'` reads `Ready c`, and a `Failed` exception prints its `ToString` text.
- **`TraceSite.assemblyOf`** takes `[<UnconditionalSuppressMessage>]` under `#if NET5_0_OR_GREATER`; netstandard2.1
  has no such attribute and is never AOT-published.
- **Culture.** `Reading.ToString` formats a non-string payload with `string`, which uses the invariant culture, as `%A`
  did.
- The traced build is not AOT-checked in CI (decision 2), and after the change above it no longer claims to be
  AOT-compatible.


## Reviewer corrections (applied above; kept for the record)

Verdict: needs fixes

- Severity: substantive. Checked against c631f23; src/ is identical at HEAD 6869724. Printf-site inventory: 'and `%s`/`%d` in `Core.fs:1530` and `Ranvier.CSharp/Bindings.fs:362`' is wrong for Bindings.fs:362. `$"A property named '%s{slot.Name}' is already bound."` has only a %s hole on a string, and F# lowers that to String.Concat (confirmed by compiling the same shape and by scanning the built Ranvier.CSharp.dll: no method except Notifications.ToString calls PrintFormat*). Only Core.fs:1530 reaches PrintfFormat, because of its %d holes. Correct text: 'and `%d` in `Core.fs:1530`; Bindings.fs:362 already compiles to String.Concat'.
- 'The ILC dependency graph ... traces them to three sources in Ranvier' / item 2 'Compiler-generated ToString on the public unions, records and exception' misses the internal types that call sprintf "%+A". IL scan of the Release build: `PositionalChange`1.ToString/__DebugDisplay` (Positional.fs:5), `FlightOutcome`1.ToString/__DebugDisplay` (Platform.fs:87), and in Ranvier.CSharp `Notifications.ToString` (Bindings.fs:82). The last one is the actual printf edge from Ranvier.CSharp. It is not Bindings.fs:362. --reflectionfree removes all of them, but the inventory and the 'only effect' claims should list them.
- '`%A` in `Projections.fs:182, 1015, 1363, 1411, 1417`' is listed as ILC-traced, but Projections.fs:1411 is inside `#if FABLE_COMPILER` (1407-1414) and never compiles on .NET. The .NET sites are 182, 1015, 1363, 1417 (plus Combinators.fs:914).
- 'Replace the nine format-string sites (§2.2 item 1, plus the three traced sites)': §2.2 item 1 lists eight sites, not nine. Of those, one is Fable-only (1411) and one needs no change (Bindings.fs:362). Correct text: 'the seven sites (six on .NET plus the Fable-only 1411), plus the three traced sites'.
- §3.1 row 'user record | one line | the record's own ToString (multi-line %+A)': %A already prints a record over several lines (TraceModel.fs:262 says so, and `sprintf "%A" {A=1;B="x"}` gives '{ A = 1\n  B = "x" }'). `string` gives the identical text. Correct: 'user record | multi-line | same'.
- §3.1 row 1 ('same') is not exhaustive. Other keys change as well: `3.0` → `3`, `1L` → `1`, `'c'` → `c` (measured with dotnet fsi). The same applies to step 3's `string v` for `Reading` payloads, e.g. `Ready 3.0` becomes `Ready 3` and `Ready (Some 1)` becomes `Ready Some(1)`. The claim 'with the text shown in the JIT column' therefore holds only for int and string payloads.
- Missed failure mode: for a user union or record key (and a `Reading` payload), `string key` calls the user type's compiler-generated ToString, which is itself `sprintf "%+A"` in the user's assembly. Under NativeAOT such a key still goes through FSharp.Core's reflective formatter, so §2.3's failure (NotSupportedException instead of KeyNotFoundException) persists unless the app is also compiled reflection-free. The smoke test will not see this because it uses int/string keys. Replace 'Every exception Ranvier throws keeps its type and message under NativeAOT' / 'All four exceptions in §2.3 keep their JIT type and text' with a scoped claim: primitive and string keys only.
- 'All four exceptions in §2.3 keep their JIT type and text' contradicts §3.1, where the `"nope"` key text changes to `nope`. Correct: 'keep their JIT type; key text changes as in §3.1'.
- §6 'Without step 3 those four lines disappear': --reflectionfree also drops the generated `NotReadyException.get_Message` (verified by compiling an F# exception with and without the flag). Baseline line 160 (`NotReadyException get_Message()`) disappears too: five lines, not four. With step 3 all five stay.
- §4 '`--reflectionfree` changes no code outside the members listed in §3' also removes the generated ToString and `__DebugDisplay` (DebuggerDisplay) on internal unions and records (PositionalChange, FlightOutcome, Notifications). There is no hot-path effect, but debugger views change.
- §7 'The smoke project also settles any perf question: BenchmarkDotNet cannot see a difference on the hot path' is unsupported: no benchmark was run, and a smoke test does not measure performance. Correct: 'No hot-path code changes (§4), so no benchmark is needed.'
- §9 recommends landing step 3 while §10 Q1/Q3 leave it and the key text open. The measured 0-warning result is for steps 1, 2 and 4 only, so say that steps 3 and 5 are unmeasured.
