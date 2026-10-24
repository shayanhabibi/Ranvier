module Ranvier.Tests.InputJournal

open Expecto
open Ranvier

let journaled () =
    let journal = MemoryJournal "test"
    journal, new Graph (GraphOptions.Default.WithJournal journal)

let intCodec: JournalCodec<int> =
    {
        Name = "int"
        Version = 1
        Encode = string
        Decode =
            fun text ->
                match System.Int32.TryParse text with
                | true, value -> Ok value
                | _ -> Error ("not an int: " + text)
    }

let originName (origin: JournalOrigin) =
    match origin with
    | JournalOrigin.External -> "External"
    | JournalOrigin.Produced _ -> "Produced"

let writes (journal: MemoryJournal) =
    journal.Entries
    |> List.filter (fun e -> e.Kind = JournalEntryKind.Write)

/// <summary>An external input to the replay property's graph.</summary>
type ReplayOp =
    | WriteA of int
    | WriteB of int
    | WriteBoth of int * int
    | SettleLoad of int
    | FailLoad
    | ExplicitFlush

/// <summary>Two keyed signals and a keyed source, two memos over them, and an effect over both memos.</summary>
type ReplayModel(journal: MemoryJournal, g: Graph) =
    let a = Signal (g, 0) |> Journal.key "a" intCodec
    let b = Signal (g, 0) |> Journal.key "b" intCodec

    let load =
        AsyncSource<int> g
        |> Journal.keySource "load" intCodec

    let sum = g.Run (fun () -> createMemo (fun _ -> a.Value * 10 + b.Value))

    let loaded =
        g.Run (fun () ->
            createMemo (fun _ ->
                match load.TryValue with
                | Ready value -> value + a.Value
                | Pending -> -1
                | Failed _ -> -2))

    let runs = ResizeArray<int * int>()

    do g.Run (fun () -> createEffect (fun () -> runs.Add (sum.Value, loaded.Value)))

    member _.Journal = journal
    member _.Graph = g

    member _.Apply(op: ReplayOp) =
        match op with
        | WriteA value -> a.Value <- value
        | WriteB value -> b.Value <- value
        | WriteBoth (x, y) ->
            g.Run (fun () ->
                batch (fun () ->
                    a.Value <- x
                    b.Value <- y))
        | SettleLoad value -> load.Settle value
        | FailLoad -> load.Fail (System.TimeoutException "late")
        | ExplicitFlush -> g.Flush ()

    /// <summary>Every keyed input's and memo's value, with the source's status as text.</summary>
    member _.State =
        let status =
            match load.TryValue with
            | Ready value -> "Ready " + string value
            | Pending -> "Pending"
            | Failed error -> "Failed " + error.Message

        a.Peek, b.Peek, status, sum.Value, loaded.Value

    member _.Runs = List.ofSeq runs

type CartLine =
    {
        Sku: string
        Price: decimal
        Qty: int
    }

type CartMsg =
    | AddLine of CartLine
    | SetQty of sku: string * qty: int

let private cents (value: decimal) =
    string (int (value * 100m))

let private ofCents (text: string) =
    decimal (int text) / 100m

let cartCodec: JournalCodec<CartMsg> =
    {
        Name = "cart"
        Version = 1
        Encode =
            function
            | AddLine line -> $"add|{line.Sku}|{cents line.Price}|{line.Qty}"
            | SetQty (sku, qty) -> $"qty|{sku}|{qty}"
        Decode =
            fun text ->
                match text.Split '|' with
                | [| "add"; sku; price; qty |] ->
                    Ok (
                        AddLine
                            {
                                Sku = sku
                                Price = ofCents price
                                Qty = int qty
                            }
                    )
                | [| "qty"; sku; qty |] -> Ok (SetQty (sku, int qty))
                | _ -> Error ("not a cart message: " + text)
    }

let decimalCodec: JournalCodec<decimal> =
    {
        Name = "cents"
        Version = 1
        Encode = cents
        Decode =
            fun text ->
                match System.Int32.TryParse text with
                | true, value -> Ok (decimal value / 100m)
                | _ -> Error ("not a cent count: " + text)
    }

/// <summary>The tracing guide's cart: the lines in a journaled MVU loop, a keyed discount, two memos and a banner.</summary>
let cart (g: Graph) =
    g.Run (fun () ->
        let update msg (lines: CartLine list) =
            match msg with
            | AddLine line -> lines @ [ line ]
            | SetQty (sku, qty) ->
                lines
                |> List.map (fun l -> if l.Sku = sku then { l with Qty = qty } else l)

        let app =
            Trace.named "lines" (fun () -> Ranvier.Elmish.Mvu.create [] update)
            |> Ranvier.Elmish.Mvu.journal "cart" cartCodec

        let discount =
            Trace.named "discount" (fun () -> createSignal 0m)
            |> Journal.key "discount" decimalCodec

        let subtotal =
            Trace.named "subtotal" (fun () -> app.Select (List.sumBy (fun l -> l.Price * decimal l.Qty)))

        let total =
            Trace.named "total" (fun () -> createMemo (fun _ -> subtotal.Value - discount.Value))

        let banner =
            Trace.named "banner" (fun () -> createEffectNode (fun () -> total.Value |> ignore))

        app, discount, banner)

#if !FABLE_COMPILER
/// <summary>
/// A single-thread synchronisation context: <c>Post</c> queues the callback with the poster's execution context, and
/// <c>RunAll</c> runs the queue on the calling thread, each callback under its poster's context.
/// </summary>
type FlowingContext() =
    inherit System.Threading.SynchronizationContext()

    let queue =
        System.Collections.Concurrent.ConcurrentQueue<System.Threading.ExecutionContext * System.Threading.SendOrPostCallback * obj>()

    override _.Post(callback, state) =
        queue.Enqueue (System.Threading.ExecutionContext.Capture (), callback, state)

    member _.RunAll() =
        let mutable item = Unchecked.defaultof<_>

        while queue.TryDequeue &item do
            let context, callback, state = item
            System.Threading.ExecutionContext.Run (context, (fun s -> callback.Invoke s), state)
#endif

[<Tests>]
let tests =
    testList
        "Journal"
        [
            test "GraphOptions.Default has no journal" { Expect.isNone GraphOptions.Default.Journal "the default records nothing" }

            test "WithJournal sets the journal" {
                let journal =
                    { new IInputJournal with
                        member _.Stream = "s"
                        member _.Record _ = ()
                    }

                let options = GraphOptions.Default.WithJournal journal

                Expect.isTrue
                    (options.Journal
                     |> Option.exists (fun j -> obj.ReferenceEquals (j, journal)))
                    "the given journal"
            }

            test "an external write to an unkeyed signal records a non-replayable Write" {
                let journal, g = journaled ()
                let s = Signal (g, 1)
                s.Value <- 2

                match journal.Entries with
                | [ entry ] ->
                    Expect.equal entry.Schema 2 "schema 2"
                    Expect.equal entry.Stream "test" "the journal's stream"
                    Expect.equal entry.Seq 1 "the first entry"
                    Expect.equal entry.Kind JournalEntryKind.Write "a write"
                    Expect.isNone entry.Key "an unkeyed node"
                    Expect.equal entry.Origin JournalOrigin.External "an external write"
                    Expect.equal entry.Payload (JournalPayload.NonReplayable entry.PayloadType) "no codec"
                    Expect.isNonEmpty entry.PayloadType "the value's type"
                | entries -> failtestf "expected one entry, got %A" entries
            }

            test "an equal write is recorded" {
                let journal, g = journaled ()
                let s = Signal (g, 1)
                s.Value <- 1
                s.Value <- 1

                Expect.equal (journal.Entries |> List.map (fun e -> e.Seq)) [ 1; 2 ] "both writes, in order"
            }

            test "a graph without a journal records nothing and writes as before" {
                let g = new Graph ()
                let s = Signal (g, 1)
                s.Value <- 2
                Expect.equal s.Peek 2 "the write lands"
            }

            test "writes made by a running computation are not journaled" {
                let journal, g = journaled ()
                let s = Signal (g, 0)
                let byEffect = Signal (g, 0)
                let byUntrack = Signal (g, 0)
                let byAct = Signal (g, 0)
                let byMemo = Signal (g, 0)

                let m =
                    g.Run (fun () ->
                        createEffect (fun () -> byEffect.Value <- s.Value)

                        createEffect (fun () ->
                            let v = s.Value
                            untrack (fun () -> byUntrack.Value <- v))

                        createEffectOn (fun () -> s.Value) (fun v -> byAct.Value <- v)

                        createMemo (fun _ ->
                            byMemo.Value <- s.Value
                            s.Value))

                Expect.equal m.Value 0 "the memo runs"
                s.Value <- 5
                Expect.equal m.Value 5 "the memo re-runs"

                Expect.equal (byEffect.Peek, byUntrack.Peek, byAct.Peek, byMemo.Peek) (5, 5, 5, 5) "every derived write landed"

                Expect.equal
                    (journal.Entries
                     |> List.filter (fun e -> e.Kind = JournalEntryKind.Write)
                     |> List.length)
                    1
                    "only the external write to s"
            }

            test "a keyed write records its key and encoded value" {
                let journal, g = journaled ()
                let s = Signal (g, 1) |> Journal.key "count" intCodec
                s.Value <- 7

                match writes journal with
                | [ entry ] ->
                    Expect.equal entry.Key (Some "count") "the key"
                    Expect.equal entry.Payload (JournalPayload.Encoded ("int", 1, "7")) "the codec's text"
                | entries -> failtestf "expected one write, got %A" entries
            }

            test "Journal.key returns the signal it registers" {
                let _, g = journaled ()
                let s = Signal (g, 1)
                Expect.isTrue (obj.ReferenceEquals (Journal.key "count" intCodec s, s)) "the same signal"
            }

            test "a duplicate key on one graph raises, naming the key" {
                let _, g = journaled ()

                Signal (g, 1)
                |> Journal.key "count" intCodec
                |> ignore

                let error =
                    try
                        Signal (g, 2)
                        |> Journal.key "count" intCodec
                        |> ignore

                        None
                    with ex ->
                        Some ex

                match error with
                | Some (:? System.ArgumentException as ex) -> Expect.stringContains ex.Message "count" "names the key"
                | other -> failtestf "expected an ArgumentException, got %A" other
            }

            test "a key registered in a disposed root is free again" {
                let journal, g = journaled ()

                let mount () =
                    g.Run (fun () ->
                        createRoot (fun owner ->
                            let s = Signal (g, 0) |> Journal.key "count" intCodec

                            owner, s))

                let first, _ = mount ()
                first.Dispose ()
                let second, s = mount ()
                s.Value <- 4

                match writes journal with
                | [ entry ] -> Expect.equal entry.Key (Some "count") "the second mount's signal holds the key"
                | entries -> failtestf "expected one write, got %A" entries

                second.Dispose ()
            }

            test "a key registered in an owning memo's body is free again when the memo re-runs" {
                let _, g = journaled ()
                let trigger = Signal (g, 0)

                let m =
                    g.Run (fun () ->
                        createMemoWith (fun _ ->
                            Signal (g, trigger.Value)
                            |> Journal.key "row" intCodec
                            |> ignore

                            trigger.Value))

                Expect.equal m.Value 0 "the first run"
                trigger.Value <- 1
                Expect.equal m.Value 1 "the re-run registers the key again"
            }

            test "Journal.key in a pure memo's body raises the scope error" {
                let _, g = journaled ()

                let m =
                    g.Run (fun () ->
                        createMemo (fun _ ->
                            Signal (g, 0)
                            |> Journal.key "row" intCodec
                            |> ignore

                            0))

                Expect.throwsT<System.InvalidOperationException> (fun () -> m.Value |> ignore) "the registration is owned"

                g.Run (fun () ->
                    Signal (g, 1)
                    |> Journal.key "row" intCodec
                    |> ignore)
            }

            test "a keyed node registered under a second key raises and keeps its first key" {
                let journal, g = journaled ()

                let owner, s =
                    g.Run (fun () -> createRoot (fun owner -> owner, Signal (g, 0) |> Journal.key "a" intCodec))

                let error =
                    try
                        g.Run (fun () -> s |> Journal.key "b" intCodec |> ignore)
                        None
                    with ex ->
                        Some ex

                match error with
                | Some (:? System.ArgumentException as ex) -> Expect.stringContains ex.Message "\"a\"" "names the held key"
                | other -> failtestf "expected an ArgumentException, got %A" other

                s.Value <- 5

                match writes journal with
                | [ entry ] -> Expect.equal entry.Key (Some "a") "the first key still records"
                | entries -> failtestf "expected one write, got %A" entries

                owner.Dispose ()

                g.Run (fun () ->
                    Signal (g, 1)
                    |> Journal.key "b" intCodec
                    |> ignore)
            }

            test "a write after an await in an async memo's body is not journaled" {
                let journal, g = journaled ()
                let target = Signal (g, 0)
                let gate = System.Threading.Tasks.TaskCompletionSource<int>()

                let a =
                    Make.AsyncMemo<int>(
                        g,
                        fun _ _ ->
                            task {
                                let! value = gate.Task
                                target.Value <- value
                                return value
                            }
                    )

                a.TryValue |> ignore
                gate.SetResult 3
                Expect.equal target.Peek 3 "the continuation's write landed"
                Expect.isEmpty (writes journal) "a continuation is part of the body"
            }

            test "the same key on two graphs is allowed" {
                let _, g1 = journaled ()
                let _, g2 = journaled ()

                Signal (g1, 1)
                |> Journal.key "count" intCodec
                |> ignore

                Signal (g2, 1)
                |> Journal.key "count" intCodec
                |> ignore
            }

            test "Journal.key on a graph without a journal is a no-op" {
                let g = new Graph ()
                let s = Signal (g, 1) |> Journal.key "count" intCodec

                Signal (g, 2)
                |> Journal.key "count" intCodec
                |> ignore

                s.Value <- 3
                Expect.equal s.Peek 3 "the write lands"
            }

            test "an Encode that raises records a non-replayable entry and the write lands" {
                let journal, g = journaled ()

                let failing =
                    { intCodec with
                        Encode = fun _ -> failwith "no"
                    }

                let s = Signal (g, 1) |> Journal.key "count" failing
                s.Value <- 4

                Expect.equal s.Peek 4 "the write lands"

                match writes journal with
                | [ entry ] ->
                    Expect.equal entry.Key (Some "count") "the key"
                    Expect.equal entry.Payload (JournalPayload.NonReplayable entry.PayloadType) "not encodable"
                | entries -> failtestf "expected one write, got %A" entries
            }

#if RANVIER_TRACE
            test "a traced Journal.key labels an unlabelled node with the key" {
                let _, g = journaled ()
                let s = Signal (g, 1) |> Journal.key "count" intCodec
                Expect.equal (Trace.resolve g "/count") (Some (s :> INode).Id) "the key is the trace label"
            }

            test "a traced Journal.key keeps an existing label" {
                let _, g = journaled ()
                let s = Signal (g, 1)
                Trace.label (g, s, "mine")
                Journal.key "count" intCodec s |> ignore
                Expect.equal (Trace.resolve g "/mine") (Some (s :> INode).Id) "the label stays"
            }
#endif

            test "settles and failures of a keyed source record in graph-thread order" {
                let journal, g = journaled ()

                let source =
                    AsyncSource<int> g
                    |> Journal.keySource "load" intCodec

                source.Settle 3
                source.Fail (System.InvalidOperationException "boom")

                Expect.equal
                    (journal.Entries
                     |> List.map (fun e -> e.Kind, e.Key, e.Payload))
                    [
                        JournalEntryKind.Settle, Some "load", JournalPayload.Encoded ("int", 1, "3")
                        JournalEntryKind.Fail, Some "load", JournalPayload.Encoded ("exn", 1, "boom")
                    ]
                    "a settle, then a failure carrying its message"

                Expect.equal (journal.Entries |> List.map (fun e -> e.Seq)) [ 1; 2 ] "consecutive positions"
#if !FABLE_COMPILER
                Expect.equal journal.Entries[1].PayloadType "System.InvalidOperationException" "the exception's type"
#endif
            }

            test "settles of an unkeyed source record non-replayable entries" {
                let journal, g = journaled ()
                let source = AsyncSource<int> g
                source.Settle 3
                source.Fail (System.InvalidOperationException "boom")

                Expect.all
                    journal.Entries
                    (fun e ->
                        e.Key = None
                        && e.Payload = JournalPayload.NonReplayable e.PayloadType)
                    "no codec"

                Expect.equal (journal.Entries.Length) 2 "both recorded"
            }

            test "a settle made by a running computation is not journaled" {
                let journal, g = journaled ()
                let s = Signal (g, 0)

                let source =
                    AsyncSource<int> g
                    |> Journal.keySource "load" intCodec

                g.Run (fun () -> createEffect (fun () -> source.Settle s.Value))
                s.Value <- 1

                match source.TryValue with
                | Ready value -> Expect.equal value 1 "the derived settle landed"
                | other -> failtestf "expected Ready 1, got %O" other

                Expect.equal
                    (journal.Entries |> List.map (fun e -> e.Kind))
                    [ JournalEntryKind.Write; JournalEntryKind.Flush ]
                    "only the external write and its flush"
            }

            test "one Flush follows each flush pass that followed recorded entries" {
                let journal, g = journaled ()
                let s = Signal (g, 0)
                let seen = ResizeArray ()
                g.Run (fun () -> createEffect (fun () -> seen.Add s.Value))
                s.Value <- 1
                s.Value <- 2

                Expect.sequenceEqual seen [ 0; 1; 2 ] "each write ran the effect"

                Expect.equal
                    (journal.Entries |> List.map (fun e -> e.Kind))
                    [
                        JournalEntryKind.Write
                        JournalEntryKind.Flush
                        JournalEntryKind.Write
                        JournalEntryKind.Flush
                    ]
                    "a boundary after each pass"

                Expect.equal (journal.Entries |> List.map (fun e -> e.Seq)) [ 1; 2; 3; 4 ] "consecutive positions"

                Expect.all
                    (journal.Entries
                     |> List.filter (fun e -> e.Kind = JournalEntryKind.Flush))
                    (fun e ->
                        e.Key = None
                        && e.Payload = JournalPayload.Empty
                        && e.PayloadType = "")
                    "a Flush carries nothing"
            }

            test "a batch of two writes gives two Write entries and one Flush" {
                let journal, g = journaled ()
                let s = Signal (g, 0)
                let t = Signal (g, 0)
                g.Run (fun () -> createEffect (fun () -> s.Value + t.Value |> ignore))

                g.Batch (fun () ->
                    s.Value <- 1
                    t.Value <- 2)

                Expect.equal
                    (journal.Entries |> List.map (fun e -> e.Kind))
                    [ JournalEntryKind.Write; JournalEntryKind.Write; JournalEntryKind.Flush ]
                    "one boundary for the batch"
            }

            test "a write that queues nothing records no Flush" {
                let journal, g = journaled ()
                let s = Signal (g, 0)
                s.Value <- 1

                Expect.equal (journal.Entries |> List.map (fun e -> e.Kind)) [ JournalEntryKind.Write ] "no flush pass followed"

                g.Flush ()

                Expect.equal
                    (journal.Entries |> List.map (fun e -> e.Kind))
                    [ JournalEntryKind.Write; JournalEntryKind.Flush ]
                    "an explicit flush after the write is a boundary"
            }

            test "replay of writes, settles and failures into a fresh graph gives equal values" {
                let build () =
                    let journal, g = journaled ()
                    let a = Signal (g, 0) |> Journal.key "a" intCodec
                    let b = Signal (g, 0) |> Journal.key "b" intCodec

                    let load =
                        AsyncSource<int> g
                        |> Journal.keySource "load" intCodec

                    let fails =
                        AsyncSource<int> g
                        |> Journal.keySource "fails" intCodec

                    let sum = g.Run (fun () -> createMemo (fun _ -> a.Value + b.Value))
                    let runs = ResizeArray<int>()
                    g.Run (fun () -> createEffect (fun () -> runs.Add sum.Value))
                    journal, g, a, b, load, fails, sum, runs

                let journal, g, a, b, load, fails, sum, runs = build ()
                a.Value <- 2

                g.Run (fun () ->
                    batch (fun () ->
                        a.Value <- 3
                        b.Value <- 4))

                load.Settle 9
                fails.Fail (System.TimeoutException "late")

                let _, g2, a2, b2, load2, fails2, sum2, runs2 = build ()
                Journal.replay ReplayMode.Diagnostic g2 journal.Entries

                Expect.equal (a2.Peek, b2.Peek) (a.Peek, b.Peek) "the signals"
                Expect.equal sum2.Value sum.Value "the memo"
                Expect.equal (List.ofSeq runs2) (List.ofSeq runs) "the effect ran once per recorded flush"

                match load2.TryValue with
                | Ready value -> Expect.equal value 9 "the recorded settle"
                | other -> failtestf "expected Ready 9, got %O" other

                match fails2.TryValue with
                | Failed (:? JournalReplayedFailure as ex) -> Expect.equal ex.Message "late" "the recorded message"
                | other -> failtestf "expected the replayed failure, got %O" other

                match fails.TryValue with
                | Failed _ -> ()
                | other -> failtestf "expected the live failure, got %O" other
            }

            test "re-enactment applies an external settle once" {
                let journal, g = journaled ()
                let a = Signal (g, 0) |> Journal.key "a" intCodec

                let load =
                    AsyncSource<int> g
                    |> Journal.keySource "load" intCodec

                a.Value <- 1
                load.Settle 9

                let _, g2 = journaled ()
                let a2 = Signal (g2, 0) |> Journal.key "a" intCodec

                let load2 =
                    AsyncSource<int> g2
                    |> Journal.keySource "load" intCodec

                Journal.replay ReplayMode.Reenact g2 journal.Entries

                Expect.equal a2.Peek 1 "the write is applied"
                Expect.equal load2.TryValue (Ready 9) "the external settle is applied"
            }

            test "Journal.replaying returns the mode while the replay runs" {
                let journal, g = journaled ()
                let a = Signal (g, 0) |> Journal.key "a" intCodec
                a.Value <- 1

                let _, g2 = journaled ()
                let a2 = Signal (g2, 0) |> Journal.key "a" intCodec
                let seen = ResizeArray<ReplayMode option>()

                g2.Run (fun () ->
                    createEffect (fun () ->
                        seen.Add (
                            a2.Value |> ignore
                            Journal.replaying g2
                        )))

                Journal.replay ReplayMode.Diagnostic g2 journal.Entries

                Expect.equal (List.ofSeq seen) [ None; Some ReplayMode.Diagnostic ] "the effect's run inside the replay"
                Expect.isNone (Journal.replaying g2) "after the replay"
            }

            test "replay requires a graph with a journal" {
                let g = new Graph (GraphOptions.Default)
                Expect.throwsT<System.InvalidOperationException> (fun () -> Journal.replay ReplayMode.Diagnostic g []) "no journal"
            }

            testList
                "replay raises JournalReplayException"
                [
                    let entry kind key payload =
                        {
                            Schema = 2
                            Stream = "test"
                            Seq = 7
                            Kind = kind
                            Key = key
                            PayloadType = "System.Int32"
                            Payload = payload
                            Origin = JournalOrigin.External
                        }

                    let raises name (entry: JournalEntry) (reason: string) =
                        test name {
                            let _, g = journaled ()
                            let a = Signal (g, 0) |> Journal.key "a" intCodec

                            match
                                (try
                                    Journal.replay ReplayMode.Diagnostic g [ entry ]
                                    None
                                 with ex ->
                                     Some ex)
                            with
                            | Some (:? JournalReplayException as ex) ->
                                Expect.equal ex.Seq 7 "names the seq"
                                Expect.equal ex.Key entry.Key "names the key"
                                Expect.equal ex.PayloadType "System.Int32" "names the payload type"
                                Expect.stringContains ex.Reason reason "says why"
                                Expect.stringContains ex.Message "Journal entry 7" "the message names the entry"
                            | other -> failtestf "expected a JournalReplayException, got %A" other

                            Expect.equal a.Peek 0 "nothing is applied"
                        }

                    raises
                        "on a non-replayable entry"
                        (entry JournalEntryKind.Write None (JournalPayload.NonReplayable "System.Int32"))
                        "non-replayable"

                    raises
                        "on a key the target graph lacks"
                        (entry JournalEntryKind.Write (Some "b") (JournalPayload.Encoded ("int", 1, "3")))
                        "not registered"

                    raises
                        "on a codec name mismatch"
                        (entry JournalEntryKind.Write (Some "a") (JournalPayload.Encoded ("text", 1, "3")))
                        "codec text version 1"

                    raises
                        "on a codec version mismatch"
                        (entry JournalEntryKind.Write (Some "a") (JournalPayload.Encoded ("int", 2, "3")))
                        "version 2"

                    raises
                        "on a decode error"
                        (entry JournalEntryKind.Write (Some "a") (JournalPayload.Encoded ("int", 1, "three")))
                        "not an int: three"

                    raises
                        "on an entry kind the key's input does not take"
                        (entry JournalEntryKind.Settle (Some "a") (JournalPayload.Encoded ("int", 1, "3")))
                        "takes no Settle"
                ]

            test "re-enactment re-runs the async work, and a changed async result flows through the replay" {
                // A query signal, a gateway effect that queues a call per query outside a diagnostic replay, a
                // keyed source the completed calls settle as inputs of the effect's work, and a memo over the source.
                let build () =
                    let journal, g = journaled ()
                    let query = Signal (g, 0) |> Journal.key "query" intCodec

                    let result =
                        AsyncSource<int> g
                        |> Journal.keySource "result" intCodec

                    let calls = System.Collections.Generic.Queue<int * JournalWork>()

                    g.Run (fun () ->
                        createEffect (fun () ->
                            let q = query.Value

                            if
                                q > 0
                                && Journal.replaying g <> Some ReplayMode.Diagnostic
                            then
                                calls.Enqueue (q, Journal.work g)))

                    let shown =
                        g.Run (fun () ->
                            createMemo (fun _ ->
                                match result.TryValue with
                                | Ready value -> value
                                | _ -> -1))

                    let complete (service: int -> int) =
                        while calls.Count > 0 do
                            let q, work = calls.Dequeue ()
                            Journal.produce work (fun () -> result.Settle (service q))

                    journal, g, query, shown, calls, complete

                let journal, _, query, shown, _, complete = build ()
                query.Value <- 2
                complete (fun q -> q * 10)
                Expect.equal shown.Value 20 "the live result"

                let _, g2, _, shown2, calls2, _ = build ()
                Journal.replay ReplayMode.Diagnostic g2 journal.Entries
                Expect.equal calls2.Count 0 "the diagnostic replay makes no call"
                Expect.equal shown2.Value 20 "the recorded settle"

                let replayed3, g3, _, shown3, calls3, complete3 = build ()
                Journal.replay ReplayMode.Reenact g3 journal.Entries
                Expect.equal (calls3 |> Seq.map fst |> List.ofSeq) [ 2 ] "the re-enactment calls the service again"
                Expect.equal shown3.Value -1 "the recorded settle is skipped"
                complete3 (fun q -> q * 100)
                Expect.equal shown3.Value 200 "the changed result reaches the memo"

                Expect.equal
                    (replayed3.Entries
                     |> List.filter (fun e -> e.Kind = JournalEntryKind.Settle)
                     |> List.length)
                    1
                    "the source settles once"
            }

#if !FABLE_COMPILER
            // .NET only: Fable has no async context to carry the work into a continuation.
            testCaseAsync "an effect continuation's direct settle reaches each replay once"
            <| async {
                // An effect over query settles the keyed source with service q after an await on the gate.
                let build (service: int -> int) =
                    let journal, g = journaled ()
                    let gate = System.Threading.Tasks.TaskCompletionSource<unit>()
                    let query = Signal (g, 0) |> Journal.key "query" intCodec

                    let result =
                        AsyncSource<int> g
                        |> Journal.keySource "result" intCodec

                    g.Run (fun () ->
                        createEffect (fun () ->
                            let q = query.Value

                            if q > 0 then
                                task {
                                    do! gate.Task
                                    result.Settle (service q)
                                }
                                |> ignore))

                    journal, g, query, result, gate

                let settles (journal: MemoryJournal) =
                    journal.Entries
                    |> List.filter (fun e -> e.Kind = JournalEntryKind.Settle)

                let journal, _, query, result, gate = build (fun q -> q * 10)
                query.Value <- 2
                gate.SetResult ()
                do! drained ()
                Expect.equal result.TryValue (Ready 20) "the live result"

                Expect.isTrue
                    (settles journal
                     |> List.forall (fun e -> e.Origin <> JournalOrigin.External))
                    "the continuation's settle is attributed to the effect's work"

                for mode in [ ReplayMode.Diagnostic; ReplayMode.Reenact ] do
                    let replayed, g2, _, result2, gate2 = build (fun q -> q * 100)
                    Journal.replay mode g2 journal.Entries
                    gate2.SetResult ()
                    do! drained ()

                    let expected = if mode = ReplayMode.Diagnostic then 20 else 200
                    Expect.equal result2.TryValue (Ready expected) $"the {mode} replay's result"
                    Expect.equal (settles replayed |> List.length) 1 $"the {mode} replay settles once"
                    ignore g2
            }

            test "an external dispatch drained under an effect run's execution context records an External entry" {
                let pump = FlowingContext ()
                let previous = System.Threading.SynchronizationContext.Current
                System.Threading.SynchronizationContext.SetSynchronizationContext pump

                try
                    let journal, g = journaled ()
                    let go = Signal (g, 0) |> Journal.key "go" intCodec
                    let a = Signal (g, 0) |> Journal.key "a" intCodec
                    let b = Signal (g, 0) |> Journal.key "b" intCodec

                    // A thread started by the effect, carrying the effect run's execution context, dispatches the write
                    // to a, so the pump's first drain runs under that context. A thread outside every work then
                    // dispatches the write to b before the pump runs.
                    let onThread (dispatch: unit -> unit) =
                        let thread = System.Threading.Thread (fun () -> dispatch ())
                        thread.Start ()
                        thread.Join ()

                    g.Run (fun () ->
                        createEffect (fun () ->
                            if go.Value > 0 then
                                onThread (fun () -> g.Dispatch (fun () -> a.Value <- 1))
                                use _ = System.Threading.ExecutionContext.SuppressFlow ()
                                onThread (fun () -> g.Dispatch (fun () -> b.Value <- 1))))

                    go.Value <- 1
                    pump.RunAll ()
                    Expect.equal (a.Peek, b.Peek) (1, 1) "both writes landed"

                    Expect.equal
                        (writes journal
                         |> List.map (fun e -> e.Key, originName e.Origin))
                        [ Some "go", "External"; Some "a", "Produced"; Some "b", "External" ]
                        "the write from outside every work is External"

                    let _, g2 = journaled ()
                    let b2 = Signal (g2, 0) |> Journal.key "b" intCodec

                    Signal (g2, 0)
                    |> Journal.key "a" intCodec
                    |> ignore

                    Signal (g2, 0)
                    |> Journal.key "go" intCodec
                    |> ignore

                    Journal.replay ReplayMode.Reenact g2 journal.Entries
                    Expect.equal b2.Peek 1 "a re-enactment applies the external write"
                finally
                    System.Threading.SynchronizationContext.SetSynchronizationContext previous
            }
#endif

#if !FABLE_COMPILER
            testPropertyWithConfig
                { FsCheckConfig.defaultConfig with
                    maxTest = 200
                }
                "replay in either mode into a fresh untraced graph gives every keyed input and memo the recorded run's value"
            <| fun (ops: ReplayOp list) ->
                let live = ReplayModel (journaled ())

                for op in ops do
                    live.Apply op

                for mode in [ ReplayMode.Diagnostic; ReplayMode.Reenact ] do
                    let replayed = ReplayModel (journaled ())
                    Journal.replay mode replayed.Graph live.Journal.Entries

                    Expect.equal replayed.State live.State $"the {mode} replay's inputs and memos"
                    Expect.equal replayed.Runs live.Runs $"the {mode} replay's effect runs"
#endif

#if RANVIER_TRACE
            test "a diagnostic replay of the traced cart gives the live run's snapshot and the why of every banner run" {
                let journal, g = journaled ()
                let app, discount, banner = cart g

                app.Dispatch (AddLine { Sku = "tea"; Price = 4m; Qty = 1 })

                discount.Value <- 1m
                app.Dispatch (SetQty ("tea", 3))

                g.Run (fun () ->
                    batch (fun () ->
                        discount.Value <- 2m

                        app.Dispatch (AddLine { Sku = "jam"; Price = 2.5m; Qty = 2 })))

                let _, g2 = journaled ()
                let _, _, banner2 = cart g2
                Journal.replay ReplayMode.Diagnostic g2 journal.Entries

                Expect.equal (Trace.snapshot g2) (Trace.snapshot g) "the final snapshot"

                let runs = (Trace.history g banner).Runs.Length
                Expect.equal (Trace.history g2 banner2).Runs.Length runs "the banner's run count"
                Expect.isGreaterThan runs 3 "the banner ran for each flush"

                for run in 1..runs do
                    Expect.equal (Trace.whyAt g2 banner2 run) (Trace.whyAt g banner run) $"the why of banner run {run}"
            }
#endif

            testList
                "Mvu.journal"
                [
                    let counter (g: Graph) =
                        g.Run (fun () ->
                            Ranvier.Elmish.Mvu.withCmd (0, []) (fun (msg: int) model ->
                                if msg = 0 then
                                    model, []
                                else
                                    model + msg, [ fun dispatch -> dispatch 0 ])
                            |> Ranvier.Elmish.Mvu.journal "counter" intCodec)

                    test "an external dispatch records one Message entry and its flush" {
                        let journal, g = journaled ()
                        let app = counter g
                        let seen = ResizeArray<int>()
                        g.Run (fun () -> createEffect (fun () -> seen.Add app.Model))
                        app.Dispatch 2

                        Expect.equal
                            (journal.Entries
                             |> List.map (fun e -> e.Kind, e.Key, e.Payload))
                            [
                                JournalEntryKind.Message, Some "counter", JournalPayload.Encoded ("int", 1, "2")
                                JournalEntryKind.Flush, None, JournalPayload.Empty
                            ]
                            "the model write and the command's dispatch are derived"
                    }

                    test "a dispatch from an effect records nothing" {
                        let journal, g = journaled ()
                        let app = counter g
                        let s = Signal (g, 0)

                        g.Run (fun () ->
                            createEffect (fun () ->
                                if s.Value > 0 then
                                    app.Dispatch s.Value))

                        s.Value <- 3

                        Expect.equal app.Model 3 "the dispatch landed"

                        Expect.equal
                            (journal.Entries |> List.map (fun e -> e.Kind))
                            [ JournalEntryKind.Write; JournalEntryKind.Flush ]
                            "only the external write"
                    }

                    test "replay dispatches the recorded messages" {
                        let journal, g = journaled ()
                        let app = counter g
                        app.Dispatch 2
                        app.Dispatch 5

                        let replayed, g2 = journaled ()
                        let app2 = counter g2
                        Journal.replay ReplayMode.Diagnostic g2 journal.Entries

                        Expect.equal app2.Model app.Model "the model"
                        Expect.equal replayed.Entries journal.Entries "the replay records the same entries"
                    }

                    // Message 1 starts a command that dispatches 2 after an await on the gate. An effect reads the model.
                    let awaiting (journal: MemoryJournal, g: Graph) =
                        let gate = System.Threading.Tasks.TaskCompletionSource<unit>()
                        let seen = ResizeArray<int>()

                        let app =
                            g.Run (fun () ->
                                Ranvier.Elmish.Mvu.withCmd ([], []) (fun (msg: int) model ->
                                    seen.Add msg

                                    if msg = 1 then
                                        msg :: model,
                                        [
                                            fun dispatch ->
                                                task {
                                                    do! gate.Task
                                                    dispatch 2
                                                }
                                                |> ignore
                                        ]
                                    else
                                        msg :: model, [])
                                |> Ranvier.Elmish.Mvu.journal "awaiting" intCodec)

                        g.Run (fun () -> createEffect (fun () -> app.Model |> ignore))
                        journal, g, app, seen, gate

                    let recorded () =
                        async {
                            let journal, g, app, seen, gate = awaiting (journaled ())
                            app.Dispatch 1
                            gate.SetResult ()
                            do! drained ()
                            Expect.equal (List.ofSeq seen) [ 1; 2 ] "the live run's updates"
                            return journal, g, app
                        }

                    testCaseAsync "a re-enactment applies a message a command dispatches after an await once"
                    <| async {
                        let! journal, g, app = recorded ()
                        let _, g2, app2, seen2, gate2 = awaiting (journaled ())
                        Journal.replay ReplayMode.Reenact g2 journal.Entries
                        gate2.SetResult ()
                        do! drained ()

                        Expect.equal (List.ofSeq seen2) [ 1; 2 ] "each message reaches update once"
                        Expect.equal app2.Model app.Model "the live run's model"
#if RANVIER_TRACE
                        Expect.equal (Trace.snapshot g2) (Trace.snapshot g) "the live run's snapshot"
#else
                        ignore g
#endif
                    }

                    testCaseAsync "a diagnostic replay applies a message a command dispatches after an await once"
                    <| async {
                        let! journal, g, app = recorded ()
                        let _, g2, app2, seen2, gate2 = awaiting (journaled ())
                        Journal.replay ReplayMode.Diagnostic g2 journal.Entries
                        Expect.equal (List.ofSeq seen2) [ 1; 2 ] "the recorded messages"
                        gate2.SetResult ()
                        do! drained ()

                        Expect.equal (List.ofSeq seen2) [ 1; 2 ] "each message reaches update once"
                        Expect.equal app2.Model app.Model "the live run's model"
#if RANVIER_TRACE
                        Expect.equal (Trace.snapshot g2) (Trace.snapshot g) "the live run's snapshot"
#else
                        ignore g
#endif
                    }

                    testCaseAsync "a message a command dispatches after an await records a Produced entry"
                    <| async {
                        let! journal, _, _ = recorded ()

                        Expect.equal
                            (journal.Entries
                             |> List.map (fun e -> e.Kind, e.Payload, originName e.Origin))
                            [
                                JournalEntryKind.Message, JournalPayload.Encoded ("int", 1, "1"), "External"
                                JournalEntryKind.Flush, JournalPayload.Empty, "External"
                                JournalEntryKind.Message, JournalPayload.Encoded ("int", 1, "2"), "Produced"
                                JournalEntryKind.Flush, JournalPayload.Empty, "External"
                            ]
                            "the dispatched message's origin"
                    }

                    testCaseAsync "a diagnostic replay of a journal cut before a command dispatches applies the dispatch once"
                    <| async {
                        let journal, _, app, _, _ = awaiting (journaled ())
                        app.Dispatch 1
                        let cut = journal.Entries

                        let _, g2, _, seen2, gate2 = awaiting (journaled ())
                        Journal.replay ReplayMode.Diagnostic g2 cut
                        gate2.SetResult ()
                        do! drained ()

                        Expect.equal (List.ofSeq seen2) [ 1; 2 ] "the re-run command's dispatch is live"
                        ignore g2
                    }

                    // An odd message m starts a command that dispatches m * 10 after an await on the gate. An effect
                    // reads the model. prelude runs on the graph first.
                    let commanding (prelude: Graph -> unit) (journal: MemoryJournal, g: Graph) =
                        prelude g
                        let gate = System.Threading.Tasks.TaskCompletionSource<unit>()
                        let seen = ResizeArray<int>()

                        let app =
                            g.Run (fun () ->
                                Ranvier.Elmish.Mvu.withCmd ([], []) (fun (msg: int) model ->
                                    seen.Add msg

                                    if msg % 2 = 1 then
                                        msg :: model,
                                        [
                                            fun dispatch ->
                                                task {
                                                    do! gate.Task
                                                    dispatch (msg * 10)
                                                }
                                                |> ignore
                                        ]
                                    else
                                        msg :: model, [])
                                |> Ranvier.Elmish.Mvu.journal "commanding" intCodec)

                        g.Run (fun () -> createEffect (fun () -> app.Model |> ignore))
                        journal, g, app, seen, gate

                    let noPrelude (_: Graph) = ()

                    // The journal of a run that dispatches 1, whose command dispatches 10, and the run's model.
                    let recordedCommand () =
                        async {
                            let journal, _, app, seen, gate = commanding noPrelude (journaled ())
                            app.Dispatch 1
                            gate.SetResult ()
                            do! drained ()
                            Expect.equal (List.ofSeq seen) [ 1; 10 ] "the live run's updates"
                            return journal, app.Model
                        }

                    testCaseAsync "a diagnostic replay keeps the inputs of work started on the target before it"
                    <| async {
                        let! journal, _ = recordedCommand ()

                        Expect.exists journal.Entries (fun e -> e.Origin <> JournalOrigin.External) "the journal holds a Produced entry"

                        let _, g2, app2, seen2, gate2 = commanding noPrelude (journaled ())
                        app2.Dispatch 3
                        Journal.replay ReplayMode.Diagnostic g2 journal.Entries
                        gate2.SetResult ()
                        do! drained ()

                        Expect.equal
                            (List.ofSeq seen2)
                            [ 3; 1; 10; 30 ]
                            "the earlier command's dispatch is live, and the re-run command's is the recorded one"
                    }

                    testCaseAsync "a diagnostic replay into a target that ran an extra effect first applies each message once"
                    <| async {
                        let! journal, model = recordedCommand ()

                        let extraEffect (g: Graph) =
                            g.Run (fun () -> createEffect (fun () -> ()))

                        let _, g2, app2, seen2, gate2 = commanding extraEffect (journaled ())
                        Journal.replay ReplayMode.Diagnostic g2 journal.Entries
                        gate2.SetResult ()
                        do! drained ()

                        Expect.equal (List.ofSeq seen2) [ 1; 10 ] "each message reaches update once"
                        Expect.equal app2.Model model "the live run's model"
                    }

                    testCaseAsync "a diagnostic replay after a commanded message on the target applies each message once"
                    <| async {
                        let! journal, _ = recordedCommand ()
                        let _, g2, app2, seen2, gate2 = commanding noPrelude (journaled ())
                        app2.Dispatch 3
                        gate2.SetResult ()
                        do! drained ()
                        Journal.replay ReplayMode.Diagnostic g2 journal.Entries
                        do! drained ()

                        Expect.equal (List.ofSeq seen2) [ 3; 30; 1; 10 ] "each message reaches update once"
                        Expect.equal app2.Model [ 10; 1; 30; 3 ] "the model of the same messages dispatched live"
                    }

                    testCaseAsync "a diagnostic replay into a fresh graph applies the dispatch of an initial command once"
                    <| async {
                        // The initial command dispatches 5 after an await on the gate.
                        let build (journal: MemoryJournal, g: Graph) =
                            let gate = System.Threading.Tasks.TaskCompletionSource<unit>()
                            let seen = ResizeArray<int>()

                            let app =
                                g.Run (fun () ->
                                    Ranvier.Elmish.Mvu.withCmd
                                        ([],
                                         [
                                             fun dispatch ->
                                                 task {
                                                     do! gate.Task
                                                     dispatch 5
                                                 }
                                                 |> ignore
                                         ])
                                        (fun (msg: int) model ->
                                            seen.Add msg
                                            msg :: model, [])
                                    |> Ranvier.Elmish.Mvu.journal "initial" intCodec)

                            journal, g, app, seen, gate

                        let journal, _, app, seen, gate = build (journaled ())
                        app.Dispatch 2
                        gate.SetResult ()
                        do! drained ()
                        Expect.equal (List.ofSeq seen) [ 2; 5 ] "the live run's updates"

                        let _, g2, app2, seen2, gate2 = build (journaled ())
                        Journal.replay ReplayMode.Diagnostic g2 journal.Entries
                        gate2.SetResult ()
                        do! drained ()

                        Expect.equal (List.ofSeq seen2) [ 2; 5 ] "each message reaches update once"
                        Expect.equal app2.Model app.Model "the live run's model"
                    }

                    testCaseAsync "a second diagnostic replay into the same graph applies each message once"
                    <| async {
                        let! journal, _ = recordedCommand ()
                        let _, g2, app2, seen2, gate2 = commanding noPrelude (journaled ())
                        Journal.replay ReplayMode.Diagnostic g2 journal.Entries
                        gate2.SetResult ()
                        do! drained ()
                        Journal.replay ReplayMode.Diagnostic g2 journal.Entries
                        do! drained ()

                        Expect.equal (List.ofSeq seen2) [ 1; 10; 1; 10 ] "each message of each replay reaches update once"
                        Expect.equal app2.Model [ 10; 1; 10; 1 ] "the model of the run dispatched twice"
                    }

                    testCaseAsync "each replay mode applies a command's synchronous dispatch and its dispatch after an await once"
                    <| async {
                        // Message 1 starts a command that dispatches 0, then 2 after an await on the gate.
                        let build (journal: MemoryJournal, g: Graph) =
                            let gate = System.Threading.Tasks.TaskCompletionSource<unit>()
                            let seen = ResizeArray<int>()

                            let app =
                                g.Run (fun () ->
                                    Ranvier.Elmish.Mvu.withCmd ([], []) (fun (msg: int) model ->
                                        seen.Add msg

                                        if msg = 1 then
                                            msg :: model,
                                            [
                                                fun dispatch ->
                                                    dispatch 0

                                                    task {
                                                        do! gate.Task
                                                        dispatch 2
                                                    }
                                                    |> ignore
                                            ]
                                        else
                                            msg :: model, [])
                                    |> Ranvier.Elmish.Mvu.journal "loading" intCodec)

                            journal, g, app, seen, gate

                        let journal, _, app, seen, gate = build (journaled ())
                        app.Dispatch 1
                        gate.SetResult ()
                        do! drained ()
                        Expect.equal (List.ofSeq seen) [ 1; 0; 2 ] "the live run's updates"

                        for mode in [ ReplayMode.Reenact; ReplayMode.Diagnostic ] do
                            let _, g2, app2, seen2, gate2 = build (journaled ())
                            Journal.replay mode g2 journal.Entries
                            gate2.SetResult ()
                            do! drained ()

                            Expect.equal (List.ofSeq seen2) [ 1; 0; 2 ] $"each message of the {mode} replay reaches update once"
                            Expect.equal app2.Model app.Model $"the {mode} replay's model"
                            ignore g2
                    }

                    testCaseAsync
                        "a diagnostic replay after the gate opens applies a command's synchronous dispatch and its dispatch after an await once"
                    <| async {
                        // Message 1 starts a command that dispatches 0, then 2 after an await on the gate.
                        let gate = System.Threading.Tasks.TaskCompletionSource<unit>()

                        let build (journal: MemoryJournal, g: Graph) =
                            let seen = ResizeArray<int>()

                            let app =
                                g.Run (fun () ->
                                    Ranvier.Elmish.Mvu.withCmd ([], []) (fun (msg: int) model ->
                                        seen.Add msg

                                        if msg = 1 then
                                            msg :: model,
                                            [
                                                fun dispatch ->
                                                    dispatch 0

                                                    task {
                                                        do! gate.Task
                                                        dispatch 2
                                                    }
                                                    |> ignore
                                            ]
                                        else
                                            msg :: model, [])
                                    |> Ranvier.Elmish.Mvu.journal "loading" intCodec)

                            journal, g, app, seen

                        let journal, _, app, seen = build (journaled ())
                        app.Dispatch 1
                        gate.SetResult ()
                        do! drained ()
                        Expect.equal (List.ofSeq seen) [ 1; 0; 2 ] "the live run's updates"

                        let _, g2, app2, seen2 = build (journaled ())
                        Journal.replay ReplayMode.Diagnostic g2 journal.Entries
                        do! drained ()

                        Expect.equal (List.ofSeq seen2) [ 1; 0; 2 ] "each message reaches update once"
                        Expect.equal app2.Model app.Model "the live run's model"
                        ignore g2
                    }

                    testCaseAsync "a replay records the entries of the run it replays"
                    <| async {
                        let! journal, _, _ = recorded ()

                        for mode in [ ReplayMode.Reenact; ReplayMode.Diagnostic ] do
                            let replayed, g2, _, _, gate2 = awaiting (journaled ())
                            Journal.replay mode g2 journal.Entries
                            gate2.SetResult ()
                            do! drained ()

                            Expect.equal
                                (replayed.Entries
                                 |> List.map (fun e -> e.Payload, e.Origin))
                                (journal.Entries
                                 |> List.map (fun e -> e.Payload, e.Origin))
                                $"the entries of the {mode} replay"
                    }
                ]

            testList
                "Journal.produce"
                [
                    // An effect over q writes q * 10 to out after an await on the gate.
                    let writer (journal: MemoryJournal, g: Graph) =
                        let gate = System.Threading.Tasks.TaskCompletionSource<unit>()
                        let q = Signal (g, 0) |> Journal.key "q" intCodec
                        let out = Signal (g, 0) |> Journal.key "out" intCodec
                        let written = ResizeArray<int>()

                        g.Run (fun () ->
                            createEffect (fun () ->
                                let v = q.Value

                                if v > 0 then
                                    let work = Journal.work g

                                    task {
                                        do! gate.Task

                                        Journal.produce work (fun () ->
                                            written.Add (v * 10)
                                            out.Value <- v * 10)
                                    }
                                    |> ignore))

                        journal, g, q, out, written, gate

                    let recorded () =
                        async {
                            let journal, _, q, out, _, gate = writer (journaled ())
                            q.Value <- 2
                            gate.SetResult ()
                            do! drained ()
                            Expect.equal out.Peek 20 "the live run's write"
                            return journal
                        }

                    testCaseAsync "a write in a continuation records a Produced entry"
                    <| async {
                        let! journal = recorded ()

                        Expect.equal
                            (writes journal
                             |> List.map (fun e -> e.Key, originName e.Origin))
                            [ Some "q", "External"; Some "out", "Produced" ]
                            "the writes' origins"
                    }

                    testCaseAsync "each replay mode applies a write in a continuation once"
                    <| async {
                        let! journal = recorded ()

                        for mode in [ ReplayMode.Reenact; ReplayMode.Diagnostic ] do
                            let replayed, g2, _, out2, written2, gate2 = writer (journaled ())
                            Journal.replay mode g2 journal.Entries
                            gate2.SetResult ()
                            do! drained ()

                            Expect.equal out2.Peek 20 $"the {mode} replay's value"
                            Expect.equal (List.ofSeq written2) [ 20 ] $"the {mode} replay's continuation runs"

                            Expect.equal
                                (writes replayed
                                 |> List.map (fun e -> e.Key, e.Origin))
                                (writes journal
                                 |> List.map (fun e -> e.Key, e.Origin))
                                $"the {mode} replay's writes"
                    }

                    testCaseAsync "an async memo's continuation write through Journal.produce reaches each replay once"
                    <| async {
                        // An async memo over a positive q writes q * 10 to out after an await on the gate, and returns q.
                        let build () =
                            let journal, g = journaled ()
                            let gate = System.Threading.Tasks.TaskCompletionSource<unit>()
                            let q = Signal (g, 0) |> Journal.key "q" intCodec
                            let out = Signal (g, 0) |> Journal.key "out" intCodec

                            let memo =
                                Make.AsyncMemo<int>(
                                    g,
                                    fun _ _ ->
                                        let v = q.Value
                                        let work = Journal.work g

                                        if v > 0 then
                                            task {
                                                do! gate.Task
                                                Journal.produce work (fun () -> out.Value <- v * 10)
                                                return v
                                            }
                                        else
                                            System.Threading.Tasks.Task.FromResult v
                                )

                            g.Run (fun () -> createEffect (fun () -> memo.TryValue |> ignore))
                            journal, g, q, out, gate

                        let journal, _, q, out, gate = build ()
                        q.Value <- 2
                        gate.SetResult ()
                        do! drained ()
                        Expect.equal out.Peek 20 "the live run's write"

                        for mode in [ ReplayMode.Reenact; ReplayMode.Diagnostic ] do
                            let replayed, g2, _, out2, gate2 = build ()
                            Journal.replay mode g2 journal.Entries
                            gate2.SetResult ()
                            do! drained ()

                            Expect.equal out2.Peek 20 $"the {mode} replay's value"

                            Expect.equal
                                (writes replayed
                                 |> List.map (fun e -> e.Key, e.Origin))
                                (writes journal
                                 |> List.map (fun e -> e.Key, e.Origin))
                                $"the {mode} replay's writes"

                            ignore g2
                    }

                    testCaseAsync "each replay mode applies a work's synchronous produce and its produce after an await once"
                    <| async {
                        // An effect over a positive q adds 100 to total through a work, and 1 after an await on the gate.
                        let build () =
                            let journal, g = journaled ()
                            let gate = System.Threading.Tasks.TaskCompletionSource<unit>()
                            let q = Signal (g, 0) |> Journal.key "q" intCodec
                            let total = Signal (g, 0) |> Journal.key "total" intCodec

                            g.Run (fun () ->
                                createEffect (fun () ->
                                    if q.Value > 0 then
                                        let work = Journal.work g
                                        Journal.produce work (fun () -> total.Value <- total.Peek + 100)

                                        task {
                                            do! gate.Task
                                            Journal.produce work (fun () -> total.Value <- total.Peek + 1)
                                        }
                                        |> ignore))

                            journal, g, q, total, gate

                        let journal, _, q, total, gate = build ()
                        q.Value <- 1
                        gate.SetResult ()
                        do! drained ()
                        Expect.equal total.Peek 101 "the live run's total"

                        for mode in [ ReplayMode.Reenact; ReplayMode.Diagnostic ] do
                            let _, g2, _, total2, gate2 = build ()
                            Journal.replay mode g2 journal.Entries
                            gate2.SetResult ()
                            do! drained ()

                            Expect.equal total2.Peek 101 $"the {mode} replay's total"
                            ignore g2
                    }

                    test "work captured with no journal runs its input" {
                        let g = new Graph (GraphOptions.Default)
                        let s = Signal (g, 0)
                        Journal.produce (Journal.work g) (fun () -> s.Value <- 3)
                        Expect.equal s.Peek 3 "the write"
                    }
                ]

            test "the entry unions print their case names" {
                Expect.equal ((JournalOrigin.Produced (3, 1, 2)).ToString()) "Produced (3, 1, 2)" "an origin"
                Expect.equal (JournalEntryKind.Settle.ToString ()) "Settle" "a kind"
                Expect.equal (JournalPayload.Empty.ToString ()) "Empty" "Empty"
                Expect.equal ((JournalPayload.Encoded ("int", 1, "3")).ToString()) "Encoded (int, 1, 3)" "Encoded"
                Expect.equal ((JournalPayload.NonReplayable "T").ToString()) "NonReplayable T" "NonReplayable"
            }
        ]
