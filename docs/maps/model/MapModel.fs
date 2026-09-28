namespace Ranvier.Docs.Maps

#if RANVIER_TRACE
open Ranvier

/// <summary>The animation a frame plays on the map.</summary>
type Cue =
    /// <summary>Applied without animation.</summary>
    | Quiet
    /// <summary>A signal was written.</summary>
    | Flash of int
    /// <summary>A write reached an observer along its edge.</summary>
    | Pulse of source: int * target: int
    /// <summary>A run moved its node's value, and the change travels to every observer.</summary>
    | Surge of source: int * targets: int list
    | Ring of int
    | Rest of int
    | Flight of int
    | Drop of int
    | Settled of int
    | Failed of int
    | Waits of node: int * source: int
    /// <summary>A replayed input's write, logged before the events it causes.</summary>
    | Said

/// <summary>The map's state between frames.</summary>
type Scene =
    {
        Snapshot: TraceSnapshot
        /// <summary>The newest open flight number of each async node in flight.</summary>
        Flights: Map<int, int>
        /// <summary>The pending source each suspended reader waits on.</summary>
        Waiting: Map<int, int>
        /// <summary>The error text of each node whose last flight failed.</summary>
        Errors: Map<int, string>
    }

/// <summary>One event as the map plays it.</summary>
[<NoComparison; NoEquality>]
type Frame =
    {
        Event: TraceEvent
        Cue: Cue
        /// <summary>The event as one line of the map's log.</summary>
        Log: string
        /// <summary>The scene once the event applies.</summary>
        After: Scene
    }

[<RequireQualifiedAccess>]
module MapModel =

    let start: Scene =
        {
            Snapshot = TraceModel.emptySnapshot
            Flights = Map.empty
            Waiting = Map.empty
            Errors = Map.empty
        }

    /// <summary>The node's label, else its kind in lower case.</summary>
    let name (snapshot: TraceSnapshot) (node: int) : string =
        match snapshot.Nodes.TryFind node with
        | Some { Label = Some label } -> label
        | Some n ->
            // Fable prints an enum as its number.
            match n.Kind with
            | TraceNodeKind.Signal -> "signal"
            | TraceNodeKind.AsyncSource -> "async source"
            | TraceNodeKind.Memo -> "memo"
            | TraceNodeKind.Effect -> "effect"
            | TraceNodeKind.AsyncMemo -> "async memo"
            | TraceNodeKind.Boundary -> "boundary"
            | TraceNodeKind.Projection -> "projection"
            | _ -> "#" + string node
        | None -> "#" + string node

    /// <summary>True for the node kinds a map draws; projection internals stay hidden.</summary>
    let visible (node: TraceSnapshotNode) : bool =
        match node.Kind with
        | TraceNodeKind.ProjectionBeacon
        | TraceNodeKind.RowWatch
        | TraceNodeKind.LookupCell -> false
        | _ -> true

    /// <summary>The (source, observer) pairs between visible nodes, in observer then slot order.</summary>
    let edges (snapshot: TraceSnapshot) : (int * int) list =
        let shown id =
            snapshot.Nodes.TryFind id |> Option.exists visible

        [
            for KeyValue (observer, sources) in snapshot.Sources do
                if shown observer then
                    for source in List.distinct sources do
                        if shown source then
                            source, observer
        ]

    let private dropReason (flag: int) =
        match enum<TraceDropReason> flag with
        | TraceDropReason.Superseded -> "superseded"
        | TraceDropReason.Disposed -> "disposed"
        | TraceDropReason.Suspended -> "suspended"
        | _ -> "dropped"

    let private payload (e: TraceEvent) =
        if isNull e.Payload then
            ""
        else
            TraceModel.valueText e.Payload

    let private cueOf (scene: Scene) (e: TraceEvent) =
        let observers () =
            scene.Snapshot.Observers.TryFind e.Node
            |> Option.map Set.toList
            |> Option.defaultValue []

        match e.Kind with
        | TraceEventKind.Write -> Flash e.Node
        | TraceEventKind.Mark -> Pulse (e.Other, e.Node)
        | TraceEventKind.RunStart -> Ring e.Node
        | TraceEventKind.RunEnd -> Rest e.Node
        | TraceEventKind.Moved -> Surge (e.Node, observers ())
        | TraceEventKind.FlightStart -> Flight e.Node
        | TraceEventKind.FlightDrop -> Drop e.Node
        | TraceEventKind.Settle -> Settled e.Node
        | TraceEventKind.Fail -> Failed e.Node
        | TraceEventKind.Suspend -> Waits (e.Node, e.Other)
        | _ -> Quiet

    let private logOf (scene: Scene) (e: TraceEvent) =
        let name = name scene.Snapshot
        let node = name e.Node

        match e.Kind with
        | TraceEventKind.Write when isNull e.Payload -> $"write %s{node}"
        | TraceEventKind.Write -> $"write %s{node} = %s{payload e}"
        | TraceEventKind.Mark -> $"mark %s{node} from %s{name e.Other}"
        | TraceEventKind.RunStart -> $"run %s{node}"
        | TraceEventKind.RunEnd -> $"end %s{node} (%s{(string (enum<RunStatus> e.Arg)).ToLowerInvariant()})"
        | TraceEventKind.Moved when isNull e.Payload -> $"%s{node} moved"
        | TraceEventKind.Moved -> $"%s{node} = %s{payload e}"
        | TraceEventKind.FlightStart -> $"flight %s{node}"
        | TraceEventKind.FlightDrop -> $"drop %s{node} (%s{dropReason e.Flag})"
        | TraceEventKind.Settle -> $"settle %s{node} = %s{payload e}"
        | TraceEventKind.Fail -> $"fail %s{node}: %s{payload e}"
        | TraceEventKind.Suspend -> $"%s{node} waits on %s{name e.Other}"
        | TraceEventKind.NodeNew -> $"new %s{node}"
        | kind when e.Node = 0 -> (string kind).ToLowerInvariant()
        | kind -> $"%s{(string kind).ToLowerInvariant()} %s{node}"

    let private settle (scene: Scene) (e: TraceEvent) =
        let flights =
            match scene.Flights.TryFind e.Node with
            | Some newest when newest = e.Arg || e.Arg = 0 -> scene.Flights.Remove e.Node
            | _ -> scene.Flights

        { scene with Flights = flights }

    let private advance (scene: Scene) (e: TraceEvent) =
        match e.Kind with
        | TraceEventKind.FlightStart ->
            { scene with
                Flights = scene.Flights.Add (e.Node, e.Arg)
            }
        | TraceEventKind.FlightDrop -> settle scene e
        | TraceEventKind.Settle ->
            { settle scene e with
                Errors = scene.Errors.Remove e.Node
            }
        | TraceEventKind.Fail ->
            { settle scene e with
                Errors = scene.Errors.Add (e.Node, payload e)
            }
        | TraceEventKind.Suspend ->
            { scene with
                Waiting = scene.Waiting.Add (e.Node, e.Other)
            }
        | TraceEventKind.RunStart ->
            { scene with
                Waiting = scene.Waiting.Remove e.Node
            }
        | TraceEventKind.RunEnd when enum<RunStatus> e.Arg = RunStatus.Error ->
            let error =
                scene.Snapshot.Nodes.TryFind e.Node
                |> Option.bind _.Value
                |> Option.defaultValue "failed"

            { scene with
                Errors = scene.Errors.Add (e.Node, error)
            }
        | TraceEventKind.RunEnd when enum<RunStatus> e.Arg <> RunStatus.Abandoned ->
            { scene with
                Errors = scene.Errors.Remove e.Node
            }
        | _ -> scene

    /// <summary>
    /// True for a <c>Moved</c> whose run ends <c>Pending</c>: its value is a placeholder, and readers keep the value
    /// from before the run.
    /// </summary>
    let private placeholder (events: TraceEvent[]) (i: int) =
        let e = events[i]

        e.Kind = TraceEventKind.Moved
        && (events[i + 1 ..]
            |> Array.tryFind (fun r -> r.Kind = TraceEventKind.RunEnd && r.Node = e.Node)
            |> Option.exists (fun r -> enum<RunStatus> r.Arg = RunStatus.Pending))

    /// <summary>One frame per event, played on from <c>scene</c>.</summary>
    /// <remarks>
    /// A <c>NodeNew</c> or <c>OwnerNew</c> folds together with the <c>Trace.named</c> label that follows it, so the node takes its
    /// name from its first frame.
    /// </remarks>
    let frames (scene: Scene) (events: TraceEvent[]) : Frame[] =
        let mutable scene = scene

        [|
            for i in 0 .. events.Length - 1 do
                let e = events[i]

                let folded =
                    if
                        (e.Kind = TraceEventKind.NodeNew
                         || e.Kind = TraceEventKind.OwnerNew)
                        && i + 1 < events.Length
                        && events[i + 1].Kind = TraceEventKind.Label
                        && events[i + 1].Arg = 0
                        && events[i + 1].Node = e.Node
                    then
                        events[i .. i + 1]
                    else
                        [| e |]

                let before = scene
                let held = placeholder events i

                let snapshot =
                    if held then
                        before.Snapshot
                    else
                        TraceModel.fold before.Snapshot folded

                // The log names nodes as they stand after a creation or a label, and before a disposal.
                let named =
                    match e.Kind with
                    | TraceEventKind.NodeNew
                    | TraceEventKind.Label -> { before with Snapshot = snapshot }
                    | _ -> before

                scene <- advance { before with Snapshot = snapshot } e

                {
                    Event = e
                    Cue = if held then Quiet else cueOf scene e
                    Log =
                        if held then
                            $"%s{name named.Snapshot e.Node} holds its value"
                        else
                            logOf named e
                    After = scene
                }
        |]

    /// <summary>True while the node has a flight in progress or waits on a pending source.</summary>
    let pending (scene: Scene) (node: int) : bool =
        scene.Flights.ContainsKey node
        || scene.Waiting.ContainsKey node

    /// <summary>The scene after the frame at <c>index</c>; <c>scene</c> itself for an index before the first.</summary>
    let stateAt (scene: Scene) (frames: Frame[]) (index: int) : Scene =
        if index < 0 || frames.Length = 0 then
            scene
        else
            frames[min index (frames.Length - 1)].After

    /// <summary>
    /// The timeline position, from 0 to 1, of frame <c>index</c> of <c>count</c>; <c>None</c> for one of the first
    /// <c>setup</c> frames.
    /// </summary>
    let tickAt (setup: int) (count: int) (index: int) : float option =
        if index < setup then
            None
        else
            Some (float (index + 1 - setup) / float (count - setup))

    /// <summary>
    /// <c>index</c> held between the last setup frame and the last of <c>count</c> frames; -1 is the scene before
    /// the first frame.
    /// </summary>
    let clampCursor (setup: int) (count: int) (index: int) : int =
        max (setup - 1) (min index (count - 1))

    /// <summary>A frame that logs <c>text</c> and leaves <c>scene</c> as it is.</summary>
    let said (scene: Scene) (text: string) : Frame =
        {
            Event = Unchecked.defaultof<TraceEvent>
            Cue = Said
            Log = text
            After = scene
        }
#endif
