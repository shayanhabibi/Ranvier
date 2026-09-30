module Ranvier.Tests.Editables

open System
open Expecto
open Ranvier

type private Line = { Sku: string; Quantity: int }

/// <summary>Counts the runs of an effect that reads <c>read</c>.</summary>
let private countRuns (read: unit -> 'a) =
    let runs = ref 0

    createEffect (fun () ->
        runs.Value <- runs.Value + 1
        read () |> ignore)

    runs

[<Tests>]
let tests =
    testList
        "Editables"
        [
            test "an editable reads the seed until edited, then the edit" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal 1
                let e = createEditable (fun _ -> source.Value * 10)

                Expect.equal e.Value 10 "the seed's value"
                Expect.isFalse e.IsEdited "unedited"

                e.Value <- 7

                Expect.equal e.Value 7 "the edit"
                Expect.isTrue e.IsEdited "edited"
                Expect.equal e.Upstream 10 "the seed is unchanged"
            }

            test "an upstream change drops the edit of an editable" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal 1
                let e = createEditable (fun _ -> source.Value)
                let seen = ResizeArray<int>()
                createEffect (fun () -> seen.Add e.Value)

                e.Value <- 5
                source.Value <- 2

                Expect.equal e.Value 2 "the seed's new value"
                Expect.isFalse e.IsEdited "the edit is dropped"
                Expect.sequenceEqual seen [ 1; 5; 2 ] "each value reaches the reader"
            }

            test "A -> B -> A observed drops the edit" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal "A"
                let e = createEditable (fun _ -> source.Value)
                countRuns (fun () -> e.Value) |> ignore

                e.Value <- "edit"
                source.Value <- "B"
                source.Value <- "A"

                Expect.equal e.Value "A" "the seed's value"
                Expect.isFalse e.IsEdited "B was published, so the edit is dropped"
            }

            test "A -> B -> A inside a batch keeps the edit" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal "A"
                let e = createEditable (fun _ -> source.Value)
                countRuns (fun () -> e.Value) |> ignore

                e.Value <- "edit"

                batch (fun () ->
                    source.Value <- "B"
                    source.Value <- "A")

                Expect.equal e.Value "edit" "the seed ran once and published an equal value"
                Expect.isTrue e.IsEdited "edited"
            }

            test "A -> B -> A with nothing reading the seed keeps the edit" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal "A"
                let e = createEditable (fun _ -> source.Value)
                e.Value <- "edit"

                source.Value <- "B"
                source.Value <- "A"

                Expect.equal e.Value "edit" "the seed ran once and published an equal value"
            }

            test "a seed that re-runs to an equal value keeps the edit and wakes no reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal 1
                let e = createEditable (fun _ -> source.Value % 2)
                let runs = countRuns (fun () -> e.Value)

                e.Value <- 9
                source.Value <- 3

                Expect.equal e.Value 9 "the edit survives an equal seed value"
                Expect.equal runs.Value 2 "one run for the edit, none for the seed re-run"
            }

            test "a seed returning a new but equal record drops the edit under the default policy" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal 1

                let e =
                    createEditable (fun _ ->
                        {
                            Sku = "x"
                            Quantity = source.Value * 0
                        })

                countRuns (fun () -> e.Value) |> ignore

                e.Value <- { Sku = "x"; Quantity = 4 }
                source.Value <- 2

                Expect.equal e.Value.Quantity 0 "a new record is a new value under reference comparison"
                Expect.isFalse e.IsEdited "the edit is dropped"
            }

            test "a seed returning its previous record keeps the edit" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal 1

                let e =
                    createEditable (fun prev ->
                        source.Value |> ignore

                        match prev with
                        | ValueSome line -> line
                        | ValueNone -> { Sku = "x"; Quantity = 1 })

                countRuns (fun () -> e.Value) |> ignore

                e.Value <- { Sku = "x"; Quantity = 4 }
                source.Value <- 2

                Expect.equal e.Value.Quantity 4 "the same record is an unchanged seed"
                Expect.isTrue e.IsEdited "edited"
            }

            test "an equal edit wakes no reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal 1
                let e = createEditable (fun _ -> source.Value)
                let runs = countRuns (fun () -> e.Value)

                e.Value <- 5
                e.Value <- 5

                Expect.equal runs.Value 2 "one run for the first edit"
            }

            test "Reset drops the edit" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal 1
                let e = createEditable (fun _ -> source.Value)

                e.Value <- 5
                e.Reset ()

                Expect.equal e.Value 1 "the seed's value"
                Expect.isFalse e.IsEdited "unedited"
            }

            test "IsEdited wakes its reader on edit, on reset and when upstream drops the edit" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal 1
                let e = createEditable (fun _ -> source.Value)
                let seen = ResizeArray<bool>()
                createEffectOn (fun () -> e.IsEdited) seen.Add

                e.Value <- 5
                e.Reset ()
                e.Value <- 6
                source.Value <- 2

                Expect.sequenceEqual seen [ false; true; false; true; false ] "every transition is seen"
            }

            test "the seed receives its own last value" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal 1
                let prevs = ResizeArray<int voption>()

                let e =
                    createEditable (fun prev ->
                        prevs.Add prev
                        source.Value)

                e.Value |> ignore
                e.Value <- 50
                source.Value <- 2
                e.Value |> ignore

                Expect.sequenceEqual prevs [ ValueNone; ValueSome 1 ] "the seed's values, never the edit"
            }

            test "a draft keeps its edit across upstream changes until Reset" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal 1
                let d = createDraft (fun _ -> source.Value)
                let upstream = ResizeArray<int>()
                createEffect (fun () -> upstream.Add d.Upstream)

                d.Value <- 5
                source.Value <- 2

                Expect.equal d.Value 5 "the edit"
                Expect.isTrue d.IsEdited "edited"
                Expect.sequenceEqual upstream [ 1; 2 ] "Upstream tracks the seed"

                d.Reset ()

                Expect.equal d.Value 2 "the seed's value after Reset"
            }

            test "an edit while the seed is pending belongs to the last settled value" {
                use g = new Graph ()
                use _ = g.Activate ()
                let trigger = createSignal 0
                let first = createAsyncSource<int>()
                first.Settle 1
                let later = createAsyncSource<int>()

                let e =
                    createEditable (fun _ -> if trigger.Value = 0 then first.Value else later.Value)

                countRuns (fun () -> e.TryValue) |> ignore

                trigger.Value <- 1
                Expect.equal e.Status Status.Pending "the seed is pending"

                e.Value <- 5
                Expect.equal e.Value 5 "the edit is in force while the seed is pending"

                later.Settle 1
                Expect.equal e.Value 5 "an equal settled value keeps the edit"

                trigger.Value <- 2
                trigger.Value <- 3
                Expect.equal e.Value 5 "an unrelated re-run keeps the edit"
            }

            test "a pending seed that settles to a new value drops the edit" {
                use g = new Graph ()
                use _ = g.Activate ()
                let trigger = createSignal 0
                let first = createAsyncSource<int>()
                first.Settle 1
                let later = createAsyncSource<int>()

                let e =
                    createEditable (fun _ -> if trigger.Value = 0 then first.Value else later.Value)

                countRuns (fun () -> e.TryValue) |> ignore

                trigger.Value <- 1
                e.Value <- 5
                later.Settle 2

                Expect.equal e.Value 2 "the seed's new value"
                Expect.isFalse e.IsEdited "the edit is dropped"
            }

            test "an edit before the seed first settles is dropped by an editable and kept by a draft" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createAsyncSource<int>()
                let e = createEditable (fun _ -> source.Value)
                let d = createDraft (fun _ -> source.Value)

                Expect.equal e.TryValue Pending "the seed is pending"

                e.Value <- 5
                d.Value <- 5
                source.Settle 1

                Expect.equal e.Value 1 "the first settled value drops the edit"
                Expect.equal d.Value 5 "the draft keeps the edit"
            }

            test "disposing the owner detaches the editable from its seed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal 1
                let runs = ref 0

                let e, owner =
                    createRoot (fun owner ->
                        createEditable (fun _ ->
                            runs.Value <- runs.Value + 1
                            source.Value),
                        owner)

                e.Value |> ignore
                owner.Dispose ()
                source.Value <- 2
                e.Value |> ignore

                Expect.equal runs.Value 1 "the seed stays detached"
                Expect.equal source.ObserverCount 0 "the source holds no edge to the seed"
            }

#if !FABLE_COMPILER
            // .NET only: JavaScript exposes no allocation counter.
            untracedOnly
            <| test "an edit of an int allocates no more than a write through a chain of two memos" {
                let perWrite (setup: unit -> int -> unit) =
                    let g = new Graph ()
                    use _ = g.Activate ()
                    let write = setup ()

                    for i in 1..100 do
                        write i

                    let before = GC.GetAllocatedBytesForCurrentThread ()

                    for i in 1..10_000 do
                        write (i + 100)

                    (GC.GetAllocatedBytesForCurrentThread () - before)
                    / 10_000L

                let chain =
                    perWrite (fun () ->
                        let s = createSignal 0
                        let a = createMemo (fun _ -> s.Value)
                        let b = createMemo (fun _ -> a.Value)
                        let mutable sum = 0
                        createEffect (fun () -> sum <- sum + b.Value)
                        fun i -> s.Value <- i)

                let edit =
                    perWrite (fun () ->
                        let source = createSignal 1
                        let e = createEditable (fun _ -> source.Value)
                        let mutable sum = 0
                        createEffect (fun () -> sum <- sum + e.Value)
                        fun i -> e.Value <- i)

                Expect.isLessThanOrEqual edit chain $"an edit allocated %d{edit} bytes, a two-memo chain write %d{chain}"
            }
#endif
        ]
