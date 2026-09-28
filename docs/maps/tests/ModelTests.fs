module Ranvier.Docs.Maps.Tests.ModelTests

open Expecto

#if RANVIER_TRACE
open Ranvier
open Ranvier.Docs.Maps

type private Line =
    {
        Sku: string
        Price: decimal
        Qty: int
    }

/// The docs' cart: a quoted shipping cost between the subtotal and the total.
type private Cart =
    {
        Graph: Graph
        Desk: Desk<decimal>
        Lines: Signal<Line list>
        Subtotal: int
        Shipping: int
        Total: int
    }

let private cart () =
    let g = new Graph ()
    use _ = g.Activate ()
    let desk = Desk<decimal>()
    let lines = createSignal [ { Sku = "tea"; Price = 4m; Qty = 1 } ]
    Trace.label (g, lines, "lines")

    let subtotal =
        createMemo (fun _ ->
            lines.Value
            |> List.sumBy (fun l -> l.Price * decimal l.Qty))

    Trace.label (g, subtotal, "subtotal")
    let shipping = createAsync (fun _ _ -> desk.Quote subtotal.Value)
    Trace.label (g, shipping, "shipping")
    let total = createMemo (fun _ -> subtotal.Value + shipping.Value)
    Trace.label (g, total, "total")
    createEffect (fun () -> total.TryValue |> ignore)

    {
        Graph = g
        Desk = desk
        Lines = lines
        Subtotal = (subtotal :> INode).Id
        Shipping = (shipping :> INode).Id
        Total = (total :> INode).Id
    }

/// The frames of the events recorded by <c>act</c>, played on from the scene before it.
let private framesOf (c: Cart) (act: unit -> unit) =
    let before = Trace.events c.Graph

    let scene =
        (MapModel.frames MapModel.start before
         |> Array.last)
            .After

    act ()
    let after = Trace.events c.Graph
    MapModel.frames scene after[before.Length ..]

/// The landing page's example: an async price, a memo over it, and a boundary over the memo.
type private Quote =
    {
        Graph: Graph
        Price: AsyncSource<decimal>
        Total: int
        View: int
    }

let private quote () =
    let g = new Graph ()
    use _ = g.Activate ()
    let price = createAsyncSource<decimal>()
    let total = createMemo (fun _ -> price.Value * 3m)
    Trace.label (g, total, "total")

    let view =
        createBoundary (fun _ -> "Loading") (fun ex _ -> "Unavailable: " + ex.Message) (fun () -> sprintf "Total %M" total.Value)

    createEffect (fun () -> view.Value |> ignore)

    {
        Graph = g
        Price = price
        Total = (total :> INode).Id
        View = (view :> INode).Id
    }

let private sceneOf (g: Graph) =
    (MapModel.frames MapModel.start (Trace.events g)
     |> Array.last)
        .After

let private cues (frames: Frame[]) =
    frames |> Array.map _.Cue |> List.ofArray

let private indexOf (cue: Cue) (frames: Frame[]) =
    frames |> Array.findIndex (fun f -> f.Cue = cue)

[<Tests>]
let tests =
    testList
        "MapModel"
        [
            test "a write pulses the subtotal before the total runs" {
                let c = cart ()

                let frames =
                    framesOf c (fun () -> c.Lines.Value <- [ { Sku = "tea"; Price = 4m; Qty = 2 } ])

                let linesId = (c.Lines :> INode).Id
                let pulse = indexOf (Pulse (linesId, c.Subtotal)) frames
                let ring = indexOf (Ring c.Total) frames
                Expect.isLessThan pulse ring "the pulse travels before the total runs"
                Expect.equal (indexOf (Flash linesId) frames) 0 "the write comes first"
                Expect.equal frames[0].Log "write lines = [{ Sku = \"tea\"; Price = 4M; Qty = 2 }]" "the log names the write"
            }

            test "two quick writes drop the first flight" {
                let c = cart ()

                let frames =
                    framesOf c (fun () ->
                        c.Lines.Value <- [ { Sku = "tea"; Price = 4m; Qty = 2 } ]
                        c.Lines.Value <- [ { Sku = "tea"; Price = 4m; Qty = 3 } ])

                Expect.contains (cues frames) (Drop c.Shipping) "an older flight drops"

                let drop =
                    frames
                    |> Array.find (fun f -> f.Cue = Drop c.Shipping)

                Expect.equal drop.Log "drop shipping (superseded)" "the log gives the reason"
                let last = (Array.last frames).After
                Expect.isTrue (last.Flights.ContainsKey c.Shipping) "the newest flight stays in progress"
            }

            test "settle after two writes settles the live flight" {
                let c = cart ()

                let frames =
                    framesOf c (fun () ->
                        c.Lines.Value <- [ { Sku = "tea"; Price = 4m; Qty = 2 } ]
                        c.Lines.Value <- [ { Sku = "tea"; Price = 4m; Qty = 3 } ]
                        c.Desk.Settle 5m)

                Expect.contains (cues frames) (Settled c.Shipping) "the live flight settles"
                Expect.equal c.Desk.Pending 0 "the desk forgot the older request"
                let last = (Array.last frames).After
                Expect.isFalse (last.Flights.ContainsKey c.Shipping) "no flight in progress"
                Expect.isFalse (MapModel.pending last c.Shipping) "the settled node is no longer pending"
                Expect.equal last.Snapshot.Nodes[c.Total].Value (Some "17M") "the total takes the quote"
            }

            test "a failure lands in Errors with its message" {
                let c = cart ()
                let frames = framesOf c (fun () -> c.Desk.Fail "quote down")
                Expect.contains (cues frames) (Failed c.Shipping) "the flight fails"
                let last = (Array.last frames).After
                Expect.stringContains last.Errors[c.Shipping] "quote down" "the error keeps its message"
                Expect.isFalse (last.Flights.ContainsKey c.Shipping) "no flight in progress"
            }

            test "a memo whose run fails is failed, and a later run clears it" {
                let q = quote ()
                q.Price.Settle 4m
                q.Price.Fail (exn "offline")
                let failed = sceneOf q.Graph

                Expect.stringContains
                    (failed.Errors.TryFind q.Total
                     |> Option.defaultValue "")
                    "offline"
                    "the memo carries the error"

                Expect.isFalse (failed.Errors.ContainsKey q.View) "the boundary recovered"
                q.Price.Settle 5m
                let settled = sceneOf q.Graph
                Expect.isFalse (settled.Errors.ContainsKey q.Total) "the memo recovered"
                Expect.equal settled.Snapshot.Nodes[q.Total].Value (Some "15M") "with its new value"
            }

            test "a run that suspends keeps the value readers saw" {
                let q = quote ()
                let frames = MapModel.frames MapModel.start (Trace.events q.Graph)

                Expect.all
                    frames
                    (fun f ->
                        f.After.Snapshot.Nodes.TryFind q.Total
                        |> Option.forall (fun n -> n.Value.IsNone))
                    "no placeholder value"

                Expect.isFalse
                    (frames
                     |> Array.exists (fun f -> f.Cue = Surge (q.Total, [ q.View ])))
                    "nothing travels"

                Expect.equal (sceneOf q.Graph).Snapshot.Nodes[q.View].Value (Some "Loading") "the boundary shows its fallback"
            }

            test "a read of a pending source waits on it" {
                let c = cart ()
                let frames = MapModel.frames MapModel.start (Trace.events c.Graph)
                Expect.contains (cues frames) (Waits (c.Total, c.Shipping)) "the total suspends"
                let last = (Array.last frames).After
                Expect.equal (last.Waiting.TryFind c.Total) (Some c.Shipping) "the total waits on the shipping"
                Expect.isTrue (last.Flights.ContainsKey c.Shipping) "the first flight is in progress"
                Expect.isTrue (MapModel.pending last c.Shipping) "the node in flight is pending"
                Expect.isTrue (MapModel.pending last c.Total) "the reader waiting on it is pending"
            }

            test "the last scene agrees with the snapshot, labels from Trace.named included" {
                use g = new Graph ()
                use _ = g.Activate ()
                let a = Trace.named "a" (fun () -> createSignal 1)
                let b = Trace.named "b" (fun () -> createMemo (fun _ -> a.Value * 2))
                createEffect (fun () -> b.Value |> ignore)
                a.Value <- 3
                let events = Trace.events g
                let expected = TraceModel.snapshot events

                let actual =
                    (MapModel.frames MapModel.start events
                     |> Array.last)
                        .After.Snapshot

                let view (s: TraceSnapshot) =
                    s.Nodes
                    |> Map.map (fun _ n -> TraceModel.pathOf s n.Id, n.Label, n.Value, n.Status)

                Expect.equal (view actual) (view expected) "paths, labels, values and statuses agree"
                Expect.equal (MapModel.name actual (a :> INode).Id) "a" "the name is the label"
                Expect.equal (MapModel.frames MapModel.start events).Length events.Length "one frame per event"
            }

            test "an unlabelled node is named by its kind" {
                use g = new Graph ()
                use _ = g.Activate ()
                let a = createSignal 1
                createEffect (fun () -> a.Value |> ignore)
                let snapshot = TraceModel.snapshot (Trace.events g)

                let effect =
                    snapshot.Nodes.Values
                    |> Seq.find (fun n -> n.Kind = TraceNodeKind.Effect)

                Expect.equal (MapModel.name snapshot effect.Id) "effect" "the kind, in lower case"
            }

            test "frames played in two batches reach the same scene as one" {
                let c = cart ()
                c.Lines.Value <- [ { Sku = "tea"; Price = 4m; Qty = 2 } ]
                c.Desk.Settle 5m
                let events = Trace.events c.Graph

                let whole =
                    (MapModel.frames MapModel.start events
                     |> Array.last)
                        .After

                let half = events.Length / 2

                let first =
                    (MapModel.frames MapModel.start events[.. half - 1]
                     |> Array.last)
                        .After

                let both = (MapModel.frames first events[half..] |> Array.last).After
                Expect.equal both.Snapshot.Nodes whole.Snapshot.Nodes "the same nodes"
                Expect.equal both.Flights whole.Flights "the same flights"
                Expect.equal (MapModel.stateAt MapModel.start (MapModel.frames MapModel.start events) -1) MapModel.start "before the first frame"
            }

            test "the timeline spans the frames after setup" {
                Expect.equal (MapModel.tickAt 2 6 1) None "a setup frame has no tick"
                Expect.equal (MapModel.tickAt 2 6 2) (Some 0.25) "the first frame after setup"
                Expect.equal (MapModel.tickAt 2 6 5) (Some 1.0) "the last frame ends the track"
                Expect.equal (MapModel.tickAt 0 4 0) (Some 0.25) "no setup: every frame counts"
            }

            test "the cursor stays between the baseline and the last frame" {
                Expect.equal (MapModel.clampCursor 3 10 -1) 2 "before the baseline: the baseline"
                Expect.equal (MapModel.clampCursor 3 10 5) 5 "inside: unchanged"
                Expect.equal (MapModel.clampCursor 3 10 42) 9 "past the end: the last frame"
                Expect.equal (MapModel.clampCursor 0 0 4) -1 "no frames: before the first"
            }

            test "edges join visible sources to their observers" {
                let c = cart ()

                let scene =
                    (MapModel.frames MapModel.start (Trace.events c.Graph)
                     |> Array.last)
                        .After

                let edges = MapModel.edges scene.Snapshot
                Expect.contains edges ((c.Lines :> INode).Id, c.Subtotal) "lines feeds the subtotal"
                Expect.contains edges (c.Shipping, c.Total) "shipping feeds the total"
            }

            test "layout layers a chain by longest path" {
                let placed = Layout.place [ 1; 2; 3 ] (Map [ 2, [ 1 ]; 3, [ 2; 1 ] ])
                Expect.equal placed (Map [ 1, (0, 0); 2, (1, 0); 3, (2, 0) ]) "one layer per step"
            }

            test "layout orders a layer by the barycentre of its sources" {
                let sources = Map [ 3, [ 2 ]; 4, [ 1 ] ]
                let placed = Layout.place [ 1; 2; 3; 4 ] sources
                Expect.equal placed[4] (1, 0) "the observer of the first source comes first"
                Expect.equal placed[3] (1, 1) "then the observer of the second"
                Expect.equal (Layout.place [ 4; 3; 2; 1 ] sources) placed "the order of the input does not matter"
            }
        ]
#else
[<Tests>]
let tests = testList "MapModel" []
#endif
