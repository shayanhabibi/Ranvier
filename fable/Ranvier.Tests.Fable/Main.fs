module Ranvier.Tests.Fable.Main

open Fable.Core
open Fable.Mocha

/// <summary>
/// Sets how a test's <c>TaskCompletionSource</c> delivers: <c>"inline"</c> or <c>"promise"</c>. The sample is a
/// <c>TaskCompletionSource</c> of the compiled Fable library.
/// </summary>
[<Import("install", "./Delivery.js")>]
let private install (mode: string) (source: obj) : string = jsNative

[<Emit("process.env.RANVIER_FABLE_DELIVERY ?? 'promise'")>]
let private requested: string = jsNative

install requested (System.Threading.Tasks.TaskCompletionSource<unit>())
|> ignore

// Every [<Tests>] value of tests/Ranvier.Tests, in compile order, under the name of its file.
let all =
    testList
        "Ranvier"
        [
            testList "Tests.fs" [ Ranvier.Tests.Diamond.tests ]
            testList "Effects.fs" [ Ranvier.Tests.Effects.tests ]
            testList "EffectSplit.fs" [ Ranvier.Tests.EffectSplit.tests ]
            testList "AmbientGraph.fs" [ Ranvier.Tests.AmbientGraph.tests ]
            testList "Owners.fs" [ Ranvier.Tests.Owners.tests ]
            testList "Equality.fs" [ Ranvier.Tests.Equality.tests ]
            testList "Async.fs" [ Ranvier.Tests.Async.tests ]
            testList "Boundaries.fs" [ Ranvier.Tests.Boundaries.tests ]
            testList "Threading.fs" [ Ranvier.Tests.Threading.tests ]
            testList "Edges.fs" [ Ranvier.Tests.Edges.tests ]
            testList "Invalidation.fs" [ Ranvier.Tests.Invalidation.tests ]
            testList "Lifetime.fs" [ Ranvier.Tests.Lifetime.tests ]
            testList "Retention.fs" [ Ranvier.Tests.Retention.tests ]
            testList "Tracing.fs" [ Ranvier.Tests.Tracing.tests ]
            testList "TraceModelTests.fs" [ Ranvier.Tests.TraceModelTests.tests ]
            testList "Observers.fs" [ Ranvier.Tests.Observers.tests; Ranvier.Tests.Observers.sourceTests ]
            testList "Api.fs" [ Ranvier.Tests.Api.tests ]
            testList "FanOut.fs" [ Ranvier.Tests.FanOut.tests ]
            testList "Reentrancy.fs" [ Ranvier.Tests.Reentrancy.tests ]
            testList "Cutoff.fs" [ Ranvier.Tests.Cutoff.tests ]
            testList "SuspensionEdges.fs" [ Ranvier.Tests.SuspensionEdges.tests ]
            testList "Scopes.fs" [ Ranvier.Tests.Scopes.tests ]
            testList "Propagation.fs" [ Ranvier.Tests.Propagation.tests ]
            testList "Reads.fs" [ Ranvier.Tests.Reads.tests ]
            testList "Batching.fs" [ Ranvier.Tests.Batching.tests ]
            testList "AsyncEdges.fs" [ Ranvier.Tests.AsyncEdges.tests ]
            testList "Projections.fs" [ Ranvier.Tests.Projections.tests; Ranvier.Tests.Projections.summaryTests ]
            testList "Lookups.fs" [ Ranvier.Tests.Lookups.tests ]
            testList "MemoScopes.fs" [ Ranvier.Tests.MemoScopes.tests ]
            testList "MemoPurity.fs" [ Ranvier.Tests.MemoPurity.tests ]
            testList "ScopeContexts.fs" [ Ranvier.Tests.ScopeContexts.tests ]
            testList "DischargeReentry.fs" [ Ranvier.Tests.DischargeReentry.tests ]
            testList "MapSemantics.fs" [ Ranvier.Tests.MapSemantics.tests ]
            testList "Lenses.fs" [ Ranvier.Tests.Lenses.tests ]
            testList
                "Combinators.fs"
                [
                    Ranvier.Tests.Combinators.filterTests
                    Ranvier.Tests.Combinators.chooseTests
                    Ranvier.Tests.Combinators.mapTests
                    Ranvier.Tests.Combinators.sortByTests
                    Ranvier.Tests.Combinators.sliceTests
                    Ranvier.Tests.Combinators.chainTests
                    Ranvier.Tests.Combinators.mapWithTests
                    Ranvier.Tests.Combinators.groupByTests
                    Ranvier.Tests.Combinators.chainPendingTests
                    Ranvier.Tests.Combinators.foldTests
                ]
            testList "PreviousValues.fs" [ Ranvier.Tests.PreviousValues.tests; Ranvier.Tests.PreviousValues.asyncTests ]
            testList "Editables.fs" [ Ranvier.Tests.Editables.tests ]
            testList "MvuBridge.fs" [ Ranvier.Tests.MvuBridge.tests ]
        ]

Mocha.runTests all |> ignore
