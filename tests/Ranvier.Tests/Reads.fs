module Ranvier.Tests.Reads

open Expecto
open Ranvier

#if !FABLE_COMPILER
open System.Runtime.CompilerServices

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private failInNamedHelper () : int =
    raise (System.InvalidOperationException "the helper failed")

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private readFailure (read: unit -> int) : exn =
    try
        read () |> ignore
        failtest "the read did not raise"
    with ex ->
        ex
#endif

/// <summary>
/// The three ways to read a node — <c>Value</c>, <c>TryValue</c>, <c>Peek</c> — differ in two
/// independent respects: whether the read records an edge, and whether it is
/// allowed to run the body. Every combination has a caller, and getting one
/// wrong is silent: an untracked read that should have been tracked produces a
/// node that is simply never woken again, and no assertion about its value will
/// notice until the value is stale.
/// </summary>
/// <remarks>
/// <c>Untrack</c> is the same question asked at the scope level, and it nests, so it
/// has to restore rather than clear.
/// </remarks>
[<Tests>]
let tests =
    testList
        "Reads"
        [
            test "Peek on a never-read memo does not run the body" {
                let g = new Graph ()
                let source = Signal (g, 5)
                let m = Make.Memo (g, (fun _ -> source.Value * 2))

                // The honest answer for a memo that has never computed is the
                // default, because there is nothing else to give without
                // running the body — which is the one thing `Peek` promises not
                // to do.
                Expect.equal m.Peek unset "an uninitialised Peek is the default, not the computed value"
                Expect.equal m.Runs 0 "and it stayed uncomputed"

                Expect.equal m.Value 10 "a real read computes it"
                Expect.equal m.Peek 10 "and now Peek has something to report"
            }

            test "Peek does not recompute a stale memo" {
                let g = new Graph ()
                let source = Signal (g, 1)
                let m = Make.Memo (g, (fun _ -> source.Value * 10))

                Expect.equal m.Value 10 "precondition"

                source.Value <- 2
                Expect.equal m.Peek 10 "Peek is the last computed value, staleness included"
                Expect.equal m.Runs 1 "it did not run the body to find out"

                Expect.equal m.Value 20 "and a tracked read brings it current"
            }

            test "Peek records no edge, so the reader is never woken" {
                let g = new Graph ()
                let source = Signal (g, 1)
                let runs = ref 0

                new Effect (
                    g,
                    fun () ->
                        source.Peek |> ignore
                        runs.Value <- runs.Value + 1
                )
                |> ignore

                Expect.equal runs.Value 1 "constructed and run"
                Expect.equal source.ObserverCount 0 "a peek is not a subscription"

                source.Value <- 2
                Expect.equal runs.Value 1 "so the write reaches nobody"
            }

            test "a memo read only through Peek still counts as unread" {
                let g = new Graph ()
                let source = Signal (g, 1)
                let m = Make.Memo (g, (fun _ -> source.Value * 2))

                m.Peek |> ignore
                Expect.equal source.ObserverCount 0 "peeking a memo does not make it read its own sources"
            }

            test "Untrack nests, and the outer tracking comes back" {
                let g = new Graph ()
                let tracked = Signal (g, 1)
                let hidden = Signal (g, 10)
                let alsoTracked = Signal (g, 100)

                let m =
                    Make.Memo (
                        g,
                        fun _ ->
                            let a = tracked.Value

                            let b =
                                g.Untrack (fun () ->
                                    // A nested untrack must not, on its way
                                    // out, turn tracking back *on* inside the
                                    // untracked region it was nested in.
                                    let inner = g.Untrack (fun () -> hidden.Value)
                                    inner + hidden.Value)

                            a + b + alsoTracked.Value
                    )

                Expect.equal m.Value 121 "1 + 20 + 100"
                Expect.equal m.SourceCount 2 "only the two tracked reads recorded edges"
                Expect.equal hidden.ObserverCount 0 "and the hidden one has no observers"

                hidden.Value <- 20
                Expect.equal m.Value 121 "a write to the untracked source wakes nobody"

                alsoTracked.Value <- 200
                // 1 + (20 + 20) + 200. Untracked does not mean cached: the
                // next run reads the current value of `hidden`, it just never
                // learns that it changed. That is the trap in untracking — the
                // stale read is not stale *forever*, it updates at whatever
                // moment something else happens to wake the node.
                Expect.equal m.Value 241 "the read after the untracked region was still tracked"
            }

            test "Untrack outside any computation is just a read" {
                let g = new Graph ()
                let s = Signal (g, 7)
                Expect.equal (g.Untrack (fun () -> s.Value)) 7 "the value comes back"
                Expect.equal s.ObserverCount 0 "and there was nobody to record an edge for anyway"
            }

            test "an untracked read of a stale memo still recomputes it" {
                let g = new Graph ()
                let source = Signal (g, 1)
                let m = Make.Memo (g, (fun _ -> source.Value * 10))

                Expect.equal m.Value 10 "precondition"
                source.Value <- 2

                // Untracked is about the *edge*, not about freshness. The memo
                // still owes its reader a current value; it just does not
                // remember who asked.
                Expect.equal (g.Untrack (fun () -> m.Value)) 20 "the value is current"
                Expect.equal m.Runs 2 "because the body ran"
            }

            test "TryValue records an edge exactly as Value does" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                let runs = ref 0

                new Effect (
                    g,
                    fun () ->
                        a.TryValue |> ignore
                        runs.Value <- runs.Value + 1
                )
                |> ignore

                Expect.equal runs.Value 1 "ran, without suspending"

                a.Settle 1
                Expect.equal runs.Value 2 "and the non-throwing read still got it woken"
            }

            test "TryValue on a pending source reports rather than throws" {
                let g = new Graph ()
                let a = AsyncSource<int> g

                Expect.equal a.TryValue Pending "the whole point of the non-throwing read"

                a.Settle 3
                Expect.equal a.TryValue (Ready 3) "and it reports the settle the same way"
            }

            test "a fresh memo is Uninitialized until something reads it" {
                let g = new Graph ()
                let source = Signal (g, 1)
                let m = Make.Memo (g, (fun _ -> source.Value))

                Expect.isTrue (m.Status.HasFlag Status.Uninitialized) "nothing has computed yet"

                m.Value |> ignore
                Expect.isFalse (m.Status.HasFlag Status.Uninitialized) "and the first read clears it"
            }

            test "reading a memo twice in one body is one edge and one computation" {
                let g = new Graph ()
                let source = Signal (g, 2)
                let m = Make.Memo (g, (fun _ -> source.Value * 2))
                let outer = Make.Memo (g, (fun _ -> m.Value + m.Value))

                Expect.equal outer.Value 8 "precondition"
                Expect.equal m.Runs 1 "the second read hit the cache"
                Expect.equal m.ObserverCount 1 "and recorded one edge, not two"
            }
#if !FABLE_COMPILER

            // .NET only: a JavaScript rethrow keeps the stack of the original `Error`.
            test "a stored failure keeps its throw site across repeated reads" {
                let g = new Graph ()
                let failing = Make.Memo (g, (fun _ -> failInNamedHelper ()))
                let dependent = Make.Memo (g, (fun _ -> failing.Value + 1))

                let read () =
                    dependent.Value

                let first = readFailure read
                let firstTrace = first.StackTrace
                let second = readFailure read

                Expect.isTrue (obj.ReferenceEquals (first, second)) "both reads raise the stored instance"
                Expect.stringContains second.StackTrace (nameof failInNamedHelper) "the trace names the throw site"
                Expect.equal second.StackTrace firstTrace "a repeated read leaves the trace unchanged"
            }
#endif
        ]
