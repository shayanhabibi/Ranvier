namespace Ranvier

#if RANVIER_COUNTERS
#if FABLE_COMPILER
/// <summary>
/// A JavaScript number: <c>int64</c> compiles to <c>bigint</c>, and every <c>bigint</c>
/// increment allocates.
/// </summary>
type internal Tally = float
#else
type internal Tally = int64
#endif

module internal Tallies =
    let one: Tally = LanguagePrimitives.GenericOne
    let zero: Tally = LanguagePrimitives.GenericZero

/// <summary>
/// Process-wide counts of graph operations, compiled in by the MSBuild
/// property <c>RanvierCounters=true</c>. Each count is a plain static increment:
/// exact while a single thread mutates graphs, and undefined otherwise.
/// </summary>
[<AbstractClass; Sealed>]
type Counters =
    [<DefaultValue>]
    static val mutable private signalsCreated: Tally

    [<DefaultValue>]
    static val mutable private memosCreated: Tally

    [<DefaultValue>]
    static val mutable private effectsCreated: Tally

    [<DefaultValue>]
    static val mutable private ownersCreated: Tally

    [<DefaultValue>]
    static val mutable private edgesAdded: Tally

    [<DefaultValue>]
    static val mutable private edgesRemoved: Tally

    [<DefaultValue>]
    static val mutable private observerInserts: Tally

    [<DefaultValue>]
    static val mutable private observerRemoves: Tally

    [<DefaultValue>]
    static val mutable private memoRecomputes: Tally

    [<DefaultValue>]
    static val mutable private effectRuns: Tally

    [<DefaultValue>]
    static val mutable private flushes: Tally

    static member internal SignalCreated() =
        Counters.signalsCreated <- Counters.signalsCreated + Tallies.one

    static member internal MemoCreated() =
        Counters.memosCreated <- Counters.memosCreated + Tallies.one

    static member internal EffectCreated() =
        Counters.effectsCreated <- Counters.effectsCreated + Tallies.one

    static member internal OwnerCreated() =
        Counters.ownersCreated <- Counters.ownersCreated + Tallies.one

    static member internal EdgeAdded() =
        Counters.edgesAdded <- Counters.edgesAdded + Tallies.one

    static member internal EdgeRemoved() =
        Counters.edgesRemoved <- Counters.edgesRemoved + Tallies.one

    static member internal ObserverInserted() =
        Counters.observerInserts <- Counters.observerInserts + Tallies.one

    static member internal ObserverRemoved() =
        Counters.observerRemoves <- Counters.observerRemoves + Tallies.one

    static member internal MemoRecomputed() =
        Counters.memoRecomputes <- Counters.memoRecomputes + Tallies.one

    static member internal EffectRan() =
        Counters.effectRuns <- Counters.effectRuns + Tallies.one

    static member internal Flushed() =
        Counters.flushes <- Counters.flushes + Tallies.one

    /// <summary>
    /// Sets every count to zero.
    /// </summary>
    static member Reset() =
        Counters.signalsCreated <- Tallies.zero
        Counters.memosCreated <- Tallies.zero
        Counters.effectsCreated <- Tallies.zero
        Counters.ownersCreated <- Tallies.zero
        Counters.edgesAdded <- Tallies.zero
        Counters.edgesRemoved <- Tallies.zero
        Counters.observerInserts <- Tallies.zero
        Counters.observerRemoves <- Tallies.zero
        Counters.memoRecomputes <- Tallies.zero
        Counters.effectRuns <- Tallies.zero
        Counters.flushes <- Tallies.zero

    /// <summary>
    /// Every count as a name-value pair, in a fixed order. <c>Owner</c> counts every
    /// scope: explicit owners, roots and the scopes of computation runs.
    /// <c>EdgesAdded</c> and <c>EdgesRemoved</c> count entries in a computation's source
    /// list; <c>ObserverInserts</c> and <c>ObserverRemoves</c> count entries in a
    /// source's observer set. A re-run that reads its sources in the same order
    /// leaves all four unchanged. <c>Flushes</c> counts drains of the effect queue.
    /// </summary>
    static member Snapshot() : (string * int64)[] =
        [|
            "SignalsCreated", int64 Counters.signalsCreated
            "MemosCreated", int64 Counters.memosCreated
            "EffectsCreated", int64 Counters.effectsCreated
            "OwnersCreated", int64 Counters.ownersCreated
            "EdgesAdded", int64 Counters.edgesAdded
            "EdgesRemoved", int64 Counters.edgesRemoved
            "ObserverInserts", int64 Counters.observerInserts
            "ObserverRemoves", int64 Counters.observerRemoves
            "MemoRecomputes", int64 Counters.memoRecomputes
            "EffectRuns", int64 Counters.effectRuns
            "Flushes", int64 Counters.flushes
        |]
#endif
