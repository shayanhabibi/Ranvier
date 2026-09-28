module Ranvier.Tests.EffectSplit

open System
open System.Collections.Generic
open System.Threading.Tasks
open Expecto
open Ranvier

/// <summary>
/// A flight the test settles by hand, on the test's own thread.
/// </summary>
type private Flight<'T>() =
    let source = TaskCompletionSource<'T>()
    member _.Task = source.Task

    member _.Settle(v: 'T) =
        source.SetResult v

    member _.Fail(e: exn) =
        source.SetException e

/// <summary>
/// Each pair runs one scenario through <c>createEffect</c> and through <c>createEffectOn</c>.
/// </summary>
[<Tests>]
let tests =
    testList
        "EffectSplit"
        [
            test "createEffect repeats work done before a pending read" {
                let g = new Graph ()
                use _ = g.Activate ()
                let a = AsyncSource<int>(g)
                let log = ResizeArray ()

                do
                    (createEffect (fun () ->
                        log.Add "request"
                        log.Add $"saw {a.Value}"))

                a.Settle 5

                Expect.sequenceEqual log [ "request"; "request"; "saw 5" ] "the aborted attempt already logged"
            }

            test "createEffectOn runs no work before a pending read settles" {
                let g = new Graph ()
                use _ = g.Activate ()
                let a = AsyncSource<int>(g)
                let log = ResizeArray ()

                do
                    (createEffectOn (fun () -> a.Value) (fun v ->
                        log.Add "request"
                        log.Add $"saw {v}"))

                a.Settle 5

                Expect.sequenceEqual log [ "request"; "saw 5" ] "one attempt, with the settled value"
            }

            test "createEffect repeats work once per source still pending" {
                let g = new Graph ()
                use _ = g.Activate ()
                let a = AsyncSource<int>(g)
                let b = AsyncSource<int>(g)
                let sent = ref 0

                do
                    (createEffect (fun () ->
                        incr sent
                        let x = a.Value
                        let y = b.Value
                        ignore (x + y)))

                a.Settle 1
                b.Settle 2

                Expect.equal sent.Value 3 "construction, a's settle and b's settle each reached the work"
            }

            test "createEffectOn waits for every pending source" {
                let g = new Graph ()
                use _ = g.Activate ()
                let a = AsyncSource<int>(g)
                let b = AsyncSource<int>(g)
                let sent = ResizeArray ()

                do (createEffectOn (fun () -> a.Value + b.Value) sent.Add)

                a.Settle 1
                Expect.isEmpty sent "b is still pending"

                b.Settle 2
                Expect.sequenceEqual sent [ 3 ] "one action, once both have settled"
            }

            test "createEffect opens a subscription for each aborted attempt" {
                let g = new Graph ()
                use _ = g.Activate ()
                let a = AsyncSource<int>(g)
                let opened = ref 0
                let closed = ref 0

                do
                    (createEffect (fun () ->
                        incr opened
                        onCleanup (fun () -> incr closed)
                        ignore a.Value))

                a.Settle 1

                Expect.equal (opened.Value, closed.Value) (2, 1) "the aborted attempt's subscription was opened and closed"
            }

            test "createEffectOn opens one subscription per settled value" {
                let g = new Graph ()
                use _ = g.Activate ()
                let a = AsyncSource<int>(g)
                let opened = ref 0
                let closed = ref 0

                do
                    (createEffectOn (fun () -> a.Value) (fun _ ->
                        incr opened
                        onCleanup (fun () -> incr closed)))

                a.Settle 1

                Expect.equal (opened.Value, closed.Value) (1, 0) "no subscription churn"
            }

            test "createEffect repeats work on each re-flight of an async value" {
                let g = new Graph ()
                use _ = g.Activate ()
                let key = Signal (g, 1)
                let flights = Dictionary<int, Flight<string>>()

                let user =
                    new AsyncMemo<string> (
                        g,
                        fun _ ->
                            let f = Flight<string>()
                            flights[key.Value] <- f
                            f.Task
                    )

                let log = ResizeArray ()

                do
                    (createEffect (fun () ->
                        log.Add "render"
                        log.Add $"show {user.Value}"))

                flights[1].Settle "ada"
                key.Value <- 2
                flights[2].Settle "grace"

                Expect.sequenceEqual
                    log
                    [ "render"; "render"; "show ada"; "render"; "render"; "show grace" ]
                    "each flight start and settle reached the work"
            }

            test "createEffectOn acts once per settled value of an async value" {
                let g = new Graph ()
                use _ = g.Activate ()
                let key = Signal (g, 1)
                let flights = Dictionary<int, Flight<string>>()

                let user =
                    new AsyncMemo<string> (
                        g,
                        fun _ ->
                            let f = Flight<string>()
                            flights[key.Value] <- f
                            f.Task
                    )

                let log = ResizeArray ()

                do
                    (createEffectOn (fun () -> user.Value) (fun name ->
                        log.Add $"show {name}"
                        onCleanup (fun () -> log.Add $"hide {name}")))

                flights[1].Settle "ada"
                key.Value <- 2
                let whilePending = List.ofSeq log
                flights[2].Settle "grace"

                Expect.sequenceEqual whilePending [ "show ada" ] "the previous action stays in place during the re-flight"
                Expect.sequenceEqual log [ "show ada"; "hide ada"; "show grace" ] "and is replaced once the new value settles"
            }

            test "createEffect has done its work before a failing read" {
                let g = new Graph ()
                use _ = g.Activate ()
                let flight = Flight<int>()
                let a = new AsyncMemo<int> (g, (fun _ -> flight.Task))
                let log = ResizeArray ()

                do
                    (createEffect (fun () ->
                        log.Add "request"
                        log.Add $"saw {a.Value}"))

                flight.Fail (InvalidOperationException "offline")

                Expect.sequenceEqual log [ "request"; "request" ] "both attempts did the work, and neither finished"
            }

            test "createEffectOn does no work when compute fails" {
                let g = new Graph ()
                use _ = g.Activate ()
                let flight = Flight<int>()
                let a = new AsyncMemo<int> (g, (fun _ -> flight.Task))
                let log = ResizeArray ()

                do (createEffectOn (fun () -> a.Value) (fun v -> log.Add $"saw {v}"))

                flight.Fail (InvalidOperationException "offline")

                Expect.isEmpty log "the failure stops before the action"
            }

            test "createEffectOn keeps the previous action when compute fails" {
                let g = new Graph ()
                use _ = g.Activate ()
                let key = Signal (g, 1)
                let flights = Dictionary<int, Flight<string>>()

                let user =
                    new AsyncMemo<string> (
                        g,
                        fun _ ->
                            let f = Flight<string>()
                            flights[key.Value] <- f
                            f.Task
                    )

                let log = ResizeArray ()

                do
                    createEffectOn (fun () -> user.Value) (fun name ->
                        log.Add $"show {name}"
                        onCleanup (fun () -> log.Add $"hide {name}"))

                flights[1].Settle "ada"
                key.Value <- 2
                flights[2].Fail(InvalidOperationException "offline")

                Expect.sequenceEqual log [ "show ada" ] "the failed re-flight leaves ada shown"
            }

            test "createEffect tracks reads made by helpers it calls" {
                let g = new Graph ()
                use _ = g.Activate ()
                let count = Signal (g, 1)
                let theme = Signal (g, "light")

                let render (n: int) =
                    $"{n} in {theme.Value}"

                let log = ResizeArray ()

                do (createEffect (fun () -> log.Add (render count.Value)))

                theme.Value <- "dark"

                Expect.sequenceEqual log [ "1 in light"; "1 in dark" ] "the helper's read subscribed the effect"
            }

            test "createEffectOn tracks only what compute reads" {
                let g = new Graph ()
                use _ = g.Activate ()
                let count = Signal (g, 1)
                let theme = Signal (g, "light")

                let render (n: int) =
                    $"{n} in {theme.Value}"

                let log = ResizeArray ()

                do (createEffectOn (fun () -> count.Value) (fun n -> log.Add (render n)))

                theme.Value <- "dark"
                count.Value <- 2

                Expect.sequenceEqual log [ "1 in light"; "2 in dark" ] "theme is read, not tracked"
            }

            test "createEffect re-runs for writes that leave its derived value equal" {
                let g = new Graph ()
                use _ = g.Activate ()
                let items = Signal (g, [ 1; 2 ])
                let sent = ref 0

                do
                    (createEffect (fun () ->
                        let n = List.length items.Value
                        incr sent
                        ignore n))

                items.Value <- [ 3; 4 ]
                items.Value <- [ 5; 6 ]

                Expect.equal sent.Value 3 "the length never changed"
            }

            test "createEffectOn acts only when its derived value changes" {
                let g = new Graph ()
                use _ = g.Activate ()
                let items = Signal (g, [ 1; 2 ])
                let sent = ResizeArray ()

                do (createEffectOn (fun () -> List.length items.Value) sent.Add)

                items.Value <- [ 3; 4 ]
                items.Value <- [ 5; 6; 7 ]

                Expect.sequenceEqual sent [ 2; 3 ] "the equal length was cut off"
            }

            test "createEffectOn feeds another through a write in its action" {
                let g = new Graph ()
                use _ = g.Activate ()
                let s = Signal (g, 1)
                let doubled = Signal (g, 0)
                let seen = ResizeArray ()

                do
                    (createEffectOn (fun () -> s.Value) (fun v -> doubled.Value <- v * 2)
                     createEffectOn (fun () -> doubled.Value) seen.Add)

                s.Value <- 5

                Expect.sequenceEqual seen [ 2; 10 ] "the cascade resolves within one flush"
            }

            test "createEffectOn runs the action's cleanup when its scope is disposed" {
                let g = new Graph ()
                use _ = g.Activate ()
                let s = Signal (g, 1)
                let log = ResizeArray ()

                let root =
                    createRoot (fun owner ->
                        createEffectOn (fun () -> s.Value) (fun v ->
                            log.Add $"show {v}"
                            onCleanup (fun () -> log.Add $"hide {v}"))

                        owner)

                root.Dispose ()
                s.Value <- 2

                Expect.sequenceEqual log [ "show 1"; "hide 1" ] "disposal runs the cleanup and stops the effect"
            }

            test "createEffectOn does not act again when a re-flight settles on the acted value" {
                let g = new Graph ()
                use _ = g.Activate ()
                let key = Signal (g, 1)
                let flights = Dictionary<int, Flight<string>>()

                let user =
                    new AsyncMemo<string> (
                        g,
                        fun _ ->
                            let f = Flight<string>()
                            flights[key.Value] <- f
                            f.Task
                    )

                let log = ResizeArray ()

                do
                    createEffectOn (fun () -> user.Value) (fun name ->
                        log.Add $"show {name}"
                        onCleanup (fun () -> log.Add $"hide {name}"))

                flights[1].Settle "ada"
                key.Value <- 2
                flights[2].Settle "ada"
                key.Value <- 3
                flights[3].Fail(InvalidOperationException "offline")
                key.Value <- 4
                flights[4].Settle "ada"

                Expect.sequenceEqual log [ "show ada" ] "the cutoff compares with the last acted value"
            }

            test "createEffectOn acts with a value written by the previous action's cleanup" {
                let g = new Graph ()
                use _ = g.Activate ()
                let s = Signal (g, 1)
                let log = ResizeArray ()

                do
                    (createEffectOn (fun () -> s.Value) (fun v ->
                        log.Add v

                        onCleanup (fun () ->
                            if v = 2 then
                                s.Value <- 10)))

                s.Value <- 2
                s.Value <- 3

                Expect.sequenceEqual log [ 1; 2; 3; 10 ] "the cleanup's write re-queues the effect"
            }

            test "createEffectOn runs no action once a cleanup disposes its scope" {
                let g = new Graph ()
                use _ = g.Activate ()
                let s = Signal (g, 1)
                let log = ResizeArray ()
                let mutable root = Unchecked.defaultof<Owner>

                root <-
                    createRoot (fun owner ->
                        createEffectOn (fun () -> s.Value) (fun v ->
                            log.Add $"show {v}"

                            onCleanup (fun () ->
                                log.Add $"hide {v}"
                                root.Dispose ()))

                        owner)

                s.Value <- 2
                s.Value <- 3

                Expect.sequenceEqual log [ "show 1"; "hide 1" ] "the disposed effect stops before its action"
            }

            test "createEffectOn disposes the action's nodes before the next action and with its scope" {
                let g = new Graph ()
                use _ = g.Activate ()
                let s = Signal (g, 1)
                let tick = Signal (g, 0)
                let log = ResizeArray ()

                let root =
                    createRoot (fun owner ->
                        createEffectOn (fun () -> s.Value) (fun v ->
                            createEffect (fun () -> log.Add $"inner {v} {tick.Value}")
                            untrack (fun () -> onCleanup (fun () -> log.Add $"untracked hide {v}"))
                            onCleanup (fun () -> log.Add $"hide {v}"))

                        owner)

                tick.Value <- 1
                s.Value <- 2
                tick.Value <- 2
                root.Dispose ()
                tick.Value <- 3

                Expect.sequenceEqual
                    log
                    [
                        "inner 1 0"
                        "inner 1 1"
                        "hide 1"
                        "untracked hide 1"
                        "inner 2 1"
                        "inner 2 2"
                        "hide 2"
                        "untracked hide 2"
                    ]
                    "each action's effect and cleanups end with it"
            }

            test "createEffectOn owns an untracked cleanup registered before any other creation in act" {
                let g = new Graph ()
                use _ = g.Activate ()
                let s = Signal (g, 1)
                let log = ResizeArray ()

                let root =
                    createRoot (fun owner ->
                        createEffectOn (fun () -> s.Value) (fun v ->
                            untrack (fun () -> onCleanup (fun () -> log.Add $"untracked hide {v}"))
                            log.Add $"act {v}")

                        owner)

                s.Value <- 2
                root.Dispose ()

                Expect.sequenceEqual log [ "act 1"; "untracked hide 1"; "act 2"; "untracked hide 2" ] "the untracked cleanup belongs to the action"

                Expect.isEmpty g.Root.Errors "the cleanup found its owner"
            }

            test "createEffectOn records a throwing action cleanup on the graph root and acts again" {
                let g = new Graph ()
                use _ = g.Activate ()
                let s = Signal (g, 1)
                let log = ResizeArray ()

                do
                    (createEffectOn (fun () -> s.Value) (fun v ->
                        log.Add $"act {v}"
                        onCleanup (fun () -> failwith $"cleanup {v}")))

                s.Value <- 2

                Expect.sequenceEqual log [ "act 1"; "act 2" ] "the next action runs past the failing cleanup"

                Expect.sequenceEqual
                    (g.Root.Errors |> Seq.map (fun e -> e.Message))
                    [ "cleanup 1" ]
                    "the cleanup's failure reaches the graph root's errors"
            }

            test "createEffectOn keeps acting after an action throws" {
                let g = new Graph ()
                use _ = g.Activate ()
                let s = Signal (g, 1)
                let log = ResizeArray ()

                do
                    (createEffectOn (fun () -> s.Value % 10) (fun v ->
                        log.Add v

                        if v = 2 then
                            failwith "boom"))

                s.Value <- 2
                s.Value <- 12
                s.Value <- 3

                Expect.sequenceEqual log [ 1; 2; 3 ] "the thrown action still counts as acted on"
            }

            test "createEffectOn fails a run whose compute catches its own purity violation" {
                let g = new Graph ()
                use _ = g.Activate ()
                let s = Signal (g, 1)
                let log = ResizeArray ()
                let caught = ref 0

                do
                    (createEffectOn
                        (fun () ->
                            try
                                onCleanup ignore
                            with :? InvalidOperationException ->
                                incr caught

                            s.Value)
                        log.Add)

                s.Value <- 2

                Expect.isEmpty log "no run acts"
                Expect.equal caught.Value 2 "each run raised the violation"
            }

            test "createEffectOn disposed while pending never acts" {
                let g = new Graph ()
                use _ = g.Activate ()
                let a = AsyncSource<int>(g)
                let log = ResizeArray ()

                let root =
                    createRoot (fun owner ->
                        createEffectOn (fun () -> a.Value) log.Add
                        owner)

                root.Dispose ()
                a.Settle 5

                Expect.isEmpty log "the settle reaches no disposed effect"
            }
        ]
