/// <summary>
/// The measured Node.js process. It writes the JSON shape of the .NET worker's
/// <c>WorkerRun</c>, so the driver in <c>bench/Ranvier.Counters</c> reads both
/// with one reader.
/// </summary>
/// <remarks>
/// <para>
/// Usage: node --expose-gc --single-threaded Main.fs.js --out &lt;file> [--scale k] [--warmup k] [--only prefix] [--scenario name] [--handshake]
/// </para>
/// <para>
/// <c>--list</c> writes each scenario name once, one per line in measurement order, and measures nothing.
/// <c>--scenario</c> selects the cases of exactly one scenario.
/// </para>
/// <para>
/// <c>--handshake</c> brackets each measured region for the processor-counter
/// collector: the process writes <c>BEGIN &lt;region></c> or <c>END &lt;region></c> to standard
/// output and blocks until a line arrives on standard input.
/// </para>
/// <para>
/// Each case runs its warm-up, a full collection, then settling runs at 2N under full tiering. Tier-up stays
/// disabled across both regions, so the regions at N and 2N run the same code. A region that compiles any function
/// reports positive <c>CodeBytes</c>.
/// </para>
/// </remarks>
module FableCounters.Main

open Fable.Core
open FableCounters.Scenarios

/// <summary>
/// <c>Appended.appended</c>, loading its module on the first call. Every case of <c>Scenarios.all</c> then runs with
/// the code loaded by a harness without the appended cases.
/// </summary>
[<Emit("process.getBuiltinModule('node:module').createRequire(import.meta.url)('./Appended.fs.js').appended($0)")>]
let private loadAppended (scale: int) : Case list = jsNative

type private Options =
    {
        Out: string
        Scale: int
        Warmup: int
        Only: string
        Scenario: string
        Handshake: bool
        List: bool
    }

let rec private parse (options: Options) (args: string list) =
    match args with
    | [] -> options
    | "--out" :: path :: rest -> parse { options with Out = path } rest
    | "--scale" :: k :: rest -> parse { options with Scale = int k } rest
    | "--warmup" :: k :: rest -> parse { options with Warmup = int k } rest
    | "--only" :: prefix :: rest -> parse { options with Only = prefix } rest
    | "--scenario" :: name :: rest -> parse { options with Scenario = name } rest
    | "--handshake" :: rest -> parse { options with Handshake = true } rest
    | "--list" :: rest -> parse { options with List = true } rest
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
/// Writes a region boundary <c>line</c> for the collector and waits for its reply. Inlined into <c>measure</c>.
/// </summary>
let inline private signal (handshake: bool) (line: string) =
    if handshake then
        Node.writeLine 1 line
        Node.waitLine 0

/// <summary>
/// The most runs <c>settle</c> spends on a case.
/// </summary>
[<Literal>]
let private SettleLimit = 40

/// <summary>
/// The consecutive compilation-free runs that end <c>settle</c>.
/// </summary>
[<Literal>]
let private SettledRuns = 2

/// <summary>
/// Runs <c>case</c> at 2N until <c>SettledRuns</c> consecutive runs leave the code spaces unchanged, or
/// <c>SettleLimit</c> runs.
/// </summary>
let private settle (case: Case) =
    let mutable runs = 0
    let mutable quiet = 0

    while runs < SettleLimit && quiet < SettledRuns do
        let prepared = case.Prepare (2 * case.Ops)
        let before = Node.codeHeapUsed ()
        prepared.Run ()

        quiet <- if Node.codeHeapUsed () > before then 0 else quiet + 1

        prepared.Teardown ()
        runs <- runs + 1

/// <summary>
/// Runs <c>ops</c> operations of <c>case</c>, between the markers numbered <c>region</c> when
/// <c>handshake</c> holds.
/// </summary>
/// <remarks>
/// <para>
/// Young-generation collections empty the new space before the region and keep the installed code.
/// <c>CodeBytes</c> is positive when V8 compiled during the region.
/// </para>
/// <para>
/// After the reply to <c>BEGIN</c>, a one-millisecond sleep switches the thread out,
/// and the region's first run interval starts after the collector's <c>Begin</c>
/// marker. The sleep and the handshakes are the same work at N and 2N, and
/// cancel in the difference.
/// </para>
/// </remarks>
let private measure (handshake: bool) (case: Case) (ops: int) (region: int) =
    let prepared = case.Prepare ops
    Node.collectYoung ()
    Node.collectYoung ()
    resetCounters ()
    let stopRecording = Node.recordCollections ()
    let heapBefore = Node.heapUsed ()
    let before = Node.objectHeapUsed ()
    let opening = $"BEGIN %d{region}"
    let closing = $"END %d{region}"
    let codeBefore = Node.codeHeapUsed ()
    signal handshake opening

    if handshake then
        Node.sleep 1

    prepared.Run ()
    signal handshake closing
    let codeAfter = Node.codeHeapUsed ()
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
        CodeBytes = codeAfter - codeBefore
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
                Only = ""
                Scenario = ""
                Handshake = false
                List = false
            }
            (List.ofArray argv)

    if not (Node.canCollect ()) then
        failwith "Run node with --expose-gc."

    if options.Handshake then
        Node.compileLines ()

    let selected (case: Case) =
        case.Scenario.StartsWith options.Only
        && (options.Scenario = ""
            || case.Scenario = options.Scenario)

    // Measures the selected cases, numbering their regions from 2 * first.
    let measureAll (first: int) (cases: Case list) =
        cases
        |> List.filter selected
        |> List.mapi (fun offset case ->
            let index = first + offset

            for _ in 1 .. options.Warmup do
                let warm = case.Prepare (2 * case.Ops)
                warm.Run ()
                warm.Teardown ()

            Node.collect ()
            Node.collect ()
            settle case
            Node.setTierUp false

            let measurements =
                [|
                    measure options.Handshake case case.Ops (2 * index)
                    measure options.Handshake case (2 * case.Ops) (2 * index + 1)
                |]

            Node.setTierUp true

            {|
                Scenario = case.Scenario
                Engine = case.Engine
                Unit = case.Unit
                Measurements = measurements
            |})
        |> List.toArray

    if options.List then
        Scenarios.all options.Scale
        @ loadAppended options.Scale
        |> List.map _.Scenario
        |> List.distinct
        |> List.iter (Node.writeLine 1)
    else
        let measured = measureAll 0 (Scenarios.all options.Scale)

        let results =
            Array.append measured (measureAll measured.Length (loadAppended options.Scale))

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
