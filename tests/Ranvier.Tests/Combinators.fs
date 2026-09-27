module Ranvier.Tests.Combinators

open System
open Expecto
open Ranvier

type private Item = { Id: int; N: int }

let private items (pairs: (int * int) list) =
    [ for id, n in pairs -> { Id = id; N = n } ]

let private setN id n (xs: Item list) =
    xs |> List.map (fun x -> if x.Id = id then { x with N = n } else x)

let private rows (source: Signal<Item list>) =
    createProjection (fun x -> x.Id) (fun x -> x.N) (fun () -> source.Value)

/// <summary>An effect reading <c>Keys</c> and then <c>Get</c> for each key, and the count of its runs.</summary>
let private keysAndRows (view: Projection<int, 'V>) =
    let runs = ref 0

    createEffect (fun () ->
        runs.Value <- runs.Value + 1

        for key in view.Keys do
            view.Get key |> ignore)

    runs

/// <summary>Writes <c>next</c> and expects exactly one more run in <c>runs</c>.</summary>
let private once (runs: int ref) (source: Signal<Item list>) (name: string) (next: Item list -> Item list) =
    let before = runs.Value
    source.Value <- next source.Value
    Expect.equal runs.Value (before + 1) name

[<Tests>]
let filterTests =
    testList "Projection.filter" [
        test "200 single edits at N=1000 make 200 predicate calls" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..1000 -> i, 2 * i ])
            let calls = ref 0

            let view =
                rows source
                |> Projection.filter (fun n ->
                    calls.Value <- calls.Value + 1
                    n % 4 = 0)

            keysAndRows view |> ignore
            calls.Value <- 0

            for i in 1..200 do
                source.Value <- setN i (2 * i + 2) source.Value

            Expect.equal calls.Value 200 "one predicate call per edit"
        }

        test "a Keys reader wakes only when membership flips" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 4; 2, 8; 3, 3 ])
            let view = rows source |> Projection.filter (fun n -> n % 4 = 0)
            let runs = ref 0

            createEffect (fun () ->
                view.Keys |> ignore
                runs.Value <- runs.Value + 1)

            source.Value <- setN 1 12 source.Value
            Expect.equal runs.Value 1 "an edit that keeps membership"

            source.Value <- setN 1 6 source.Value
            Expect.equal runs.Value 2 "an edit that flips membership"

            source.Value <- setN 3 16 source.Value
            Expect.equal runs.Value 3 "an edit that flips a key in"
            Expect.sequenceEqual view.Keys [ 2; 3 ] "the new membership"
        }

        test "an effect reading Keys and Get runs exactly once per write" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..5 -> i, i ])
            let view = rows source |> Projection.filter (fun n -> n % 2 = 0)
            let runs = keysAndRows view

            once runs source "edit a member's value" (setN 2 6)
            once runs source "flip a member out" (setN 4 5)
            once runs source "flip a key in" (setN 1 8)
            once runs source "remove a member" (List.filter (fun x -> x.Id <> 2))
            once runs source "add a member" (fun xs -> xs @ items [ 6, 10 ])
            once runs source "reorder" List.rev
            Expect.sequenceEqual view.Keys [ 6; 1 ] "the final membership, in upstream order"
        }

        test "an unread view leaves an unread upstream idle" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 2; 2, 3 ])
            let up = rows source
            let view = up |> Projection.filter (fun n -> n % 2 = 0)

            for i in 1..10 do
                source.Value <- setN 1 (2 * i) source.Value

            Expect.equal up.Runs 0 "no upstream pass"
            Expect.equal view.Runs 0 "no view pass"
        }

        test "a key removed and re-added while unread follows the new upstream row" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 2; 2, 4; 3, 5 ])
            let view = rows source |> Projection.filter (fun n -> n % 2 = 0)

            Expect.sequenceEqual view.Keys [ 1; 2 ] "initial membership"
            source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
            source.Value <- source.Value @ items [ 2, 6 ]
            Expect.sequenceEqual view.Keys [ 1; 2 ] "key 2 re-added even"

            source.Value <- setN 2 7 source.Value
            Expect.sequenceEqual view.Keys [ 1 ] "key 2 made odd"
        }

        test "a pending predicate keeps membership; a never-settled key is only in PendingKeys" {
            use g = new Graph ()
            use _ = g.Activate ()
            let flights = dict [ for n in [ 10; 11; 30 ] -> n, createAsyncSource<bool> () ]
            flights[10].Settle true
            let source = createSignal (items [ 1, 10; 3, 30 ])
            let view = rows source |> Projection.filter (fun n -> flights[n].Value)

            Expect.sequenceEqual view.Keys [ 1 ] "key 3 has never settled"
            Expect.sequenceEqual view.PendingKeys [ 3 ] "key 3 is pending"

            source.Value <- setN 1 11 source.Value
            Expect.sequenceEqual view.Keys [ 1 ] "key 1 keeps its last membership"

            flights[30].Settle true
            Expect.sequenceEqual view.Keys [ 1; 3 ] "key 3 joins once settled"
            Expect.isFalse (Array.contains 3 view.PendingKeys) "key 3 is no longer pending"

            flights[11].Settle false
            Expect.sequenceEqual view.Keys [ 3 ] "key 1 leaves once its predicate settles false"
        }

        test "a PendingKeys reader wakes when a never-settled key settles excluded" {
            use g = new Graph ()
            use _ = g.Activate ()
            let flight = createAsyncSource<bool> ()
            let source = createSignal (items [ 1, 1 ])
            let view = rows source |> Projection.filter (fun _ -> flight.Value)
            let seen = ResizeArray<int[]> ()

            createEffect (fun () -> seen.Add view.PendingKeys)
            flight.Settle false

            Expect.sequenceEqual seen [ [| 1 |]; [||] ] "pending, then settled"
            Expect.isEmpty view.Keys "the key settled excluded"
        }

        test "a throwing predicate excludes the key, and Get and TryGet raise its error" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 2; 2, 4 ])

            let view =
                rows source
                |> Projection.filter (fun n -> if n = 13 then invalidOp "boom 13" else n % 2 = 0)

            let keysRuns = ref 0
            let getRuns = ref 0

            createEffect (fun () ->
                view.Keys |> ignore
                keysRuns.Value <- keysRuns.Value + 1)

            createEffect (fun () ->
                getRuns.Value <- getRuns.Value + 1

                try
                    view.Get 2 |> ignore
                with _ ->
                    ())

            source.Value <- setN 2 13 source.Value

            Expect.sequenceEqual view.Keys [ 1 ] "key 2 is excluded"
            Expect.equal keysRuns.Value 2 "the Keys reader woke"
            Expect.equal getRuns.Value 2 "the Get reader woke"

            Expect.throwsC
                (fun () -> view.Get 2 |> ignore)
                (fun ex -> Expect.equal ex.Message "boom 13" "Get raises the predicate's error")

            Expect.throwsC
                (fun () -> view.TryGet 2 |> ignore)
                (fun ex -> Expect.equal ex.Message "boom 13" "TryGet raises the predicate's error")

            Expect.equal view.Status Status.None "the pass itself succeeded"

            source.Value <- setN 2 4 source.Value
            Expect.sequenceEqual view.Keys [ 1; 2 ] "key 2 returns once the predicate succeeds"
            Expect.equal (view.Get 2) 4 "and reads its value"
        }

        test "Get outside Keys raises KeyNotFoundException" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 2; 2, 3 ])
            let view = rows source |> Projection.filter (fun n -> n % 2 = 0)

            Expect.equal (view.Get 1) 2 "a member reads its value"

            Expect.throwsT<Collections.Generic.KeyNotFoundException>
                (fun () -> view.Get 2 |> ignore)
                "an excluded key"

            Expect.equal (view.TryGet 2) None "TryGet of an excluded key"
        }

        test "removing a key disposes the key's memo" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 10; 2, 20; 3, 30 ])
            let threshold = createSignal 0
            let calls = ResizeArray<int> ()

            let view =
                rows source
                |> Projection.filter (fun n ->
                    calls.Add n
                    n > threshold.Value)

            keysAndRows view |> ignore
            source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
            calls.Clear ()
            threshold.Value <- 15

            Expect.sequenceEqual (Seq.sort calls) [ 10; 30 ] "the removed key's predicate never runs again"
            Expect.sequenceEqual view.Keys [ 3 ] "the survivors re-evaluated"
        }

        test "disposing the view detaches it from the upstream" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 2; 2, 4 ])
            let calls = ref 0

            let view =
                rows source
                |> Projection.filter (fun n ->
                    calls.Value <- calls.Value + 1
                    n % 2 = 0)

            keysAndRows view |> ignore
            view.Dispose ()
            calls.Value <- 0
            source.Value <- setN 1 6 source.Value

            Expect.equal calls.Value 0 "no predicate runs after disposal"
            Expect.isEmpty view.Keys "a disposed view is empty"
        }
    ]

[<Tests>]
let chooseTests =
    let halves (n: int) = if n % 2 = 0 then Some (n / 2) else None

    testList "Projection.choose" [
        test "choose keeps the Some keys in upstream order with their values" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 4; 2, 3; 3, 8; 4, 6 ])
            let view = rows source |> Projection.choose halves

            Expect.sequenceEqual view.Keys [ 1; 3; 4 ] "the Some keys, in upstream order"
            Expect.sequenceEqual [ for key in view.Keys -> view.Get key ] [ 2; 4; 3 ] "the Some values"
            Expect.equal (view.TryGet 2) None "TryGet of a None key"

            Expect.throwsT<Collections.Generic.KeyNotFoundException>
                (fun () -> view.Get 2 |> ignore)
                "Get of a None key"
        }

        test "200 single edits at N=1000 make 200 chooser calls" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..1000 -> i, i ])
            let calls = ref 0

            let view =
                rows source
                |> Projection.choose (fun n ->
                    calls.Value <- calls.Value + 1
                    halves n)

            keysAndRows view |> ignore
            calls.Value <- 0

            for i in 1..200 do
                source.Value <- setN i (i + 1) source.Value

            Expect.equal calls.Value 200 "one chooser call per edit"
        }

        test "a Keys reader wakes only when membership flips, and a Get reader when the value changes" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 4; 2, 8; 3, 3 ])
            let view = rows source |> Projection.choose halves
            let keysRuns = ref 0
            let getRuns = ref 0

            createEffect (fun () ->
                view.Keys |> ignore
                keysRuns.Value <- keysRuns.Value + 1)

            createEffect (fun () ->
                view.TryGet 1 |> ignore
                getRuns.Value <- getRuns.Value + 1)

            let passes = view.Runs
            source.Value <- setN 1 12 source.Value
            Expect.equal view.Runs passes "a Some to Some edit runs no view pass"
            Expect.equal keysRuns.Value 1 "and keeps membership"
            Expect.equal getRuns.Value 2 "and wakes the key's reader"
            Expect.equal (view.Get 1) 6 "with the new value"

            source.Value <- setN 2 16 source.Value
            Expect.equal getRuns.Value 2 "another key's edit leaves the reader asleep"

            source.Value <- setN 1 5 source.Value
            Expect.equal keysRuns.Value 2 "a Some to None edit flips membership"

            source.Value <- setN 3 10 source.Value
            Expect.equal keysRuns.Value 3 "a None to Some edit flips a key in"
            Expect.sequenceEqual view.Keys [ 2; 3 ] "the new membership"
            Expect.equal (view.Get 3) 5 "the new member's value"
        }

        test "an effect reading Keys and Get runs exactly once per write" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..5 -> i, i ])
            let view = rows source |> Projection.choose halves
            let runs = keysAndRows view

            once runs source "edit a member's value" (setN 2 6)
            once runs source "flip a member out" (setN 4 5)
            once runs source "flip a key in" (setN 1 8)
            once runs source "remove a member" (List.filter (fun x -> x.Id <> 2))
            once runs source "add a member" (fun xs -> xs @ items [ 6, 10 ])
            once runs source "reorder" List.rev
            Expect.sequenceEqual view.Keys [ 6; 1 ] "the final membership, in upstream order"
            Expect.sequenceEqual [ view.Get 6; view.Get 1 ] [ 5; 4 ] "the final values"
        }

        test "a chooser reading a signal re-runs when the signal changes" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 10; 2, 20; 3, 30 ])
            let threshold = createSignal 15
            let calls = ref 0

            let view =
                rows source
                |> Projection.choose (fun n ->
                    calls.Value <- calls.Value + 1
                    if n > threshold.Value then Some (n - threshold.Value) else None)

            keysAndRows view |> ignore
            Expect.sequenceEqual view.Keys [ 2; 3 ] "initial membership"
            calls.Value <- 0
            threshold.Value <- 5

            Expect.equal calls.Value 3 "one chooser call per key"
            Expect.sequenceEqual view.Keys [ 1; 2; 3 ] "the new membership"
            Expect.sequenceEqual [ for key in view.Keys -> view.Get key ] [ 5; 15; 25 ] "the new values"
        }

        test "a pending chooser keeps membership; a never-settled key is only in PendingKeys" {
            use g = new Graph ()
            use _ = g.Activate ()
            let flights = dict [ for n in [ 10; 11; 12; 30 ] -> n, createAsyncSource<int option> () ]
            flights[10].Settle (Some 1)
            let source = createSignal (items [ 1, 10; 3, 30 ])
            let view = rows source |> Projection.choose (fun n -> flights[n].Value)

            Expect.sequenceEqual view.Keys [ 1 ] "key 3 has never settled"
            Expect.sequenceEqual view.PendingKeys [ 3 ] "key 3 is pending"
            Expect.isFalse view.AnyPending "AnyPending counts rows of keys in Keys"

            source.Value <- setN 1 11 source.Value
            Expect.sequenceEqual view.Keys [ 1 ] "key 1 keeps its last membership"
            Expect.throwsT<NotReadyException> (fun () -> view.Get 1 |> ignore) "key 1's row is pending"
            Expect.isTrue view.AnyPending "key 1's row counts as pending"

            flights[30].Settle (Some 3)
            Expect.sequenceEqual view.Keys [ 1; 3 ] "key 3 joins once settled"
            Expect.equal (view.Get 3) 3 "with its value"
            Expect.isFalse (Array.contains 3 view.PendingKeys) "key 3 is no longer pending"

            flights[11].Settle None
            Expect.sequenceEqual view.Keys [ 3 ] "key 1 leaves once its chooser settles None"

            source.Value <- setN 1 12 source.Value
            Expect.sequenceEqual view.Keys [ 3 ] "a pending chooser keeps key 1 out"
            Expect.isEmpty view.PendingKeys "a key kept out after settling is not pending"
        }

        test "a throwing chooser excludes the key, and Get and TryGet raise its error" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 2; 2, 4 ])

            let view =
                rows source
                |> Projection.choose (fun n -> if n = 13 then invalidOp "boom 13" else halves n)

            let keysRuns = ref 0

            createEffect (fun () ->
                view.Keys |> ignore
                keysRuns.Value <- keysRuns.Value + 1)

            source.Value <- setN 2 13 source.Value

            Expect.sequenceEqual view.Keys [ 1 ] "key 2 is excluded"
            Expect.equal keysRuns.Value 2 "the Keys reader woke"

            Expect.throwsC
                (fun () -> view.Get 2 |> ignore)
                (fun ex -> Expect.equal ex.Message "boom 13" "Get raises the chooser's error")

            Expect.throwsC
                (fun () -> view.TryGet 2 |> ignore)
                (fun ex -> Expect.equal ex.Message "boom 13" "TryGet raises the chooser's error")

            Expect.equal view.Status Status.None "the pass itself succeeded"

            source.Value <- setN 2 4 source.Value
            Expect.sequenceEqual view.Keys [ 1; 2 ] "key 2 returns once the chooser succeeds"
            Expect.equal (view.Get 2) 2 "and reads its value"
        }

        test "disposing the view detaches it from the upstream" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 2; 2, 4 ])
            let calls = ref 0

            let view =
                rows source
                |> Projection.choose (fun n ->
                    calls.Value <- calls.Value + 1
                    halves n)

            keysAndRows view |> ignore
            view.Dispose ()
            calls.Value <- 0
            source.Value <- setN 1 6 source.Value

            Expect.equal calls.Value 0 "no chooser runs after disposal"
            Expect.isEmpty view.Keys "a disposed view is empty"
        }

        test "choose composes with filter and sortBy" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 8; 2, 3; 3, 4; 4, 12; 5, 20 ])

            let view =
                rows source
                |> Projection.filter (fun n -> n < 20)
                |> Projection.choose halves
                |> Projection.sortBy (fun n -> -n)

            let runs = keysAndRows view
            Expect.sequenceEqual view.Keys [ 4; 1; 3 ] "filtered, chosen, then sorted descending"
            Expect.sequenceEqual [ for key in view.Keys -> view.Get key ] [ 6; 4; 2 ] "the chosen values"

            once runs source "flip a key in" (setN 2 18)
            Expect.sequenceEqual view.Keys [ 2; 4; 1; 3 ] "key 2 joins at its rank"
            once runs source "filter a key out" (setN 4 24)
            Expect.sequenceEqual view.Keys [ 2; 1; 3 ] "key 4 leaves"

            let chosenFirst =
                rows source
                |> Projection.choose halves
                |> Projection.filter (fun n -> n > 2)

            Expect.sequenceEqual chosenFirst.Keys [ 1; 2; 4; 5 ] "filter over the chosen values"
        }

        test "choose composes with choose, and keeps a Some None value" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 8; 2, 3; 3, 4 ])

            let nested =
                rows source
                |> Projection.choose (fun n -> Some (if n % 2 = 0 then Some n else None))

            Expect.sequenceEqual nested.Keys [ 1; 2; 3 ] "every key, Some None included"
            Expect.equal (nested.Get 2) None "the inner None is the value"

            let twice = nested |> Projection.choose id
            Expect.sequenceEqual twice.Keys [ 1; 3 ] "the second choose drops the inner None"

            source.Value <- items [ 1, 8; 2, 6; 3, 4 ]
            Expect.sequenceEqual twice.Keys [ 1; 2; 3 ] "key 2 joins once its value is even"
            Expect.equal (twice.Get 2) 6 "the unwrapped value"
        }
    ]

[<Tests>]
let mapTests =
    testList "Projection.map" [
        test "map follows upstream keys and order, and re-runs one mapping per edit" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])
            let calls = ref 0

            let view =
                rows source
                |> Projection.map (fun n ->
                    calls.Value <- calls.Value + 1
                    n * 10)

            keysAndRows view |> ignore
            calls.Value <- 0
            source.Value <- setN 2 5 source.Value

            Expect.equal calls.Value 1 "one mapping call"
            Expect.equal (view.Get 2) 50 "the new value"

            source.Value <- List.rev source.Value
            Expect.sequenceEqual view.Keys [ 3; 2; 1 ] "upstream order"
        }

        test "an effect reading Keys and Get runs exactly once per write" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..4 -> i, i ])
            let view = rows source |> Projection.map (fun n -> n + 1)
            let runs = keysAndRows view

            once runs source "edit a value" (setN 2 6)
            once runs source "remove a key" (List.filter (fun x -> x.Id <> 2))
            once runs source "add a key" (fun xs -> xs @ items [ 5, 5 ])
            once runs source "reorder" List.rev
        }

        test "a throwing mapping keeps the key, and Get raises its error" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 1; 2, 2 ])
            let view = rows source |> Projection.map (fun n -> if n = 13 then invalidOp "boom 13" else n)

            keysAndRows view |> ignore
            source.Value <- setN 2 13 source.Value

            Expect.sequenceEqual view.Keys [ 1; 2 ] "the key stays"

            Expect.throwsC
                (fun () -> view.Get 2 |> ignore)
                (fun ex -> Expect.equal ex.Message "boom 13" "Get raises the mapping's error")
        }

        test "removing a key disposes the key's memo" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 10; 2, 20 ])
            let offset = createSignal 0
            let calls = ResizeArray<int> ()

            let view =
                rows source
                |> Projection.map (fun n ->
                    calls.Add n
                    n + offset.Value)

            keysAndRows view |> ignore
            source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
            calls.Clear ()
            offset.Value <- 1

            Expect.sequenceEqual calls [ 10 ] "the removed key's mapping never runs again"
        }
    ]

let private comparisons = ref 0

/// <summary>A sort key whose comparisons are counted in <c>comparisons</c>.</summary>
[<CustomEquality; CustomComparison>]
type private Counted =
    | Counted of int

    override this.Equals other =
        match other with
        | :? Counted as o -> compare this o = 0
        | _ -> false

    override this.GetHashCode() = let (Counted n) = this in n

    interface IComparable with
        member this.CompareTo other =
            comparisons.Value <- comparisons.Value + 1
            let (Counted a) = this
            let (Counted b) = other :?> Counted
            compare a b

/// <summary>A sort key whose comparison raises when either operand is 666.</summary>
[<CustomEquality; CustomComparison>]
type private Boom =
    | Boom of int

    override this.Equals other =
        match other with
        | :? Boom as o -> compare this o = 0
        | _ -> false

    override this.GetHashCode() = let (Boom n) = this in n

    interface IComparable with
        member this.CompareTo other =
            let (Boom a) = this
            let (Boom b) = other :?> Boom

            if a = 666 || b = 666 then
                invalidOp "boom 666"

            compare a b

[<Tests>]
let sortByTests =
    testList "Projection.sortBy" [
        test "sorts ascending, and equal sort keys keep upstream order" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 5; 2, 3; 3, 5; 4, 1 ])
            let view = rows source |> Projection.sortBy id

            Expect.sequenceEqual view.Keys [ 4; 2; 1; 3 ] "ascending, ties in upstream order"
            Expect.equal (view.Get 3) 5 "a row reads its own value"

            source.Value <- List.rev source.Value
            Expect.sequenceEqual view.Keys [ 4; 2; 3; 1 ] "the tie follows the upstream reorder"

            source.Value <- setN 2 5 source.Value
            Expect.sequenceEqual view.Keys [ 4; 3; 2; 1 ] "a key joining a tie takes its upstream position"
        }

        test "ties keep upstream order at 40 keys, across reorder and removal" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..40 -> i, i % 3 ])
            let view = rows source |> Projection.sortBy id
            let expected () = source.Value |> List.sortBy (fun x -> x.N) |> List.map (fun x -> x.Id)

            Expect.sequenceEqual view.Keys (expected ()) "initial ties in upstream order"

            source.Value <- List.rev source.Value
            Expect.sequenceEqual view.Keys (expected ()) "ties follow the upstream reorder"

            source.Value <- source.Value |> List.filter (fun x -> x.Id % 7 <> 0)
            Expect.sequenceEqual view.Keys (expected ()) "ties keep upstream order after removals"
        }

        test "NaN sort keys sort after every other key, across updates" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, -1; 2, 3; 3, -1; 4, 1 ])
            let view = rows source |> Projection.sortBy (fun n -> if n < 0 then nan else float n)

            Expect.sequenceEqual view.Keys [ 4; 2; 1; 3 ] "NaN last, in upstream order"

            source.Value <- setN 4 -1 source.Value
            Expect.sequenceEqual view.Keys [ 2; 1; 3; 4 ] "a key turned NaN moves last"

            source.Value <- setN 1 0 source.Value
            Expect.sequenceEqual view.Keys [ 1; 2; 3; 4 ] "a key leaving NaN takes its sorted slot"

            source.Value <- List.rev source.Value
            Expect.sequenceEqual view.Keys [ 1; 2; 4; 3 ] "NaN ties follow the upstream reorder"
        }

        test "None sorts before Some, across updates" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 2; 2, -1; 3, 1; 4, -1 ])
            let view = rows source |> Projection.sortBy (fun n -> if n < 0 then None else Some n)

            Expect.sequenceEqual view.Keys [ 2; 4; 3; 1 ] "None first, in upstream order"

            source.Value <- setN 1 -1 source.Value
            Expect.sequenceEqual view.Keys [ 1; 2; 4; 3 ] "a key turned None moves first"

            source.Value <- setN 2 0 source.Value
            Expect.sequenceEqual view.Keys [ 1; 4; 2; 3 ] "a key leaving None takes its sorted slot"
        }

        test "200 single edits at N=1000 make 200 sort-key calls" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..1000 -> i, i ])
            let calls = ref 0

            let view =
                rows source
                |> Projection.sortBy (fun n ->
                    calls.Value <- calls.Value + 1
                    -n)

            keysAndRows view |> ignore
            calls.Value <- 0

            for i in 1..200 do
                source.Value <- setN i (i + 2000) source.Value

            Expect.equal calls.Value 200 "one sort-key call per edit"
            Expect.equal view.Keys[0] 200 "the last edit sorts first"
        }

        test "a Keys reader wakes only when the order changes" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 10; 2, 20; 3, 30 ])
            let view = rows source |> Projection.sortBy (fun n -> n / 10)
            let runs = ref 0

            createEffect (fun () ->
                view.Keys |> ignore
                runs.Value <- runs.Value + 1)

            source.Value <- setN 1 11 source.Value
            Expect.equal runs.Value 1 "an edit that keeps the sort key"

            source.Value <- setN 1 15 source.Value
            Expect.equal runs.Value 1 "an edit that keeps the order"

            source.Value <- setN 1 45 source.Value
            Expect.equal runs.Value 2 "an edit that moves a key"
            Expect.sequenceEqual view.Keys [ 2; 3; 1 ] "the new order"

            source.Value <- List.rev source.Value
            Expect.equal runs.Value 2 "an upstream reorder that keeps the sorted order"
        }

        test "a pass whose sort keys and upstream order are unchanged does not re-sort" {
            use g = new Graph ()
            use _ = g.Activate ()
            let flight = createAsyncSource<Counted> ()
            let source = createSignal (items [ for i in 1..1000 -> i, i ])

            let view =
                rows source
                |> Projection.sortBy (fun n -> if n < 0 then flight.Value else Counted n)

            keysAndRows view |> ignore
            comparisons.Value <- 0

            source.Value <- setN 1 -1 source.Value
            flight.Settle (Counted 1)

            Expect.isLessThan comparisons.Value 3000 "O(N) comparisons over two passes, no sort"
            Expect.equal view.Keys[0] 1 "key 1 keeps its slot"

            comparisons.Value <- 0
            source.Value <- setN 2 5000 source.Value
            Expect.isGreaterThan comparisons.Value 0 "a changed sort key re-sorts"
            Expect.equal (Array.last view.Keys) 2 "key 2 sorts last"
        }

        test "an effect reading Keys and Get runs exactly once per write" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..5 -> i, 10 * i ])
            let up = rows source
            let view = up |> Projection.sortBy (fun n -> -(n / 10))
            let runs = keysAndRows view

            // An edit in place that keeps the sort key runs no view pass.
            let passes viewPasses name next =
                let upRuns, viewRuns = up.Runs, view.Runs
                once runs source name next
                Expect.equal up.Runs (upRuns + 1) $"{name}: one upstream pass"
                Expect.equal view.Runs (viewRuns + viewPasses) $"{name}: view passes"

            passes 0 "edit a value in place" (setN 2 21)
            passes 1 "move a key" (setN 4 0)
            passes 1 "remove a key" (List.filter (fun x -> x.Id <> 2))
            passes 1 "add a key" (fun xs -> xs @ items [ 6, 30 ])
            passes 1 "reorder" List.rev
            Expect.sequenceEqual view.Keys [ 5; 6; 3; 1; 4 ] "the final order, the tie in upstream order"
        }

        test "an unread view leaves an unread upstream idle" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 2; 2, 3 ])
            let up = rows source
            let view = up |> Projection.sortBy id

            for i in 1..10 do
                source.Value <- setN 1 (2 * i) source.Value

            Expect.equal up.Runs 0 "no upstream pass"
            Expect.equal view.Runs 0 "no view pass"
        }

        test "a key removed and re-added while unread sorts by the new upstream row" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])
            let view = rows source |> Projection.sortBy id

            Expect.sequenceEqual view.Keys [ 1; 2; 3 ] "initial order"
            source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
            source.Value <- source.Value @ items [ 2, 0 ]
            Expect.sequenceEqual view.Keys [ 2; 1; 3 ] "key 2 re-added with a lower sort key"

            source.Value <- setN 2 9 source.Value
            Expect.sequenceEqual view.Keys [ 1; 3; 2 ] "key 2 follows its new row"
        }

        test "a pending sort key keeps its last settled sort key; a never-settled key is only in PendingKeys" {
            use g = new Graph ()
            use _ = g.Activate ()
            let flights = dict [ for n in [ 10; 11; 20; 30 ] -> n, createAsyncSource<int> () ]
            flights[10].Settle 3
            flights[20].Settle 1
            let source = createSignal (items [ 1, 10; 2, 20; 3, 30 ])
            let view = rows source |> Projection.sortBy (fun n -> flights[n].Value)

            Expect.sequenceEqual view.Keys [ 2; 1 ] "key 3 has never settled"
            Expect.sequenceEqual view.PendingKeys [ 3 ] "key 3 is pending"

            source.Value <- setN 1 11 source.Value
            Expect.sequenceEqual view.Keys [ 2; 1 ] "key 1 keeps its last settled sort key"

            flights[30].Settle 2
            Expect.sequenceEqual view.Keys [ 2; 3; 1 ] "key 3 joins once settled"
            Expect.isFalse (Array.contains 3 view.PendingKeys) "key 3 is no longer pending"

            flights[11].Settle 0
            Expect.sequenceEqual view.Keys [ 1; 2; 3 ] "key 1 moves once its sort key settles"
        }

        test "a PendingKeys reader wakes when a never-settled key settles" {
            use g = new Graph ()
            use _ = g.Activate ()
            let flight = createAsyncSource<int> ()
            let source = createSignal (items [ 1, 1 ])
            let view = rows source |> Projection.sortBy (fun _ -> flight.Value)
            let seen = ResizeArray<int[]> ()

            createEffect (fun () -> seen.Add view.PendingKeys)
            flight.Settle 0

            Expect.sequenceEqual seen [ [| 1 |]; [||] ] "pending, then settled"
            Expect.sequenceEqual view.Keys [ 1 ] "the key joined"
        }

        test "a throwing sort key excludes the key, and Get and TryGet raise its error" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 2; 2, 4 ])

            let view =
                rows source
                |> Projection.sortBy (fun n -> if n = 13 then invalidOp "boom 13" else -n)

            let keysRuns = ref 0
            let getRuns = ref 0

            createEffect (fun () ->
                view.Keys |> ignore
                keysRuns.Value <- keysRuns.Value + 1)

            createEffect (fun () ->
                getRuns.Value <- getRuns.Value + 1

                try
                    view.Get 2 |> ignore
                with _ ->
                    ())

            source.Value <- setN 2 13 source.Value

            Expect.sequenceEqual view.Keys [ 1 ] "key 2 is excluded"
            Expect.equal keysRuns.Value 2 "the Keys reader woke"
            Expect.equal getRuns.Value 2 "the Get reader woke"

            Expect.throwsC
                (fun () -> view.Get 2 |> ignore)
                (fun ex -> Expect.equal ex.Message "boom 13" "Get raises the sort key's error")

            Expect.throwsC
                (fun () -> view.TryGet 2 |> ignore)
                (fun ex -> Expect.equal ex.Message "boom 13" "TryGet raises the sort key's error")

            Expect.equal view.Status Status.None "the pass itself succeeded"

            source.Value <- setN 2 4 source.Value
            Expect.sequenceEqual view.Keys [ 2; 1 ] "key 2 returns once the sort key succeeds"
            Expect.equal (view.Get 2) 4 "and reads its value"
        }

        test "a pass after a failed comparison re-sorts" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..20 -> i, 21 - i ])
            let view = rows source |> Projection.sortBy Boom
            let before = source.Value

            Expect.sequenceEqual view.Keys [ 20..-1..1 ] "initial order"

            source.Value <- before |> setN 1 100 |> setN 12 666
            Expect.throws (fun () -> view.Keys |> ignore) "the comparison raises"

            source.Value <- before
            Expect.sequenceEqual view.Keys [ 20..-1..1 ] "the restored rows sort as before"
        }

        test "Get outside Keys raises KeyNotFoundException" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 2 ])
            let view = rows source |> Projection.sortBy id

            Expect.throwsT<Collections.Generic.KeyNotFoundException>
                (fun () -> view.Get 2 |> ignore)
                "an absent key"

            Expect.equal (view.TryGet 2) None "TryGet of an absent key"
        }

        test "removing a key disposes the key's memo" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 10; 2, 20; 3, 30 ])
            let offset = createSignal 0
            let calls = ResizeArray<int> ()

            let view =
                rows source
                |> Projection.sortBy (fun n ->
                    calls.Add n
                    n + offset.Value)

            keysAndRows view |> ignore
            source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
            calls.Clear ()
            offset.Value <- 1

            Expect.sequenceEqual (Seq.sort calls) [ 10; 30 ] "the removed key's sort key never runs again"
        }

        test "disposing the view detaches it from the upstream" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 2; 2, 4 ])
            let calls = ref 0

            let view =
                rows source
                |> Projection.sortBy (fun n ->
                    calls.Value <- calls.Value + 1
                    n)

            keysAndRows view |> ignore
            view.Dispose ()
            calls.Value <- 0
            source.Value <- setN 1 6 source.Value

            Expect.equal calls.Value 0 "no sort-key runs after disposal"
            Expect.isEmpty view.Keys "a disposed view is empty"
        }
    ]

[<Tests>]
let sliceTests =
    testList "Projection.take, skip and sub" [
        test "take, skip and sub select positions and clamp counts as List.truncate and a clamped skip do" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..5 -> i, 10 * i ])
            let up = rows source
            let keysOf (view: Projection<int, int>) = List.ofArray view.Keys

            Expect.equal (keysOf (up |> Projection.take (fun () -> 2))) [ 1; 2 ] "take 2"
            Expect.equal (keysOf (up |> Projection.take (fun () -> 9))) [ 1; 2; 3; 4; 5 ] "take past the end"
            Expect.equal (keysOf (up |> Projection.take (fun () -> -1))) [] "take a negative count"
            Expect.equal (keysOf (up |> Projection.skip (fun () -> 3))) [ 4; 5 ] "skip 3"
            Expect.equal (keysOf (up |> Projection.skip (fun () -> 9))) [] "skip past the end"
            Expect.equal (keysOf (up |> Projection.skip (fun () -> -2))) [ 1; 2; 3; 4; 5 ] "skip a negative count"
            Expect.equal (keysOf (up |> Projection.sub (fun () -> 1) (fun () -> 3))) [ 2; 3; 4 ] "sub 1 3"
            Expect.equal (keysOf (up |> Projection.sub (fun () -> 4) (fun () -> 3))) [ 5 ] "sub past the end"
            Expect.equal (keysOf (up |> Projection.sub (fun () -> -1) (fun () -> 2))) [ 1; 2 ] "sub at a negative offset"
            Expect.equal (keysOf (up |> Projection.sub (fun () -> 2) (fun () -> -1))) [] "sub of a negative count"

            Expect.equal
                (keysOf (up |> Projection.sub (fun () -> 1) (fun () -> Int32.MaxValue)))
                [ 2; 3; 4; 5 ]
                "sub of Int32.MaxValue"

            Expect.equal ((up |> Projection.sub (fun () -> 1) (fun () -> 3)).Get 3) 30 "a row reads the upstream value"

            Expect.throwsT<Collections.Generic.KeyNotFoundException>
                (fun () -> (up |> Projection.take (fun () -> 2)).Get 3 |> ignore)
                "Get outside the window"
        }

        test "the window follows signals read by offset and count, and a reader runs once per change" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..6 -> i, i ])
            let offset = createSignal 0
            let size = createSignal 2
            let view = rows source |> Projection.sub (fun () -> offset.Value) (fun () -> size.Value)
            let runs = keysAndRows view

            size.Value <- 3
            Expect.equal runs.Value 2 "a larger count"
            Expect.sequenceEqual view.Keys [ 1; 2; 3 ] "three keys"

            offset.Value <- 2
            Expect.equal runs.Value 3 "a shifted offset"
            Expect.sequenceEqual view.Keys [ 3; 4; 5 ] "the shifted window"

            size.Value <- 3
            Expect.equal runs.Value 3 "an unchanged count"
        }

        test "a key that stays in a shifted window keeps its row" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..6 -> i, i ])
            let offset = createSignal 0
            let view = rows source |> Projection.sub (fun () -> offset.Value) (fun () -> 3)
            let wakes = Array.zeroCreate<int> 7

            for key in 1..3 do
                createEffect (fun () ->
                    wakes[key] <- wakes[key] + 1

                    try
                        view.Get key |> ignore
                    with _ ->
                        ())

            offset.Value <- 1
            Expect.sequenceEqual view.Keys [ 2; 3; 4 ] "the shifted window"
            Expect.equal wakes[1] 2 "the reader of the key that left wakes"
            Expect.equal wakes[2] 1 "the reader of a surviving key stays asleep"
            Expect.equal wakes[3] 1 "the reader of a surviving key stays asleep"
        }

        test "membership and order changes upstream move keys through the window" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..5 -> i, i ])
            let view = rows source |> Projection.take (fun () -> 3)
            let keysRuns = ref 0

            createEffect (fun () ->
                view.Keys |> ignore
                keysRuns.Value <- keysRuns.Value + 1)

            source.Value <- setN 2 20 source.Value
            Expect.equal keysRuns.Value 1 "a value edit leaves a Keys reader asleep"
            Expect.equal (view.Get 2) 20 "and reaches the row"

            source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
            Expect.sequenceEqual view.Keys [ 1; 3; 4 ] "the next key slides in"

            source.Value <- items [ 0, 0 ] @ source.Value
            Expect.sequenceEqual view.Keys [ 0; 1; 3 ] "a key added in front pushes the last out"

            source.Value <- List.rev source.Value
            Expect.sequenceEqual view.Keys [ 5; 4; 3 ] "upstream order"

            source.Value <- source.Value |> List.filter (fun x -> x.Id = 5)
            Expect.sequenceEqual view.Keys [ 5 ] "fewer keys than the count"
            Expect.equal keysRuns.Value 5 "one Keys run per membership change"
        }

        test "a pending row keeps its key, and a key held out upstream is in PendingKeys" {
            use g = new Graph ()
            use _ = g.Activate ()
            let flights = dict [ for k in 1..3 -> k, createAsyncSource<int> () ]
            flights[1].Settle 1
            let up = createProjection id (fun k -> flights[k].Value) (fun () -> [ 1; 2; 3 ])
            let view = up |> Projection.take (fun () -> 2)
            keysAndRows view |> ignore

            Expect.sequenceEqual view.Keys [ 1; 2 ] "a pending row keeps its position"
            Expect.isTrue view.AnyPending "key 2's row is pending"
            Expect.sequenceEqual view.PendingKeys [ 2 ] "key 3 is outside the window"

            flights[2].Settle 2
            Expect.isFalse view.AnyPending "settled"
            Expect.equal (view.Get 2) 2 "the settled value"

            let flight = createAsyncSource<bool> ()

            let filtered =
                createProjection id id (fun () -> [ 1; 2; 3 ])
                |> Projection.filter (fun n -> n <> 2 || flight.Value)

            let sliced = filtered |> Projection.take (fun () -> 5)

            Expect.sequenceEqual sliced.Keys [ 1; 3 ] "key 2 is held out upstream"
            Expect.sequenceEqual sliced.PendingKeys [ 2 ] "and pending in the slice"
            Expect.isFalse sliced.AnyPending "AnyPending counts rows of keys in Keys"

            flight.Settle true
            Expect.sequenceEqual sliced.Keys [ 1; 2; 3 ] "key 2 joins once settled"
            Expect.isEmpty sliced.PendingKeys "and leaves PendingKeys"
        }

        test "a throwing count fails the pass, and a failed upstream row stays in the window" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])
            let size = createSignal 2
            let up = rows source |> Projection.map (fun n -> if n = 13 then invalidOp "boom 13" else n)

            let view =
                up
                |> Projection.take (fun () -> if size.Value < 0 then invalidOp "bad count" else size.Value)

            keysAndRows view |> ignore
            size.Value <- -1
            Expect.equal view.Status Status.Error "the pass failed"

            Expect.throwsC
                (fun () -> view.Keys |> ignore)
                (fun ex -> Expect.equal ex.Message "bad count" "Keys raises the count's error")

            size.Value <- 2
            Expect.sequenceEqual view.Keys [ 1; 2 ] "the recovered window"
            Expect.equal view.Status Status.None "the pass recovered"

            source.Value <- setN 2 13 source.Value
            Expect.sequenceEqual view.Keys [ 1; 2 ] "the failed row keeps its key"

            Expect.throwsC
                (fun () -> view.Get 2 |> ignore)
                (fun ex -> Expect.equal ex.Message "boom 13" "Get raises the upstream error")
        }

        test "disposing the view detaches it from the upstream and its count" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])
            let size = createSignal 2
            let calls = ref 0

            let view =
                rows source
                |> Projection.take (fun () ->
                    calls.Value <- calls.Value + 1
                    size.Value)

            keysAndRows view |> ignore
            view.Dispose ()
            calls.Value <- 0
            size.Value <- 3
            source.Value <- List.rev source.Value

            Expect.equal calls.Value 0 "count never runs after disposal"
            Expect.isEmpty view.Keys "a disposed view is empty"
        }

        test "a write outside the window wakes no reader" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..6 -> i, i ])
            let view = rows source |> Projection.sub (fun () -> 1) (fun () -> 2)
            let runs = keysAndRows view

            source.Value <- setN 5 50 source.Value
            source.Value <- source.Value @ items [ 7, 7 ]
            Expect.equal runs.Value 1 "an edit and an append past the window"
            Expect.sequenceEqual view.Keys [ 2; 3 ] "the window is unchanged"

            source.Value <- setN 3 30 source.Value
            Expect.equal runs.Value 2 "an edit inside the window"
            Expect.equal (view.Get 3) 30 "the edited value"
        }

        test "take composes with filter and sortBy" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..8 -> i, (i * 5) % 8 ])
            let size = createSignal 3

            let view =
                rows source
                |> Projection.filter (fun n -> n % 2 = 1)
                |> Projection.sortBy id
                |> Projection.take (fun () -> size.Value)

            Expect.sequenceEqual [ for k in view.Keys -> view.Get k ] [ 1; 3; 5 ] "the three smallest odd values"

            size.Value <- 1
            Expect.sequenceEqual [ for k in view.Keys -> view.Get k ] [ 1 ] "the smallest odd value"

            let over =
                rows source
                |> Projection.take (fun () -> 4)
                |> Projection.filter (fun n -> n % 2 = 0)

            Expect.sequenceEqual [ for k in over.Keys -> over.Get k ] [ 2; 4 ] "even values among the first four keys"
        }
    ]

[<Tests>]
let chainTests =
    let chains: (string * (Projection<int, int> -> Projection<int, int>)) list = [
        "filter then map", Projection.filter (fun n -> n % 2 = 0) >> Projection.map (fun n -> n * 3)
        "map then map", Projection.map (fun n -> n + 1) >> Projection.map (fun n -> n * 3)
        "map three times", Projection.map (fun n -> n + 1) >> Projection.map (fun n -> n * 3) >> Projection.map id
        "sortBy", Projection.sortBy (fun n -> n / 4)
        "filter, sortBy, then map",
        Projection.filter (fun n -> n % 2 = 0) >> Projection.sortBy (fun n -> n / 4) >> Projection.map (fun n -> n * 3)
        "sortBy then filter", Projection.sortBy (fun n -> n / 4) >> Projection.filter (fun n -> n % 2 = 0)
        "filter then mapWith", Projection.filter (fun n -> n % 2 = 0) >> Projection.mapWith (fun _ v -> fun () -> v () + 1)
        "choose then sortBy",
        Projection.choose (fun n -> if n % 2 = 0 then Some (n + 1) else None) >> Projection.sortBy (fun n -> n / 4)
        "sortBy then take", Projection.sortBy (fun n -> n / 4) >> Projection.take (fun () -> 10)
        "sub then map", Projection.sub (fun () -> 0) (fun () -> 10) >> Projection.map (fun n -> n * 3)
    ]

    testList "chained views" [
        for name, chain in chains ->
            test $"an effect reading Keys and Get of {name} runs exactly once per write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..6 -> i, 2 * i ])
                let view = rows source |> chain
                let runs = keysAndRows view

                once runs source "remove the first key" (List.filter (fun x -> x.Id <> 1))
                once runs source "remove a middle key" (List.filter (fun x -> x.Id <> 4))
                once runs source "edit a value" (setN 2 8)
                once runs source "flip a key's parity" (setN 3 7)
                once runs source "add a key" (fun xs -> xs @ items [ 7, 14 ])
                once runs source "reorder" List.rev
            }
    ]

[<Tests>]
let mapWithTests =
    testList "Projection.mapWith" [
        test "mapping runs once per key and its reader re-runs on an edit" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])
            let factories = ref 0
            let readers = ref 0

            let view =
                rows source
                |> Projection.mapWith (fun key value ->
                    factories.Value <- factories.Value + 1

                    fun () ->
                        readers.Value <- readers.Value + 1
                        key * 100 + value ())

            keysAndRows view |> ignore
            Expect.equal factories.Value 3 "one factory call per key"
            readers.Value <- 0
            source.Value <- setN 2 5 source.Value

            Expect.equal factories.Value 3 "an edit runs no factory"
            Expect.equal readers.Value 1 "an edit re-runs one reader"
            Expect.equal (view.Get 2) 205 "the new value"

            source.Value <- List.rev source.Value
            Expect.sequenceEqual view.Keys [ 3; 2; 1 ] "upstream order"
        }

        test "a node the mapping creates is disposed with its key" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 1; 2, 2 ])
            let disposed = ResizeArray<int> ()

            let view =
                rows source
                |> Projection.mapWith (fun key value ->
                    let doubled = createMemo (fun () -> value () * 2)
                    onCleanup (fun () -> disposed.Add key)
                    fun () -> doubled.Value)

            keysAndRows view |> ignore
            Expect.equal (view.Get 2) 4 "the memo the mapping created"

            source.Value <- List.filter (fun x -> x.Id <> 2) source.Value
            Expect.sequenceEqual disposed [ 2 ] "the removed key's scope"
        }

        test "an effect reading Keys and Get runs exactly once per write" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..4 -> i, i ])
            let view = rows source |> Projection.mapWith (fun _ value -> fun () -> value () + 1)
            let runs = keysAndRows view

            once runs source "edit a value" (setN 2 9)
            once runs source "remove a key" (List.filter (fun x -> x.Id <> 3))
            once runs source "add a key" (fun xs -> xs @ items [ 5, 5 ])
            once runs source "reorder" List.rev
        }
    ]

/// <summary>The keys of each group of <c>view</c>, in group order.</summary>
let private groupsOf (view: Projection<'G, Projection<int, int>>) =
    [ for key in view.Keys -> key, List.ofArray (view.Get key).Keys ]

[<Tests>]
let groupByTests =
    testList "Projection.groupBy" [
        test "groups follow each group's first member, and members keep upstream order" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 3; 2, 2; 3, 5; 4, 4; 5, 1 ])
            let view = rows source |> Projection.groupBy (fun n -> n % 2)

            Expect.equal (groupsOf view) [ 1, [ 1; 3; 5 ]; 0, [ 2; 4 ] ] "odd first"
            Expect.equal ((view.Get 0).Get 4) 4 "an inner view reads the upstream value"

            source.Value <- List.rev source.Value
            Expect.equal (groupsOf view) [ 1, [ 5; 3; 1 ]; 0, [ 4; 2 ] ] "after a reorder"

            source.Value <- setN 5 2 source.Value
            Expect.equal (groupsOf view) [ 0, [ 5; 4; 2 ]; 1, [ 3; 1 ] ] "a group's first member moved"
        }

        test "moving a key between groups publishes the outer Keys once and both inner views are current" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 1; 2, 2; 3, 3; 4, 4 ])
            let view = rows source |> Projection.groupBy (fun n -> n % 2)
            let seen = ResizeArray ()
            let outer = ref 0

            createEffect (fun () ->
                view.Keys |> ignore
                outer.Value <- outer.Value + 1)

            createEffect (fun () -> seen.Add (groupsOf view))
            seen.Clear ()
            source.Value <- setN 3 6 source.Value

            Expect.equal outer.Value 1 "the group order is unchanged"
            Expect.equal (List.ofSeq seen) [ [ 1, [ 1 ]; 0, [ 2; 3; 4 ] ] ] "one run, both groups current"

            source.Value <- setN 1 8 source.Value
            Expect.equal outer.Value 2 "the emptied group leaves the outer Keys"
            Expect.equal (List.last (List.ofSeq seen)) [ 0, [ 1; 2; 3; 4 ] ] "one group"
        }

        test "an edit that keeps a key's group wakes no Keys reader" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 1; 2, 2 ])
            let view = rows source |> Projection.groupBy (fun n -> n % 2)
            let inner = view.Get 0
            let runs = ref 0

            createEffect (fun () ->
                view.Keys |> ignore
                inner.Keys |> ignore
                runs.Value <- runs.Value + 1)

            source.Value <- setN 2 4 source.Value
            Expect.equal runs.Value 1 "no Keys changed"
            Expect.equal (inner.Get 2) 4 "the inner row is current"
        }

        test "a held inner view of an emptied group is disposed" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 1; 2, 2 ])
            let view = rows source |> Projection.groupBy (fun n -> n % 2)
            let odd = view.Get 1
            let keys = ResizeArray ()
            createEffect (fun () -> keys.Add (List.ofArray odd.Keys))

            source.Value <- setN 1 4 source.Value

            Expect.equal (List.ofSeq keys) [ [ 1 ]; [] ] "a Keys reader wakes to the empty key set"
            Expect.isEmpty odd.Keys "empty Keys"
            Expect.isFalse odd.AnyPending "nothing pending"
            Expect.throwsT<ObjectDisposedException> (fun () -> odd.Get 1 |> ignore) "Get raises"
            Expect.isNone (odd.TryGet 1) "TryGet reports absent"

            source.Value <- setN 1 5 source.Value
            Expect.isFalse (obj.ReferenceEquals (view.Get 1, odd)) "a group that returns has a new inner view"
            Expect.sequenceEqual (view.Get 1).Keys [ 1 ] "with its members"
        }

        test "a reader of an inner view alone follows upstream edits and runs once per write" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 1; 2, 2; 3, 3; 4, 4 ])
            let view = rows source |> Projection.groupBy (fun n -> n % 2)
            let even = view.Get 0
            let runs = keysAndRows even

            once runs source "remove a member" (List.filter (fun x -> x.Id <> 2))
            Expect.sequenceEqual even.Keys [ 4 ] "the removed member left"
            once runs source "move a key in" (setN 1 6)
            Expect.sequenceEqual even.Keys [ 1; 4 ] "the moved key joined"
            once runs source "edit a member" (setN 4 8)
            Expect.equal (even.Get 4) 8 "the edited row"
        }

        test "an effect reading every group and row runs exactly once per write" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ for i in 1..6 -> i, i ])
            let view = rows source |> Projection.groupBy (fun n -> n % 3)
            let runs = ref 0

            createEffect (fun () ->
                runs.Value <- runs.Value + 1

                for key in view.Keys do
                    let inner = view.Get key

                    for member' in inner.Keys do
                        inner.Get member' |> ignore)

            once runs source "edit a value within its group" (setN 2 5)
            once runs source "move a key" (setN 1 3)
            once runs source "empty a group" (List.filter (fun x -> x.N % 3 <> 2))
            once runs source "add a key" (fun xs -> xs @ items [ 7, 7 ])
            once runs source "reorder" List.rev
        }

        test "a pending group key keeps the key's group; a never-settled key is in no group" {
            use g = new Graph ()
            use _ = g.Activate ()
            let flights = dict [ for n in [ 10; 11; 30 ] -> n, createAsyncSource<int> () ]
            flights[10].Settle 1
            let source = createSignal (items [ 1, 10; 2, 30 ])
            let view = rows source |> Projection.groupBy (fun n -> flights[n].Value)

            Expect.equal (groupsOf view) [ 1, [ 1 ] ] "key 2 has never settled"

            source.Value <- setN 1 11 source.Value
            Expect.equal (groupsOf view) [ 1, [ 1 ] ] "key 1 keeps its group while pending"

            flights[30].Settle 1
            flights[11].Settle 2
            Expect.equal (groupsOf view) [ 2, [ 1 ]; 1, [ 2 ] ] "both settled, key 1's group first"
        }

        test "a throwing group key leaves the key in no group" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 1; 2, 2 ])

            let view =
                rows source
                |> Projection.groupBy (fun n -> if n < 0 then failwith "boom" else n % 2)

            Expect.equal (groupsOf view) [ 1, [ 1 ]; 0, [ 2 ] ] "precondition"
            source.Value <- setN 1 -1 source.Value
            Expect.equal (groupsOf view) [ 0, [ 2 ] ] "key 1 is in no group"
            Expect.equal view.Status Status.None "the pass succeeds"
        }

        test "disposing the view disposes its inner views" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 1; 2, 2 ])
            let view = rows source |> Projection.groupBy (fun n -> n % 2)
            let odd = view.Get 1

            view.Dispose ()
            Expect.isEmpty view.Keys "the outer view is empty"
            Expect.isEmpty odd.Keys "the inner view is empty"
            Expect.throwsT<ObjectDisposedException> (fun () -> odd.Get 1 |> ignore) "and raises on Get"
        }

        test "a never-settled key is in UngroupedKeys, in upstream order, until it settles and joins its group" {
            use g = new Graph ()
            use _ = g.Activate ()
            let flights = dict [ for n in [ 10; 30; 31 ] -> n, createAsyncSource<int> () ]
            flights[10].Settle 1
            let source = createSignal (items [ 1, 10; 2, 30; 3, 31 ])
            let view: Grouping<int, int, int> = rows source |> Projection.groupBy (fun n -> flights[n].Value)

            Expect.sequenceEqual view.UngroupedKeys [ 2; 3 ] "keys 2 and 3 have never settled"
            source.Value <- List.rev source.Value
            Expect.sequenceEqual view.UngroupedKeys [ 3; 2 ] "after a reorder"

            flights[30].Settle 0
            Expect.sequenceEqual view.UngroupedKeys [ 3 ] "key 2 left"
            Expect.equal (groupsOf view) [ 0, [ 2 ]; 1, [ 1 ] ] "and joined its group"

            flights[31].Settle 1
            Expect.isEmpty view.UngroupedKeys "every key is grouped"
            Expect.equal (groupsOf view) [ 1, [ 3; 1 ]; 0, [ 2 ] ] "key 3 joined its group"
        }

        test "a raising key is in UngroupedKeys and GroupOf raises its error until it recovers" {
            use g = new Graph ()
            use _ = g.Activate ()
            let source = createSignal (items [ 1, 1; 2, 2 ])

            let view =
                rows source
                |> Projection.groupBy (fun n -> if n < 0 then raise (TimeoutException "boom") else n % 2)

            Expect.isEmpty view.UngroupedKeys "precondition"
            source.Value <- setN 1 -1 source.Value

            Expect.sequenceEqual view.UngroupedKeys [ 1 ] "the raising key is ungrouped"
            Expect.throwsT<TimeoutException> (fun () -> view.GroupOf 1 |> ignore) "GroupOf raises the key's error, not its last group"
            Expect.equal view.Status Status.None "the pass succeeds"

            source.Value <- setN 1 3 source.Value
            Expect.isEmpty view.UngroupedKeys "the recovered key left"
            Expect.equal (view.GroupOf 1) 1 "GroupOf returns its group"
            Expect.equal (groupsOf view) [ 1, [ 1 ]; 0, [ 2 ] ] "and it is in its group"
        }

        test "a reader of UngroupedKeys runs once per settle" {
            use g = new Graph ()
            use _ = g.Activate ()
            let flights = dict [ for n in [ 30; 31 ] -> n, createAsyncSource<int> () ]
            let source = createSignal (items [ 1, 1; 2, 30; 3, 31 ])

            let view =
                rows source
                |> Projection.groupBy (fun n -> if n >= 30 then flights[n].Value else n % 2)

            let seen = ResizeArray<int list> ()
            createEffect (fun () -> seen.Add (List.ofArray view.UngroupedKeys))

            Expect.equal (List.ofSeq seen) [ [ 2; 3 ] ] "precondition"
            flights[30].Settle 0
            Expect.equal (List.ofSeq seen) [ [ 2; 3 ]; [ 3 ] ] "one run for the first settle"
            flights[31].Settle 1
            Expect.equal (List.ofSeq seen) [ [ 2; 3 ]; [ 3 ]; [] ] "one run for the second settle"
        }

        test "a reader of UngroupedKeys wakes on a settle after a pass that leaves it unchanged" {
            use g = new Graph ()
            use _ = g.Activate ()
            let flight = createAsyncSource<int> ()
            let source = createSignal (items [ 1, 1; 2, 30 ])

            let view =
                rows source
                |> Projection.groupBy (fun n -> if n >= 30 then flight.Value else n % 2)

            let seen = ResizeArray<int list> ()
            createEffect (fun () -> seen.Add (List.ofArray view.UngroupedKeys))

            source.Value <- setN 1 2 source.Value
            Expect.equal (List.ofSeq seen) [ [ 2 ] ] "the move leaves the list unchanged"
            flight.Settle 0
            Expect.equal (List.ofSeq seen) [ [ 2 ]; [] ] "the settle wakes the reader"
        }

        test "filter then groupBy lists the keys the filter holds out in UngroupedKeys" {
            use g = new Graph ()
            use _ = g.Activate ()
            let flights = dict [ for k in 1..3 -> k, createAsyncSource<int> () ]
            let gate = createAsyncSource<int> ()
            flights[1].Settle 2
            flights[3].Settle 7
            let rows = createProjection id (fun k -> flights[k].Value) (fun () -> [ 1; 2; 3 ])

            let view =
                rows
                |> Projection.filter (fun n -> n > 0)
                |> Projection.groupBy (fun n -> if n = 7 then gate.Value else n % 2)

            Expect.sequenceEqual view.UngroupedKeys [ 3; 2 ] "the never-settled key, then the key the filter holds out"
            Expect.throwsT<Collections.Generic.KeyNotFoundException>
                (fun () -> view.GroupOf 2 |> ignore)
                "a key the filter holds out is absent, as in the filter's Get"
            Expect.equal (groupsOf view) [ 0, [ 1 ] ] "the settled key only"

            gate.Settle 1
            Expect.sequenceEqual view.UngroupedKeys [ 2 ] "the key the filter holds out"

            flights[2].Settle 5
            Expect.isEmpty view.UngroupedKeys "the held-out key settled"
            Expect.equal (groupsOf view) [ 0, [ 1 ]; 1, [ 2; 3 ] ] "and joined its group"
        }

        test "GroupOf returns a grouped key's group, and a pending key's last settled group" {
            use g = new Graph ()
            use _ = g.Activate ()
            let flights = dict [ for n in [ 10; 11; 12 ] -> n, createAsyncSource<int> () ]
            flights[10].Settle 1
            let source = createSignal (items [ 1, 10; 2, 2; 3, 12 ])

            let view =
                rows source
                |> Projection.groupBy (fun n -> if n >= 10 then flights[n].Value else n % 2)

            Expect.equal (view.GroupOf 1) 1 "key 1's group"
            Expect.equal (view.GroupOf 2) 0 "key 2's group"
            Expect.throwsT<NotReadyException> (fun () -> view.GroupOf 3 |> ignore) "key 3 has never settled"

            source.Value <- setN 1 11 source.Value
            Expect.equal (view.GroupOf 1) 1 "a pending key keeps its last group"
            Expect.sequenceEqual view.UngroupedKeys [ 3 ] "and is not ungrouped"

            flights[11].Settle 2
            Expect.equal (view.GroupOf 1) 2 "the settled group"
        }
    ]

/// <summary>
/// A projection of keys 1 and 2 whose rows await one flight each, with key 1 settled to 2 and key 2 in flight.
/// </summary>
let private halfSettled () =
    let flights = dict [ 1, createAsyncSource<int> (); 2, createAsyncSource<int> () ]
    flights[1].Settle 2
    let rows = createProjection id (fun k -> flights[k].Value) (fun () -> [ 1; 2 ])
    flights, rows

[<Tests>]
let chainPendingTests =
    let chains: (string * (Projection<int, int> -> Projection<int, int>) * (Projection<int, int> -> Projection<int, int>)) list = [
        "filter then map", Projection.filter (fun n -> n > 0), Projection.map (fun n -> n * 10)
        "sortBy then map", Projection.sortBy (fun n -> -n), Projection.map (fun n -> n * 10)
        "filter then filter", Projection.filter (fun n -> n > 0), Projection.filter (fun n -> n < 100)
        "filter then sortBy", Projection.filter (fun n -> n > 0), Projection.sortBy (fun n -> -n)
        "filter, map, then map", Projection.filter (fun n -> n > 0), Projection.map (fun n -> n * 10) >> Projection.map id
        "filter then mapWith", Projection.filter (fun n -> n > 0), Projection.mapWith (fun _ v -> fun () -> v () * 10)
        "choose then map", Projection.choose (fun n -> if n > 0 then Some n else None), Projection.map (fun n -> n * 10)
        "filter then choose", Projection.filter (fun n -> n > 0), Projection.choose (fun n -> Some (n * 10))
        "filter then take", Projection.filter (fun n -> n > 0), Projection.take (fun () -> 5)
        "filter, skip, then map", Projection.filter (fun n -> n > 0), Projection.skip (fun () -> 0) >> Projection.map (fun n -> n * 10)
    ]

    testList "chained pending" [
        for name, first, rest in chains do
            test $"the end of {name} lists a never-settled upstream key in PendingKeys, and not in AnyPending" {
                use g = new Graph ()
                use _ = g.Activate ()
                let _, rows = halfSettled ()
                let view = rows |> first |> rest

                Expect.sequenceEqual view.Keys [ 1 ] "the settled key only"
                Expect.sequenceEqual view.PendingKeys [ 2 ] "the key an upstream stage holds out of Keys"
                Expect.isFalse view.AnyPending "AnyPending counts rows of keys in Keys"
            }

            test $"an effect reading PendingKeys and Keys at the end of {name} runs once when the upstream key settles" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights, rows = halfSettled ()
                let view = rows |> first |> rest
                let seen = ResizeArray<int list * (int * int) list> ()

                createEffect (fun () ->
                    let pending = List.ofArray view.PendingKeys
                    let values = [ for key in view.Keys -> key, view.Get key ] |> List.sort
                    seen.Add (pending, values))

                let before = seen.Count
                Expect.equal (fst seen[before - 1]) [ 2 ] "precondition"
                flights[2].Settle 5
                let added = List.ofSeq seen |> List.skip before
                Expect.equal (List.map fst added) [ [] ] "one run, with the key no longer pending"
                Expect.equal (List.map (snd >> List.map fst) added) [ [ 1; 2 ] ] "and the key in Keys"
            }

            test $"a key the first stage of {name} excludes on failure moves from PendingKeys to absent" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights, rows = halfSettled ()
                let excluding = rows |> first
                let view = excluding |> rest

                Expect.sequenceEqual view.PendingKeys [ 2 ] "precondition"
                flights[2].Fail (TimeoutException "boom")

                Expect.throwsT<TimeoutException> (fun () -> excluding.Get 2 |> ignore) "the excluding stage raises the error"
                Expect.sequenceEqual view.Keys [ 1 ] "the failed key is out of Keys"
                Expect.isEmpty view.PendingKeys "and out of PendingKeys"
                Expect.throwsT<Collections.Generic.KeyNotFoundException> (fun () -> view.Get 2 |> ignore) "and absent"
                Expect.equal view.Status Status.None "Status describes the pass"
            }
    ]
