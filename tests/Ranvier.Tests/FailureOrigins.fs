module Ranvier.Tests.FailureOrigins

open System
open System.Collections.Generic
open Expecto
open Ranvier

/// <summary>True when <c>origin</c> is the node <c>expected</c>.</summary>
let private isNode (expected: obj) (origin: INode) =
    obj.ReferenceEquals (origin, expected)

let private failedWith (reading: Reading<'T>) =
    match reading with
    | Failed ex -> ex
    | other -> failtestf "expected Failed, got %A" other

#if !FABLE_COMPILER
/// <summary>Structural equality whose <c>int</c> comparer throws while <c>Armed</c> is set.</summary>
type private ThrowingIntPolicy() =
    member val Armed = false with get, set

    interface IEqualityPolicy with
        member this.Comparer<'T>() =
            let inner = EqualityComparer<'T>.Default
            let throws = typeof<'T> = typeof<int>

            { new IEqualityComparer<'T> with
                member _.Equals(a, b) =
                    if throws && this.Armed then
                        raise (InvalidOperationException "comparer")

                    inner.Equals (a, b)

                member _.GetHashCode a =
                    inner.GetHashCode a
            }

[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let private throwFromOrigin () : int =
    raise (InvalidOperationException "origin")
#endif

[<Tests>]
let tests =
    testList
        "Failure origins"
        [
            test "a memo whose body throws is its own origin" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = Make.Memo (g, (fun _ -> if s.Value > 0 then failwith "boom" else s.Value))

                failedWith a.TryValue |> ignore
                Expect.isTrue (isNode a a.ErrorOrigin) "the memo raised the exception"
            }

            test "the origin passes unchanged through every reader" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = Make.Memo (g, (fun _ -> if s.Value > 0 then failwith "boom" else s.Value))
                let b = Make.Memo (g, (fun _ -> a.Value + 1))
                let c = Make.Memo (g, (fun _ -> b.Value + 1))

                let error = failedWith c.TryValue
                Expect.isTrue (obj.ReferenceEquals (error, failedWith a.TryValue)) "the same instance reaches the end"
                Expect.isTrue (isNode a c.ErrorOrigin) "the last reader reports the first memo"
                Expect.isTrue (isNode a b.ErrorOrigin) "and so does the one between"
            }

            test "the origin is null while the node is not failed, and clears on recovery" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = Make.Memo (g, (fun _ -> if s.Value > 0 then failwith "boom" else s.Value))
                let b = Make.Memo (g, (fun _ -> a.Value + 1))

                failedWith b.TryValue |> ignore
                Expect.isFalse (isNull (box b.ErrorOrigin)) "precondition: failed"

                s.Value <- 0
                Expect.equal b.TryValue (Ready 1) "recovered"
                Expect.isTrue (isNull (box b.ErrorOrigin)) "the reader clears its origin"
                Expect.isTrue (isNull (box a.ErrorOrigin)) "and so does the origin"
            }

            test "a wrapping exception originates at the wrapper" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = Make.Memo (g, (fun _ -> if s.Value > 0 then failwith "boom" else s.Value))

                let b =
                    Make.Memo (
                        g,
                        fun _ ->
                            try
                                a.Value
                            with ex ->
                                raise (InvalidOperationException ("wrapped", ex))
                    )

                let error = failedWith b.TryValue
                Expect.isTrue (isNode b b.ErrorOrigin) "the wrapper raised a new exception"
                Expect.isTrue (obj.ReferenceEquals (error.InnerException, failedWith a.TryValue)) "the upstream failure is inner"
            }

            test "a failed async source is the origin of its readers' failure" {
                let g = new Graph ()
                let source = AsyncSource<int>(g)
                let reader = Make.Memo (g, (fun _ -> source.Value * 2))

                Expect.isTrue (isNull (box source.ErrorOrigin)) "pending is not failed"
                source.Fail (exn "boom")

                failedWith reader.TryValue |> ignore
                Expect.isTrue (isNode source source.ErrorOrigin) "the source failed itself"
                Expect.isTrue (isNode source reader.ErrorOrigin) "the reader reports the source"

                source.Settle 1
                Expect.equal reader.TryValue (Ready 2) "recovered"
                Expect.isTrue (isNull (box source.ErrorOrigin)) "a settled source reports no origin"
            }

            test "a faulted flight originates at the async memo" {
                let g = new Graph ()
                let a = Make.AsyncMemo<int>(g, (fun _ _ -> faulted<int>(exn "flight")))
                let reader = Make.Memo (g, (fun _ -> a.Value + 1))

                failedWith a.TryValue |> ignore
                failedWith reader.TryValue |> ignore
                Expect.isTrue (isNode a a.ErrorOrigin) "the async memo's flight faulted"
                Expect.isTrue (isNode a reader.ErrorOrigin) "the reader reports the async memo"
            }

            test "an effect reports the origin of the failure it read" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = Make.Memo (g, (fun _ -> if s.Value > 0 then failwith "boom" else s.Value))
                let own = new Effect (g, (fun () -> failwith "own"))
                let reading = new Effect (g, (fun () -> a.Value |> ignore))

                Expect.isTrue (isNode own own.ErrorOrigin) "an effect body that throws is its own origin"
                Expect.isTrue (isNode a reading.ErrorOrigin) "an effect that read a failure reports its origin"

                s.Value <- 0
                Expect.isTrue (isNull (box reading.ErrorOrigin)) "a successful run clears it"
            }

            test "an error boundary reports where the caught failure came from" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = Make.Memo (g, (fun _ -> if s.Value > 0 then failwith "boom" else s.Value))
                let b = Make.Memo (g, (fun _ -> a.Value + 1))
                let boundary = Boundary<int>.Errors(g, (fun () -> b.Value), (fun _ _ -> -1))

                Expect.equal boundary.TryValue (Ready -1) "caught"
                Expect.isTrue (isNode a boundary.CaughtFrom) "the caught failure came from the first memo"
                Expect.isTrue (isNull (box boundary.ErrorOrigin)) "the boundary itself is not failed"

                s.Value <- 0
                Expect.equal boundary.TryValue (Ready 1) "recovered"
                Expect.isTrue (isNull (box boundary.CaughtFrom)) "nothing caught, no origin"
            }

            test "a boundary body that throws is the origin of what it catches" {
                let g = new Graph ()
                let boundary = Boundary<int>.Errors(g, (fun () -> failwith "own"), (fun _ _ -> -1))

                Expect.equal boundary.TryValue (Ready -1) "caught"
                Expect.isTrue (isNode boundary boundary.CaughtFrom) "the boundary's body raised it"
            }

            test "a recover that rethrows keeps the upstream origin, and a new exception originates at the boundary" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = Make.Memo (g, (fun _ -> if s.Value > 0 then failwith "boom" else s.Value))

                let rethrowing =
                    Boundary<int>.Errors(g, (fun () -> a.Value), (fun ex _ -> raise ex))

                let replacing =
                    Boundary<int>.Errors(g, (fun () -> a.Value), (fun _ _ -> raise (InvalidOperationException "replaced")))

                let reader = Make.Memo (g, (fun _ -> rethrowing.Value))

                failedWith rethrowing.TryValue |> ignore
                Expect.isTrue (isNode a rethrowing.ErrorOrigin) "a rethrow passes the failure through"
                failedWith reader.TryValue |> ignore
                Expect.isTrue (isNode a reader.ErrorOrigin) "and its reader sees the same origin"

                failedWith replacing.TryValue |> ignore
                Expect.isTrue (isNode replacing replacing.ErrorOrigin) "a new exception originates at the boundary"
            }

            test "a failed projection row originates at the projection" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]

                let proj =
                    createProjection id (fun x -> if x = 2 then failwith "row" else x) (fun () -> items.Value)

                let reader = Make.Memo (g, (fun _ -> proj.Get 2))

                failedWith reader.TryValue |> ignore
                Expect.isTrue (isNode proj reader.ErrorOrigin) "the row is internal, so the projection is reported"
                Expect.isTrue (isNull (box proj.ErrorOrigin)) "the pass itself succeeded"
            }

            test "a failed projection pass originates at the projection" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]

                let proj =
                    createProjection (fun x -> if x = 2 then failwith "keyOf" else x) id (fun () -> items.Value)

                let reader = Make.Memo (g, (fun _ -> proj.Keys.Length))

                failedWith reader.TryValue |> ignore
                Expect.isTrue (isNode proj proj.ErrorOrigin) "keyOf failed the pass"
                Expect.isTrue (isNode proj reader.ErrorOrigin) "the reader of the keys reports the projection"
            }

            test "a projection whose source rethrows a failed read reports the upstream node" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let a = Make.Memo (g, (fun _ -> if s.Value > 0 then failwith "boom" else s.Value))
                let proj = createProjection id id (fun () -> [ a.Value ])
                let reader = Make.Memo (g, (fun _ -> proj.Keys.Length))

                failedWith reader.TryValue |> ignore
                Expect.isTrue (isNode a proj.ErrorOrigin) "the pass read the failure"
                Expect.isTrue (isNode a reader.ErrorOrigin) "and so did its reader"
            }

            test "a fold over a failed row reports the row's origin" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]

                let proj =
                    createProjection id (fun x -> if x = 2 then failwith "row" else x) (fun () -> items.Value)

                let total = proj |> Projection.foldGroup (+) (-) 0

                failedWith total.TryValue |> ignore
                Expect.isTrue (isNode proj total.ErrorOrigin) "the fold raised the row's failure"
            }

            test "a lookup over a failed source reports the upstream node" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let a = Make.Memo (g, (fun _ -> if s.Value > 0 then failwith "boom" else s.Value))
                let lookup = createLookup (fun v k -> v + k) (fun _ _ -> []) (fun () -> a.Value)
                let reader = Make.Memo (g, (fun _ -> lookup.Get 1))

                failedWith reader.TryValue |> ignore
                Expect.isTrue (isNode a reader.ErrorOrigin) "the lookup's source read the failure"
            }

            test "a failed read swallowed by a body leaves the next failure its own" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = Make.Memo (g, (fun _ -> if s.Value > 0 then failwith "boom" else s.Value))

                let b =
                    Make.Memo (
                        g,
                        fun _ ->
                            (try
                                a.Value
                             with _ ->
                                 0)
                            |> ignore

                            failwith "own"
                    )

                failedWith b.TryValue |> ignore
                Expect.isTrue (isNode b b.ErrorOrigin) "the swallowed failure is not the one raised"
            }

#if !FABLE_COMPILER
            // .NET only: a custom comparer policy and stack traces.
            test "a throwing comparer is the origin of the failure" {
                let policy = ThrowingIntPolicy ()

                let g =
                    new Graph (
                        { GraphOptions.Default with
                            Equality = policy
                        }
                    )

                let s = Signal (g, "1")
                let a = Make.Memo (g, (fun _ -> int s.Value))
                let b = Make.Memo (g, (fun _ -> string (a.Value + 1)))

                Expect.equal b.TryValue (Ready "2") "precondition"
                policy.Armed <- true
                s.Value <- "5"

                failedWith b.TryValue |> ignore
                Expect.isTrue (isNode a a.ErrorOrigin) "the memo's comparer raised it"
                Expect.isTrue (isNode a b.ErrorOrigin) "and the reader reports that memo"
            }

            test "every reader rethrows with the origin's frames and its own" {
                let g = new Graph ()
                let a = Make.Memo (g, (fun _ -> throwFromOrigin ()))
                let b = Make.Memo (g, (fun _ -> a.Value + 1))
                let c = Make.Memo (g, (fun _ -> b.Value + 1))

                let trace =
                    try
                        c.Value |> ignore
                        ""
                    with ex ->
                        ex.StackTrace

                Expect.stringContains trace "throwFromOrigin" "the origin's throw site survives"

                let marker = "--- End of stack trace from previous location ---"

                let count =
                    (trace.Length - trace.Replace(marker, "").Length)
                    / marker.Length

                Expect.equal count 1 "one capture, shared by every node on the path"
            }
#endif
        ]
