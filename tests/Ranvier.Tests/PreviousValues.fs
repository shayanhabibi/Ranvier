module Ranvier.Tests.PreviousValues

open System
open Expecto
open Ranvier
open Ranvier.Tests.Readings

/// <summary>
/// A memo's compute receives the value the memo last published: <c>ValueNone</c>
/// before the first, and the last settled value after a run that suspends or fails.
/// </summary>
[<Tests>]
let tests =
    testList
        "PreviousValues"
        [
            test "the first run receives ValueNone" {
                let g = new Graph ()
                let seen = ResizeArray ()

                let m =
                    Memo (
                        g,
                        fun prev ->
                            seen.Add prev
                            1
                    )

                m.Value |> ignore
                Expect.sequenceEqual seen [ ValueNone ] "nothing has been published"
            }

            test "a later run receives the value last published" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let seen = ResizeArray ()

                let m =
                    Memo (
                        g,
                        fun prev ->
                            seen.Add prev
                            s.Value * 10
                    )

                m.Value |> ignore
                s.Value <- 2
                m.Value |> ignore
                Expect.sequenceEqual seen [ ValueNone; ValueSome 10 ] "the second run sees the first result"
            }

            test "a fold steps once per unbatched write to an observed memo" {
                let g = new Graph ()
                let s = Signal (g, 0)

                let total = Memo (g, (fun prev -> ValueOption.defaultValue 0 prev + s.Value))

                new Effect (g, (fun () -> total.Value |> ignore))
                |> ignore

                s.Value <- 1
                s.Value <- 2
                s.Value <- 3
                Expect.equal total.Peek 6 "each write is folded in"
            }

            test "a fold steps once per batch" {
                let g = new Graph ()
                let s = Signal (g, 0)

                let total = Memo (g, (fun prev -> ValueOption.defaultValue 0 prev + s.Value))

                new Effect (g, (fun () -> total.Value |> ignore))
                |> ignore

                g.Batch (fun () ->
                    s.Value <- 1
                    s.Value <- 2
                    s.Value <- 3)

                Expect.equal total.Peek 3 "the batch folds its final value once"
                Expect.equal total.Runs 2 "one run for the batch"
            }

            test "a suspended run leaves prev unchanged" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = AsyncSource<int> g
                let gate = Signal (g, false)
                let seen = ResizeArray ()

                let m =
                    Memo (
                        g,
                        fun prev ->
                            seen.Add prev
                            let v = s.Value
                            if gate.Value then v + a.Value else v
                    )

                Expect.equal m.Value 1 "published"

                gate.Value <- true
                Expect.equal m.TryValue Pending "suspended"
                s.Value <- 2
                Expect.equal m.TryValue Pending "still suspended"

                a.Settle 10
                Expect.equal m.Value 12 "settled"

                Expect.sequenceEqual seen [ ValueNone; ValueSome 1; ValueSome 1; ValueSome 1 ] "every run after the first sees the last settled value"
            }

            test "a failed run leaves prev at the last settled value" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let seen = ResizeArray ()

                let m =
                    Memo (
                        g,
                        fun prev ->
                            seen.Add prev

                            if s.Value < 0 then
                                failwith "negative"

                            s.Value
                    )

                m.Value |> ignore
                s.Value <- -1
                Expect.isTrue (m.TryValue.IsFailed) "failed"
                s.Value <- 5
                Expect.equal m.Value 5 "recovered"

                Expect.sequenceEqual seen [ ValueNone; ValueSome 1; ValueSome 1 ] "the failure published nothing"
            }

            test "a failed first run passes ValueNone to the next run" {
                let g = new Graph ()
                let s = Signal (g, -1)
                let seen = ResizeArray ()

                let m =
                    Memo (
                        g,
                        fun prev ->
                            seen.Add prev

                            if s.Value < 0 then
                                failwith "negative"

                            s.Value
                    )

                Expect.isTrue (m.TryValue.IsFailed) "failed"
                s.Value <- 5
                Expect.equal m.Value 5 "recovered"

                Expect.sequenceEqual seen [ ValueNone; ValueNone ] "nothing was ever published"
            }

            test "returning prev triggers the cutoff" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let observed = ref 0

                let m =
                    Memo (
                        g,
                        fun prev ->
                            let v = s.Value

                            match prev with
                            | ValueSome (p: obj) when v > 0 -> p
                            | _ -> box v
                    )

                new Effect (
                    g,
                    fun () ->
                        m.Value |> ignore
                        observed.Value <- observed.Value + 1
                )
                |> ignore

                s.Value <- 2
                Expect.equal m.Runs 2 "the memo re-ran"
                Expect.equal observed.Value 1 "the same instance does not wake the observer"
                Expect.equal (unbox<int> m.Peek) 1 "the retained value is the first"
            }
        ]
