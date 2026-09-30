module Ranvier.Tests.Texts

open System
open Expecto
open Ranvier

[<Tests>]
let tests =
    testList
        "Texts"
        [
            test "ThreadAffinity and FlightPolicy print their case names" {
                Expect.equal (Guarded.ToString ()) "Guarded" "Guarded"
                Expect.equal (Unchecked.ToString ()) "Unchecked" "Unchecked"
                Expect.equal (CancelPrevious.ToString ()) "CancelPrevious" "CancelPrevious"
                Expect.equal (KeepLatest.ToString ()) "KeepLatest" "KeepLatest"
                Expect.equal (FlightPolicy.Queue.ToString ()) "Queue" "Queue"
            }

            test "a Reading prints its case and payload, quoting a string" {
                Expect.equal ((Ready 3).ToString()) "Ready 3" "an int payload"
                Expect.equal ((Ready "x").ToString()) "Ready \"x\"" "a string payload, quoted"
                Expect.equal ((Ready (null: string)).ToString()) "Ready null" "a null payload"
                Expect.equal (Reading<int>.Pending.ToString()) "Pending" "Pending"

                Expect.stringStarts ((Failed (InvalidOperationException "boom"): Reading<int>).ToString()) "Failed " "Failed, then the exception"
            }

            test "GraphOptions prints its fields one per line" {
                let text = GraphOptions.Default.ToString ()

                Expect.isTrue
                    (text.EndsWith "\n  FlightPolicy = CancelPrevious\n  ThreadAffinity = Guarded\n  Dispatcher = None }")
                    "the fields after Equality"
#if !FABLE_COMPILER
                Expect.equal
                    text
                    "{ Equality = Ranvier.JsIdentityPolicy\n  FlightPolicy = CancelPrevious\n  ThreadAffinity = Guarded\n  Dispatcher = None }"
                    "the record text"
#endif
            }

#if !FABLE_COMPILER
            // .NET only: Fable gives an F# exception an empty message.
            test "NotReadyException's message names the exception" {
                use g = new Graph ()
                let s = Signal (g, 0)
                Expect.stringStarts (NotReadyException(s :> INode).Message) "NotReadyException " "the name, then the source"
            }
#endif

            test "a missing key's message holds the key's text" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let proj = createProjection id id (fun () -> items.Value)

                Expect.throwsT<Collections.Generic.KeyNotFoundException> (fun () -> proj.Get 99 |> ignore) "the documented exception"

                Expect.throwsC (fun () -> proj.Get 99 |> ignore) (fun ex ->
                    Expect.equal ex.Message "The projection has no key 99." "the key as its string text")
            }

            test "a duplicate key's message holds the key's text" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ "a"; "a" ]
                let proj = createProjection id id (fun () -> items.Value)

                Expect.throwsT<InvalidOperationException> (fun () -> proj.Keys |> ignore) "the documented exception"

                Expect.throwsC (fun () -> proj.Keys |> ignore) (fun ex ->
                    Expect.stringStarts ex.Message "The projection produced the key a twice" "the key, unquoted")
            }
        ]
