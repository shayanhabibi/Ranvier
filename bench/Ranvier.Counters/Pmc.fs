/// <summary>
/// Processor counter collection through ETW, in the driver process.
/// </summary>
/// <remarks>
/// The kernel session attaches the configured counters to every context-switch
/// event: each <c>CSwitch</c> carries the raw per-processor counter values at the
/// moment of the switch. A thread's count over one run interval is the value at
/// its switch-out minus the value at its switch-in on the same processor. The
/// counts are exact up to the interrupts serviced while the thread runs.
/// </remarks>
module CounterBench.Pmc

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Reflection.Emit
open System.Runtime.InteropServices
open System.Diagnostics.Tracing
open Microsoft.Diagnostics.Tracing
open Microsoft.Diagnostics.Tracing.Parsers
open Microsoft.Diagnostics.Tracing.Parsers.Kernel
open Microsoft.Diagnostics.Tracing.Session

let DefaultSources = [| "InstructionRetired"; "TotalCycles"; "BranchMispredictions" |]

let isElevated () =
    let elevated = TraceEventSession.IsElevated ()
    elevated.HasValue && elevated.Value

/// <summary>
/// Profile sources available on this machine, by name.
/// </summary>
let availableSources () =
    TraceEventProfileSources.GetInfo ()
    |> Seq.map (fun pair -> pair.Key, pair.Value.ID)
    |> Seq.toArray

module private Native =

    [<DllImport("advapi32.dll", CharSet = CharSet.Unicode)>]
    extern int ControlTraceW(uint64 sessionHandle, string sessionName, nativeint properties, uint32 controlCode)

    [<DllImport("advapi32.dll")>]
    extern int TraceSetInformation(uint64 sessionHandle, int informationClass, nativeint information, uint32 length)

    [<Literal>]
    let ControlQuery = 0u

    [<Literal>]
    let TracePmcEventListInfo = 8

    [<Literal>]
    let TracePmcCounterListInfo = 9

    /// <summary>
    /// <c>sizeof(EVENT_TRACE_PROPERTIES)</c>.
    /// </summary>
    [<Literal>]
    let PropertiesSize = 120

    [<Literal>]
    let NameBytes = 2048

    /// <summary>
    /// The <c>Thread_V2</c> event class, whose opcode 36 is <c>CSwitch</c>.
    /// </summary>
    let ThreadClass = Guid "3d6fa8d1-fe05-11d0-9dda-00c04fd7ba7c"

    [<Literal>]
    let CSwitchOpcode = 36uy

    let check (operation: string) (status: int) =
        if status <> 0 then
            let message = Marshal.GetPInvokeErrorMessage status
            failwith $"%s{operation} failed with %d{status}: %s{message}"

    /// <summary>
    /// The handle of the running session named <c>name</c>.
    /// </summary>
    let sessionHandle (name: string) =
        let size = PropertiesSize + 2 * NameBytes
        let buffer = Marshal.AllocHGlobal size

        try
            for offset in 0 .. size - 1 do
                Marshal.WriteByte (buffer, offset, 0uy)

            Marshal.WriteInt32 (buffer, 0, size)
            Marshal.WriteInt32 (buffer, 112, PropertiesSize + NameBytes)
            Marshal.WriteInt32 (buffer, 116, PropertiesSize)
            check "ControlTrace(QUERY)" (ControlTraceW (0UL, name, buffer, ControlQuery))
            uint64 (Marshal.ReadInt64 (buffer, 8))
        finally
            Marshal.FreeHGlobal buffer

    /// <summary>
    /// Attaches the profile sources <c>ids</c> to every <c>CSwitch</c> event of the
    /// session named <c>name</c>.
    /// </summary>
    let attachCounters (name: string) (ids: int[]) =
        let handle = sessionHandle name
        let counters = Marshal.AllocHGlobal (4 * ids.Length)
        let events = Marshal.AllocHGlobal 24

        try
            ids |> Array.iteri (fun i id -> Marshal.WriteInt32 (counters, 4 * i, id))

            check
                "TraceSetInformation(TracePmcCounterListInfo)"
                (TraceSetInformation (handle, TracePmcCounterListInfo, counters, uint32 (4 * ids.Length)))

            for offset in 0..23 do
                Marshal.WriteByte (events, offset, 0uy)

            Marshal.Copy (ThreadClass.ToByteArray (), 0, events, 16)
            Marshal.WriteByte (events, 16, CSwitchOpcode)

            check "TraceSetInformation(TracePmcEventListInfo)" (TraceSetInformation (handle, TracePmcEventListInfo, events, 24u))
        finally
            Marshal.FreeHGlobal counters
            Marshal.FreeHGlobal events

/// <summary>
/// The two running sessions: the kernel session with its counters, and the
/// session recording the worker's markers. Both write to files in <c>Directory</c>.
/// </summary>
type Collector =
    {
        Kernel: TraceEventSession
        Markers: TraceEventSession
        KernelFile: string
        MarkerFile: string
        Sources: string[]
    }

    member this.Stop() =
        this.Markers.Dispose ()
        this.Kernel.Dispose ()

let private markerSessionName = "Ranvier-Counters-Markers"

/// <summary>
/// Starts both sessions. Stops any earlier session under either name, which
/// includes a kernel session another tool holds. On failure, both sessions
/// are stopped before the exception propagates.
/// </summary>
let start (directory: string) (sources: string[]) =
    let available = dict (availableSources ())

    let ids =
        sources
        |> Array.map (fun name ->
            match available.TryGetValue name with
            | true, id -> id
            | _ ->
                let known = String.Join (", ", available.Keys)
                failwith $"Profile source '%s{name}' is not available here. Available: %s{known}")

    let kernelFile = Path.Combine (directory, "kernel.etl")
    let markerFile = Path.Combine (directory, "markers.etl")
    let kernel = new TraceEventSession (KernelTraceEventParser.KernelSessionName, kernelFile)

    try
        kernel.BufferSizeMB <- 512

        kernel.EnableKernelProvider (
            KernelTraceEventParser.Keywords.ContextSwitch
            ||| KernelTraceEventParser.Keywords.Process
            ||| KernelTraceEventParser.Keywords.Thread
        )
        |> ignore

        Native.attachCounters KernelTraceEventParser.KernelSessionName ids

        let markers = new TraceEventSession (markerSessionName, markerFile)

        try
            markers.EnableProvider (EventSource.GetGuid typeof<Markers>) |> ignore
        with _ ->
            markers.Dispose ()
            reraise ()

        {
            Kernel = kernel
            Markers = markers
            KernelFile = kernelFile
            MarkerFile = markerFile
            Sources = sources
        }
    with _ ->
        kernel.Dispose ()
        reraise ()

/// <summary>
/// Reads <c>TraceEvent.eventRecord</c>, the native <c>EVENT_RECORD*</c> behind the
/// dispatched event, which holds the PMC extended data item.
/// </summary>
let private recordOf: Func<TraceEvent, nativeint> =
    let field =
        typeof<TraceEvent>.GetField ("eventRecord", BindingFlags.Instance ||| BindingFlags.NonPublic)

    let getter =
        DynamicMethod ("eventRecordOf", typeof<nativeint>, [| typeof<TraceEvent> |], typeof<TraceEvent>.Module, true)

    let il = getter.GetILGenerator ()
    il.Emit OpCodes.Ldarg_0
    il.Emit (OpCodes.Ldfld, field)
    il.Emit OpCodes.Conv_I
    il.Emit OpCodes.Ret
    getter.CreateDelegate typeof<Func<TraceEvent, nativeint>> :?> Func<TraceEvent, nativeint>

/// <summary>
/// The raw QPC timestamp of <c>data</c>: <c>EVENT_RECORD.EventHeader.TimeStamp</c>.
/// Values from different files share one timeline.
/// </summary>
let private qpcOf (data: TraceEvent) = Marshal.ReadInt64 (recordOf.Invoke data, 16)

/// <summary>
/// <c>EVENT_HEADER_EXT_TYPE_PMC_COUNTERS</c>.
/// </summary>
[<Literal>]
let private PmcCountersExtType = 8us

/// <summary>
/// The counter values carried by <c>data</c>, in the configured order.
/// </summary>
let private countersOf (data: TraceEvent) : uint64[] voption =
    let record = recordOf.Invoke data
    // EVENT_RECORD: ExtendedDataCount at 84, ExtendedData at 88.
    let count = int (uint16 (Marshal.ReadInt16 (record, 84)))
    let items = Marshal.ReadIntPtr (record, 88)
    let mutable result = ValueNone
    let mutable i = 0

    while result.IsNone && i < count do
        // EVENT_HEADER_EXTENDED_DATA_ITEM: ExtType at 2, DataSize at 6,
        // DataPtr at 8; 16 bytes each.
        let item = items + nativeint (16 * i)

        if uint16 (Marshal.ReadInt16 (item, 2)) = PmcCountersExtType then
            let size = int (uint16 (Marshal.ReadInt16 (item, 6)))
            let data = Marshal.ReadIntPtr (item, 8)
            result <- ValueSome (Array.init (size / 8) (fun j -> uint64 (Marshal.ReadInt64 (data, 8 * j))))

        i <- i + 1

    result

/// <summary>
/// The span a hardware counter wraps at: AMD and Intel counters are 48 bits.
/// </summary>
let private counterSpan = 1UL <<< 48

let private delta (later: uint64) (earlier: uint64) =
    if later >= earlier then
        later - earlier
    else
        later + counterSpan - earlier

type RegionCounts =
    {
        Region: int
        /// <summary>
        /// Summed counter values, in the configured source order.
        /// </summary>
        Values: uint64[]
        /// <summary>
        /// Run intervals attributed to the region.
        /// </summary>
        Intervals: int
    }

/// <summary>
/// Marker timestamps in raw QPC ticks, on the timeline of <c>Interval.Start</c>.
/// </summary>
type private Bracket =
    {
        mutable Begin: int64
        mutable End: int64
    }

/// <summary>
/// Fails when the ETW session that wrote <c>source</c> dropped any event.
/// </summary>
let private requireNoLoss (source: ETWTraceEventSource) (file: string) =
    if source.EventsLost > 0 then
        failwith
            $"%s{file} lost %d{source.EventsLost} event(s); the counter totals would be incomplete. Rerun on a quieter machine."

/// <summary>
/// The region brackets marked by <c>markerProcess</c>, and the thread that marked
/// them.
/// </summary>
let private readBrackets (collector: Collector) (markerProcess: int) =
    let brackets = Dictionary<int, Bracket> ()
    let mutable threadId = -1
    let markerProvider = EventSource.GetGuid typeof<Markers>
    use source = new ETWTraceEventSource (collector.MarkerFile)

    source.add_AllEvents (
        Action<TraceEvent> (fun data ->
            let id = int data.ID

            if
                data.ProviderGuid = markerProvider
                && data.ProcessID = markerProcess
                && (id = MarkerIds.Begin || id = MarkerIds.End)
            then
                let region = BitConverter.ToInt32 (data.EventData (), 0)

                let bracket =
                    match brackets.TryGetValue region with
                    | true, b -> b
                    | _ ->
                        let b = { Begin = Int64.MaxValue; End = Int64.MinValue }
                        brackets[region] <- b
                        b

                threadId <- data.ThreadID

                if id = MarkerIds.Begin then
                    bracket.Begin <- qpcOf data
                else
                    bracket.End <- qpcOf data)
    )

    source.Process () |> ignore
    requireNoLoss source collector.MarkerFile

    if threadId < 0 then
        failwith "No markers reached the marker session."

    brackets, threadId

[<Struct>]
type private Interval =
    {
        Thread: int
        /// <summary>
        /// Raw QPC ticks at the switch-in.
        /// </summary>
        Start: int64
        Values: uint64[]
    }

/// <summary>
/// The run intervals of every thread in <c>tracked</c>. With <c>processId</c>, every
/// thread that process starts joins <c>tracked</c>; the result's second item is the
/// first of them, or -1.
/// </summary>
let private readIntervals (collector: Collector) (tracked: HashSet<int>) (processId: int voption) =
    let intervals = ResizeArray<Interval> ()
    let switchedIn = Dictionary<int, Interval> ()
    let mutable mainThread = -1
    use source = new ETWTraceEventSource (collector.KernelFile)

    source.Kernel.add_ThreadStart (
        Action<ThreadTraceData> (fun data ->
            if processId = ValueSome data.ProcessID then
                if mainThread < 0 then
                    mainThread <- data.ThreadID

                tracked.Add data.ThreadID |> ignore)
    )

    source.Kernel.add_ThreadCSwitch (
        Action<CSwitchTraceData> (fun data ->
            let oldTracked = tracked.Contains data.OldThreadID
            let newTracked = tracked.Contains data.NewThreadID

            if oldTracked || newTracked then
                let cpu = data.ProcessorNumber

                match countersOf data with
                | ValueNone -> switchedIn.Remove cpu |> ignore
                | ValueSome values ->
                    if oldTracked then
                        match switchedIn.TryGetValue cpu with
                        | true, start when start.Thread = data.OldThreadID ->
                            intervals.Add
                                { start with
                                    Values = Array.map2 delta values start.Values
                                }

                            switchedIn.Remove cpu |> ignore
                        | _ -> ()

                    if newTracked then
                        switchedIn[cpu] <-
                            {
                                Thread = data.NewThreadID
                                Start = qpcOf data
                                Values = values
                            })
    )

    source.Process () |> ignore
    requireNoLoss source collector.KernelFile
    intervals, mainThread

/// <summary>
/// Counter totals per region over the intervals of threads accepted by
/// <c>includes</c>. An interval belongs to a region when it starts between the
/// region's <c>Begin</c> and <c>End</c> markers.
/// </summary>
let private totals
    (sources: int)
    (brackets: Dictionary<int, Bracket>)
    (intervals: ResizeArray<Interval>)
    (includes: int -> bool)
    : RegionCounts[] =
    brackets
    |> Seq.sortBy (fun pair -> pair.Key)
    |> Seq.map (fun pair ->
        let bracket = pair.Value
        let sums = Array.zeroCreate<uint64> sources
        let mutable count = 0

        for interval in intervals do
            if includes interval.Thread && interval.Start > bracket.Begin && interval.Start < bracket.End then
                count <- count + 1

                for k in 0 .. min sums.Length interval.Values.Length - 1 do
                    sums[k] <- sums[k] + interval.Values[k]

        {
            Region = pair.Key
            Values = sums
            Intervals = count
        })
    |> Seq.toArray

/// <summary>
/// Fails when a region of <c>counts</c> holds no run interval of the measured
/// thread.
/// </summary>
let private requireIntervals (counts: RegionCounts[]) =
    let empty = counts |> Array.filter (fun c -> c.Intervals = 0)

    if empty.Length > 0 then
        let regions = String.Join (", ", empty |> Array.map (fun c -> c.Region))
        failwith $"Region(s) %s{regions} hold no run interval of the measured thread."

    counts

/// <summary>
/// Counter totals per region for the thread of <c>processId</c> that wrote the
/// markers, from the files written by the stopped <c>collector</c>. Every region
/// holds at least one run interval.
/// </summary>
let analyse (collector: Collector) (processId: int) : RegionCounts[] =
    let brackets, thread = readBrackets collector processId
    let intervals, _ = readIntervals collector (HashSet [ thread ]) ValueNone
    totals collector.Sources.Length brackets intervals ((=) thread) |> requireIntervals

/// <summary>
/// Counter totals per region for a process whose regions were marked by
/// <c>markerProcess</c>: first for the process's main thread, then for all its
/// threads. The process must start after <c>collector</c> started. Every region
/// holds at least one run interval of the main thread.
/// </summary>
let analyseProcess (collector: Collector) (markerProcess: int) (processId: int) =
    let brackets, _ = readBrackets collector markerProcess
    let intervals, mainThread = readIntervals collector (HashSet ()) (ValueSome processId)

    if mainThread < 0 then
        failwith $"No thread of process %d{processId} started during the kernel session."

    let sources = collector.Sources.Length
    let main = totals sources brackets intervals ((=) mainThread) |> requireIntervals
    main, totals sources brackets intervals (fun _ -> true)
