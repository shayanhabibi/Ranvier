module Ranvier.Tests.QuerySupport

open Expecto
open Ranvier
open System.Threading.Tasks

/// <summary>An isolated graph for asynchronous tests that resume on different test-runner threads.</summary>
let newGraph () =
    new Graph (
        { GraphOptions.Default with
            ThreadAffinity = ThreadAffinity.Unchecked
            Dispatcher = Some (ImmediateDispatcher ())
        }
    )

/// <summary>Yields until a controlled completion has been applied, failing after five seconds.</summary>
let waitUntil condition =
    async {
        let mutable attempts = 0

        while not (condition ()) && attempts < 5000 do
            do! Async.Sleep 1
            attempts <- attempts + 1

        Expect.isTrue (condition ()) "controlled request completion was applied"
    }

/// <summary>Awaits a mutation outcome with a bounded completion check on .NET.</summary>
let awaitOutcome (pending: Task<'T>) =
    async {
#if !FABLE_COMPILER
        do! waitUntil (fun () -> pending.IsCompleted)
#endif
        return! Async.AwaitTask pending
    }
