# Runtime-preallocated exception experiment

The native-symbol route works on Windows x64 CoreCLR .NET 10.0.12. This experiment retrieves the runtime's actual preallocated OutOfMemoryException, verifies its identity, and throws it repeatedly without captured frames or managed allocations.

This is an isolated experiment, not a Ranvier implementation change. The cheaper agents explored acquisition routes; the primary agent implemented the native probe and revised harness.

## Reproduce

Run from the repository root with the .NET 10 SDK:

```powershell
rtk proxy dotnet run --project experiments/preallocated-exception-bench -c Release -- --verify
rtk proxy dotnet run --project experiments/preallocated-exception-bench -c Release -- --out experiments/preallocated-exception-bench/results-native.json
rtk proxy pwsh -NoProfile -File experiments/preallocated-exception-bench/verify-results.ps1
```

The first run downloads the matching coreclr.pdb from Microsoft's symbol server into the experiment's ignored bin directory. No native tool installation or third-party package is required. --probe-native is an alias for --verify. All benchmark samples run in separate dotnet child processes. Verification itself runs in the experiment's dotnet process.

## How acquisition works

NativePreallocated.cs locates the loaded coreclr.dll and reads its PE CodeView GUID and age. It downloads that exact PDB and uses Windows DbgHelp with exact-symbol matching to resolve coreclr!g_pPreallocatedOutOfMemoryException.

The symbol points to storage containing an OBJECTHANDLE. Reading that pointer obtains the GC handle cell; GCHandle.FromIntPtr(cell).Target obtains the managed object through CoreCLR's handle machinery. The code never frees that borrowed handle or changes its target. It does not change runtime globals, patch the runtime, exhaust memory, or cause a real stack overflow.

Verification requires all of the following:

- The object is an OutOfMemoryException and the runtime's private IsImmutableAgileException identity predicate returns true.
- Repeated throws through both tested depths leave zero frames in new StackTrace(exception).
- A forced compacting collection preserves the object, and acquiring it again returns the same reference.
- Ordinary exceptions, the StackTrace override, and both HRESULT-created candidates record 2 or 18 frames after throwing. The override's rendered trace remains empty.

The verified PDB identity for the recorded run is 07FB51ECFD5748BE9DB932401E0670CD1.

## Recorded results

[results-native.json](results-native.json) retains verification evidence, every sample, medians, minima, maxima, and environment settings. Seven fresh processes per scenario and depth each perform 20,000 warmup calls and 200,000 measured operations.

Compared with a cached, ordinary OutOfMemoryException of the same type:

- Shallow path (2 recorded frames in the ordinary control): ordinary 1,069.61 ns/op and 176 B/op; runtime-preallocated 864.54 ns/op and 0 B/op. About 19.2% less elapsed time.
- Deeper path (18 recorded frames in the ordinary control): ordinary 3,609.90 ns/op and 2,256 B/op; runtime-preallocated 2,944.61 ns/op and 0 B/op. About 18.4% less elapsed time.

Across the seven samples, shallow ordinary throws ranged from 1,060.41 to 1,076.06 ns/op and native throws from 851.78 to 869.80 ns/op. Deeper ordinary throws ranged from 3,579.24 to 3,708.72 ns/op and native throws from 2,914.97 to 3,106.89 ns/op.

The cached custom exception and its StackTrace override still allocate identically: 176 B/op shallow and 2,256 B/op deeper. The nonthrowing loop is approximately 1.3 ns/op with zero allocations; it is a lower bound, not a depth-matched substitute or a baseline subtracted from throw timings.

The driver explicitly disables tiered compilation, tiered PGO, ReadyToRun, and server GC in its children. Both OOM cases use the same delegate, non-inlined recursive throw helper, reference-identity catch filter, and sink update. Timestamp APIs avoid allocating a Stopwatch. Symbol lookup, identity checks, and stack inspection occur outside measurement. Scenario order is shuffled reproducibly in each round. No debugger or CPU affinity is configured by the harness.

This measures the whole runtime-preallocated exception path, not stack capture in isolation: CoreCLR also reuses the special global handle rather than creating an ordinary exception handle. The comparison cannot attribute all elapsed-time savings exclusively to trace capture. Stack traversal for handler search and unwinding still happens.

## Limits

The symbol name, foreign-handle representation, and reflected predicate are runtime implementation details. This experiment has been verified only on Windows x64 .NET 10.0.12. It fails rather than substitutes an ordinary exception if acquisition or verification fails. DbgHelp calls must remain serialized in an isolated process.

The acquired object is still the runtime's shared emergency OutOfMemoryException. A real runtime failure can use the very same instance, so reference identity cannot distinguish a deliberate pending-state signal from that failure. This does not create a distinct capture-free NotReadyException suitable for Ranvier.

The original [results.json](results.json) is preserved as historical output from the incomplete harness. Its zero-valued HRESULT rows were skipped, unthrown candidates; they were not capture-free measurements. Its tiering and timing setup also differs, so compare scenarios within the new report, not absolute timings across the two reports. The new verification explicitly throws both HRESULT candidates before inspecting their traces.

## Sources

- [CoreCLR identity checks and preallocated handles](https://github.com/dotnet/runtime/blob/v10.0.12/src/coreclr/vm/clrex.cpp).
- [Native implementation of IsImmutableAgileException](https://github.com/dotnet/runtime/blob/v10.0.12/src/coreclr/vm/comutilnative.cpp).
- [Exception stack capture and the preallocated-handle exemption](https://github.com/dotnet/runtime/blob/v10.0.12/src/coreclr/vm/excep.cpp).
- [Selection of the preallocated handle when throwing](https://github.com/dotnet/runtime/blob/v10.0.12/src/coreclr/vm/threads.cpp).
- [GCHandle representation and Target](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/GCHandle.cs).
- [DbgHelp SymFromName](https://learn.microsoft.com/en-us/windows/win32/api/dbghelp/nf-dbghelp-symfromname).
