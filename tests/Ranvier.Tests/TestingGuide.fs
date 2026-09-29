/// The samples of docs/content/guide/testing.md, verbatim. A change here changes the page.
module Ranvier.Tests.TestingGuide

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Ranvier

/// <summary>A graph under <c>policy</c> whose off-thread work waits for <c>Pump</c>.</summary>
let newGraph (policy: FlightPolicy) =
    new Graph (
        { GraphOptions.Default with
            Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher)
            FlightPolicy = policy
        }
    )

/// <summary>A request to the fake service. The test lands it by completing <c>Reply</c>.</summary>
type Call<'T> =
    {
        Query: string
        Token: CancellationToken
        Reply: TaskCompletionSource<'T>
    }

/// <summary>A search over a fake service. <c>calls</c> holds one call per flight, in start order.</summary>
let search (graph: Graph) =
    graph.Run (fun () ->
        let query = createSignal "a"
        let calls = ResizeArray<Call<string list>>()

        let hits =
            createAsync (fun _ token ->
                let call =
                    {
                        Query = query.Value
                        Token = token
                        Reply = TaskCompletionSource<string list>()
                    }

                calls.Add call
                call.Reply.Task)

        query, calls, hits)

[<Tests>]
let tests =
    testList
        "Testing guide"
        [
            testCase "the test decides when each flight lands"
            <| fun () ->
                use graph = newGraph FlightPolicy.CancelPrevious
                let query, calls, hits = search graph

                Expect.equal hits.TryValue Pending "the first read starts a flight"
                calls[0].Reply.SetResult [ "ab"; "ac" ]
                Expect.equal hits.TryValue (Ready [ "ab"; "ac" ]) "the flight landed"

                query.Value <- "x"
                Expect.equal hits.TryValue Pending "a new query starts a new flight"
                Expect.equal calls[1].Query "x" "the flight read the new query"
                calls[1].Reply.SetException(TimeoutException "timed out")

                match hits.TryValue with
                | Failed e -> Expect.equal e.Message "timed out" "the fault is the original exception"
                | other -> failtestf "expected Failed, got %A" other

            testCase "an async source settles and fails by hand"
            <| fun () ->
                use graph = newGraph FlightPolicy.CancelPrevious

                let user, greeting =
                    graph.Run (fun () ->
                        let user = createAsyncSource<string>()
                        user, createMemo (fun _ -> "Hello, " + user.Value))

                Expect.equal greeting.TryValue Pending "nothing settled yet"
                let offline = exn "offline"
                user.Fail offline
                Expect.equal greeting.TryValue (Failed offline) "the failure reaches the memo"
                user.Settle "Ada"
                Expect.equal greeting.TryValue (Ready "Hello, Ada") "a settle clears the failure"

            testCase "CancelPrevious discards the superseded flight"
            <| fun () ->
                use graph = newGraph FlightPolicy.CancelPrevious
                let query, calls, hits = search graph

                hits.TryValue |> ignore
                query.Value <- "b"
                hits.TryValue |> ignore
                Expect.isTrue calls[0].Token.IsCancellationRequested "the superseded token is cancelled"

                calls[0].Reply.SetResult [ "a1" ]
                Expect.equal hits.TryValue Pending "the superseded result is discarded"
                calls[1].Reply.SetResult [ "b1" ]
                Expect.equal hits.TryValue (Ready [ "b1" ]) "the newest flight lands"

            testCase "Queue applies every flight in start order"
            <| fun () ->
                use graph = newGraph FlightPolicy.Queue
                let query, calls, hits = search graph
                let seen = ResizeArray<string list>()
                graph.Run (fun () -> createEffect (fun () -> seen.Add hits.Value))

                query.Value <- "b"
                calls[1].Reply.SetResult [ "b1" ]
                Expect.isEmpty seen "the second result waits for the first"
                calls[0].Reply.SetResult [ "a1" ]
                Expect.equal (List.ofSeq seen) [ [ "a1" ]; [ "b1" ] ] "both results, in start order"

            testCase "a boundary shows its fallback while a flight is in progress"
            <| fun () ->
                use graph = newGraph FlightPolicy.CancelPrevious
                let query, calls, hits = search graph

                let results =
                    graph.Run (fun () -> createSuspense (fun last -> ValueOption.defaultValue [] last) (fun () -> hits.Value))

                Expect.equal (results.Value, results.IsWaiting) ([], true) "the fallback before the first result"
                calls[0].Reply.SetResult [ "a1" ]
                Expect.equal (results.Value, results.IsWaiting) ([ "a1" ], false) "the body's value"
                query.Value <- "b"
                Expect.equal (results.Value, results.IsWaiting) ([ "a1" ], true) "the last value while refreshing"
        ]
