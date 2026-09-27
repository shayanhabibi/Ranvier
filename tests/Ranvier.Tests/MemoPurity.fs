module Ranvier.Tests.MemoPurity

open System
open System.Threading.Tasks
open Expecto
open Ranvier

/// <summary>
/// The <c>InvalidOperationException</c> raised by <c>f</c>.
/// </summary>
let private invalidOp (f: unit -> unit) (message: string) : InvalidOperationException =
    try
        f ()
        failtest message
    with :? InvalidOperationException as ex ->
        ex

/// <summary>
/// <c>createMemo</c>, <c>createAsync</c>, a projection row's reader and a lookup's <c>f</c>
/// are pure: creating an owned node in them fails the run with
/// <c>InvalidOperationException</c>.
/// </summary>
[<Tests>]
let tests =
    testList
        "MemoPurity"
        [
            test "a pure memo that creates a memo fails, naming createMemoWith" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let m = createMemo (fun () -> (createMemo (fun () -> s.Value)).Value)

                let ex = invalidOp (fun () -> m.Value |> ignore) "creation fails the run"
                Expect.stringContains ex.Message "createMemoWith" "the message names the owning memo"
            }

            test "a pure memo that registers a cleanup fails, and the cleanup never runs" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let mutable cleaned = 0

                let m =
                    createMemo (fun () ->
                        onCleanup (fun () -> cleaned <- cleaned + 1)
                        s.Value)

                Expect.throwsT<InvalidOperationException> (fun () -> m.Value |> ignore) "onCleanup fails the run"
                s.Value <- 2
                Expect.throwsT<InvalidOperationException> (fun () -> m.Value |> ignore) "and every later run"
                m.Dispose ()
                Expect.equal cleaned 0 "the cleanup was never registered"
            }

            test "a pure memo that creates an effect fails, and the effect never runs" {
                use g = new Graph ()
                use _ = g.Activate ()
                let mutable ran = false

                let m =
                    createMemo (fun () ->
                        createEffect (fun () -> ran <- true)
                        1)

                Expect.throwsT<InvalidOperationException> (fun () -> m.Value |> ignore) "the effect fails the run"
                Expect.isFalse ran "the effect never ran"
            }

            test "a pure memo that creates a node inside untrack still fails" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let m = createMemo (fun () -> (untrack (fun () -> createMemo (fun () -> s.Value))).Value)

                Expect.throwsT<InvalidOperationException> (fun () -> m.Value |> ignore) "untrack does not hide a creation"
            }

            test "a pure memo that catches the creation's exception still fails" {
                use g = new Graph ()
                use _ = g.Activate ()

                let m =
                    createMemo (fun () ->
                        try
                            createRoot (fun _ -> 1)
                        with _ ->
                            0)

                Expect.throwsT<InvalidOperationException> (fun () -> m.Value |> ignore) "the run fails regardless"
            }

            test "a pure memo recovers once its body stops creating nodes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let create = createSignal true

                let m =
                    createMemo (fun () ->
                        if create.Value then
                            onCleanup ignore

                        42)

                Expect.throwsT<InvalidOperationException> (fun () -> m.Value |> ignore) "the creating run fails"
                create.Value <- false
                Expect.equal m.Value 42 "the next run succeeds"
            }

            test "a pure memo may create signals" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let m = createMemo (fun () -> (createSignal (s.Value * 2)).Value)

                Expect.equal m.Value 2 "a signal is an unowned source"
            }

            test "a pure memo is not blamed for the nodes an owning memo it pulls creates" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let mutable cleaned = 0

                let owning =
                    createMemoWith (fun () ->
                        let n = s.Value
                        onCleanup (fun () -> cleaned <- cleaned + 1)
                        n)

                let reader = createMemo (fun () -> owning.Value * 10)

                Expect.equal reader.Value 10 "the pull succeeds"
                s.Value <- 2
                Expect.equal reader.Value 20 "and re-runs"
                Expect.equal cleaned 1 "the owning memo discharged its first run"
            }

            test "the direct constructor is pure by default" {
                use g = new Graph ()
                use _ = g.Activate ()
                let m = Memo (g, (fun () -> (Memo (g, (fun () -> 1))).Value))

                Expect.throwsT<InvalidOperationException> (fun () -> m.Value |> ignore) "the constructor defaults to pure"
            }

            test "the direct constructor with owning = true owns the body's nodes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let cleaned = ResizeArray<int> ()

                let m =
                    Memo (
                        g,
                        (fun () ->
                            let n = s.Value
                            onCleanup (fun () -> cleaned.Add n)
                            n),
                        true
                    )

                Expect.equal m.Value 1 "first run"
                s.Value <- 2
                Expect.equal m.Value 2 "second run"
                Expect.sequenceEqual cleaned [ 1 ] "the first run's cleanup ran"
            }

            test "a pure async memo that creates a node fails, naming createAsyncWith" {
                use g = new Graph ()
                use _ = g.Activate ()

                let a =
                    createAsync (fun _ ->
                        onCleanup ignore
                        Task.FromResult 1)

                let ex = invalidOp (fun () -> a.Value |> ignore) "creation fails the flight"
                Expect.stringContains ex.Message "createAsyncWith" "the message names the owning async value"
            }

            test "a lookup's f that creates a node fails its key" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1

                let lookup =
                    createLookup
                        (fun (sel: int) (k: int) ->
                            if k = 2 then
                                onCleanup ignore

                            sel = k)
                        (fun prev next -> [ prev; next ])
                        (fun () -> s.Value)

                Expect.isTrue (lookup.Get 1) "a key whose f creates nothing computes"
                let ex = invalidOp (fun () -> lookup.Get 2 |> ignore) "f is pure"
                Expect.stringContains ex.Message "lookup" "the message names the lookup"
            }

            test "a reader is not blamed for a lookup's f creating a node" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1

                let lookup =
                    createLookup
                        (fun (sel: int) (k: int) ->
                            if k = 2 then
                                onCleanup ignore

                            sel = k)
                        (fun prev next -> [ prev; next ])
                        (fun () -> s.Value)

                let reader =
                    createMemo (fun () ->
                        try
                            lookup.Get 2
                        with :? InvalidOperationException ->
                            false)

                Expect.isFalse reader.Value "the reader catches the key's failure and completes"
            }
        ]
