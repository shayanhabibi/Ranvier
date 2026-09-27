namespace CounterBench

open System.Diagnostics.Tracing

module MarkerIds =

    [<Literal>]
    let Begin = 1

    [<Literal>]
    let End = 2

/// <summary>
/// ETW markers bracketing each measured region. A region's processor counts
/// cover every run interval of the marking thread that starts between <c>Begin</c>
/// and <c>End</c>.
/// </summary>
[<Sealed; EventSource(Name = "Ranvier-Counters")>]
type Markers private () =
    inherit EventSource ()

    static member val Log = new Markers ()

    [<Event(MarkerIds.Begin)>]
    member this.Begin(region: int) = this.WriteEvent (MarkerIds.Begin, region)

    [<Event(MarkerIds.End)>]
    member this.End(region: int) = this.WriteEvent (MarkerIds.End, region)
