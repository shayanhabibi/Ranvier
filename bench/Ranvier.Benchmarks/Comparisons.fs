module Ranvier.Benchmarks.Comparisons

open System
open BenchmarkDotNet.Attributes
open FSharp.Data.Adaptive
open Ranvier

/// <summary>
/// What the competition costs for the work this library also does.
/// </summary>
/// <remarks>
/// <para>
/// Read these with the caveat attached, because the libraries are not the same
/// shape:
/// </para>
/// <para>
/// - FSharp.Data.Adaptive is the true peer: pull-based, cutoff, a graph
///   that recomputes on read. Its writes go through <c>transact</c>, which is a real
///   part of its model and so is measured as part of its write.
/// - R3 and System.Reactive are push-based streams. A derived value is
///   a <c>Select</c> plus a subscription that holds the latest, which is a different
///   thing from a memo — it recomputes eagerly whether or not anyone reads, and
///   nothing deduplicates a diamond. They are here because they are what a .NET
///   developer reaches for today, not because the comparison is apples to
///   apples.
/// - Manual is the floor: a field and a function, recomputed on every read.
///   Any reactive library that loses to it on a given shape is not paying for
///   itself on that shape.
/// </para>
/// <para>
/// None of the four has a pending channel, so suspension — the thing this
/// library exists for — has no column here. That is the honest summary of the
/// comparison: on the overlap we should be competitive, and off the overlap
/// there is nothing to compare against.
/// </para>
/// </remarks>
[<MemoryDiagnoser; BenchmarkCategory "Comparison">]
type DerivedValueComparison() =
    let graph = new Graph ()
    let signal = Signal (graph, 1)
    let memo = Memo (graph, (fun _ -> signal.Value * 2))

    let changeable = cval 1
    let adaptive = AVal.map (fun v -> v * 2) changeable

    let reactiveProperty = new R3.ReactiveProperty<int> (1)
    let mutable r3Latest = 0
    let mutable r3Subscription: IDisposable = null

    let subject = new System.Reactive.Subjects.BehaviorSubject<int> (1)
    let mutable rxLatest = 0
    let mutable rxSubscription: IDisposable = null

    let mutable manual = 1
    let mutable counter = 0

    [<GlobalSetup>]
    member _.Setup() =
        memo.TryValue |> ignore
        AVal.force adaptive |> ignore

        r3Subscription <-
            R3.ObservableSubscribeExtensions.Subscribe (R3.ObservableExtensions.Select (reactiveProperty, (fun v -> v * 2)), fun v -> r3Latest <- v)

        rxSubscription <- System.Reactive.Linq.Observable.Select(subject, (fun v -> v * 2)).Subscribe(fun v -> rxLatest <- v)

    [<GlobalCleanup>]
    member _.Cleanup() =
        if not (isNull r3Subscription) then
            r3Subscription.Dispose ()

        if not (isNull rxSubscription) then
            rxSubscription.Dispose ()

        reactiveProperty.Dispose ()
        subject.Dispose ()
        graph.Dispose ()

    /// <summary>
    /// Write, then read the derived value. The whole round trip, which is the
    /// only fair unit when one side computes on write and the other on read.
    /// </summary>
    [<Benchmark(Baseline = true)>]
    member _.Ranvier() =
        counter <- counter + 1
        signal.Value <- counter
        memo.Value

    [<Benchmark>]
    member _.Adaptive() =
        counter <- counter + 1
        transact (fun () -> changeable.Value <- counter)
        AVal.force adaptive

    [<Benchmark>]
    member _.R3() =
        counter <- counter + 1
        reactiveProperty.Value <- counter
        r3Latest

    [<Benchmark>]
    member _.Rx() =
        counter <- counter + 1
        subject.OnNext counter
        rxLatest

    [<Benchmark>]
    member _.Manual() =
        counter <- counter + 1
        manual <- counter
        manual * 2

/// <summary>
/// Depth is where the models diverge most: a pull graph only recomputes what is
/// read, a push graph recomputes everything it has been told about. At depth 1
/// that difference is invisible.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Comparison">]
type ChainComparison() =
    let graph = new Graph ()
    let signal = Signal (graph, 1)
    let mutable memoTail: Memo<int> = Unchecked.defaultof<Memo<int>>

    let changeable = cval 1
    let mutable adaptiveTail: aval<int> = Unchecked.defaultof<aval<int>>

    let reactiveProperty = new R3.ReactiveProperty<int> (1)
    let mutable r3Latest = 0
    let mutable r3Subscription: IDisposable = null

    let mutable counter = 0

    [<Params(1, 4, 16)>]
    member val Depth = 1 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        let mutable memoPrevious = Memo (graph, (fun _ -> signal.Value + 1))
        let mutable adaptivePrevious = AVal.map (fun v -> v + 1) changeable

        let mutable r3Previous =
            R3.ObservableExtensions.Select (reactiveProperty, (fun v -> v + 1))

        for _ in 2 .. this.Depth do
            let inner = memoPrevious
            memoPrevious <- Memo (graph, (fun _ -> inner.Value + 1))
            adaptivePrevious <- AVal.map (fun v -> v + 1) adaptivePrevious
            r3Previous <- R3.ObservableExtensions.Select (r3Previous, (fun v -> v + 1))

        memoTail <- memoPrevious
        adaptiveTail <- adaptivePrevious

        memoTail.TryValue |> ignore
        AVal.force adaptiveTail |> ignore
        r3Subscription <- R3.ObservableSubscribeExtensions.Subscribe (r3Previous, (fun v -> r3Latest <- v))

    [<GlobalCleanup>]
    member _.Cleanup() =
        if not (isNull r3Subscription) then
            r3Subscription.Dispose ()

        reactiveProperty.Dispose ()
        graph.Dispose ()

    [<Benchmark(Baseline = true)>]
    member _.Ranvier() =
        counter <- counter + 1
        signal.Value <- counter
        memoTail.Value

    [<Benchmark>]
    member _.Adaptive() =
        counter <- counter + 1
        transact (fun () -> changeable.Value <- counter)
        AVal.force adaptiveTail

    [<Benchmark>]
    member _.R3() =
        counter <- counter + 1
        reactiveProperty.Value <- counter
        r3Latest

/// <summary>
/// Writing a value equal to the current one. A graph with cutoff should notice
/// and stop; a stream has no reason to. This is the shape where the pull model
/// earns its keep, and it is worth knowing by how much.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Comparison">]
type CutoffComparison() =
    let graph = new Graph ()
    let signal = Signal (graph, 1)
    let memo = Memo (graph, (fun _ -> signal.Value * 2))

    let changeable = cval 1
    let adaptive = AVal.map (fun v -> v * 2) changeable

    let reactiveProperty = new R3.ReactiveProperty<int> (1)
    let mutable r3Latest = 0
    let mutable r3Subscription: IDisposable = null

    [<GlobalSetup>]
    member _.Setup() =
        memo.TryValue |> ignore
        AVal.force adaptive |> ignore

        r3Subscription <-
            R3.ObservableSubscribeExtensions.Subscribe (R3.ObservableExtensions.Select (reactiveProperty, (fun v -> v * 2)), fun v -> r3Latest <- v)

    [<GlobalCleanup>]
    member _.Cleanup() =
        if not (isNull r3Subscription) then
            r3Subscription.Dispose ()

        reactiveProperty.Dispose ()
        graph.Dispose ()

    [<Benchmark(Baseline = true)>]
    member _.Ranvier() =
        signal.Value <- 1
        memo.Value

    [<Benchmark>]
    member _.Adaptive() =
        transact (fun () -> changeable.Value <- 1)
        AVal.force adaptive

    [<Benchmark>]
    member _.R3() =
        reactiveProperty.Value <- 1
        r3Latest

[<Literal>]
let private FanOutWidth = 100

/// <summary>
/// One source, 100 derived readers. A write reaches every reader, and the
/// round trip reads all of them, so each library pays for the full width.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Comparison">]
type FanOutComparison() =
    let graph = new Graph ()
    let signal = Signal (graph, 1)

    let memos =
        Array.init FanOutWidth (fun i -> Memo (graph, (fun _ -> signal.Value + i)))

    let changeable = cval 1

    let adaptives =
        Array.init FanOutWidth (fun i -> AVal.map (fun v -> v + i) changeable)

    let reactiveProperty = new R3.ReactiveProperty<int> (1)
    let r3Latest = Array.zeroCreate<int> FanOutWidth
    let mutable r3Subscriptions: IDisposable array = [||]

    let subject = new System.Reactive.Subjects.BehaviorSubject<int> (1)
    let rxLatest = Array.zeroCreate<int> FanOutWidth
    let mutable rxSubscriptions: IDisposable array = [||]

    let mutable counter = 0

    [<GlobalSetup>]
    member _.Setup() =
        for memo in memos do
            memo.TryValue |> ignore

        for adaptive in adaptives do
            AVal.force adaptive |> ignore

        r3Subscriptions <-
            Array.init FanOutWidth (fun i ->
                R3.ObservableSubscribeExtensions.Subscribe (
                    R3.ObservableExtensions.Select (reactiveProperty, (fun v -> v + i)),
                    fun v -> r3Latest[i] <- v
                ))

        rxSubscriptions <-
            Array.init FanOutWidth (fun i -> System.Reactive.Linq.Observable.Select(subject, (fun v -> v + i)).Subscribe(fun v -> rxLatest[i] <- v))

    [<GlobalCleanup>]
    member _.Cleanup() =
        for subscription in r3Subscriptions do
            subscription.Dispose ()

        for subscription in rxSubscriptions do
            subscription.Dispose ()

        reactiveProperty.Dispose ()
        subject.Dispose ()
        graph.Dispose ()

    /// <summary>
    /// Write, then read every derived value.
    /// </summary>
    [<Benchmark(Baseline = true)>]
    member _.Ranvier() =
        counter <- counter + 1
        signal.Value <- counter
        let mutable sum = 0

        for memo in memos do
            sum <- sum + memo.Value

        sum

    [<Benchmark>]
    member _.Adaptive() =
        counter <- counter + 1
        transact (fun () -> changeable.Value <- counter)
        let mutable sum = 0

        for adaptive in adaptives do
            sum <- sum + AVal.force adaptive

        sum

    [<Benchmark>]
    member _.R3() =
        counter <- counter + 1
        reactiveProperty.Value <- counter
        let mutable sum = 0

        for latest in r3Latest do
            sum <- sum + latest

        sum

    [<Benchmark>]
    member _.Rx() =
        counter <- counter + 1
        subject.OnNext counter
        let mutable sum = 0

        for latest in rxLatest do
            sum <- sum + latest

        sum

/// <summary>
/// The diamond: <c>a -> b</c>, <c>a -> c</c>, <c>d = f(b, c)</c>. Each case writes
/// <c>a</c> and reads the latest <c>d</c>.
/// </summary>
/// <remarks>
/// The streams build <c>d</c> with <c>CombineLatest</c>, which emits once per
/// upstream emission. A write to <c>a</c> therefore runs <c>d</c> twice in R3 and
/// System.Reactive, and the first of the two pairs the new <c>b</c> with the old
/// <c>c</c> (a glitch). Ranvier and FSharp.Data.Adaptive run <c>d</c> once per
/// write, with both inputs current.
/// </remarks>
[<MemoryDiagnoser; BenchmarkCategory "Comparison">]
type DiamondComparison() =
    let graph = new Graph ()
    let signal = Signal (graph, 1)
    let left = Memo (graph, (fun _ -> signal.Value + 1))
    let right = Memo (graph, (fun _ -> signal.Value * 2))
    let join = Memo (graph, (fun _ -> left.Value + right.Value))

    let changeable = cval 1
    let adaptiveLeft = AVal.map (fun v -> v + 1) changeable
    let adaptiveRight = AVal.map (fun v -> v * 2) changeable
    let adaptiveJoin = AVal.map2 (+) adaptiveLeft adaptiveRight

    let reactiveProperty = new R3.ReactiveProperty<int> (1)
    let mutable r3Latest = 0
    let mutable r3Subscription: IDisposable = null

    let subject = new System.Reactive.Subjects.BehaviorSubject<int> (1)
    let mutable rxLatest = 0
    let mutable rxSubscription: IDisposable = null

    let mutable counter = 0

    [<GlobalSetup>]
    member _.Setup() =
        join.TryValue |> ignore
        AVal.force adaptiveJoin |> ignore

        let r3Left = R3.ObservableExtensions.Select (reactiveProperty, (fun v -> v + 1))
        let r3Right = R3.ObservableExtensions.Select (reactiveProperty, (fun v -> v * 2))

        r3Subscription <-
            R3.ObservableSubscribeExtensions.Subscribe (R3.Observable.CombineLatest (r3Left, r3Right, (fun l r -> l + r)), fun v -> r3Latest <- v)

        let rxLeft = System.Reactive.Linq.Observable.Select (subject, (fun v -> v + 1))
        let rxRight = System.Reactive.Linq.Observable.Select (subject, (fun v -> v * 2))

        rxSubscription <- System.Reactive.Linq.Observable.CombineLatest(rxLeft, rxRight, (fun l r -> l + r)).Subscribe(fun v -> rxLatest <- v)

    [<GlobalCleanup>]
    member _.Cleanup() =
        if not (isNull r3Subscription) then
            r3Subscription.Dispose ()

        if not (isNull rxSubscription) then
            rxSubscription.Dispose ()

        reactiveProperty.Dispose ()
        subject.Dispose ()
        graph.Dispose ()

    [<Benchmark(Baseline = true)>]
    member _.Ranvier() =
        counter <- counter + 1
        signal.Value <- counter
        join.Value

    [<Benchmark>]
    member _.Adaptive() =
        counter <- counter + 1
        transact (fun () -> changeable.Value <- counter)
        AVal.force adaptiveJoin

    [<Benchmark>]
    member _.R3() =
        counter <- counter + 1
        reactiveProperty.Value <- counter
        r3Latest

    [<Benchmark>]
    member _.Rx() =
        counter <- counter + 1
        subject.OnNext counter
        rxLatest
