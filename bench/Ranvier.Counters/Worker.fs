/// <summary>
/// The measured process. Every method runs fully optimised from its first
/// call. The driver reads back the JSON it writes.
/// </summary>
module CounterBench.Worker

open System
open System.IO
open System.Runtime
open System.Runtime.InteropServices
open System.Text.Json
open System.Threading
open CounterBench.Scenarios

[<DllImport("kernel32.dll")>]
extern uint32 GetCurrentThreadId()

let countersCompiled =
#if RANVIER_COUNTERS
    true
#else
    false
#endif

let private resetCounters () =
#if RANVIER_COUNTERS
    Ranvier.Counters.Reset ()
#else
    ()
#endif

let private readCounters () : CounterValue[] =
#if RANVIER_COUNTERS
    Ranvier.Counters.Snapshot ()
    |> Array.map (fun (name, value) -> { Name = name; Value = value })
#else
    [||]
#endif

/// <summary>
/// Budgets tried in turn: the largest the runtime accepts is used.
/// </summary>
let private noGcBudgets = [ 240L <<< 20; 120L <<< 20; 60L <<< 20; 30L <<< 20 ]

let private enterNoGcRegion () =
    noGcBudgets
    |> List.exists (fun budget ->
        try
            GC.TryStartNoGCRegion budget
        with :? ArgumentOutOfRangeException ->
            false)

let private exitNoGcRegion () =
    if GCSettings.LatencyMode = GCLatencyMode.NoGCRegion then
        GC.EndNoGCRegion ()

/// <summary>
/// Runs <c>ops</c> operations of <c>case</c> between the markers numbered <c>region</c>.
/// </summary>
/// <remarks>
/// Each marker is followed by a one-millisecond sleep, which switches the
/// thread out. The counted intervals run from the wake after <c>Begin</c> to the
/// sleep after <c>End</c>. The sleeps and markers cancel in the N and 2N difference.
/// </remarks>
let measure (case: Case) (ops: int) (region: int) =
    let prepared = case.Prepare ops
    GC.Collect ()
    GC.WaitForPendingFinalizers ()
    GC.Collect ()
    let noGc = enterNoGcRegion ()
    let collections = GC.CollectionCount 0
    resetCounters ()

    Markers.Log.Begin region
    Thread.Sleep 1
    let before = GC.GetAllocatedBytesForCurrentThread ()
    prepared.Run ()
    let after = GC.GetAllocatedBytesForCurrentThread ()
    Markers.Log.End region
    Thread.Sleep 1

    let collections = GC.CollectionCount 0 - collections
    let counters = readCounters ()
    exitNoGcRegion ()
    prepared.Teardown ()

    {
        Ops = ops
        Region = region
        Bytes = after - before
        Gen0Collections = collections
        NoGcRegion = noGc && collections = 0
        Counters = counters
    }

/// <summary>
/// Measures every case at N and 2N after one unmeasured run at N, which
/// compiles every method and initialises every static the region reaches.
/// </summary>
let run (scale: int) (output: string) =
    let cases = Scenarios.all scale

    let results =
        cases
        |> List.mapi (fun index case ->
            let warm = case.Prepare case.Ops
            warm.Run ()
            warm.Teardown ()

            {
                Scenario = case.Scenario
                Engine = case.Engine
                Unit = case.Unit
                Measurements =
                    [|
                        measure case case.Ops (2 * index)
                        measure case (2 * case.Ops) (2 * index + 1)
                    |]
            })
        |> List.toArray

    let run =
        {
            ProcessId = Environment.ProcessId
            ThreadId = int (GetCurrentThreadId ())
            CountersCompiled = countersCompiled
            Cases = results
        }

    File.WriteAllText (output, JsonSerializer.Serialize run)
