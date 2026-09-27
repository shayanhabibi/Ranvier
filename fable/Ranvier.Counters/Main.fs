/// <summary>
/// The measured Node.js process. It writes the JSON shape of the .NET worker's
/// <c>WorkerRun</c>, so the driver in <c>bench/Ranvier.Counters</c> reads both
/// with one reader.
/// </summary>
/// <remarks>
/// <para>
/// Usage: node --expose-gc --single-threaded Main.fs.js --out &lt;file> [--scale k] [--warmup k] [--handshake]
/// </para>
/// <para>
/// <c>--handshake</c> brackets each measured region for the processor-counter
/// collector: the process writes <c>BEGIN &lt;region></c> or <c>END &lt;region></c> to standard
/// output and blocks until a line arrives on standard input.
/// </para>
/// </remarks>
module FableCounters.Main

open FableCounters.Scenarios

type private Options =
    {
        Out: string
        Scale: int
        Warmup: int
        Handshake: bool
    }

let rec private parse (options: Options) (args: string list) =
    match args with
    | [] -> options
    | "--out" :: path :: rest -> parse { options with Out = path } rest
    | "--scale" :: k :: rest -> parse { options with Scale = int k } rest
    | "--warmup" :: k :: rest -> parse { options with Warmup = int k } rest
    | "--handshake" :: rest -> parse { options with Handshake = true } rest
    | unknown :: _ -> failwith $"Unknown argument '%s{unknown}'."

let private countersCompiled =
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

let private readCounters () =
#if RANVIER_COUNTERS
    Ranvier.Counters.Snapshot ()
    |> Array.map (fun (name, value) -> {| Name = name; Value = float value |})
#else
    [||]
#endif

/// <summary>
/// Marks a region boundary for the collector and waits for its reply.
/// </summary>
let private signal (handshake: bool) (marker: string) (region: int) =
    if handshake then
        Node.writeLine $"%s{marker} %d{region}"
        Node.waitLine ()

/// <summary>
/// Runs <c>ops</c> operations of <c>case</c>, between the markers numbered <c>region</c> when
/// <c>handshake</c> holds.
/// </summary>
/// <remarks>
/// After the reply to <c>BEGIN</c>, a one-millisecond sleep switches the thread out,
/// and the region's first run interval starts after the collector's <c>Begin</c>
/// marker. The sleep and the handshakes are the same work at N and 2N, and
/// cancel in the difference.
/// </remarks>
let private measure (handshake: bool) (case: Case) (ops: int) (region: int) =
    let prepared = case.Prepare ops
    Node.collect ()
    Node.collect ()
    resetCounters ()
    let stopRecording = Node.recordCollections ()
    let heapBefore = Node.heapUsed ()
    let before = Node.objectHeapUsed ()
    signal handshake "BEGIN" region

    if handshake then
        Node.sleep 1

    prepared.Run ()
    signal handshake "END" region
    let after = Node.objectHeapUsed ()
    let heapAfter = Node.heapUsed ()
    let collections = stopRecording ()
    let counters = readCounters ()
    prepared.Teardown ()

    {|
        Ops = ops
        Region = region
        Bytes = after - before
        HeapUsedBytes = heapAfter - heapBefore
        Gen0Collections = collections
        NoGcRegion = collections = 0
        Counters = counters
    |}

[<EntryPoint>]
let main argv =
    let options =
        parse
            {
                Out = ""
                Scale = 1
                Warmup = 10
                Handshake = false
            }
            (List.ofArray argv)

    if not (Node.canCollect ()) then
        failwith "Run node with --expose-gc."

    let results =
        Scenarios.all options.Scale
        |> List.mapi (fun index case ->
            for _ in 1 .. options.Warmup do
                let warm = case.Prepare (2 * case.Ops)
                warm.Run ()
                warm.Teardown ()

            {|
                Scenario = case.Scenario
                Engine = case.Engine
                Unit = case.Unit
                Measurements =
                    [|
                        measure options.Handshake case case.Ops (2 * index)
                        measure options.Handshake case (2 * case.Ops) (2 * index + 1)
                    |]
            |})
        |> List.toArray

    let run =
        {|
            ProcessId = Node.processId ()
            ThreadId = 0
            CountersCompiled = countersCompiled
            Cases = results
        |}

    if options.Out <> "" then
        Node.writeFile (options.Out, Node.toJson run)

    0
