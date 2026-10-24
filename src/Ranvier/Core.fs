namespace Ranvier

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks

#if FABLE_COMPILER
module private FableLocks =
    let inline lock (_target: obj) ([<InlineIfLambda>] action: unit -> 'T) : 'T = action ()

open FableLocks
#endif

/// <summary>
/// Dirtiness — the axis every reactive graph already has. Deliberately separate
/// from <c>Status</c>, which carries pending and error. Solid 2's contribution is
/// that the second axis exists and propagates; collapsing them into one ordered
/// enum is what stops every prior implementation short.
/// </summary>
/// <remarks>
/// An enum rather than a union, and measured rather than assumed: <c>=</c> on a
/// three-case struct DU goes through a generated <c>Equals</c> that does not inline,
/// and a clean tracked read — the most travelled path in the library — pays
/// that comparison on every read. Nothing pattern-matches on this type, so the
/// exhaustiveness <c>WarningsAsErrors 0025</c> would buy is not being given up.
/// </remarks>
type internal Freshness =
    | Clean = 0uy
    /// <summary>
    /// Maybe-dirty. Something above this node changed, but not necessarily in a
    /// way that moved the value it reads. Solid's <c>STATE_CHECK</c>.
    /// </summary>
    /// <remarks>
    /// This is what makes a cutoff work below the first derivation. Dirtiness
    /// has to propagate on the write, before any memo has recomputed and so
    /// before anyone knows whether values moved; without a third state the only
    /// thing a transitive dependent can be told is "dirty", and it re-runs
    /// whether or not anything it reads actually changed. <c>Check</c> is what it is
    /// told instead: ask your sources before you run.
    /// </remarks>
    | Check = 1uy
    | Dirty = 2uy

module internal Staleness =
    /// <summary><c>Status.Stale</c> when the node is not <c>Clean</c>.</summary>
    let inline ofFreshness (freshness: Freshness) =
        if freshness = Freshness.Clean then
            Status.None
        else
            Status.Stale

/// <summary>
/// A node that can be depended upon (AKA IObservable).
/// </summary>
/// <remarks>
/// Each edge has a handle: the observer's position in the source's observer list. The observer stores the handle
/// beside its own record of the edge, at a position of its choosing called the slot, and removes the edge by handle.
/// A swap-remove that moves an observer calls its <c>MovedIn</c> with the slot and the new handle.
/// </remarks>
type internal ISource =
    inherit INode
    /// <summary>
    /// Links an observer the caller holds no edge for, and returns the edge's handle. <c>MovedIn</c> receives
    /// <c>slot</c> back.
    /// </summary>
    abstract AddObserver: observer: IComputation * slot: int -> int

    /// <summary>
    /// <c>AddObserver</c> for an observer that may already be linked. Returns <c>EdgeHandle.None</c>, linking
    /// nothing, when the observer is linked, and <c>EdgeHandle.Unknown</c>, linking nothing, when the answer would
    /// take a scan of more than <c>EdgeHandle.ScanLimit</c> observers.
    /// </summary>
    abstract TryAddObserver: observer: IComputation * slot: int -> int

    /// <summary>
    /// Unlinks the observer at <c>handle</c>. <c>EdgeHandle.None</c> does nothing. Raises
    /// <c>InvalidOperationException</c> when another observer holds the handle. The caller then stores
    /// <c>EdgeHandle.None</c> as the handle.
    /// </summary>
    abstract RemoveObserverAt: observer: IComputation * handle: int -> unit

    /// <summary>
    /// Brings this source up to date if it is stale, so an observer resolving
    /// its own <c>Check</c> gets a definite answer. A no-op on sources that cannot
    /// be stale — a signal's value is whatever was last written to it.
    /// </summary>
    /// <remarks>
    /// A source that recomputes to a different value marks its observers dirty on the way out;
    /// a source whose value did not move says nothing, and leaves the asker in <c>Check</c>.
    /// </remarks>
    abstract UpdateIfNecessary: unit -> unit

/// <summary>
/// A node that depends on sources, and therefore can be invalidated.
/// </summary>
and internal IComputation =
    inherit INode
    /// <summary>
    /// Noop if already <c>Dirty</c>.
    /// </summary>
    abstract MarkDirty: unit -> unit

    /// <summary>
    /// A <b>source</b> of this node may have changed. Does nothing if this node is
    /// already <c>Check</c> or <c>Dirty</c>: <c>Dirty</c> is strictly stronger information and
    /// must never be lost to a later <c>Check</c>.
    /// </summary>
    abstract MarkCheck: unit -> unit
    abstract AddSource: ISource -> unit

    /// <summary>
    /// The edge this node recorded at <c>slot</c> now has <c>handle</c>. Called by a source's swap-remove, possibly
    /// during a notification walk.
    /// </summary>
    abstract MovedIn: slot: int * handle: int -> unit

/// <summary>
/// Something the scheduler can run. Memos are pull-based and never need this —
/// they recompute when read. Effects are the push side: nothing reads them, so
/// something has to run them.
/// </summary>
and internal IScheduled =
    abstract Execute: unit -> unit

/// <summary>
/// Edge handle values that name no position, and the scan bound shared by both ends of an edge.
/// </summary>
module internal EdgeHandle =
    /// <summary>The handle of a missing edge, and of a slot that shares an earlier slot's edge.</summary>
    [<Literal>]
    let Unlinked = -1

    /// <summary>The <c>TryAddObserver</c> result that leaves the link to the caller.</summary>
    [<Literal>]
    let Unknown = -2

    /// <summary>The longest list either end of an edge scans for a repeated link.</summary>
    [<Literal>]
    let ScanLimit = 8

/// <summary>
/// One slot in an observer list.
/// </summary>
/// <remarks>
/// On .NET, a struct slot holds the observer and its slot, and avoids covariant interface-array store checks.
/// Under Fable, the slot is the observer reference itself, and the observer's slot is kept in a parallel array.
/// Replace whole slots when updating the array.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Erase; NoEquality; NoComparison>]
type internal ObserverSlot =
    | ObserverSlot of IComputation

    member inline this.Observer =
        let (ObserverSlot observer) = this
        observer
#else
[<Struct; NoEquality; NoComparison>]
type internal ObserverSlot =
    val mutable Observer: IComputation

    /// <summary>The position the observer recorded the edge at, passed back in <c>MovedIn</c>.</summary>
    val mutable Slot: int

    new(observer: IComputation, slot: int) = { Observer = observer; Slot = slot }
#endif

module internal ObserverSlots =
    /// <summary>
    /// The zero-length list an observer set holds until its first observer.
    /// </summary>
    let none: ObserverSlot[] = [||]

#if FABLE_COMPILER
    /// <summary>
    /// The zero-length slot list an observer set holds until its first observer.
    /// </summary>
    let noSlots: int[] = Platform.positions 0
#endif

/// <summary>
/// The observers of one source, and the only safe way to notify them.
/// </summary>
/// <remarks>
/// An array rather than a <c>HashSet</c>, because notification is the hot half and
/// a set cannot be walked while it is mutated.
/// <para>
/// Each observer is held at most once. <c>Add</c> appends unconditionally; <c>TryAdd</c> answers a repeat from the
/// latest add's handle or from a scan of a list of at most <c>EdgeHandle.ScanLimit</c>. A position is an edge's handle,
/// and removal goes by handle, checked against the observer.
/// </para>
/// </remarks>
type internal ObserverSet() =
#if FABLE_COMPILER
    let mutable items: ObserverSlot[] = ObserverSlots.none

    /// <summary>
    /// The slot of each observer. Grown and blanked together with <c>items</c>, and never read past <c>count</c>.
    /// </summary>
    let mutable slots: int[] = ObserverSlots.noSlots
#else
    let mutable items: ObserverSlot[] = ObserverSlots.none
#endif
    let mutable count = 0

    /// <summary>
    /// The handle the latest add returned, moved along when a swap-remove moves its observer. Holds no reference, so
    /// a hit is checked against the observer at the handle.
    /// </summary>
    let mutable latest = 0

#if RANVIER_TRACE
    let mutable traceLog: TraceLog = null
    let mutable traceId = 0
#endif

    /// <summary>
    /// Doubles the capacity of every array, a first observer getting a single slot: most sources keep exactly one.
    /// </summary>
    let grow () =
        let size = if count = 0 then 1 else max 4 (count * 2)
#if FABLE_COMPILER
        let grownObservers = Array.zeroCreate size
        let grownSlots = Platform.positions size
        Array.blit items 0 grownObservers 0 count
        Platform.copyPositions slots grownSlots count
        items <- grownObservers
        slots <- grownSlots
#else
        let grown = Array.zeroCreate size
        Array.blit items 0 grown 0 count
        items <- grown
#endif

    /// <summary>
    /// How many observers this source has. Diagnostic: duplication in here is
    /// invisible in what the graph computes — a second dirty mark on a dirty
    /// node is a no-op — so nothing short of counting can assert against it.
    /// </summary>
    member _.Count = count

    /// <summary>
    /// Appends <c>c</c>, recording <c>slot</c> for its <c>MovedIn</c>, and returns its handle.
    /// </summary>
    member this.Add(c: IComputation, slot: int) =
        if count = items.Length then
            grow ()

        let handle = count
#if FABLE_COMPILER
        Platform.setItemAt handle items (ObserverSlot c)
        Platform.setItemAt handle slots slot
#else
        items[handle] <- ObserverSlot (c, slot)
#endif
        count <- handle + 1
        latest <- handle
#if RANVIER_COUNTERS
        Counters.ObserverInserted ()
#endif
        Tracer.ObserverAdd (this, c)
        handle

    /// <summary>
    /// <c>Add</c> unless <c>c</c> is held: <c>EdgeHandle.Unlinked</c> when it is, and <c>EdgeHandle.Unknown</c>, adding
    /// nothing, when the latest add's handle misses on a list longer than <c>EdgeHandle.ScanLimit</c>.
    /// </summary>
    member this.TryAdd(c: IComputation, slot: int) =
        if count = 0 then
            this.Add (c, slot)
        elif
            latest < count
            && obj.ReferenceEquals ((Platform.itemAt latest items).Observer, c)
        then
            EdgeHandle.Unlinked
        elif count <= EdgeHandle.ScanLimit then
            let mutable i = 0

            while i < count
                  && not (obj.ReferenceEquals ((Platform.itemAt i items).Observer, c)) do
                i <- i + 1

            if i < count then
                EdgeHandle.Unlinked
            else
                this.Add (c, slot)
        else
            EdgeHandle.Unknown

    /// <summary>
    /// Swap-removes the observer at <c>handle</c>: the last observer fills the hole and is told its new handle, so
    /// order is not preserved. Nothing here depends on order — a dirty mark is a set operation — and preserving it
    /// would cost a shift on the path a conditional body takes every time it drops a branch.
    /// </summary>
    /// <remarks>
    /// <c>EdgeHandle.Unlinked</c> does nothing. A handle holding another observer raises
    /// <c>InvalidOperationException</c>, in every build.
    /// </remarks>
    member this.RemoveAt(c: IComputation, handle: int) =
        if handle >= 0 then
            if
                handle >= count
                || not (obj.ReferenceEquals ((Platform.itemAt handle items).Observer, c))
            then
                raise (
                    InvalidOperationException (
                        "Edge handle "
                        + string handle
                        + " of "
                        + string count
                        + " does not hold the observer removed."
                    )
                )

#if RANVIER_COUNTERS
            Counters.ObserverRemoved ()
#endif
            Tracer.ObserverRemove (this, c)
            let last = count - 1

            if handle = last then
                // Blanked so a dropped observer is not kept alive by a slot past
                // the end of the list.
#if FABLE_COMPILER
                Platform.setItemAt last items (ObserverSlot Unchecked.defaultof<IComputation>)
                Platform.setItemAt last slots EdgeHandle.Unlinked
#else
                items[last] <- ObserverSlot (Unchecked.defaultof<IComputation>, EdgeHandle.Unlinked)
#endif
                count <- last
            else
                let moved = (Platform.itemAt last items).Observer
#if FABLE_COMPILER
                let movedSlot = Platform.itemAt last slots
#else
                let movedSlot = (Platform.itemAt last items).Slot
#endif
#if FABLE_COMPILER
                Platform.setItemAt handle items (ObserverSlot moved)
                Platform.setItemAt handle slots movedSlot
#else
                items[handle] <- ObserverSlot (moved, movedSlot)
#endif
#if FABLE_COMPILER
                Platform.setItemAt last items (ObserverSlot Unchecked.defaultof<IComputation>)
                Platform.setItemAt last slots EdgeHandle.Unlinked
#else
                items[last] <- ObserverSlot (Unchecked.defaultof<IComputation>, EdgeHandle.Unlinked)
#endif
                count <- last

                if latest = last then
                    latest <- handle

                moved.MovedIn (movedSlot, handle)

    /// <summary>
    /// Marks every current observer dirty. Observers added during the loop
    /// are not notified: they were linked by a computation that has already
    /// re-read this source, so they are current by construction.
    /// </summary>
    /// <remarks>
    /// Walked backwards, and re-clamped each step, because <c>MarkDirty</c> may
    /// remove observers — including this one — from underneath the walk. A
    /// removal at or below the cursor moves an already-visited observer down
    /// into the hole, so nothing unvisited is skipped; the moved observer is
    /// visited twice, which is free because <c>MarkDirty</c> on an already-dirty
    /// node returns immediately. Forwards, the same removal would skip.
    /// </remarks>
    member this.NotifyDirty() =
        let mutable i = count - 1

        while i >= 0 do
            if i >= count then
                i <- count - 1

            if i >= 0 then
                Tracer.Mark (this, (Platform.itemAt i items).Observer, true)
                (Platform.itemAt i items).Observer.MarkDirty()
                i <- i - 1

    /// <summary>
    /// <c>NotifyDirty</c>, skipping one observer.
    /// </summary>
    /// <remarks>
    /// The exception is the computation whose body is running right now. It is
    /// the reason this source is recomputing — it read it — and it receives the
    /// new value from that very read, so it is current with respect to this
    /// source and marking it dirty would only make it re-run on a value it
    /// already has. Without the skip, every cutoff decision taken mid-body
    /// costs the reader a spurious second run.
    /// </remarks>
    member this.NotifyDirtyExcept(running: IComputation) =
        let mutable i = count - 1

        while i >= 0 do
            if i >= count then
                i <- count - 1

            if i >= 0 then
                let observer = (Platform.itemAt i items).Observer

                if not (obj.ReferenceEquals (observer, running)) then
                    Tracer.Mark (this, observer, true)
                    observer.MarkDirty ()
                else
                    Tracer.MarkSkip (this, observer)

                i <- i - 1

    /// <summary>
    /// <c>NotifyDirty</c>'s weaker sibling: tells every observer that something
    /// above it moved, without claiming that this node's own value did. Same
    /// backwards re-clamped walk, for the same reason.
    /// </summary>
    member this.NotifyCheck() =
        let mutable i = count - 1

        while i >= 0 do
            if i >= count then
                i <- count - 1

            if i >= 0 then
                Tracer.Mark (this, (Platform.itemAt i items).Observer, false)
                (Platform.itemAt i items).Observer.MarkCheck()
                i <- i - 1

#if RANVIER_TRACE
    interface ITraced with
        member _.TraceLog
            with get () = traceLog
            and set log = traceLog <- log

        member _.TraceId
            with get () = traceId
            and set id = traceId <- id

    interface ITracedEdges with
        member _.Owner = traceId
        member _.IsSources = false
        member _.Ids = Array.init count (fun i -> (Platform.itemAt i items).Observer.Id)
#endif

/// <summary>
/// An edge from a source to an observer shared by many sources. Forwards marks to the observer and holds the edge's
/// handle.
/// </summary>
/// <remarks>Reports the observer's id, so a trace names the observer.</remarks>
[<Sealed>]
type internal ForwardingEdge(target: IComputation) =
    member val Handle = EdgeHandle.Unlinked with get, set

    interface INode with
        member _.Id = target.Id
        member _.Status = target.Status

    interface IComputation with
        member _.AddSource _ = ()

        member _.MarkDirty() =
            target.MarkDirty ()

        member _.MarkCheck() =
            target.MarkCheck ()

        member this.MovedIn(_, handle) =
            this.Handle <- handle

/// <summary>
/// One slot in a computation's dependency list, with the same target-specific representation as <c>ObserverSlot</c>:
/// the source and its edge handle on .NET, the source alone under Fable.
/// </summary>
#if FABLE_COMPILER
[<Fable.Core.Erase; NoEquality; NoComparison>]
type internal SourceSlot =
    | SourceSlot of ISource

    member inline this.Source =
        let (SourceSlot source) = this
        source
#else
[<Struct; NoEquality; NoComparison>]
type internal SourceSlot =
    val mutable Source: ISource

    /// <summary>The handle of the slot's edge, or <c>EdgeHandle.Unlinked</c> for a source held by an earlier slot.</summary>
    val mutable Handle: int

    new(source: ISource, handle: int) = { Source = source; Handle = handle }
#endif

module internal SourceSlots =
    /// <summary>
    /// The zero-length list a source list holds until its second source.
    /// </summary>
    let none: SourceSlot[] = [||]

#if FABLE_COMPILER
    /// <summary>
    /// The zero-length handle list a source list holds until its second source.
    /// </summary>
    let noHandles: int[] = Platform.positions 0
#endif

/// <summary>
/// The sources one computation read on its last run, in the order it read
/// them.
/// </summary>
/// <remarks>
/// <para>
/// Order is the point. A body re-run after invalidation almost always reads
/// the same sources in the same order — a conditional that flips a branch is
/// the exception, not the rule — so the list is matched positionally against
/// the reads as they arrive, and an edge that is already in the right place
/// costs one reference comparison and nothing else. The alternative, which
/// this replaces, dropped every edge before the body and rebuilt it after:
/// measured at ~7 ns per edge per run, paid to arrive back where it started.
/// </para>
/// <para>
/// Divergence is handled by truncation rather than by patching: the first read
/// that does not match its slot drops that slot and everything after it, and
/// the rest of the run appends. A body that reads the same sources in a
/// different order therefore rebuilds all of them, which is the old cost and
/// no worse.
/// </para>
/// <para>
/// Each slot holds its edge's handle, and a slot's position is the slot the source passes back in <c>MovedIn</c>. A
/// source held by several slots has one edge, owned by its first slot; the later slots hold
/// <c>EdgeHandle.Unlinked</c>. Truncation drops an owning slot together with every later slot.
/// </para>
/// <para>
/// Upstream solves the same problem with a doubly-linked <c>Link</c> per edge, each
/// one simultaneously an entry in the source's subscriber list and in the
/// consumer's dependency list (<c>solid/packages/signals/src/core/graph.ts</c>,
/// after alien-signals), which also handles out-of-order re-reads by generation-stamping.
/// </para>
/// </remarks>
type internal SourceList() =
    /// <summary>
    /// The source at position 0.
    /// </summary>
    let mutable first: ISource = Unchecked.defaultof<ISource>

    /// <summary>
    /// The handle of position 0.
    /// </summary>
    let mutable firstHandle = EdgeHandle.Unlinked

    /// <summary>
    /// Sources after the first. <c>rest[i - 1]</c> is the source at position <c>i</c>.
    /// </summary>
    let mutable rest: SourceSlot[] = SourceSlots.none

#if FABLE_COMPILER
    /// <summary>
    /// The handle of each source in <c>rest</c>. Grown and blanked together with <c>rest</c>, and never read past
    /// <c>count</c>.
    /// </summary>
    let mutable restHandles: int[] = SourceSlots.noHandles
#endif

    let mutable count = 0

    /// <summary>
    /// How far into the list the current run has matched. Meaningless outside
    /// a run, which is why nothing reads it there.
    /// </summary>
    let mutable cursor = 0

    /// <summary>
    /// Every source in the list, for repeat checks over more than <c>EdgeHandle.ScanLimit</c> slots. Built by the first
    /// such check, extended by every later append and dropped by a trim; null otherwise.
    /// </summary>
    let mutable held: Platform.RefIndex<ISource> = null

#if RANVIER_TRACE
    let mutable traceLog: TraceLog = null
    let mutable traceId = 0
#endif

    let sourceAt (i: int) =
        if i = 0 then
            first
        else
            (Platform.itemAt (i - 1) rest).Source

    let handleAt (i: int) =
        if i = 0 then
            firstHandle
        else
#if FABLE_COMPILER
            Platform.itemAt (i - 1) restHandles
#else
            (Platform.itemAt (i - 1) rest).Handle
#endif

    let setHandle (i: int) (handle: int) =
        if i = 0 then
            firstHandle <- handle
        else
#if FABLE_COMPILER
            Platform.setItemAt (i - 1) restHandles handle
#else
            rest[i - 1].Handle <- handle
#endif

    /// <summary>
    /// Doubles the capacity of every array after the first position.
    /// </summary>
    let grow () =
        let size = max 4 (rest.Length * 2)
#if FABLE_COMPILER
        let grownSources = Array.zeroCreate size
        let grownHandles = Platform.positions size
        Array.blit rest 0 grownSources 0 rest.Length
        Platform.copyPositions restHandles grownHandles rest.Length
        rest <- grownSources
        restHandles <- grownHandles
#else
        let grown = Array.zeroCreate size
        Array.blit rest 0 grown 0 rest.Length
        rest <- grown
#endif

    /// <summary>
    /// Whether <c>source</c> holds a slot before <c>until</c>.
    /// </summary>
    let heldBefore (until: int) (source: ISource) =
        let mutable i = 0

        while i < until
              && not (obj.ReferenceEquals (sourceAt i, source)) do
            i <- i + 1

        i < until

    /// <summary>
    /// Whether <c>source</c> holds a slot before <c>until</c>. A scan up to <c>EdgeHandle.ScanLimit</c> slots, and a
    /// lookup in <c>held</c> above it.
    /// </summary>
    let holds (until: int) (source: ISource) =
        if until <= EdgeHandle.ScanLimit then
            heldBefore until source
        else
            if Platform.isNull held then
                held <- Platform.createRefIndex (until * 2)

                for i in 0 .. until - 1 do
                    Platform.refIndexSet held (sourceAt i) i

            Platform.refIndexFind held source >= 0

    /// <summary>
    /// Drops the slots from <c>from</c> on, unlinking the edges they own.
    /// </summary>
    let trimFrom (self: IComputation) from =
        if not (Platform.isNull held) then
            held <- null

        // Each handle is read when its slot is reached: a removal can move this list's later edges.
        for i in from .. count - 1 do
            // Blanked so a source dropped by a conditional body is not kept
            // alive by a slot past the end of the list.
            if i = 0 then
                if firstHandle >= 0 then
                    first.RemoveObserverAt (self, firstHandle)

                first <- Unchecked.defaultof<ISource>
                firstHandle <- EdgeHandle.Unlinked
            else
                let k = i - 1
#if FABLE_COMPILER
                let handle = Platform.itemAt k restHandles

                if handle >= 0 then
                    (Platform.itemAt k rest).Source.RemoveObserverAt(self, handle)

                Platform.setItemAt k rest (SourceSlot Unchecked.defaultof<ISource>)
                Platform.setItemAt k restHandles EdgeHandle.Unlinked
#else
                let handle = (Platform.itemAt k rest).Handle

                if handle >= 0 then
                    (Platform.itemAt k rest).Source.RemoveObserverAt(self, handle)

                rest[k] <- SourceSlot (Unchecked.defaultof<ISource>, EdgeHandle.Unlinked)
#endif
#if RANVIER_COUNTERS
            Counters.EdgeRemoved ()
#endif

        count <- from

    member _.Count = count

    /// <summary>
    /// The source at <c>i</c>. For the <c>Check</c> walk, which has to ask each source in
    /// turn whether it actually moved, and is the only caller that looks at
    /// this list outside a run.
    /// </summary>
    member _.SourceAt(i: int) =
        sourceAt i

    /// <summary>
    /// The edge handle at <c>i</c>.
    /// </summary>
    member _.HandleAt(i: int) =
        handleAt i

    /// <summary>
    /// Stores the handle a source's swap-remove gave the edge at <c>slot</c>.
    /// </summary>
    member _.MovedIn(slot: int, handle: int) =
        setHandle slot handle

    /// <summary>
    /// Opens a run. Deliberately does not detach: the edges stay live so the
    /// ones that are read again can be recognised.
    /// </summary>
    member _.BeginRun() =
        cursor <- 0

    /// <summary>
    /// Appends <c>source</c> after a read that missed its slot, trimming the unmatched slots first. Kept out of
    /// <c>Add</c>, whose matching path runs on every tracked read.
    /// </summary>
    member private this.Append(self: IComputation, source: ISource) =
        if cursor < count then
            Tracer.EdgesRemove (this, cursor)
            trimFrom self cursor

        let slot = count

        if slot = 0 then
            first <- source
            count <- 1
            cursor <- 1
#if RANVIER_COUNTERS
            Counters.EdgeAdded ()
#endif
            Tracer.EdgeAdd (this, source, 0)
            firstHandle <- source.AddObserver (self, 0)
        else
            let k = slot - 1

            if k = rest.Length then
                grow ()

#if FABLE_COMPILER
            Platform.setItemAt k rest (SourceSlot source)
#else
            rest[k] <- SourceSlot (source, EdgeHandle.Unlinked)
#endif
            count <- slot + 1
            cursor <- count
#if RANVIER_COUNTERS
            Counters.EdgeAdded ()
#endif
            Tracer.EdgeAdd (this, source, slot)

            // A source already in the list keeps the one edge it has: A, B, A links A once.
            let tried = source.TryAddObserver (self, slot)

            let handle =
                if tried <> EdgeHandle.Unknown then tried
                elif holds slot source then EdgeHandle.Unlinked
                else source.AddObserver (self, slot)

#if FABLE_COMPILER
            Platform.setItemAt k restHandles handle
#else
            rest[k].Handle <- handle
#endif

            if not (Platform.isNull held) then
                Platform.refIndexSet held source slot

    member this.Add(self: IComputation, source: ISource) =
        if
            cursor < count
            && obj.ReferenceEquals (sourceAt cursor, source)
        then
            cursor <- cursor + 1
        elif
            cursor > 0
            && obj.ReferenceEquals (sourceAt (cursor - 1), source)
        then
            // The same source read twice in a row — a loop over one signal, or
            // a helper that reads what the caller just read. Recording it
            // again would make the list as long as the read count, and would
            // still match positionally, so nothing would ever shrink it.
            ()
        else
            this.Append (self, source)

    /// <summary>
    /// Closes a run, dropping whatever the body did not read this time. Must
    /// run even when the body threw: a suspended body read a prefix, and the
    /// edges past that prefix are no longer ones it depends on.
    /// </summary>
    member this.EndRun(self: IComputation) =
        if cursor < count then
            Tracer.EdgesRemove (this, cursor)
            trimFrom self cursor

    /// <summary>
    /// Drops every edge. Disposal, not re-collection.
    /// </summary>
    member this.Clear(self: IComputation) =
        Tracer.EdgesRemove (this, 0)
        trimFrom self 0
        cursor <- 0

#if RANVIER_TRACE
    interface ITraced with
        member _.TraceLog
            with get () = traceLog
            and set log = traceLog <- log

        member _.TraceId
            with get () = traceId
            and set id = traceId <- id

    interface ITracedEdges with
        member _.Owner = traceId
        member _.IsSources = true
        member _.Ids = Array.init count (fun i -> (sourceAt i).Id)
#endif

/// <summary>
/// A child disposed with its owner's scope.
/// </summary>
type internal IOwned =
    /// <summary>
    /// Called only by the owner, after the child's link has left the owner's
    /// list. The child disposes without unlinking.
    /// </summary>
    abstract Release: unit -> unit

/// <summary>
/// One child's place in its owner's list.
/// </summary>
/// <remarks>
/// A flat list cannot serve: removing one entry by identity is a linear scan,
/// and an index is invalidated by every removal before it. Most children are
/// disposed individually rather than with their scope — re-running an effect
/// disposes its own scope, and a memo can outlive nothing at all — so a child
/// has to be able to leave its owner in constant time. Without that the list
/// only grows, and it retains every computation the graph has ever created,
/// closure included, long after disposal.
/// </remarks>
[<AllowNullLiteral>]
type internal OwnerLink(child: IOwned, owner: Owner) =
    member _.Child = child
    member _.Owner = owner

    member this.Detach() =
        owner.Unlink this

    member val Prev: OwnerLink = null with get, set
    member val Next: OwnerLink = null with get, set

    /// <summary>
    /// The owner's list generation the link joined, or -1 once the link has left the list.
    /// </summary>
    /// <remarks>
    /// A link at -1 is ignored by a later unlink, which would otherwise null out the owner's head.
    /// </remarks>
    member val Generation = 0 with get, set

    /// <summary>
    /// The owner's acquisition count when the link joined.
    /// </summary>
    member val Acquired = 0 with get, set

/// <summary>
/// A disposal scope.
/// </summary>
/// <remarks>
/// <para>
/// Reactive graphs leak by default: an effect is kept alive by the sources it
/// reads, so nothing collects it and nothing stops it running. Disposing every
/// effect by hand is the kind of bookkeeping that is wrong in one place and
/// silently keeps a subtree running. An owner makes lifetime structural — a
/// scope is torn down as a unit, in reverse acquisition order.
/// </para>
/// <para>
/// Owners nest, and every computation with a body is itself an owner: nodes
/// created and cleanups registered inside an effect, memo, async memo or
/// boundary body belong to that run and are discharged before the next one.
/// <c>OnCleanup</c> scopes subscriptions, timers and handles to the run that
/// created them.
/// </para>
/// </remarks>
and Owner internal (sink: Owner) =
    let mutable head: OwnerLink = null
    let mutable tail: OwnerLink = null

    /// <summary>
    /// The earliest cleanup registered since the last teardown, or null.
    /// </summary>
    let mutable firstCleanup: unit -> unit = Unchecked.defaultof<_>

    /// <summary>
    /// The acquisition count of <c>firstCleanup</c>.
    /// </summary>
    let mutable firstOrder = 0

    /// <summary>
    /// The cleanups registered after <c>firstCleanup</c>. Allocated by the second <c>OnCleanup</c>; null while a
    /// teardown runs the cleanups it detached.
    /// </summary>
    let mutable cleanups: ResizeArray<unit -> unit> = null

    /// <summary>
    /// The acquisition count of each entry in <c>cleanups</c>.
    /// </summary>
    let mutable cleanupOrder: ResizeArray<int> = null

    /// <summary>
    /// Counts cleanups and children registered since the last teardown.
    /// </summary>
    let mutable acquisitions = 0

    /// <summary>
    /// Allocated by the first error recorded without a sink.
    /// </summary>
    let mutable errors: ResizeArray<exn> = null
    let mutable disposed = false

    /// <summary>
    /// Bumped each time a teardown detaches the child list. A link from an
    /// earlier generation belongs to a detached list. Always non-negative.
    /// </summary>
    let mutable generation = 0

    /// <summary>
    /// Where this scope sits in its own parent, so it can leave in constant
    /// time when disposed on its own rather than with the parent.
    /// </summary>
    let mutable parentLink: OwnerLink = null

#if RANVIER_TRACE
    let mutable traceLog: TraceLog = null
    let mutable traceId = 0
#endif

#if RANVIER_COUNTERS
    do Counters.OwnerCreated ()
#endif

    new() = new Owner (Unchecked.defaultof<Owner>)

    member _.IsDisposed = disposed

    member internal this.SetParent(link: OwnerLink) =
        Tracer.OwnerAdopt (link.Owner, this, (this :? RootScope))
        parentLink <- link

    /// <summary>
    /// Exceptions recorded by this scope: from its cleanups, and on <c>Graph.Root</c> from the inbox.
    /// </summary>
    /// <remarks>
    /// Only <c>Graph.Root</c>, a <c>createRoot</c> scope and a scope from <c>new Owner ()</c> hold errors. The
    /// scope of a computation, projection or lookup records into <c>Graph.Root.Errors</c>, and its own list stays empty.
    /// </remarks>
    member _.Errors =
        Seq.delay (fun () ->
            if Platform.isNull errors then
                Seq.empty
            else
                errors :> seq<exn>)

    member _.ErrorCount = if Platform.isNull errors then 0 else errors.Count

    /// <summary>
    /// The errors of <c>Errors</c> recorded at or after position <c>index</c>, in recording order.
    /// </summary>
    /// <exception cref="T:System.ArgumentOutOfRangeException"><c>index</c> is negative.</exception>
    member _.ErrorsFrom(index: int) : exn[] =
        if index < 0 then
            raise (ArgumentOutOfRangeException (nameof index))

        if Platform.isNull errors || index >= errors.Count then
            [||]
        else
            errors.GetRange(index, errors.Count - index).ToArray()

    /// <summary>
    /// Takes ownership of <c>child</c> without a handle: it is disposed with the
    /// scope, and cannot be released before then. On a disposed scope <c>child</c> is disposed immediately, in the context
    /// <c>OnCleanup</c> describes.
    /// </summary>
    member this.Attach(child: IDisposable) =
        Tracer.OwnerAdopt (this, child, false)

        this.AttachLinked
            { new IOwned with
                member _.Release() =
                    child.Dispose ()
            }
        |> ignore

    member internal this.AttachLinked(child: IOwned) =
        if disposed then
            this.DisposeLate child
        else
            this.Append child

    /// <summary>
    /// Disposes a child attached after the scope was disposed, handing back
    /// an unlinked link.
    /// </summary>
    member private this.DisposeLate(child: IOwned) =
        this.RunLate child.Release
        OwnerLink (child, this, Generation = -1)

    /// <summary>
    /// Runs <c>f</c> on behalf of the disposed scope: under the graph's teardown
    /// context for a root scope, inline otherwise. An exception from <c>f</c> is
    /// recorded.
    /// </summary>
    member private this.RunLate(f: unit -> unit) =
        match box this with
        | :? RootScope as r -> (r.Runner: ILateRunner).RunDetached(this, f)
        | _ ->
            try
                f ()
            with ex ->
                this.RecordError ex

    member private this.Append(child: IOwned) =
        let link = OwnerLink (child, this, Generation = generation, Acquired = acquisitions)
        acquisitions <- acquisitions + 1

        if Platform.isNull tail then
            head <- link
            tail <- link
        else
            link.Prev <- tail
            tail.Next <- link
            tail <- link

        link

    /// <summary>
    /// Releases a child that was disposed on its own. A no-op for a link on a
    /// list a teardown detached: the teardown's walk follows those pointers,
    /// and discards the list when it finishes.
    /// </summary>
    member internal _.Unlink(link: OwnerLink) =
        if
            not (Platform.isNull link)
            && link.Generation = generation
        then
            link.Generation <- -1

            if Platform.isNull link.Prev then
                head <- link.Next
            else
                link.Prev.Next <- link.Next

            if Platform.isNull link.Next then
                tail <- link.Prev
            else
                link.Next.Prev <- link.Prev

            link.Prev <- null
            link.Next <- null

    /// <summary>
    /// Records a failure from teardown or the inbox instead of rethrowing it.
    /// </summary>
    /// <remarks>
    /// The scope of an effect, memo, async memo, boundary, projection key or pass, or lookup records into
    /// <c>Graph.Root</c>. A <c>createRoot</c> scope and a scope from <c>new Owner ()</c> keep their own errors.
    /// </remarks>
    member internal _.RecordError(ex: exn) =
        if Platform.isNull (box sink) then
            if Platform.isNull errors then
                errors <- ResizeArray<exn>()

            errors.Add ex
        else
            sink.RecordError ex

    /// <summary>
    /// Registers <c>f</c> to run when the scope is torn down.
    /// </summary>
    /// <remarks>
    /// On a disposed scope <c>f</c> runs immediately. A scope created by
    /// <c>createRoot</c> runs it untracked, with effects deferred until it returns
    /// and the scope as the owner of every node it creates. Any other scope,
    /// including <c>Graph.Root</c> and a scope constructed with <c>new Owner ()</c>,
    /// runs it inline in the caller's tracking context.
    /// </remarks>
    member this.OnCleanup(f: Action) =
        this.AddCleanup f.Invoke

    member internal this.AddCleanup(f: unit -> unit) =
        if disposed then
            this.RunLate f
        else
            if Platform.isNull (box firstCleanup) then
                firstCleanup <- f
                firstOrder <- acquisitions
            else
                if Platform.isNull cleanups then
                    cleanups <- ResizeArray<unit -> unit>()
                    cleanupOrder <- ResizeArray<int>()

                cleanups.Add f
                cleanupOrder.Add acquisitions

            acquisitions <- acquisitions + 1

    /// <summary>
    /// Tears the scope down without retiring it, so it can be filled again.
    /// This is what an effect does before re-running its body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cleanups and children are torn down together in reverse acquisition
    /// order, as <c>use</c> bindings are disposed. A cleanup runs while every
    /// node created before its registration is still live.
    /// </para>
    /// <para>
    /// The cleanups and children are detached before the first one runs. A
    /// cleanup or child registered while this teardown runs joins the emptied
    /// scope and outlives the teardown. During the teardown <c>Dispose</c> runs, a
    /// cleanup or child registered with the scope runs, or is disposed,
    /// immediately, as <c>OnCleanup</c> describes.
    /// </para>
    /// </remarks>
    member internal this.DisposeScope() =
        let first = firstCleanup
        let firstAcquired = firstOrder
        let detached = cleanups
        let detachedOrder = cleanupOrder
        let mutable node = tail
        head <- null
        tail <- null
        generation <- (generation + 1) &&& 0x7FFFFFFF
        acquisitions <- 0
        firstCleanup <- Unchecked.defaultof<_>
        cleanups <- null
        cleanupOrder <- null

        // Index 0 is `first` when it is set; the rest index `detached` from `offset`.
        let offset = if Platform.isNull (box first) then 0 else 1

        let mutable index =
            offset
            + (if Platform.isNull detached then 0 else detached.Count)
            - 1

        while index >= 0 || not (Platform.isNull node) do
            if
                not (Platform.isNull node)
                && (index < 0
                    || node.Acquired >
                        (if index < offset then
                             firstAcquired
                         else
                             Platform.entryAt (index - offset) detachedOrder))
            then
                let prev = node.Prev

                try
                    node.Child.Release ()
                with ex ->
                    this.RecordError ex

                node <- prev
            else
                try
                    if index < offset then
                        first ()
                    else
                        (Platform.entryAt (index - offset) detached) ()
                with ex ->
                    this.RecordError ex

                index <- index - 1

        if not (Platform.isNull detached) then
            // Guarded because `Clear` is not free where it matters most. Fable
            // compiles it to `col.splice(0)`, which allocates the array of
            // removed elements whether or not there were any.
            if detached.Count > 0 then
                detached.Clear ()
                detachedOrder.Clear ()

            if Platform.isNull cleanups then
                cleanups <- detached
                cleanupOrder <- detachedOrder

    /// <summary>
    /// Tears the scope down and retires it. Idempotent.
    /// </summary>
    /// <remarks>
    /// A root scope's teardown runs untracked, with effects deferred until it
    /// returns, and owns every node its cleanups create.
    /// </remarks>
    /// <exception cref="T:System.InvalidOperationException">
    /// A root scope of a <c>Serialised</c> graph is disposed outside the construction context, or while another
    /// thread is inside the graph. The scope stays live.
    /// </exception>
    member this.Dispose() =
        if not disposed then
            match box this with
            | :? RootScope as r ->
                let runner = r.Runner
                let held = runner.HoldTeardown ()

                try
                    this.TearDown runner
                finally
                    if held then
                        runner.ReleaseTeardown ()
            | _ -> this.TearDown Unchecked.defaultof<ILateRunner>

    /// <summary>
    /// Marks the scope disposed, disposes its children and unlinks it from its parent. A root scope passes its
    /// <c>runner</c>, which runs the teardown detached; any other scope passes null.
    /// </summary>
    member private this.TearDown(runner: ILateRunner) =
        disposed <- true
        Tracer.OwnerDispose this

        if Platform.isNull (box runner) then
            this.DisposeScope ()
        else
            runner.RunDetached (this, this.DisposeScope)

        if not (Platform.isNull parentLink) then
            parentLink.Detach ()
            parentLink <- null

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

    interface IOwned with
        member this.Release() =
            parentLink <- null
            this.Dispose ()

#if RANVIER_TRACE
    interface ITraced with
        member _.TraceLog
            with get () = traceLog
            and set log = traceLog <- log

        member _.TraceId
            with get () = traceId
            and set id = traceId <- id
#endif

/// <summary>
/// Runs work on behalf of a disposed root scope.
/// </summary>
and internal ILateRunner =
    /// <summary>
    /// Runs <c>f</c> untracked, with effects deferred until it returns and <c>owner</c>
    /// as the owner of every node it creates. An exception from <c>f</c> is
    /// recorded on <c>owner</c>.
    /// </summary>
    abstract RunDetached: owner: Owner * f: (unit -> unit) -> unit

    /// <summary>
    /// Holds a <c>Serialised</c> graph for a root scope's teardown.
    /// </summary>
    /// <returns>True when the caller must pair it with <c>ReleaseTeardown</c>.</returns>
    /// <exception cref="T:System.InvalidOperationException">
    /// The caller runs outside the construction context, or another thread holds the graph.
    /// </exception>
    abstract HoldTeardown: unit -> bool

    /// <summary>
    /// Frees a graph held by <c>HoldTeardown</c>.
    /// </summary>
    abstract ReleaseTeardown: unit -> unit

/// <summary>
/// The scope <c>Graph.CreateRoot</c> hands out.
/// </summary>
and [<Sealed>] internal RootScope(runner: ILateRunner) =
    inherit Owner(Unchecked.defaultof<Owner>)

    member _.Runner: ILateRunner = runner

/// <summary>
/// What a computation's body may create. Fixed at construction.
/// </summary>
type internal ScopeMode =
    /// <summary>
    /// Creating an owned node raises <c>InvalidOperationException</c>.
    /// </summary>
    | Pure = 0uy
    /// <summary>
    /// The body's nodes and cleanups belong to the computation's own scope.
    /// </summary>
    | Owning = 1uy
    /// <summary>
    /// A value-form projection row: pure, raising <c>ScopeMessages.valueRow</c>.
    /// </summary>
    | ValueRow = 2uy
    /// <summary>
    /// A factory-form projection row: pure, raising <c>ScopeMessages.factoryRow</c>.
    /// </summary>
    | FactoryRow = 3uy
    /// <summary>
    /// A lookup's <c>f</c>: pure, raising <c>ScopeMessages.lookup</c>.
    /// </summary>
    | Lookup = 4uy
    /// <summary>
    /// A pure async memo, raising <c>ScopeMessages.asyncMemo</c>.
    /// </summary>
    | PureAsync = 5uy
    /// <summary>
    /// The <c>compute</c> of <c>createEffectOn</c>: pure, raising <c>ScopeMessages.effectOn</c>.
    /// </summary>
    | EffectOn = 6uy

/// <summary>
/// The <c>InvalidOperationException</c> messages for a pure body that creates an
/// owned node.
/// </summary>
module internal ScopeMessages =
    [<Literal>]
    let private nodes =
        "a memo, effect, async value, boundary, root, projection, lookup, selector or onCleanup"

    let memo =
        $"A memo created by createMemo created an owned node in its body: {nodes}. createMemo is a pure derivation. Use createMemoWith for a memo that owns the nodes its body creates: they are disposed before each re-run and with the memo."

    let asyncMemo =
        $"An async value created by createAsync created an owned node in its body: {nodes}. createAsync is a pure derivation. Use createAsyncWith for an async value that owns the nodes its body creates: they are disposed before each flight and with the async value."

    let valueRow =
        $"A projection's map created an owned node: {nodes}. map re-runs whenever its key's item or a value it read changes. Use createProjectionWith, or createIndexProjectionWith, and create the node in the factory, which runs once per key."

    let factoryRow =
        $"A projection row's reader created an owned node: {nodes}. The reader re-runs whenever its key's item or a value it read changes. Move the creation into the factory body, which runs once per key."

    let lookup =
        $"A lookup's f created an owned node: {nodes}. f is a pure function of the source and the key, re-run for every affected key. Create the node outside the lookup."

    let effectOn =
        $"An effect created by createEffectOn created an owned node in its compute: {nodes}. compute is a pure derivation. Create the node in act, which owns it: it is disposed before the next act and with the effect."

    let lookupAffected =
        $"A lookup's affected created an owned node: {nodes}. affected is a pure function of the previous and the next source, re-run on every source change. Create the node outside the lookup."

    let forMode (mode: ScopeMode) =
        match mode with
        | ScopeMode.ValueRow -> valueRow
        | ScopeMode.FactoryRow -> factoryRow
        | ScopeMode.Lookup -> lookup
        | ScopeMode.PureAsync -> asyncMemo
        | ScopeMode.EffectOn -> effectOn
        | _ -> memo

    /// <summary>
    /// True when <c>ex</c> is the violation raised under <c>message</c>.
    /// </summary>
    let isViolation (message: string) (ex: exn) =
        Platform.isExactInvalidOperation ex
        && String.Equals (ex.Message, message, StringComparison.Ordinal)

    /// <summary>
    /// The error a violating run fails with: <c>ex</c> when it is the violation's
    /// own exception, a fresh one otherwise.
    /// </summary>
    let failure (mode: ScopeMode) (ex: exn) : exn =
        let message = forMode mode

        if isViolation message ex then
            ex
        else
            InvalidOperationException message

/// <summary>
/// A computation whose body runs under a scope rule of its own.
/// </summary>
type internal IScopeHost =
    /// <summary>
    /// The computation's scope, allocated by the first creation in the body.
    /// A pure computation raises <c>InvalidOperationException</c> instead, and
    /// its current run fails even when the body catches the exception.
    /// </summary>
    abstract Scope: Owner

/// <summary>
/// A <c>Graph.Activate</c> call: the graph, the activating thread, and whether the graph is ambient on other threads.
/// </summary>
[<Sealed; AllowNullLiteral>]
type internal ActivationEntry(graph: obj, thread: int, anyThread: bool) =
    /// <summary>
    /// The graph, when ambient on the calling thread, otherwise null.
    /// </summary>
    member _.GraphOnCurrentThread =
        if anyThread || thread = Platform.currentThreadId () then
            graph
        else
            null

/// <summary>
/// The innermost <c>Graph.Activate</c> call. On .NET it flows with the execution context: an activation inside an
/// async body follows that body across an <c>await</c> and is invisible to other work on the activating thread.
/// </summary>
module internal Activation =
#if FABLE_COMPILER
    let mutable private activated: ActivationEntry = null

    let current () = activated

    let set (entry: ActivationEntry) =
        activated <- entry
#else
    let private activated = AsyncLocal<ActivationEntry>()

    let current () =
        activated.Value

    let set (entry: ActivationEntry) =
        activated.Value <- entry
#endif

/// <summary>
/// A failed node's exception and the node it originated in. Every node the exception reaches through reads holds the
/// same record.
/// </summary>
/// <remarks>
/// The origin is the node whose body, comparer, flight, <c>Fail</c> or disposal raised the exception, as reported by
/// <c>ErrorOrigin</c>.
/// </remarks>
[<Sealed; AllowNullLiteral>]
type internal Failure(error: exn, origin: INode) =
    let mutable captured: Platform.CapturedFailure = null

    member _.Error = error
    member _.Origin = origin

    /// <summary>
    /// Raises <c>Error</c>. On .NET every rethrow carries the frames captured by the first, followed by the rethrowing
    /// reader's frames.
    /// </summary>
    member _.Rethrow() : unit =
        if Platform.isNull captured then
            captured <- Platform.captureFailure null error

        Platform.rethrowStored captured

    /// <summary>The exception of <c>failure</c>, or null when <c>failure</c> is null.</summary>
    static member ErrorOf(failure: Failure) : exn =
        if Platform.isNull failure then null else failure.Error

    /// <summary>
    /// The payload of a trace <c>Moved</c> event: the exception of <c>failure</c>, or <c>value</c> when <c>failure</c> is
    /// null.
    /// </summary>
    static member Payload(failure: Failure, value: obj) : obj =
        if Platform.isNull failure then value else box failure.Error

    /// <summary>The origin of <c>failure</c>, or null when <c>failure</c> is null.</summary>
    static member OriginOf(failure: Failure) : INode =
        if Platform.isNull failure then
            Unchecked.defaultof<INode>
        else
            failure.Origin

    /// <summary>
    /// True when <c>next</c> and <c>previous</c> hold different exception instances, null counting as none. The cutoff of
    /// every node that stores a <c>Failure</c>.
    /// </summary>
    static member Moved(next: Failure, previous: Failure) =
        not (obj.ReferenceEquals (next, previous))
        && not (obj.ReferenceEquals (Failure.ErrorOf next, Failure.ErrorOf previous))

/// <summary>
/// Holds each thread's <c>AmbientCell</c>. Held apart from <c>Graph</c>, whose static initialisation check would
/// otherwise guard every access.
/// </summary>
[<AbstractClass; Sealed>]
type internal AmbientSlot =
    [<ThreadStatic; DefaultValue>]
    static val mutable private cell: AmbientCell

    /// <summary>
    /// The calling thread's cell, created on first use.
    /// </summary>
    static member Cell =
        if Platform.isNull AmbientSlot.cell then
            AmbientSlot.cell <- AmbientCell ()

        AmbientSlot.cell

    /// <summary>
    /// The calling thread's cell, or null before its first use.
    /// </summary>
    static member Existing = AmbientSlot.cell

/// <summary>
/// The running graph of one thread. A guarded graph holds its owner thread's
/// cell. Re-entering the graph already in <c>Hosting</c> writes nothing.
/// </summary>
and [<Sealed; AllowNullLiteral>] internal AmbientCell() =
    /// <summary>
    /// The graph most recently running a body, a flush or a stale read.
    /// Ambient while that graph is still running one.
    /// </summary>
    [<DefaultValue>]
    val mutable Hosting: obj

/// <summary>
/// The queue item that runs a graph's queued projection passes. Carries no id, so a trace records no node for it.
/// </summary>
[<Sealed>]
type internal PassDrain(run: unit -> unit) =
    interface IScheduled with
        member _.Execute() = run ()

#if !FABLE_COMPILER
/// <summary>
/// A <c>Graph.DispatchAsync</c> item waiting in a graph's inbox, faulted by <c>Abandon</c> when the graph is
/// disposed first.
/// </summary>
type internal IPendingDispatch =
    abstract Abandon: error: exn -> unit

/// <summary>
/// The task of a queued <c>Graph.DispatchAsync</c> item. Exactly one of <c>TryClaim</c>, <c>Cancel</c> and
/// <c>Abandon</c> takes effect; once the drain claims the item, the task completes with the work's outcome.
/// </summary>
[<Sealed>]
type internal PendingDispatch<'T>() =
    inherit TaskCompletionSource<'T>(TaskCreationOptions.RunContinuationsAsynchronously)

    /// <summary>0 while waiting, 1 once claimed by the drain, 2 once cancelled or abandoned.</summary>
    let mutable state = 0

    member private _.TryClose() =
        Interlocked.CompareExchange (&state, 2, 0) = 0

    /// <summary>True for the caller that moves the item from waiting to running.</summary>
    member _.TryClaim() =
        Interlocked.CompareExchange (&state, 1, 0) = 0

    member this.Cancel(token: CancellationToken) =
        if this.TryClose () then
            this.TrySetCanceled token |> ignore

    interface IPendingDispatch with
        member this.Abandon error =
            if this.TryClose () then
                this.SetException error
#endif

/// <summary>The application key and codec of a journaled input node, with the value type erased.</summary>
[<AbstractClass>]
type internal JournalBinding(key: string, codec: string, version: int) =
    member _.Key = key
    member _.Codec = codec
    member _.Version = version

    /// <summary>The codec's text for <c>value</c>. Raises when the codec raises.</summary>
    abstract Encode: value: obj -> string

    /// <summary>True when the node accepts entries of <c>kind</c>.</summary>
    abstract Accepts: kind: JournalEntryKind -> bool

    /// <summary>
    /// The application of a <c>kind</c> entry carrying <c>data</c> to the node, or the decode error. A <c>Fail</c>
    /// entry's <c>data</c> is the exception's message.
    /// </summary>
    abstract Prepare: kind: JournalEntryKind * data: string -> Result<unit -> unit, string>

/// <summary>
/// The work a journaled input is attributed to: external input, an async work of the graph, or a replayed entry.
/// </summary>
[<Sealed; AllowNullLiteral>]
type internal JournalAttribution(origin: JournalOrigin, replayed: int, matched: string) =
    /// <summary>The origin an input of the work records.</summary>
    member _.Origin = origin

    /// <summary>
    /// The <c>Seq</c> of the replayed entry the attribution applies, or 0. A replayed attribution is always admitted.
    /// </summary>
    member _.ReplayedSeq = replayed

    /// <summary>
    /// The key under which a diagnostic replay counts the recorded <c>Produced</c> entries of the work, or null for
    /// work started outside a diagnostic replay.
    /// </summary>
    member _.Match = matched

    /// <summary>The number of calls of the work that made inputs.</summary>
    member val Calls = 0 with get, set

    /// <summary>True while a call of the work runs, such as the body passed to <c>Journal.produce</c>.</summary>
    member val InCall = false with get, set

    /// <summary>The position of the running call among <c>Calls</c>, or -1 before the call's first input.</summary>
    member val Call = -1 with get, set

    /// <summary>
    /// The position of the call an input of the work arriving now belongs to: the running call, or a new call of its
    /// own outside one.
    /// </summary>
    member this.Claim() =
        if this.Call >= 0 then
            this.Call
        else
            let call = this.Calls
            this.Calls <- call + 1

            if this.InCall then
                this.Call <- call

            call

    static member val External = JournalAttribution (JournalOrigin.External, 0, null)

/// <summary>
/// A replay running on a graph: the position of the replayed entries, and the target <c>Seq</c> each applied entry
/// recorded.
/// </summary>
[<Sealed; AllowNullLiteral>]
type internal JournalReplaySession(id: int, diagnostic: bool, setupOpen: bool) =
    let recorded = Dictionary<int, int>()

    member _.Diagnostic = diagnostic

    /// <summary>
    /// True when the target recorded no input before the replay, so its setup work re-runs the recorded run's setup work.
    /// </summary>
    member _.SetupOpen = setupOpen

    /// <summary>The <c>Seq</c> of the last replayed non-<c>Flush</c> entry applied, or -1 before the first.</summary>
    member val Anchor = -1 with get, set

    /// <summary>The number of works started since <c>Anchor</c> was applied.</summary>
    member val Started = 0 with get, set

    /// <summary>The key of the work <c>(started, index)</c> of the replayed entries.</summary>
    member _.Key(started: int, index: int) =
        string id
        + ":"
        + string started
        + ":"
        + string index

    /// <summary>Records that the replayed entry <c>source</c> recorded the target entry <c>target</c>.</summary>
    member _.Recorded(source: int, target: int) =
        recorded[source] <- target

    /// <summary>The target <c>Seq</c> the replayed entry <c>source</c> recorded, or <c>source</c>.</summary>
    member _.TargetOf(source: int) =
        match recorded.TryGetValue source with
        | true, target -> target
        | _ -> source

/// <summary>
/// The async work of the effect body running in the calling context. On .NET it flows with the execution context into
/// every continuation of the body; under Fable it is always null.
/// </summary>
module internal JournalAmbient =
#if FABLE_COMPILER
    let current () : JournalAttribution = null
#else
    let private ambient = AsyncLocal<JournalAttribution>()

    let current () =
        ambient.Value

    let set (work: JournalAttribution) =
        ambient.Value <- work
#endif

/// <summary>
/// A graph's journal state: the journal, the bindings of its keyed input nodes, and the position of the last entry.
/// </summary>
[<Sealed; AllowNullLiteral>]
type internal JournalRecorder(journal: IInputJournal) =
    let bindings = Dictionary<int, JournalBinding>()
    let keys = Dictionary<string, int>()
    let mutable position = 0
    let mutable unflushed = false
    let mutable derivedDepth = 0
    let mutable attribution: JournalAttribution = null
    // The Seq of the last non-Flush entry, and the number of works started after it.
    let mutable anchor = 0
    let mutable started = 0
    let mutable session: JournalReplaySession = null
    let mutable sessions = 0
    let expected = Dictionary<string, int>()
    let mutable nextSite = 0
    // The call of the input last admitted, for the origin it records.
    let mutable inputCall = 0

    /// <summary>True when an entry was recorded after the last <c>Flush</c> entry.</summary>
    member _.Unflushed = unflushed

    /// <summary>The mode of the replay running on the graph, if any.</summary>
    member val Replaying: ReplayMode option = None with get, set

    /// <summary>A fresh negative id for an input site that is not a node, such as an MVU loop's messages.</summary>
    member _.NextSiteId() =
        nextSite <- nextSite - 1
        nextSite

    /// <summary>The binding registered under <c>key</c>, or null.</summary>
    member _.BindingOfKey(key: string) : JournalBinding =
        match keys.TryGetValue key with
        | true, nodeId -> bindings[nodeId]
        | _ -> Unchecked.defaultof<JournalBinding>

    /// <summary>The nesting depth of derived sections: bodies that run outside the tracking context.</summary>
    member _.DerivedDepth = derivedDepth

    member _.EnterDerived() =
        derivedDepth <- derivedDepth + 1

    member _.LeaveDerived() =
        derivedDepth <- derivedDepth - 1

    /// <summary>
    /// The next async work on the graph, identified by the last non-<c>Flush</c> entry and its position among the works
    /// started after it. During a diagnostic replay it also carries its identity in the replayed entries.
    /// </summary>
    member _.NextWork() =
        let origin = JournalOrigin.Produced (anchor, started, 0)

        let matched =
            if
                not (Platform.isNull session)
                && session.Anchor >= 0
            then
                let key =
                    if session.Diagnostic then
                        session.Key (session.Anchor, session.Started)
                    else
                        null

                session.Started <- session.Started + 1
                key
            elif anchor = 0 then
                // Setup work, started before the graph's first entry, is matched by its position from construction.
                JournalRecorder.SetupKey started
            else
                null

        started <- started + 1
        JournalAttribution (origin, 0, matched)

    static member private SetupKey(index: int) =
        "setup:" + string index

    /// <summary>The key of a call of a work, under which a diagnostic replay counts the call's replayed entries.</summary>
    static member private CallKey(work: string, call: int) =
        work + "/" + string call

    /// <summary>
    /// The key of the call <c>call</c> of a work of the replayed entries, or null for setup work the replay leaves
    /// unmatched.
    /// </summary>
    member private _.ExpectedKey(started: int, index: int, call: int) =
        if started > 0 then
            JournalRecorder.CallKey (session.Key (started, index), call)
        elif session.SetupOpen then
            JournalRecorder.CallKey (JournalRecorder.SetupKey index, call)
        else
            null

    /// <summary>Starts a replay on the graph, and returns the replay it nests in, or null.</summary>
    member _.BeginReplay(mode: ReplayMode) : JournalReplaySession =
        let previous = session
        sessions <- sessions + 1
        session <- JournalReplaySession (sessions, (mode = ReplayMode.Diagnostic), (anchor = 0))
        previous

    /// <summary>Ends the running replay, restoring <c>previous</c>.</summary>
    member _.EndReplay(previous: JournalReplaySession) =
        session <- previous

    /// <summary>Runs <c>run</c>, the application of the replayed entry <c>work</c> applies.</summary>
    member this.RunReplayed(work: JournalAttribution, run: unit -> unit) =
        if not (Platform.isNull session) then
            session.Anchor <- work.ReplayedSeq
            session.Started <- 0

        this.RunAttributed (work, run)

    /// <summary>
    /// Runs <c>run</c>, the body of an effect, as a new async work: on .NET its continuations carry the work.
    /// </summary>
    member this.RunWork(run: unit -> unit) =
        let work = this.NextWork ()
#if FABLE_COMPILER
        ignore work
        run ()
#else
        let previous = JournalAmbient.current ()
        JournalAmbient.set work

        try
            run ()
        finally
            JournalAmbient.set previous
#endif

    /// <summary>
    /// Runs <c>run</c> with its inputs attributed to <c>work</c>, as one call of a live work: a call nested in a running
    /// call of the same work joins it.
    /// </summary>
    member _.RunAttributed(work: JournalAttribution, run: unit -> unit) =
        let previous = attribution
        attribution <- work
        let opens = work.ReplayedSeq = 0 && not work.InCall

        if opens then
            work.InCall <- true
            work.Call <- -1

        try
            run ()
        finally
            attribution <- previous

            if opens then
                work.InCall <- false
                work.Call <- -1

    /// <summary>The work an input arriving now is attributed to, or null for an external input.</summary>
    member _.Current: JournalAttribution =
        if Platform.isNull attribution then
            JournalAmbient.current ()
        else
            attribution

    /// <summary>
    /// Counts one replayed <c>Produced</c> entry of the call <c>call</c> of the work <c>(started, index)</c> of the
    /// running replay, for <c>Admit</c>.
    /// </summary>
    member this.Expect(started: int, index: int, call: int) =
        let key = this.ExpectedKey (started, index, call)

        if not (isNull key) then
            match expected.TryGetValue key with
            | true, count -> expected[key] <- count + 1
            | _ -> expected[key] <- 1

    /// <summary>Withdraws one count of <c>Expect</c>, for a replayed entry left unapplied.</summary>
    member this.Unexpect(started: int, index: int, call: int) =
        let key = this.ExpectedKey (started, index, call)

        if not (isNull key) then
            match expected.TryGetValue key with
            | true, count when count > 1 -> expected[key] <- count - 1
            | true, _ -> expected.Remove key |> ignore
            | _ -> ()

    /// <summary>
    /// False when the input arriving now is dropped: it comes from a call of a work re-run by a diagnostic replay, with
    /// an unmatched replayed <c>Produced</c> entry of the same call, which it then matches.
    /// </summary>
    member this.Admit() : bool =
        this.AdmitWork this.Current

    /// <summary>
    /// <c>Admit</c> for an input made inside a computation, a flush or another derived section: only an input of a
    /// <c>Journal.produce</c> call can match a replayed entry, as when a re-run work's await completes synchronously.
    /// </summary>
    member this.AdmitDerived() : bool =
        this.AdmitWork attribution

    member private _.AdmitWork(work: JournalAttribution) : bool =
        if Platform.isNull work || work.ReplayedSeq > 0 then
            true
        else
            inputCall <- work.Claim ()

            if Platform.isNull work.Match then
                true
            else
                let key = JournalRecorder.CallKey (work.Match, inputCall)

                match expected.TryGetValue key with
                | true, count when count > 1 ->
                    expected[key] <- count - 1
                    false
                | true, _ ->
                    expected.Remove key |> ignore
                    false
                | _ -> true

    /// <summary>Registers <c>binding</c> for the node <c>nodeId</c>.</summary>
    /// <exception cref="T:System.ArgumentException">
    /// The graph already has a node with the binding's key, or the node already has a key.
    /// </exception>
    member _.Bind(nodeId: int, binding: JournalBinding) =
        match bindings.TryGetValue nodeId with
        | true, held ->
            raise (
                ArgumentException (
                    "The node is already registered under the journal key \""
                    + held.Key
                    + "\"."
                )
            )
        | _ -> ()

        if keys.ContainsKey binding.Key then
            raise (
                ArgumentException (
                    "The journal key \""
                    + binding.Key
                    + "\" is already registered on this graph."
                )
            )

        keys[binding.Key] <- nodeId
        bindings[nodeId] <- binding

    /// <summary>Removes the binding of the node <c>nodeId</c>, and frees its key for another node.</summary>
    member _.Unbind(nodeId: int, key: string) =
        match keys.TryGetValue key with
        | true, holder when holder = nodeId ->
            keys.Remove key |> ignore
            bindings.Remove nodeId |> ignore
        | _ -> ()

    /// <summary>The binding of the node <c>nodeId</c>, or null when the node is unkeyed.</summary>
    member _.BindingOf(nodeId: int) : JournalBinding =
        match bindings.TryGetValue nodeId with
        | true, binding -> binding
        | _ -> Unchecked.defaultof<JournalBinding>

    member private this.Emit(kind: JournalEntryKind, key: string option, payloadType: string, payload: JournalPayload) =
        position <- position + 1

        let work = if kind = JournalEntryKind.Flush then null else this.Current

        let origin =
            if Platform.isNull work then
                JournalOrigin.External
            elif work.ReplayedSeq > 0 then
                if not (Platform.isNull session) then
                    session.Recorded (work.ReplayedSeq, position)

                match work.Origin with
                | JournalOrigin.Produced (started, index, call) when not (Platform.isNull session) ->
                    JournalOrigin.Produced (session.TargetOf started, index, call)
                | origin -> origin
            else
                match work.Origin with
                | JournalOrigin.Produced (started, index, _) -> JournalOrigin.Produced (started, index, inputCall)
                | origin -> origin

        if kind <> JournalEntryKind.Flush then
            unflushed <- true
            anchor <- position
            started <- 0

        journal.Record
            {
                Schema = 2
                Stream = journal.Stream
                Seq = position
                Kind = kind
                Key = key
                PayloadType = payloadType
                Payload = payload
                Origin = origin
            }

    /// <summary>
    /// Records <c>value</c> arriving at the node <c>nodeId</c>: encoded by the node's codec when the node is keyed and the
    /// codec succeeds, and non-replayable otherwise. A <c>Fail</c> entry of a keyed node carries the exception's message
    /// under the <c>exn</c> codec, version 1.
    /// </summary>
    member this.Input(kind: JournalEntryKind, nodeId: int, value: obj) =
        let payloadType = Platform.typeName value
        let binding = this.BindingOf nodeId

        if Platform.isNull (box binding) then
            this.Emit (kind, None, payloadType, JournalPayload.NonReplayable payloadType)
        else
            let payload =
                try
                    if kind = JournalEntryKind.Fail then
                        JournalPayload.Encoded ("exn", 1, (value :?> exn).Message)
                    else
                        JournalPayload.Encoded (binding.Codec, binding.Version, binding.Encode value)
                with _ ->
                    JournalPayload.NonReplayable payloadType

            this.Emit (kind, Some binding.Key, payloadType, payload)

    /// <summary>Records a <c>Flush</c> entry when an entry was recorded since the previous one.</summary>
    member this.Boundary() =
        if unflushed then
            unflushed <- false
            this.Emit (JournalEntryKind.Flush, None, "", JournalPayload.Empty)

/// <summary>
/// Owns the tracking context and the construction-time policy. One graph is a
/// single consistency domain; on .NET that also means a single thread of
/// mutation, which is what <c>GraphOptions.Dispatcher</c> exists to enforce.
/// </summary>
type Graph(options: GraphOptions) =
    let mutable nextId = 0
    /// <summary>
    /// Null rather than <c>IComputation option</c>. The option was 24 bytes of
    /// garbage per tracked run — allocated on the way in to every memo
    /// recomputation and every effect run, which is the hottest path the
    /// library has.
    /// </summary>
    /// <remarks>
    /// <c>Unchecked.defaultof</c> rather than <c>null</c>, and <c>ReferenceEquals</c> rather
    /// than <c>isNull</c>, because F# interfaces do not admit the null literal
    /// without <c>AllowNullLiteral</c> — and that attribute would have to cascade
    /// to <c>INode</c> and <c>ISource</c>, weakening three public types to save one
    /// private field an unchecked cast.
    /// </remarks>
    let mutable current: IComputation = Unchecked.defaultof<IComputation>

    /// <summary>
    /// The node of the last pending read raised inside the running body, or
    /// null. Survives a <c>try/with</c> in the body that swallows the raise.
    /// </summary>
    let mutable raisedPending: INode = Unchecked.defaultof<INode>

    /// <summary>
    /// The failure of the last failed read inside a stale read or a flush, or null. Taken by the next run that catches a
    /// failure, and cleared then and at the end of the outermost stale read and of each flush.
    /// </summary>
    /// <remarks>
    /// Every computation body runs inside a stale read or a flush, so the slot is null whenever the graph is idle.
    /// </remarks>
    let mutable lastRaised: Failure = null

    /// <summary>
    /// Effects invalidated in the current turn, waiting to be run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A list read through a cursor rather than a <c>Queue</c>. Both are O(1) at
    /// either end, but <c>Queue</c> carries head/tail arithmetic and a slot clear on
    /// every dequeue, and under Fable each of those lands on fable-library's
    /// bounds-checked <c>item</c> — about 7% of a write-heavy profile between them.
    /// A cursor is one indexed read and an increment.
    /// </para>
    /// <para>
    /// The list is never cleared and never shrinks: it is a buffer whose
    /// logical length is <c>queueCount</c>, and <c>Schedule</c> overwrites a spent slot
    /// before it appends. <c>Clear</c> was the first shape tried and it cost more
    /// than the <c>Queue</c> did — under Fable it is a <c>splice(0)</c> allocation, paid
    /// once per flush, which on a one-observer write is the whole op.
    /// Releasing each slot as it is consumed keeps the references collectable
    /// for the price of one store.
    /// </para>
    /// </remarks>
    let queue = ResizeArray<IScheduled>()

    /// <summary>
    /// Entries written. Slots at or past this are spent and reusable.
    /// </summary>
    let mutable queueCount = 0

    /// <summary>
    /// Next entry to run. Between flushes it is 0, except after a throw.
    /// </summary>
    let mutable queueHead = 0

    /// <summary>
    /// Queued projection passes and lookup refreshes, read through <c>passHead</c> and <c>passCount</c> as
    /// <c>queue</c> is. A flush runs every queued pass before the next <c>queue</c> item.
    /// </summary>
    /// <remarks>
    /// <para>A reader run ahead of a queued pass reads the previous keys, rows or lookup values.</para>
    /// <para>
    /// While a pass is queued, <c>queue</c> holds <c>passDrain</c> at or after <c>queueHead</c>, so a non-empty
    /// <c>queue</c> is the one test for queued work. A pass queued while an item runs puts <c>passDrain</c> at the
    /// head of <c>queue</c>, and one queued outside a flush puts it at the tail. A pass queued while
    /// <c>RunPasses</c> runs joins that run. A flush runs the passes queued before it starts ahead of its first item.
    /// </para>
    /// </remarks>
    let passes = ResizeArray<IScheduled>()

    /// <summary>Entries written to <c>passes</c>. Reset to 0 when <c>RunPasses</c> drains the buffer.</summary>
    let mutable passCount = 0

    /// <summary>Next entry of <c>passes</c> to run.</summary>
    let mutable passHead = 0

    /// <summary>The <c>queue</c> item that runs <c>RunPasses</c>. Created by the first queued pass.</summary>
    let mutable passDrain: IScheduled = Unchecked.defaultof<IScheduled>

    /// <summary>True while <c>RunPasses</c> runs; a pass queued then runs in the same <c>RunPasses</c>.</summary>
    let mutable runningPasses = false

#if !FABLE_COMPILER
    /// <summary>
    /// The thread the graph was constructed on. Everything mutating the graph
    /// must end up here; see <c>Dispatch</c>.
    /// </summary>
    let ownerThread = Platform.currentThreadId ()
#endif
    let guarded = options.ThreadAffinity = Guarded

    /// <summary>
    /// The owner thread's ambient cell when the graph is guarded, otherwise
    /// null: an unguarded graph resolves the calling thread's cell each time.
    /// </summary>
    let ownerAmbient = if guarded then AmbientSlot.Cell else null

#if !FABLE_COMPILER
    let serialised = options.ThreadAffinity = Serialised

    /// <summary>
    /// The synchronisation context every entry of a <c>Serialised</c> graph must run on.
    /// </summary>
    let context = SynchronizationContext.Current

    /// <summary>
    /// The id of the thread inside a <c>Serialised</c> graph, or 0 when the graph is free.
    /// </summary>
    let mutable holder = 0
#endif

#if !FABLE_COMPILER
    /// <summary>
    /// True when the outermost stale read acquired the graph and its <c>ExitPull</c> releases it.
    /// </summary>
    let mutable pullHeld = false
#endif

    let mutable batchDepth = 0
    let mutable flushing = false

    /// <summary>The recorder of <c>options.Journal</c>, or null when the graph has no journal.</summary>
    let journal =
        match options.Journal with
        | Some inputJournal -> JournalRecorder inputJournal
        | None -> null

    /// <summary>
    /// The scope everything created outside an explicit <c>Root</c> belongs to.
    /// Solid warns in this case and leaks; giving the graph a root instead
    /// means <c>graph.Dispose()</c> always tears everything down, and a leak is a
    /// test assertion rather than a console message.
    /// </summary>
    let root = new Owner ()
    do Tracer.GraphNew (root, guarded)

    /// <summary>
    /// The owner a node created now attaches to. Null while a scope host's
    /// body runs and has not yet created anything: <c>CurrentOwner</c> then
    /// allocates the scope of the running computation.
    /// </summary>
    let mutable currentOwner = root

#if !FABLE_COMPILER
    /// <summary>
    /// Saved on entry to each flight continuation, keyed by the body's frame: the tracking
    /// context, owner and raised pending read. Innermost last.
    /// </summary>
    let mutable continuations: ResizeArray<struct (obj * IComputation * Owner * INode)> =
        null
#endif

    /// <summary>
    /// The scope host whose body entered <c>untrack</c> before creating anything.
    /// Read only while both the owner and the tracking context are null.
    /// </summary>
    let mutable untrackedHost: IScopeHost = Unchecked.defaultof<IScopeHost>

    /// <summary>
    /// True inside an <c>untrack</c> block entered from a computation's body.
    /// </summary>
    let mutable untrackedInBody = false

    /// <summary>
    /// True when a flush was requested while a body ran or a stale read was
    /// bringing a computation current. The outermost such read runs it.
    /// </summary>
    let mutable flushOwed = false

    /// <summary>
    /// The number of stale reads in progress: computations bringing
    /// themselves current.
    /// </summary>
    let mutable pullDepth = 0

    /// <summary>
    /// The <c>Hosting</c> graph the outermost stale read replaced.
    /// </summary>
    let mutable pullAmbient: obj = null

    /// <summary>
    /// The number of teardowns in progress.
    /// </summary>
    let mutable detachedDepth = 0

#if !FABLE_COMPILER
    /// <summary>
    /// Work that arrived from another thread, waiting for this one to come and
    /// run it. See <c>Platform.Inbox</c> for why it is the queue it is.
    /// </summary>
    let inbox = Platform.Inbox<unit -> unit>()

    /// <summary>
    /// The <c>DispatchAsync</c> items in the inbox, faulted at disposal. Its lock also guards <c>dispatchClosed</c>.
    /// </summary>
    let pendingDispatches = HashSet<IPendingDispatch>(HashIdentity.Reference)

    /// <summary>
    /// True once disposal has faulted <c>pendingDispatches</c>; a later off-thread <c>DispatchAsync</c> faults at
    /// the call.
    /// </summary>
    let mutable dispatchClosed = false
#endif

    let dispatcher =
        match options.Dispatcher with
        | Some d -> d
        | None -> Platform.defaultDispatcher ()

    new() = new Graph (GraphOptions.Default)

    member _.Options = options

    /// <summary>
    /// The dispatcher this graph resolved at construction.
    /// </summary>
    member _.Dispatcher = dispatcher

    member _.Root = root

    /// <summary>
    /// The ambient graph on this thread: the graph running a body, a flush or
    /// a stale read, otherwise the activated one.
    /// </summary>
    static member TryCurrent =
        match Graph.Ambient with
        | :? Graph as g -> ValueSome g
        | _ -> ValueNone

#if !FABLE_COMPILER
    /// <summary>
    /// True with the ambient graph on this thread, as <c>TryCurrent</c> resolves it; otherwise false with <c>null</c>.
    /// </summary>
    static member TryGetCurrent([<System.Runtime.InteropServices.Out>] graph: byref<Graph>) : bool =
        match Graph.Ambient with
        | :? Graph as g ->
            graph <- g
            true
        | _ ->
            graph <- Unchecked.defaultof<Graph>
            false
#endif

    /// <summary>
    /// The ambient graph on this thread.
    /// </summary>
    /// <remarks>
    /// Throws rather than creating one on demand. A graph nobody constructed is
    /// a graph nobody disposes, and every effect it owns would outlive the
    /// scope the caller believes they wrote.
    /// </remarks>
    static member Current =
        match Graph.Ambient with
        | :? Graph as g -> g
        | _ ->
            raise (
                InvalidOperationException
                    "No ambient graph on this thread. Activate one with `use _ = graph.Activate ()`, or construct nodes against an explicit graph."
            )

    /// <summary>
    /// Makes this the ambient graph until the returned handle is disposed,
    /// restoring whatever was ambient before.
    /// </summary>
    /// <remarks>
    /// A stack rather than an assignment, so a library that activates its own
    /// graph for the length of a call cannot strand its caller's. On .NET the
    /// activation flows with the execution context, so it may span an <c>await</c>. A guarded graph is ambient on the
    /// activating thread only; an <c>Unchecked</c> or <c>Serialised</c> graph is ambient on every thread its execution
    /// context reaches.
    /// </remarks>
    member this.Activate() =
        let cell = AmbientSlot.Cell
        let activated = Activation.current ()
        let hosting = cell.Hosting
        Activation.set (ActivationEntry (this, Platform.currentThreadId (), not guarded))
        cell.Hosting <- null

        { new IDisposable with
            member _.Dispose() =
                Activation.set activated

                if obj.ReferenceEquals (AmbientSlot.Existing, cell) then
                    cell.Hosting <- hosting
        }

    /// <summary>
    /// The calling context's ambient graph, or null.
    /// </summary>
    static member private Ambient: obj =
        let cell = AmbientSlot.Existing

        match (if Platform.isNull cell then null else cell.Hosting) with
        | :? Graph as g when g.Running -> g
        | _ ->
            let entry = Activation.current ()

            if Platform.isNull entry then
                null
            else
                entry.GraphOnCurrentThread

    /// <summary>
    /// True while the graph runs a body, a flush, a stale read or a teardown.
    /// </summary>
    member internal _.Running =
        flushing
        || pullDepth > 0
        || not (Platform.isNull (box current))
        || detachedDepth > 0
        || untrackedInBody

    /// <summary>The graph's journal recorder, or null when the graph has no journal.</summary>
    member internal _.Journal = journal

    /// <summary>
    /// Records <c>value</c> arriving at the node <c>nodeId</c> as a <c>kind</c> entry, unless a computation, flush,
    /// stale read, teardown or other derived section is running. Requires a journal.
    /// </summary>
    /// <returns>False when a diagnostic replay drops the input, which the caller then leaves unapplied.</returns>
    member internal this.RecordInput(kind: JournalEntryKind, nodeId: int, value: obj) : bool =
        if this.Running || journal.DerivedDepth > 0 then
            journal.AdmitDerived ()
        elif journal.Admit () then
            journal.Input (kind, nodeId, value)
            true
        else
            false

    /// <summary>
    /// Makes this graph ambient and returns the graph it replaced in <c>Hosting</c>.
    /// </summary>
    member inline private this.EnterAmbient() : obj =
        let cell =
            if Platform.isNull ownerAmbient then
                AmbientSlot.Cell
            else
                ownerAmbient

        let previous = cell.Hosting

        if not (obj.ReferenceEquals (previous, this)) then
            cell.Hosting <- this

        previous

    /// <summary>
    /// Hands <c>Hosting</c> back to <c>previous</c> when this graph displaced another
    /// that is still running.
    /// </summary>
    member inline private this.LeaveAmbient(previous: obj) =
        if not (obj.ReferenceEquals (previous, this)) then
            match previous with
            | :? Graph as g when g.Running ->
                let cell =
                    if Platform.isNull ownerAmbient then
                        AmbientSlot.Cell
                    else
                        ownerAmbient

                cell.Hosting <- previous
            | _ -> ()

#if !FABLE_COMPILER
    /// <summary>
    /// Makes the calling thread the graph's holder under <c>Serialised</c>.
    /// </summary>
    /// <returns>True when this call entered a free graph and must be paired with <c>Release</c>.</returns>
    /// <exception cref="T:System.InvalidOperationException">
    /// The caller runs outside the construction context, or another thread holds the graph.
    /// </exception>
    member private this.Acquire(operation: string) : bool =
        let me = Platform.currentThreadId ()

        if Volatile.Read &holder = me then
            false
        else
            this.CheckContext operation
            let prior = Interlocked.CompareExchange (&holder, me, 0)

            if prior <> 0 then
                this.FailConcurrent (operation, prior)

            true
#endif

    /// <summary>
    /// Frees a graph entered by <c>Acquire</c>, and posts a drain when the inbox holds work.
    /// </summary>
    member internal this.Release() =
#if !FABLE_COMPILER
        // A full fence: the inbox check below observes every enqueue whose drain found the graph held by this thread.
        Interlocked.Exchange (&holder, 0) |> ignore

        if not inbox.IsEmpty then
            dispatcher.Post (Action this.PumpFromDispatcher)
#else
        ()
#endif

    /// <summary>
    /// Checks the caller as <c>AssertOnGraphThread</c> does, and under <c>Serialised</c> enters the graph.
    /// </summary>
    /// <returns>True when the caller must pair it with <c>Release</c>.</returns>
    [<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)>]
    member internal this.Enter(operation: string) : bool =
#if FABLE_COMPILER
        ignore operation
        false
#else
        if guarded then
            if not (obj.ReferenceEquals (AmbientSlot.Existing, ownerAmbient)) then
                this.FailOffThread operation

            false
        else
            serialised && this.Acquire operation
#endif

    /// <summary>
    /// Runs <c>body</c> as the entry point <c>operation</c>: checked under <c>Guarded</c>, held under
    /// <c>Serialised</c>.
    /// </summary>
    member inline internal this.Entered<'T>(operation: string, [<InlineIfLambda>] body: unit -> 'T) : 'T =
#if FABLE_COMPILER
        body ()
#else
        if this.Enter operation then
            try
                body ()
            finally
                this.Release ()
        else
            body ()
#endif

    member internal _.CurrentOwner =
        if Platform.isNull (box currentOwner) then
            let host =
                if Platform.isNull (box current) then
                    untrackedHost
                else
                    current :?> IScopeHost

            currentOwner <- host.Scope

        currentOwner

    /// <summary>
    /// Creates a nested scope, runs <c>body</c> inside it, and hands back the owner
    /// so the caller can dispose the whole subtree at once.
    /// </summary>
    member this.CreateRoot(body: Func<Owner, 'T>) =
        this.RunRoot body.Invoke

    member internal this.RunRoot(body: Owner -> 'T) =
        this.Entered (
            "Creating a root",
            fun () ->
                let owner = new RootScope (this) :> Owner
                owner.SetParent (this.CurrentOwner.AttachLinked owner)
                let previous = currentOwner
                currentOwner <- owner

                try
                    body owner
                finally
                    currentOwner <- previous
        )

    /// <summary>
    /// Registers a cleanup with the innermost enclosing scope. Inside the body
    /// of an effect, memo, async memo or boundary that is the computation's own
    /// scope, so the cleanup runs before the next re-run as well as at disposal.
    /// On a disposed scope <c>f</c> runs immediately, untracked and with effects
    /// deferred, as a teardown runs it.
    /// </summary>
    member this.OnCleanup(f: Action) =
        this.AddCleanup f.Invoke

    member internal this.AddCleanup(f: unit -> unit) =
        this.Entered (
            "Registering a cleanup",
            fun () ->
                let owner = this.CurrentOwner

                if owner.IsDisposed then
                    this.Detached (owner, (fun () -> owner.AddCleanup f))
                    this.RequestFlush ()
                else
                    owner.AddCleanup f
        )

    member internal _.RunOwned(owner: Owner, body: unit -> 'T) =
        let previous = currentOwner
        currentOwner <- owner

        try
            body ()
        finally
            currentOwner <- previous

    /// <summary>
    /// Tears down every scope the graph owns, and faults the task of every <c>DispatchAsync</c> item still in the
    /// inbox with <c>ObjectDisposedException</c>.
    /// </summary>
    member this.Dispose() =
        this.Entered (
            "Disposing a graph",
            fun () ->
                root.Dispose ()
#if !FABLE_COMPILER
                let abandoned =
                    lock pendingDispatches (fun () ->
                        dispatchClosed <- true
                        let items = Seq.toArray pendingDispatches
                        pendingDispatches.Clear ()
                        items)

                for item in abandoned do
                    item.Abandon (ObjectDisposedException (nameof Graph))
#endif

                if
                    not (Platform.isNull ownerAmbient)
                    && obj.ReferenceEquals (ownerAmbient.Hosting, this)
                then
                    ownerAmbient.Hosting <- null
        )

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

#if RANVIER_TRACE
    interface ITraced with
        member _.TraceLog
            with get () = (root :> ITraced).TraceLog
            and set log = (root :> ITraced).TraceLog <- log

        member _.TraceId
            with get () = 0
            and set _ = ()
#endif

    /// <summary>
    /// True when the calling thread is the graph's owner thread; under <c>Serialised</c>, when the calling thread is
    /// inside the graph.
    /// </summary>
    member _.IsOnGraphThread =
#if FABLE_COMPILER
        true
#else
        if serialised then
            Volatile.Read &holder = Platform.currentThreadId ()
        else
            Platform.currentThreadId () = ownerThread
#endif

    /// <summary>
    /// Marshals <c>work</c> into the graph's consistency domain, running it inline
    /// when already there.
    /// </summary>
    /// <remarks>
    /// The inline fast path is the whole design. A cross-thread hop measured
    /// 2.35–3.32 µs, against 3.2 µs for a throw through ten frames — marshalling
    /// costs what suspension costs, and the queue primitive used barely matters
    /// because the consumer wake-up dominates it by ~100x. So the dispatcher's
    /// job is to be skipped, not to be fast. See
    /// docs/.ai/RESEARCH-loony-synchronization.md §4.3.
    /// </remarks>
    member this.Dispatch(work: Action) =
        this.Post work.Invoke

    member internal this.Post(work: unit -> unit) =
#if FABLE_COMPILER
        work ()
#else
        if this.IsOnGraphThread then
            work ()
        else
            // The work goes into the graph's inbox; the dispatcher receives
            // only the drain. ImmediateDispatcher drains on this thread: under
            // Unchecked the work runs here, and under Guarded the pump raises
            // and the work stays queued.
            let attributed =
                if Platform.isNull journal then
                    null
                else
                    JournalAmbient.current ()

            if Platform.isNull attributed then
                inbox.Enqueue work
            else
                inbox.Enqueue (fun () -> journal.RunAttributed (attributed, work))

            dispatcher.Post (Action this.PumpFromDispatcher)
#endif

    /// <summary>
    /// Marshals <c>work</c> as <c>Dispatch</c> does. The returned task completes once
    /// <c>work</c> has run, whether or not it threw.
    /// </summary>
    member internal this.DispatchApplied(work: unit -> unit) : Task =
#if FABLE_COMPILER
        this.Post work
        Platform.completedTask
#else
        if this.IsOnGraphThread then
            work ()
            Platform.completedTask
        else
            let applied = TaskCompletionSource ()

            this.Post (fun () ->
                try
                    work ()
                finally
                    applied.SetResult ())

            applied.Task
#endif

    /// <summary>
    /// Runs <c>work</c> in the graph's consistency domain, as <c>Dispatch</c> does, and returns a task that completes
    /// with its result once it has run.
    /// </summary>
    /// <remarks>
    /// On the graph thread the work runs before the call returns and the task is already complete. From another
    /// thread the work joins the inbox behind earlier <c>Dispatch</c> and <c>DispatchAsync</c> items and runs on the
    /// drain the dispatcher schedules; under <c>ManualDispatcher</c> that is the owner's next <c>Pump</c>. Continuations
    /// of a queued item's task run on the thread pool, outside the drain. A continuation attached to an inline call's
    /// task, by <c>await</c> or <c>ExecuteSynchronously</c>, runs at once on the graph thread, inside the graph.
    /// Blocking on the task from the owning thread deadlocks when that thread is the only one that drains.
    ///
    /// Completion marks the end of the work, not of what it triggers. Effects woken by its writes run as they do for
    /// <c>Dispatch</c> work: at the write, or at the end of a batch the work opens, before the task completes. An
    /// inline call inside an enclosing batch completes its task first, and that batch's flush runs the effects. Async
    /// memos the work starts settle later.
    ///
    /// When the work throws, the task faults with that exception, and the caller is its only recipient:
    /// <c>Root.Errors</c> stays unchanged and later inbox items still run.
    ///
    /// On a disposed graph the task faults with <c>ObjectDisposedException</c> and the work does not run. Disposal
    /// faults the task of every item still in the inbox in the same way, without waiting for a drain.
    /// </remarks>
    member this.DispatchAsync(work: Func<'T>) : Task<'T> =
        this.DispatchAsync (work, CancellationToken.None)

    /// <summary>
    /// Runs <c>work</c> in the graph's consistency domain as the single-argument overload does, and cancels the task
    /// instead when <c>cancellationToken</c> is cancelled before the work starts.
    /// </summary>
    /// <remarks>
    /// A token already cancelled at the call returns a cancelled task and queues nothing. A token cancelled while the
    /// item waits in the inbox cancels the task at once, and the drain skips the work. Once the work starts it runs to
    /// completion and the token no longer applies. Cancellation is checked before disposal.
    /// </remarks>
    member this.DispatchAsync(work: Func<'T>, cancellationToken: CancellationToken) : Task<'T> =
#if FABLE_COMPILER
        let finished = Platform.completion<'T>()

        if cancellationToken.IsCancellationRequested then
            finished.Reject (OperationCanceledException "The operation was canceled.")
        elif root.IsDisposed then
            finished.Reject (ObjectDisposedException (nameof Graph))
        else
            let outcome =
                try
                    Result.Ok (work.Invoke ())
                with ex ->
                    Result.Error ex

            match outcome with
            | Result.Ok value -> finished.Resolve value
            | Result.Error ex -> finished.Reject ex

        finished.Task
#else
        if cancellationToken.IsCancellationRequested then
            Task.FromCanceled<'T> cancellationToken
        elif root.IsDisposed then
            Task.FromException<'T>(ObjectDisposedException (nameof Graph))
        elif this.IsOnGraphThread then
            let outcome =
                try
                    Result.Ok (work.Invoke ())
                with ex ->
                    Result.Error ex

            match outcome with
            | Result.Ok value -> Task.FromResult value
            | Result.Error ex -> Task.FromException<'T> ex
        else
            this.EnqueueDispatch (work, cancellationToken)

    /// <summary>
    /// Queues <c>work</c> from another thread as a <c>DispatchAsync</c> item, registered for disposal and
    /// cancellation.
    /// </summary>
    member private this.EnqueueDispatch(work: Func<'T>, cancellationToken: CancellationToken) : Task<'T> =
        let pending = PendingDispatch<'T>()

        let admitted =
            lock pendingDispatches (fun () ->
                not dispatchClosed
                && pendingDispatches.Add pending)

        if not admitted then
            Task.FromException<'T>(ObjectDisposedException (nameof Graph))
        else
            let forget () =
                lock pendingDispatches (fun () -> pendingDispatches.Remove pending |> ignore)

            let registration =
                if cancellationToken.CanBeCanceled then
                    cancellationToken.Register (fun () ->
                        pending.Cancel cancellationToken
                        forget ())
                else
                    new CancellationTokenRegistration ()

            this.Post (fun () ->
                if pending.TryClaim () then
                    forget ()
                    registration.Dispose ()

                    let outcome =
                        try
                            Result.Ok (work.Invoke ())
                        with ex ->
                            Result.Error ex

                    match outcome with
                    | Result.Ok value -> pending.SetResult value
                    | Result.Error ex -> pending.SetException ex)

            pending.Task
#endif

    /// <summary>
    /// Work that arrived from another thread and has not been drained yet.
    /// </summary>
    member _.PendingWork =
#if FABLE_COMPILER
        0
#else
        inbox.Count
#endif

    /// <summary>
    /// Runs everything the inbox has collected, then flushes. Must be called on
    /// the graph's own thread — that is the entire point of the inbox.
    /// </summary>
    /// <remarks>
    /// Returns the number of items run, so a caller draining in a loop can tell
    /// progress from a spin.
    /// </remarks>
    member this.Pump() =
        this.Entered ("Pump", (fun () -> this.Drain ()))

    member private this.Drain() =
#if FABLE_COMPILER
        0
#else
        let mutable ran = 0
        let mutable draining = true

        // An inbox item carries its own attribution from Post, so the drain
        // runs outside the work of the context that posted the drain.
        let carried =
            if Platform.isNull journal then
                null
            else
                JournalAmbient.current ()

        if not (Platform.isNull carried) then
            JournalAmbient.set null

        try
            while draining do
                match inbox.TryTake () with
                | ValueNone -> draining <- false
                | ValueSome work ->
                    ran <- ran + 1

                    // One bad continuation must not strand the rest of the inbox,
                    // for the same reason one bad effect must not strand the flush
                    // queue.
                    try
                        work ()
                    with ex ->
                        root.RecordError ex

            if ran > 0 then
                this.RequestFlush ()
        finally
            if not (Platform.isNull carried) then
                JournalAmbient.set carried

        ran
#endif

    member private this.PumpFromDispatcher() =
#if FABLE_COMPILER
        this.Pump () |> ignore
#else
        if not serialised then
            this.Pump () |> ignore
        else
            let me = Platform.currentThreadId ()

            if Volatile.Read &holder = me then
                this.Drain () |> ignore
            else
                this.CheckContext "Pump"

                // Held by another thread: its Release posts a new drain.
                if Interlocked.CompareExchange (&holder, me, 0) = 0 then
                    try
                        this.Drain () |> ignore
                    finally
                        this.Release ()
#endif

    /// <summary>
    /// Raises if the caller is not on the thread that owns this graph. Under <c>Serialised</c>, raises if another
    /// thread is inside the graph, or if the caller is outside it and runs off the construction context.
    /// </summary>
    /// <remarks>
    /// Thread-affinity checks are omitted under Fable.
    /// </remarks>
#if FABLE_COMPILER
    member inline this.AssertOnGraphThread(operation: string) =
        ignore operation
#else
    member this.AssertOnGraphThread(operation: string) =
        // The owner thread is the one thread whose cell is `ownerAmbient`.
        if guarded then
            if not (obj.ReferenceEquals (AmbientSlot.Existing, ownerAmbient)) then
                this.FailOffThread operation
        elif serialised then
            this.CheckHeld operation
#endif

#if !FABLE_COMPILER
    member private _.FailOffThread(operation: string) : unit =
        raise (
            InvalidOperationException (
                operation
                + " ran on thread "
                + string (Platform.currentThreadId ())
                + ", but this graph is owned by thread "
                + string ownerThread
                + ". Marshal through Graph.Dispatch, or set GraphOptions.ThreadAffinity to Unchecked if affinity is guaranteed some other way."
            )
        )

    /// <summary>
    /// Raises unless the calling thread is inside the graph, or the graph is free and the caller runs on its
    /// context. Enters nothing.
    /// </summary>
    member private this.CheckHeld(operation: string) =
        let held = Volatile.Read &holder

        if held <> Platform.currentThreadId () then
            if held <> 0 then
                this.FailConcurrent (operation, held)

            this.CheckContext operation
#endif

#if !FABLE_COMPILER
    member private _.CheckContext(operation: string) =
        if not (obj.ReferenceEquals (SynchronizationContext.Current, context)) then
            raise (
                InvalidOperationException (
                    operation
                    + " ran on thread "
                    + string (Platform.currentThreadId ())
                    + " outside the synchronisation context this Serialised graph was constructed on. Marshal through Graph.Dispatch."
                )
            )

    member private _.FailConcurrent(operation: string, other: int) : unit =
        raise (
            InvalidOperationException (
                operation
                + " ran on thread "
                + string (Platform.currentThreadId ())
                + " while thread "
                + string other
                + " was inside this Serialised graph. Two threads entered it at once."
            )
        )
#endif


    member internal this.Schedule(item: IScheduled) =
        Tracer.Schedule (this, item, queueCount - queueHead + passCount - passHead)

        if queueCount < queue.Count then
#if FABLE_COMPILER
            Platform.setEntryAt queueCount queue item
#else
            queue[queueCount] <- item
#endif
        else
            queue.Add item

        queueCount <- queueCount + 1

    /// <summary>Queues a projection pass or a lookup refresh. A flush runs it before the next item <c>Schedule</c> queued.</summary>
    member internal this.SchedulePass(item: IScheduled) =
        Tracer.Schedule (this, item, passCount - passHead)

        if passHead = passCount && not runningPasses then
            this.QueueDrain ()

        if passCount < passes.Count then
#if FABLE_COMPILER
            Platform.setEntryAt passCount passes item
#else
            passes[passCount] <- item
#endif
        else
            passes.Add item

        passCount <- passCount + 1

    /// <summary>
    /// Queues <c>passDrain</c> for a pass queued while none is pending: at the head of <c>queue</c> when a spent slot
    /// precedes it, as inside a flush, and at the tail otherwise.
    /// </summary>
    member private this.QueueDrain() =
        if Platform.isNull (box passDrain) then
            passDrain <- PassDrain (fun () -> this.RunPasses ())

        if queueHead > 0 then
            queueHead <- queueHead - 1
#if FABLE_COMPILER
            Platform.setEntryAt queueHead queue passDrain
#else
            queue[queueHead] <- passDrain
#endif
        else
            if queueCount < queue.Count then
#if FABLE_COMPILER
                Platform.setEntryAt queueCount queue passDrain
#else
                queue[queueCount] <- passDrain
#endif
            else
                queue.Add passDrain

            queueCount <- queueCount + 1

    /// <summary>Runs the queued passes, including those queued by a pass, and empties the pass buffer.</summary>
    /// <remarks>A pass that throws leaves the passes after it queued, with <c>passDrain</c> queued to run them.</remarks>
    member private this.RunPasses() =
        runningPasses <- true

        try
            while passHead < passCount do
                let item = Platform.entryAt passHead passes
                Tracer.Dequeue (this, item)
                Platform.releaseSlot passes passHead
                passHead <- passHead + 1
                item.Execute ()
        finally
            runningPasses <- false

            if passHead < passCount then
                this.QueueDrain ()
            else
                passHead <- 0
                passCount <- 0

    /// <summary>
    /// Runs every queued effect. Re-entrant calls are absorbed: an effect that
    /// writes a signal adds to the queue this loop is already draining, rather
    /// than starting a nested flush.
    /// </summary>
    /// <remarks>
    /// The graph is ambient while the loop runs. Every queued projection pass and lookup refresh runs before the next
    /// queued effect, so an effect reads the keys, rows and lookup values of the write that woke it.
    /// </remarks>
    member this.Flush() =
        this.Entered ("A flush", (fun () -> this.RunFlush ()))

    member private this.RunFlush() =
        if not flushing then
#if RANVIER_COUNTERS
            Counters.Flushed ()
#endif
            Tracer.FlushStart this
            flushing <- true
            flushOwed <- false
            let previousAmbient = this.EnterAmbient ()

            try
                // Passes queued before the flush run ahead of the effects queued with them. A pass queued during the
                // flush puts `passDrain` at the head of the queue.
                if passCount <> 0 then
                    this.RunPasses ()

                while queueHead < queueCount do
                    let item = Platform.entryAt queueHead queue
                    Tracer.Dequeue (this, item)

                    // Released as it is consumed, so a spent slot leaves a
                    // disposed effect collectable.
                    Platform.releaseSlot queue queueHead
                    queueHead <- queueHead + 1
                    item.Execute ()

                // Reached only on a clean drain. An effect that throws leaves
                // the cursor where it stopped and the rest still queued, for
                // the next flush to pick up, which is what the `Queue` did.
                queueHead <- 0
                queueCount <- 0
            finally
                flushing <- false

                if not (Platform.isNull lastRaised) then
                    lastRaised <- null

                Tracer.FlushEnd this
                Tracer.Flushed this
                this.LeaveAmbient previousAmbient

                if not (Platform.isNull journal) then
                    journal.Boundary ()

    /// <summary>
    /// Runs the queued effects. Inside a batch they run when the batch ends.
    /// Inside a computation's body, including <c>untrack</c> within one, or a
    /// stale read, they run once the outermost body or read has finished.
    /// </summary>
    member internal this.RequestFlush() =
        if queueHead < queueCount && batchDepth = 0 then
            if
                pullDepth = 0
                && Platform.isNull (box current)
                && not untrackedInBody
            then
                this.Flush ()
            elif not flushing then
                flushOwed <- true

    /// <summary>
    /// Runs the owed flush when the caller is outside every body and read.
    /// </summary>
    member private this.SettleFlush() =
        if
            flushOwed
            && batchDepth = 0
            && pullDepth = 0
            && Platform.isNull (box current)
            && not untrackedInBody
        then
            this.Flush ()

    /// <summary>
    /// True inside a stale read, or while a computation's body runs outside
    /// <c>untrack</c>. False inside <c>untrack</c> within a body, where the flush is
    /// deferred as well. Under <c>Serialised</c>, false on every thread other than the holder, so a stale read from
    /// such a thread enters the graph through <c>EnterPull</c>.
    /// </summary>
    member internal _.Deferring =
        (pullDepth > 0
         || not (Platform.isNull (box current)))
#if !FABLE_COMPILER
        && not (
            serialised
            && Volatile.Read &holder
               <> Platform.currentThreadId ()
        )
#endif

    /// <summary>
    /// Marks the start of a stale read. The outermost one makes this graph
    /// ambient until it ends.
    /// </summary>
    member internal this.EnterPull() =
#if !FABLE_COMPILER
        if this.Enter "A stale read" then
            pullHeld <- true
#endif

        if pullDepth = 0 then
            let previous = this.EnterAmbient ()

            if not (obj.ReferenceEquals (pullAmbient, previous)) then
                pullAmbient <- previous

        pullDepth <- pullDepth + 1

    /// <summary>
    /// Marks the end of a stale read. The outermost one runs the owed flush.
    /// </summary>
    member internal this.ExitPull() =
        pullDepth <- pullDepth - 1

        if pullDepth = 0 then
            this.LeaveAmbient pullAmbient

            if not (Platform.isNull lastRaised) then
                lastRaised <- null

#if !FABLE_COMPILER
            if pullHeld then
                pullHeld <- false

                try
                    if flushOwed then
                        this.SettleFlush ()
                finally
                    this.Release ()
            elif flushOwed then
                this.SettleFlush ()
#else
            if flushOwed then
                this.SettleFlush ()
#endif

    /// <summary>
    /// Evaluates <c>body</c> without recording anything it reads.
    /// </summary>
    /// <remarks>
    /// The public face of the tracking context: a read inside <c>body</c> still
    /// returns the current value, it just does not create an edge, so the
    /// enclosing computation is not woken when that source changes.
    /// </remarks>
    member this.Untrack(body: Func<'T>) =
        this.RunUntracked body.Invoke

    /// <summary>
    /// The current status of <c>node</c>. A readable node (a signal, memo, async value, boundary or projection) is
    /// brought up to date first, and inside a computation the read is tracked, as <c>TryValue</c> is.
    /// </summary>
    /// <remarks>
    /// A pending or failed node returns its status without raising, so a computation can test nodes of any value type.
    /// An effect or other unreadable node returns its status untracked.
    /// </remarks>
    member this.TrackStatus(node: INode) : Status =
        let source =
#if FABLE_COMPILER
            // Fable compiles an interface type test to false: probe for the member instead.
            if Platform.hasMember node "UpdateIfNecessary" then
                node :?> ISource
            else
                Unchecked.defaultof<ISource>
#else
            match node with
            | :? ISource as source -> source
            | _ -> Unchecked.defaultof<ISource>
#endif

        if not (Platform.isNull (box source)) then
            source.UpdateIfNecessary ()
            this.Track source

        node.Status

    /// <summary>
    /// Defers the flush until <c>body</c> returns, so a group of writes produces one
    /// effect run rather than one per write.
    /// </summary>
    member this.Batch(body: Func<'T>) =
        this.RunBatch body.Invoke

    member internal this.RunBatch(body: unit -> 'T) =
        this.Entered (
            "A batch",
            fun () ->
                batchDepth <- batchDepth + 1
                Tracer.BatchEnter (this, batchDepth)

                let result =
                    try
                        body ()
                    finally
                        batchDepth <- batchDepth - 1
                        Tracer.BatchExit (this, batchDepth)

                this.RequestFlush ()
                result
        )

    /// <summary>
    /// Discharges <c>owner</c> with effects deferred. A computation discharges its
    /// scope before re-running its body. The effects woken by its cleanups
    /// run once, after the outermost stale read has finished.
    /// </summary>
    /// <remarks>
    /// The cleanups run untracked, and a node they create belongs to <c>owner</c>.
    /// </remarks>
    member internal this.Discharge(owner: Owner) =
        Tracer.DischargeStart owner
        this.Detached (owner, owner.DisposeScope)
        Tracer.DischargeEnd owner

        if
            batchDepth = 0
            && not flushing
            && queueHead < queueCount
        then
            flushOwed <- true

    /// <summary>
    /// Disposes a computation's scope as <c>Discharge</c> discharges it. A node
    /// its cleanups create is disposed immediately.
    /// </summary>
    member internal this.Retire(owner: Owner) =
        this.Detached (owner, owner.Dispose)

        if queueHead < queueCount then
            this.RequestFlush ()

    interface ILateRunner with
        member this.RunDetached(owner, f) =
            // Holds a Serialised graph for a root scope's teardown; Guarded checks each disposed node instead.
#if !FABLE_COMPILER
            let held = serialised && this.Acquire "Disposing a root"
#endif

            try
                this.Detached (
                    owner,
                    fun () ->
                        try
                            f ()
                        with ex ->
                            owner.RecordError ex
                )

                if queueHead < queueCount then
                    this.RequestFlush ()
            finally
#if !FABLE_COMPILER
                if held then
                    this.Release ()
#else
                ()
#endif

        member this.HoldTeardown() =
#if FABLE_COMPILER
            false
#else
            serialised && this.Acquire "Disposing a root"
#endif

        member this.ReleaseTeardown() =
            this.Release ()

    /// <summary>
    /// Runs <c>teardown</c> untracked, with effects deferred and <c>owner</c> as the
    /// owner of every node it creates. The graph is ambient while it runs.
    /// </summary>
    member private this.Detached(owner: Owner, teardown: unit -> unit) =
        let previous = current
        let previousOwner = currentOwner
        let previousAmbient = this.EnterAmbient ()
        current <- Unchecked.defaultof<IComputation>
        currentOwner <- owner
        batchDepth <- batchDepth + 1
        detachedDepth <- detachedDepth + 1

        try
            teardown ()
        finally
            detachedDepth <- detachedDepth - 1
            batchDepth <- batchDepth - 1
            current <- previous
            currentOwner <- previousOwner
            this.LeaveAmbient previousAmbient

    member internal this.NextId() =
        this.AssertOnGraphThread "Creating a node"
        nextId <- nextId + 1
        nextId

    /// <summary>The last id <c>NextId</c> issued.</summary>
    member internal _.NodeCount = nextId

    /// <summary>
    /// Links <c>source</c> into the computation currently being evaluated, if any.
    /// </summary>
    /// <remarks>
    /// MANDATORY: callers must invoke this before throwing or returning
    /// Pending. A read that suspends without first linking the edge leaves the
    /// consumer stranded — it never learns that the source settled. This is
    /// upstream bug #2893.
    /// </remarks>
    member internal _.Track(source: ISource) =
        if not (obj.ReferenceEquals (current, null)) then
            // The observer half is linked by the consumer's own source list,
            // which is the only side that knows whether this edge is new.
            current.AddSource source

    /// <summary>
    /// The computation whose body is running, or null. Read by a source that
    /// has just recomputed, to avoid marking its own reader dirty — see
    /// <c>ObserverSet.NotifyDirtyExcept</c>.
    /// </summary>
    member internal _.CurrentComputation = current

    /// <summary>
    /// Evaluates <c>body</c> with <c>host</c> as the tracking context and its scope as
    /// the owner of every node <c>body</c> creates. <c>host</c> implements <c>IScopeHost</c>.
    /// </summary>
    /// <remarks>
    /// A body that returns after a pending read raised inside it raises
    /// <c>NotReadyException</c> for that read, whatever it returned.
    /// </remarks>
    member internal _.RunHosted(host: IComputation, body: unit -> 'T) =
        let previous = current
        let previousOwner = currentOwner
        let previousRaised = raisedPending
        current <- host
        currentOwner <- Unchecked.defaultof<Owner>
        raisedPending <- Unchecked.defaultof<INode>

        try
            let result = body ()

            if not (Platform.isNull (box raisedPending)) then
                raise (NotReadyException raisedPending)

            result
        finally
            current <- previous

            // Each reference store is a GC write barrier; both fields usually still hold the saved value.
            if not (obj.ReferenceEquals (currentOwner, previousOwner)) then
                currentOwner <- previousOwner

            if not (obj.ReferenceEquals (raisedPending, previousRaised)) then
                raisedPending <- previousRaised

    /// <summary>
    /// Evaluates <c>body arg</c> as <c>RunHosted(host, body)</c> evaluates <c>body ()</c>.
    /// </summary>
    member internal _.RunHosted(host: IComputation, body: 'A -> 'T, arg: 'A) =
        let previous = current
        let previousOwner = currentOwner
        let previousRaised = raisedPending
        current <- host
        currentOwner <- Unchecked.defaultof<Owner>
        raisedPending <- Unchecked.defaultof<INode>

        try
            let result = body arg

            if not (Platform.isNull (box raisedPending)) then
                raise (NotReadyException raisedPending)

            result
        finally
            current <- previous

            // Each reference store is a GC write barrier; both fields usually still hold the saved value.
            if not (obj.ReferenceEquals (currentOwner, previousOwner)) then
                currentOwner <- previousOwner

            if not (obj.ReferenceEquals (raisedPending, previousRaised)) then
                raisedPending <- previousRaised

#if !FABLE_COMPILER
    /// <summary>
    /// Detaches a flight continuation starting on the graph thread from the
    /// computation it interrupts: reads in it are untracked, and nodes it
    /// creates belong to the root.
    /// </summary>
    /// <returns>
    /// True when it detaches. False off the graph thread, or when <c>frame</c> is already
    /// entered and a nested continuation is returning to it.
    /// </returns>
    member internal this.EnterContinuation(frame: obj) : bool =
        if not this.IsOnGraphThread then
            false
        else
            if Platform.isNull continuations then
                continuations <- ResizeArray ()

            let mutable entered = false

            for i in 0 .. continuations.Count - 1 do
                let struct (saved, _, _, _) = continuations[i]

                if Object.ReferenceEquals (saved, frame) then
                    entered <- true

            if entered then
                false
            else
                continuations.Add (struct (frame, current, currentOwner, raisedPending))
                current <- Unchecked.defaultof<IComputation>
                currentOwner <- root
                raisedPending <- Unchecked.defaultof<INode>

                if not (Platform.isNull journal) then
                    journal.EnterDerived ()

                true

    /// <summary>Restores what <c>EnterContinuation</c> detached for <c>frame</c>.</summary>
    member internal this.LeaveContinuation(frame: obj) =
        if
            this.IsOnGraphThread
            && not (Platform.isNull continuations)
            && continuations.Count > 0
        then
            let top = continuations.Count - 1
            let struct (saved, computation, owner, raised) = continuations[top]

            if Object.ReferenceEquals (saved, frame) then
                continuations.RemoveAt top
                current <- computation
                currentOwner <- owner
                raisedPending <- raised

                if not (Platform.isNull journal) then
                    journal.LeaveDerived ()
#endif

    /// <summary>
    /// The exception a pending read of <c>node</c> raises. Inside a computation's
    /// body, the read is recorded for <c>RunHosted</c>.
    /// </summary>
    member internal this.NotReady(node: INode) : exn =
        if not (Platform.isNull (box current)) then
            Tracer.Suspend (this, current, node)
            raisedPending <- node

        NotReadyException node

    /// <summary>
    /// Records <c>failure</c> as the failure of the last failed read, for <c>FailureOf</c> in the run that catches it.
    /// </summary>
    /// <remarks>Records nothing outside every stale read and flush.</remarks>
    member internal _.Raised(failure: Failure) =
        if pullDepth > 0 || flushing then
            lastRaised <- failure

    /// <summary>Raises the exception of <c>failure</c> to the reader of a failed node, as recorded by <c>Raised</c>.</summary>
    member internal this.Raise(failure: Failure) : unit =
        this.Raised failure
        failure.Rethrow ()

    /// <summary>
    /// The failure a run records for <c>error</c>: the last failed read's when it holds <c>error</c>, else
    /// <c>previous</c> when it holds <c>error</c>, else a new failure originating in <c>origin</c>. Clears the last
    /// failed read.
    /// </summary>
    /// <remarks>
    /// A run that catches a failed read of A, then reads a failed B, then rethrows A's exception records it as its own.
    /// </remarks>
    member internal _.FailureOf(error: exn, origin: INode, previous: Failure) : Failure =
        let raised = lastRaised

        if not (Platform.isNull raised) then
            lastRaised <- null

        if
            not (Platform.isNull raised)
            && obj.ReferenceEquals (raised.Error, error)
        then
            raised
        elif
            not (Platform.isNull previous)
            && obj.ReferenceEquals (previous.Error, error)
        then
            previous
        else
            Failure (error, origin)

    /// <summary>True when the running body has raised a pending read.</summary>
    member internal _.RaisedPending = not (Platform.isNull (box raisedPending))

    /// <summary>
    /// Forgets the pending reads raised so far in the running body. Called by
    /// a boundary that handles them.
    /// </summary>
    member internal _.ClearRaised() =
        raisedPending <- Unchecked.defaultof<INode>

    /// <summary>
    /// Raises <c>NotReadyException</c> if a pending read raised in the running body
    /// since the last <c>ClearRaised</c>.
    /// </summary>
    member internal _.CheckRaised() =
        if not (Platform.isNull (box raisedPending)) then
            raise (NotReadyException raisedPending)

    /// <summary>
    /// Evaluates <c>body</c> with tracking suppressed.
    /// </summary>
    member internal this.RunUntracked(body: unit -> 'T) =
        this.Entered (
            "An untracked read",
            fun () ->
                let previous = current
                let previousHost = untrackedHost
                let previousInBody = untrackedInBody

                if not (Platform.isNull (box previous)) then
                    untrackedInBody <- true

                    if Platform.isNull (box currentOwner) then
                        untrackedHost <- previous :?> IScopeHost

                current <- Unchecked.defaultof<IComputation>

                try
                    body ()
                finally
                    current <- previous
                    untrackedHost <- previousHost
                    untrackedInBody <- previousInBody
        )

    /// <summary>
    /// Evaluates <c>body</c> with tracking suppressed and <c>host</c>'s scope rule
    /// applied to every node <c>body</c> creates.
    /// </summary>
    member internal _.RunUntrackedHosted(host: IScopeHost, body: unit -> 'T) =
        let previous = current
        let previousOwner = currentOwner
        let previousHost = untrackedHost
        let previousInBody = untrackedInBody

        if not (Platform.isNull (box previous)) then
            untrackedInBody <- true

        current <- Unchecked.defaultof<IComputation>
        currentOwner <- Unchecked.defaultof<Owner>
        untrackedHost <- host
        let journaled = not (Platform.isNull journal)

        if journaled then
            journal.EnterDerived ()

        try
            body ()
        finally
            current <- previous
            currentOwner <- previousOwner
            untrackedHost <- previousHost
            untrackedInBody <- previousInBody

            if journaled then
                journal.LeaveDerived ()

    /// <summary>
    /// Applies <c>f</c> to <c>arg</c> with tracking suppressed and the scope rule of <c>host</c> applied to every node
    /// created by <c>f</c>.
    /// </summary>
    member internal _.RunUntrackedHostedWith(host: IScopeHost, f: 'A -> unit, arg: 'A) =
        let previous = current
        let previousOwner = currentOwner
        let previousHost = untrackedHost
        let previousInBody = untrackedInBody

        if not (Platform.isNull (box previous)) then
            untrackedInBody <- true

        current <- Unchecked.defaultof<IComputation>
        currentOwner <- Unchecked.defaultof<Owner>
        untrackedHost <- host
        let journaled = not (Platform.isNull journal)

        if journaled then
            journal.EnterDerived ()

        try
            f arg
        finally
            current <- previous
            currentOwner <- previousOwner
            untrackedHost <- previousHost
            untrackedInBody <- previousInBody

            if journaled then
                journal.LeaveDerived ()

/// <summary>
/// A settable source.
/// </summary>
type Signal<'T> internal (graph: Graph, initial: 'T, equal: IEqualityComparer<'T>) =
    let id = graph.NextId ()
    let observers = ObserverSet ()
    do Tracer.Bind (observers, graph, id)
    let mutable value = initial

#if RANVIER_COUNTERS
    do Counters.SignalCreated ()
#endif

    do Tracer.SignalNew (graph, id, observers)

    // Resolved once, here, rather than per write: the policy's generic member
    // is the only place the value type is known, and a typed comparer keeps the
    // cutoff test allocation-free.
    /// <summary>
    /// A signal holding <c>initial</c>, compared by the comparer <c>graph</c>'s equality policy supplies for <c>'T</c>.
    /// </summary>
    new(graph: Graph, initial: 'T) = Signal<'T>(graph, initial, graph.Options.Equality.Comparer<'T>())

#if RANVIER_TRACE
    interface ITracedNode with
        member _.TraceGraph = box graph
#endif

    interface INode with
        member _.Id = id
        member _.Status = Status.None

    interface ISource with
        member _.AddObserver(c, slot) =
            observers.Add (c, slot)

        member _.TryAddObserver(c, slot) =
            observers.TryAdd (c, slot)

        member _.RemoveObserverAt(c, handle) =
            observers.RemoveAt (c, handle)

        /// <summary>
        /// Nothing to bring current: the value is whatever was last written.
        /// </summary>
        member _.UpdateIfNecessary() = ()

    /// <summary>
    /// Transparent, tracked read. A plain signal is never pending, so this
    /// never throws.
    /// </summary>
    member this.Value
        with get () =
            graph.Track (this :> ISource)
            value
        and set v =
            // The guard sits before the cutoff, not after: an off-thread write
            // that happens to be equal is still a bug, and one that would
            // otherwise only show up on the write that differs.
            graph.Entered (
                "A signal write",
                fun () ->
                    if
                        Platform.isNull graph.Journal
                        || this.RecordWrite v
                    then
                        if not (equal.Equals (value, v)) then
                            value <- v
                            Tracer.Write (observers, true, box v)

                            observers.NotifyDirty ()
                            Tracer.Notified observers

                            graph.RequestFlush ()
                        else
                            Tracer.Write (observers, false, box v)
            )

    /// <summary>
    /// Writes <c>v</c> as the setter does, leaving <c>running</c> unmarked. For a computation that reads the new value
    /// in its current run.
    /// </summary>
    member internal _.WriteExcept(v: 'T, running: IComputation) =
        graph.Entered (
            "A signal write",
            fun () ->
                if not (equal.Equals (value, v)) then
                    value <- v
                    Tracer.Write (observers, true, box v)
                    observers.NotifyDirtyExcept running
                    Tracer.Notified observers
                    graph.RequestFlush ()
                else
                    Tracer.Write (observers, false, box v)
        )

    // Out of line, so the setter's body stays at one field load and a null check when the graph has no journal.
    [<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
    member private _.RecordWrite(v: 'T) : bool =
        graph.RecordInput (JournalEntryKind.Write, id, box v)

    /// <summary>Marks every reader for a check, leaving the value unchanged.</summary>
    member internal _.NotifyCheck() =
        observers.NotifyCheck ()

    /// <summary>
    /// Non-throwing, tracked read.
    /// </summary>
    member this.TryValue: Reading<'T> =
        graph.Track (this :> ISource)
        Ready value

    /// <summary>
    /// Untracked read.
    /// </summary>
    member _.Peek = value

    member internal _.Graph = graph

    /// <summary>
    /// How many computations currently read this signal. Exposed for tests
    /// that need to assert an edge was dropped, or not duplicated.
    /// </summary>
    member internal _.ObserverCount = observers.Count

/// <summary>
/// A source whose value arrives later.
/// </summary>
/// <remarks>
/// This is the primitive <c>Memo.createAsync</c> is built on, and the smallest thing
/// that can actually be pending. Reading it before it settles suspends the
/// reader.
/// </remarks>
type AsyncSource<'T>(graph: Graph) =
    let id = graph.NextId ()
    let observers = ObserverSet ()
    do Tracer.Bind (observers, graph, id)
    let mutable value = Unchecked.defaultof<'T>
    let mutable failure: Failure = null

    let mutable status = Status.Pending ||| Status.Uninitialized

    do Tracer.AsyncSourceNew (graph, id, observers)

#if RANVIER_TRACE
    interface ITracedNode with
        member _.TraceGraph = box graph
#endif

    interface INode with
        member _.Id = id
        member _.Status = status

    interface ISource with
        member _.AddObserver(c, slot) =
            observers.Add (c, slot)

        member _.TryAddObserver(c, slot) =
            observers.TryAdd (c, slot)

        member _.RemoveObserverAt(c, handle) =
            observers.RemoveAt (c, handle)

        /// <summary>
        /// Nothing to bring current: the value is whatever was last written.
        /// </summary>
        member _.UpdateIfNecessary() = ()

    member _.Status = status

    /// <summary>
    /// Publishes a value and wakes every dependent.
    /// </summary>
    /// <remarks>
    /// Marshalled through <c>Dispatch</c>, because this is the one entry point that
    /// is genuinely called from off-thread: it is what a completing <c>Task</c> runs.
    /// </remarks>
    member _.Settle(v: 'T) =
        graph.Post (fun () ->
            if
                Platform.isNull graph.Journal
                || graph.RecordInput (JournalEntryKind.Settle, id, box v)
            then
                value <- v
                status <- Status.None
                Tracer.SourceSettled (observers, false, box v)

                observers.NotifyDirty ()
                Tracer.Notified observers

                graph.RequestFlush ())

    /// <summary>
    /// Publishes a failure as a settled outcome: dependents suspended on the source read <c>reason</c> as its error.
    /// </summary>
    /// <exception cref="T:System.ArgumentNullException"><c>reason</c> is null.</exception>
    member this.Fail(reason: exn) =
        if Platform.isNull reason then
            raise (ArgumentNullException (nameof reason))

        graph.Post (fun () ->
            if
                Platform.isNull graph.Journal
                || graph.RecordInput (JournalEntryKind.Fail, id, reason)
            then
                failure <- Failure (reason, (this :> INode))
                status <- Status.Error
                Tracer.SourceSettled (observers, true, reason)

                observers.NotifyDirty ()
                Tracer.Notified observers

                graph.RequestFlush ())

    /// <summary>
    /// Transparent, tracked read. Links the edge, then suspends if pending.
    /// </summary>
    member this.Value: 'T =
        graph.Track (this :> ISource)

        if status.HasFlag Status.Pending then
            raise (graph.NotReady (this :> INode))

        if status.HasFlag Status.Error then
            graph.Raise failure

        value

    /// <summary>
    /// Non-throwing, tracked read. Links the edge exactly as <c>Value</c> does, so a
    /// <c>Pending</c> result still wakes the reader when the source settles.
    /// </summary>
    member this.TryValue: Reading<'T> =
        graph.Track (this :> ISource)

        if status.HasFlag Status.Pending then Pending
        elif status.HasFlag Status.Error then Failed failure.Error
        else Ready value

    /// <summary>The source itself while <c>Status</c> has <c>Error</c>, otherwise null.</summary>
    member this.ErrorOrigin: INode =
        if status.HasFlag Status.Error then
            this :> INode
        else
            Unchecked.defaultof<INode>

    member internal _.Graph = graph

/// <summary>
/// A derived, cached computation.
/// </summary>
/// <remarks>
/// <para>
/// On invalidation the body is re-run from the top, never resumed. Resuming
/// — keeping the reads that happened before the suspension point and running
/// only the remainder — pairs a prefix read at T0 with a suffix read at T1 and
/// yields values corresponding to no state the graph was ever in. See
/// <c>DiamondTests</c>, which pins that failure.
/// </para>
/// <para>
/// A memo is pure or owning. A pure memo's body raises
/// <c>InvalidOperationException</c> when it creates an owned node: a memo, effect,
/// async value, boundary, root, projection, lookup or cleanup. An owning
/// memo's body owns what it creates: the nodes and cleanups are disposed
/// before each re-run and with the memo.
/// </para>
/// <para>
/// The body's argument is the value last published, <c>ValueNone</c> before the
/// first. After a run that suspends or fails, the next run receives the same
/// argument. While the memo holds a value, a body that returns the value inside its
/// argument keeps its dependents clean.
/// </para>
/// </remarks>
type Memo<'T> private (graph: Graph, compute: 'T voption -> 'T, mode: ScopeMode, ?comparer: IEqualityComparer<'T>) =
    let id = graph.NextId ()
    let observers = ObserverSet ()
    do Tracer.Bind (observers, graph, id)
    let sources = SourceList ()
    do Tracer.Bind (sources, graph, id)

    /// <summary>
    /// The source of the last run's last pending read.
    /// </summary>
    let mutable pendingSources: HashSet<INode> = null

    /// <summary>
    /// The typed cutoff comparer selected at construction.
    /// </summary>
    let equal =
        match comparer with
        | Some supplied -> supplied
        | None -> graph.Options.Equality.Comparer<'T>()

    let mutable freshness = Freshness.Dirty
    let mutable status = Status.Uninitialized
    let mutable value = Unchecked.defaultof<'T>
    let mutable failure: Failure = null
    let mutable runs = 0
    let mutable disposed = false

    /// <summary>
    /// Set by the first successful run. <c>value</c> is the argument to the next run while set.
    /// </summary>
    let mutable published = false

    let mutable link: OwnerLink = null

    /// <summary>The companion disposed with the memo, or null.</summary>
    let mutable adopted: IDisposable = null

    /// <summary>
    /// Owner of the nodes an owning body creates, allocated by the first of
    /// them. Discharged before each run and disposed with the computation.
    /// </summary>
    let mutable scope: Owner = Unchecked.defaultof<Owner>

    /// <summary>
    /// Set when a pure body creates an owned node, and cleared when the run
    /// fails for it.
    /// </summary>
    let mutable violated = false

#if RANVIER_COUNTERS
    do Counters.MemoCreated ()
#endif

    // Fable cannot translate a secondary constructor with a self-identifier.
    // Under Fable the `Api` functions construct this type through `Create`.
#if !FABLE_COMPILER
    /// <summary>
    /// A pure memo over <c>compute</c>.
    /// </summary>
    new(graph: Graph, compute: Func<'T voption, 'T>) as this =
        Memo<'T>(graph, compute.Invoke, ScopeMode.Pure)
        then this.Attach ()

    /// <summary>
    /// An owning memo over <c>compute</c> when <c>owning</c> is true, a pure one
    /// otherwise.
    /// </summary>
    new(graph: Graph, compute: Func<'T voption, 'T>, owning: bool) as this =
        Memo<'T>(graph, compute.Invoke, (if owning then ScopeMode.Owning else ScopeMode.Pure))
        then this.Attach ()

    /// <summary>
    /// A pure memo over <c>compute</c>, which receives the value last published, or <c>seed</c> before the first.
    /// </summary>
    new(graph: Graph, seed: 'T, compute: Func<'T, 'T>) as this =
        Memo<'T>(graph, (fun previous -> compute.Invoke (ValueOption.defaultValue seed previous)), ScopeMode.Pure)
        then this.Attach ()

    /// <summary>
    /// A memo over <c>compute</c>, which receives the value last published, or <c>seed</c> before the first. Owning
    /// when <c>owning</c> is true, pure otherwise.
    /// </summary>
    new(graph: Graph, seed: 'T, compute: Func<'T, 'T>, owning: bool) as this =
        Memo<'T>(
            graph,
            (fun previous -> compute.Invoke (ValueOption.defaultValue seed previous)),
            (if owning then ScopeMode.Owning else ScopeMode.Pure)
        )

        then this.Attach ()
#endif

    /// <summary>
    /// A memo over <c>compute</c>, owned by the current owner.
    /// </summary>
    static member internal Create(graph: Graph, compute: 'T voption -> 'T, mode: ScopeMode) =
        let memo = Memo<'T>(graph, compute, mode)
        memo.Attach ()
        memo

    /// <summary>A memo whose cutoff uses <c>comparer</c>, owned by the current owner.</summary>
    static member internal CreateWithComparer(graph: Graph, compute: 'T voption -> 'T, mode: ScopeMode, comparer: IEqualityComparer<'T>) =
        let memo = Memo<'T>(graph, compute, mode, comparer = comparer)
        memo.Attach ()
        memo

    /// <summary>
    /// Attaches the memo to the current owner. Disposing that owner disposes
    /// the memo.
    /// </summary>


    member private this.Attach() =
        link <- graph.CurrentOwner.AttachLinked this
        Tracer.MemoNew (graph, id, link.Owner)

    /// <summary>
    /// Dependencies are re-collected on every run, so the old edges have to go.
    /// Clearing the set alone drops this memo's own record of the edge while
    /// leaving the source's observer entry in place — the source then keeps
    /// waking a memo that no longer reads it, and keeps it alive for as long as
    /// the source lives. A conditional body is where that shows up: the branch
    /// not taken is still a subscription.
    /// </summary>
    member private this.DetachSources() =
        sources.Clear (this :> IComputation)

    /// <summary>
    /// Discharges the previous run's scope, then runs the body once, unless
    /// the memo was disposed meanwhile. A read from a cleanup serves the
    /// previous value. A cleanup that writes a source and then reads the memo
    /// runs the body during the discharge, and that run replaces this one.
    /// A write after that read discharges and runs again.
    /// </summary>
    member private this.Recompute() =
        if Platform.isNull (box scope) then
            this.Run ()
        else
            this.RecomputeScoped ()

    member private this.RecomputeScoped() =
        let before = runs
        freshness <- Freshness.Clean
        graph.Discharge scope

        if not disposed then
            if runs = before then
                this.Run ()
            elif freshness = Freshness.Dirty then
                this.Recompute ()

    member private this.Run() =
        // Marked clean *before* the body runs, not after. A body that
        // invalidates one of its own sources — directly, or by reading a node
        // that settles inline — marks this memo dirty while it is still
        // running, and clearing the flag afterwards would discard that and
        // serve a value the graph has already moved past. It also turns a memo
        // that reads itself from a stack overflow into a default value.
        freshness <- Freshness.Clean
        sources.BeginRun ()

        if
            not (Platform.isNull pendingSources)
            && pendingSources.Count > 0
        then
            pendingSources.Clear ()

        // Captured before the body overwrites them: whether the observers this
        // memo told `Check` should now be told `Dirty` is exactly the question
        // of whether any of these moved.
        let previous = value
        let previousStatus = status
        let previousFailure = failure

        status <- Status.None
        failure <- null
        runs <- runs + 1
#if RANVIER_COUNTERS
        Counters.MemoRecomputed ()
#endif
        Tracer.RunStart (graph, id, runs)

        try
            try
                let prev = if published then ValueSome previous else ValueNone
                let result = graph.RunHosted (this :> IComputation, compute, prev)

                if violated then
                    raise (InvalidOperationException (ScopeMessages.forMode mode))

                value <- result
                published <- true
            finally
                if disposed then
                    sources.Clear (this :> IComputation)
                else
                    sources.EndRun (this :> IComputation)
        with
        | ex when violated ->
            violated <- false
            failure <- graph.FailureOf (ScopeMessages.failure mode ex, this, previousFailure)
            status <- Status.Error
        | NotReadyException source ->
            if Platform.isNull pendingSources then
                pendingSources <- HashSet<INode>(HashIdentity.Reference)

            pendingSources.Add source |> ignore

            status <- Status.Pending
        | ex ->
            failure <- graph.FailureOf (ex, this, previousFailure)
            status <- Status.Error

        // The cutoff, and the whole point of `Check`. Status counts as part of
        // the published value: a memo that goes from a value to pending has
        // changed what its dependents see even when `value` is untouched, and
        // so has a memo that fails with a different exception. A throwing
        // comparer fails the run as a throwing body does.
        let mutable moved =
            status <> previousStatus
            || Failure.Moved (failure, previousFailure)

        if not moved then
            try
                moved <- not (equal.Equals (previous, value))
            with ex ->
                value <- previous
                failure <- graph.FailureOf (ex, this, previousFailure)
                status <- Status.Error
                moved <- true

        if moved then
            Tracer.Moved (graph, id, Failure.Payload (failure, box value))
            observers.NotifyDirtyExcept graph.CurrentComputation
            Tracer.Notified graph
            Tracer.RunEnd (graph, id, status)
        else
            Tracer.RunEnd (graph, id, status)

    /// <summary>
    /// Resolves <c>Check</c> into <c>Clean</c> or <c>Dirty</c> by asking each source, in read
    /// order, whether it actually moved.
    /// </summary>
    /// <remarks>
    /// The walk breaks as soon as one of them says yes: a source that
    /// recomputes to a new value marks this memo <c>Dirty</c> from inside
    /// <c>UpdateIfNecessary</c>, the loop condition fails, and there is nothing to
    /// learn from the remaining sources — the body is going to run regardless.
    /// Reaching the end still in <c>Check</c> means nothing above moved, and this
    /// memo is clean without having run.
    /// </remarks>
    member private this.ResolveCheck() =
        Tracer.CheckStart (graph, id)
        let mutable i = 0

        while freshness = Freshness.Check && i < sources.Count do
            sources.SourceAt(i).UpdateIfNecessary()
            i <- i + 1

        Tracer.CheckResolved (graph, id, (freshness = Freshness.Dirty))

        if freshness = Freshness.Check then
            freshness <- Freshness.Clean

    member private this.EnsureCurrent() =
        // One comparison on the path a cached read takes. Testing `disposed`
        // or `Check` first costs every clean read an extra comparison, which
        // measured at ~1 ns against a ~1.3 ns baseline — the read is small
        // enough that one avoidable test is most of it.
        if freshness <> Freshness.Clean && not disposed then
            if graph.Deferring then this.Refresh () else this.Pull ()

    /// <summary>
    /// Brings a stale memo current when <c>Graph.Deferring</c> is false: outside
    /// every stale read and body, or inside <c>untrack</c> within a body.
    /// </summary>

    member private this.Pull() =
        graph.EnterPull ()

        try
            this.Refresh ()
        finally
            graph.ExitPull ()

    /// <summary>
    /// Brings a stale memo current.
    /// </summary>
    member private this.Refresh() =
        if freshness = Freshness.Check then
            this.ResolveCheck ()

        if freshness = Freshness.Dirty then
            this.Recompute ()

    /// <summary>
    /// Detaches from every source. Idempotent, and usually called for you by
    /// the enclosing owner.
    /// </summary>
    /// <remarks>
    /// A disposed memo still answers reads, with the last value it computed.
    /// It stops tracking and stops being woken, which is the whole point: a
    /// memo nothing reads is otherwise kept alive by the sources it reads, and
    /// goes on being marked dirty for the lifetime of the graph.
    /// </remarks>
    member this.Dispose() =
        graph.Entered (
            "Disposing a node",
            fun () ->
                if not disposed then
                    disposed <- true
                    Tracer.NodeDispose (graph, id)

                    if not (Platform.isNull link) then
                        link.Detach ()
                        link <- null

                    this.DetachSources ()

                    if not (Platform.isNull (box scope)) then
                        graph.Retire scope

                    if not (Platform.isNull adopted) then
                        let companion = adopted
                        adopted <- null
                        companion.Dispose ()
        )

    /// <summary>
    /// Marks every reader dirty. A reader of a row whose key was removed
    /// re-runs and reads the key as absent.
    /// </summary>
    member internal _.NotifyRemoved() =
        observers.NotifyDirty ()

    /// <summary>
    /// Whether the run in progress has created an owned node in a pure body. A violated run fails and keeps the previous
    /// value.
    /// </summary>
    member internal _.Violated = violated

    /// <summary>
    /// Disposes <c>companion</c> when the memo is disposed, or at once when it already is. A replaced companion is
    /// disposed at once.
    /// </summary>
    member internal _.Adopt(companion: IDisposable) =
        if disposed then
            companion.Dispose ()
        else
            let replaced = adopted
            adopted <- companion

            if
                not (Platform.isNull replaced)
                && not (obj.ReferenceEquals (replaced, companion))
            then
                replaced.Dispose ()

    interface IOwned with
        member this.Release() =
            link <- null
            this.Dispose ()

    interface IScopeHost with
        member _.Scope =
            if Platform.isNull (box scope) then
                if mode = ScopeMode.Owning then
                    scope <- new Owner (graph.Root)
                    Tracer.ScopeNew (scope, graph, id)

                    if disposed then
                        scope.Dispose ()
                else
                    violated <- true
                    raise (InvalidOperationException (ScopeMessages.forMode mode))

            scope

#if RANVIER_TRACE
    interface ITracedNode with
        member _.TraceGraph = box graph
#endif

    interface INode with
        member _.Id = id
        member _.Status = status ||| Staleness.ofFreshness freshness

    interface ISource with
        member _.AddObserver(c, slot) =
            observers.Add (c, slot)

        member _.TryAddObserver(c, slot) =
            observers.TryAdd (c, slot)

        member _.RemoveObserverAt(c, handle) =
            observers.RemoveAt (c, handle)

        member this.UpdateIfNecessary() =
            this.EnsureCurrent ()

    interface IComputation with
        member this.AddSource s =
            sources.Add (this :> IComputation, s)

        member _.MovedIn(slot, handle) =
            sources.MovedIn (slot, handle)

        member _.MarkDirty() =
            if freshness <> Freshness.Dirty then
                let wasClean = freshness = Freshness.Clean
                freshness <- Freshness.Dirty

                // `Check`, not `Dirty`: this memo knows its own body has to
                // re-run, but not yet whether re-running it will produce a
                // different value. Telling dependents `Dirty` here is what made
                // a cutoff stop at the first derivation. They are upgraded at
                // the end of `Recompute`, if the value actually moved.
                //
                // Only on the Clean transition — an already-`Check` memo has
                // told them this once, and telling them again is a no-op that
                // walks the whole fan-out to discover as much.
                if wasClean then
                    observers.NotifyCheck ()

        member _.MarkCheck() =
            if freshness = Freshness.Clean then
                freshness <- Freshness.Check

                observers.NotifyCheck ()

    member _.Status = status

    /// <summary>
    /// The source of the last run's last pending read, or empty when the run did not suspend.
    /// </summary>
    /// <remarks>
    /// A body that catches several pending reads lists only the last; a body that does not catch one stops at it.
    /// </remarks>
    member _.PendingSources =
        if Platform.isNull pendingSources then
            Seq.empty
        else
            pendingSources :> seq<INode>

    /// <summary>
    /// How many times the body has executed. Exposed for tests that need to
    /// distinguish a genuine cache hit from a silent re-run.
    /// </summary>
    member _.Runs = runs

    /// <summary>
    /// Transparent, tracked read. Suspends the caller if this memo is itself
    /// suspended, which is how the pending channel propagates.
    /// </summary>
    member this.Value: 'T =
        this.EnsureCurrent ()
        graph.Track (this :> ISource)

        if status.HasFlag Status.Pending then
            raise (graph.NotReady (this :> INode))

        if status.HasFlag Status.Error then
            graph.Raise failure

        value

    /// <summary>
    /// Non-throwing, tracked read.
    /// </summary>
    member this.TryValue: Reading<'T> =
        this.EnsureCurrent ()
        graph.Track (this :> ISource)

        if status.HasFlag Status.Pending then Pending
        elif status.HasFlag Status.Error then Failed failure.Error
        else Ready value

    /// <summary>
    /// Non-throwing read that brings the memo up to date without recording a dependency.
    /// </summary>
    member internal this.ReadUntracked() : Reading<'T> =
        this.EnsureCurrent ()

        if status.HasFlag Status.Pending then Pending
        elif status.HasFlag Status.Error then Failed failure.Error
        else Ready value

    /// <summary>
    /// The node the current failure originated in while <c>Status</c> has <c>Error</c>, otherwise null. Untracked.
    /// </summary>
    /// <remarks>
    /// The memo itself when its body, its comparer or its purity check raised the exception; the upstream node when the
    /// body rethrew the exception of a failed read.
    /// </remarks>
    member _.ErrorOrigin: INode = Failure.OriginOf failure

    /// <summary>The current failure, or null.</summary>
    member internal _.Failure = failure

    /// <summary>
    /// Untracked read of the cached value, without recomputing.
    /// </summary>
    member _.Peek = value

    /// <summary>
    /// How many computations currently read this memo. Exposed for tests that
    /// need to assert an edge was dropped, or not duplicated.
    /// </summary>
    member internal _.ObserverCount = observers.Count

    /// <summary>
    /// How many sources this memo read on its last run. Exposed for the same
    /// reason: a dependency list that grows by one entry per run changes
    /// nothing the graph computes, so only a count can catch it.
    /// </summary>
    member internal _.SourceCount = sources.Count

/// <summary>
/// A side effect that re-runs when its dependencies change.
/// </summary>
/// <remarks>
/// <para>
/// The push half of the graph: memos are pulled by whoever reads them, but
/// nothing reads an effect, so the scheduler runs it. Invalidation only queues
/// it — the body runs on the next flush, which is the end of the current write
/// or batch.
/// </para>
/// <para>
/// A suspended effect does not run its side effect. That is the point of the
/// pending channel here: a body that would have fired a request, written to a
/// DOM node, or logged a line is aborted before it does so, and re-runs from
/// the top once the source it waited on settles.
/// </para>
/// </remarks>
// Fable cannot translate a secondary constructor with a self-identifier, so
// under Fable the primary constructor starts the effect.
#if FABLE_COMPILER
type Effect(graph: Graph, body: unit -> unit) as this =
#else
type Effect private (graph: Graph, body: unit -> unit, _unstarted: unit) =
#endif
    let id = graph.NextId ()
    let sources = SourceList ()
    do Tracer.Bind (sources, graph, id)
    let mutable pendingSources: HashSet<INode> = null

    /// <summary>
    /// This run's scope. Cleanups registered by the body land here and are
    /// discharged before the next run, so a subscription opened on one run is
    /// closed before the run that replaces it opens another. Allocated by the
    /// first creation or cleanup in the body.
    /// </summary>
    let mutable scope: Owner = Unchecked.defaultof<Owner>

    /// <summary>
    /// An effect is scheduled, not read, so it carries freshness separately
    /// from <c>queued</c>: being on the queue says it has something to consider,
    /// and this says whether that something is a certainty or a maybe.
    /// </summary>
    let mutable freshness = Freshness.Clean

    let mutable status = Status.Uninitialized
    let mutable failure: Failure = null
    let mutable queued = false
    let mutable disposed = false
    let mutable link: OwnerLink = null
    let mutable runs = 0

    /// <summary>
    /// Set while this run's cleanups are being discharged, and read by
    /// <c>MarkDirty</c>/<c>MarkCheck</c> to refuse to re-queue this effect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Run</c> does not detach from its sources before running cleanups — the
    /// positional cursor in <c>SourceList</c> keeps the old edges and trims them at
    /// <c>EndRun</c>, which is what makes a re-run that reads the same sources free.
    /// The cost is that a cleanup writing a source this effect reads finds
    /// itself still an observer, and so wakes the very effect whose teardown is
    /// running it. That run registers an equivalent cleanup, and nothing bounds
    /// the loop.
    /// </para>
    /// <para>
    /// Suppressing the wake is not lossy: the body is about to run, and it
    /// reads the written value. Deliberately narrower than the whole of <c>Run</c>,
    /// because a write from the body to a source the body reads must still
    /// re-queue — that is the fixpoint an effect is allowed to climb.
    /// </para>
    /// </remarks>
    let mutable inCleanup = false

    let mutable publishes = false

#if RANVIER_COUNTERS
    do Counters.EffectCreated ()
#endif

#if FABLE_COMPILER
    do this.Start ()
#else
    /// <summary>
    /// An effect running <c>body</c>, owned by the current owner and queued for
    /// its first run.
    /// </summary>
    new(graph: Graph, body: Action) as this =
        new Effect (graph, body.Invoke, ())
        then this.Start ()
#endif

    /// <summary>
    /// An effect running <c>body</c>, owned by the current owner and queued for
    /// its first run.
    /// </summary>
    static member internal Create(graph: Graph, body: unit -> unit) =
#if FABLE_COMPILER
        new Effect (graph, body)
#else
        let effect = new Effect (graph, body, ())
        effect.Start ()
        effect
#endif

    member private this.Start() =
        // Attaching to the enclosing scope is what makes disposal structural:
        // the caller tears down one owner, not a list of effects it had to
        // remember to collect.
        link <- graph.CurrentOwner.AttachLinked this
        Tracer.EffectNew (graph, id, link.Owner)
        (this :> IComputation).MarkDirty()
        graph.RequestFlush ()

    /// <summary>
    /// Dependencies are re-collected on every run, so the old edges have to go
    /// or a source dropped by a conditional body would keep waking it.
    /// </summary>
    member private this.DetachSources() =
        sources.Clear (this :> IComputation)

    interface IOwned with
        member this.Release() =
            link <- null
            this.Dispose ()

#if RANVIER_TRACE
    interface ITracedNode with
        member _.TraceGraph = box graph
#endif

    interface INode with
        member _.Id = id
        member _.Status = status ||| Staleness.ofFreshness freshness

    interface IComputation with
        member this.AddSource s =
            sources.Add (this :> IComputation, s)

        member _.MovedIn(slot, handle) =
            sources.MovedIn (slot, handle)

        member this.MarkDirty() =
            if not disposed && not inCleanup then
                // Upgrades even when already queued: an effect that was
                // scheduled on a `Check` and has since been told `Dirty` must
                // carry the stronger fact to `Execute`.
                freshness <- Freshness.Dirty

                if not queued then
                    queued <- true

                    if publishes then
                        graph.SchedulePass (this :> IScheduled)
                    else
                        graph.Schedule (this :> IScheduled)

        member this.MarkCheck() =
            if not disposed && not inCleanup then
                if freshness = Freshness.Clean then
                    freshness <- Freshness.Check

                if not queued then
                    queued <- true

                    if publishes then
                        graph.SchedulePass (this :> IScheduled)
                    else
                        graph.Schedule (this :> IScheduled)

    /// <summary>
    /// Set on an effect whose readers are marked only when its body writes, such as a lookup's refresh. Every run
    /// queued after the flag is set runs with the projection passes, ahead of every other queued effect. The first
    /// run, queued at creation, is on the main queue.
    /// </summary>
    member internal _.Publishes
        with set value = publishes <- value

    interface IScheduled with
        member this.Execute() =
            // `queued` stays set across the resolution walk. Resolving a
            // `Check` can recompute a source, which marks this effect dirty
            // from inside the walk; with `queued` already cleared that would
            // put it back on the queue and run it twice for one write. It is
            // cleared below, once the walk has had its say.
            if disposed then
                queued <- false
            else
                if freshness = Freshness.Check then
                    Tracer.CheckStart (graph, id)
                    let mutable i = 0

                    while freshness = Freshness.Check && i < sources.Count do
                        sources.SourceAt(i).UpdateIfNecessary()
                        i <- i + 1

                    Tracer.CheckResolved (graph, id, (freshness = Freshness.Dirty))

                    if freshness = Freshness.Check then
                        freshness <- Freshness.Clean

                let shouldRun = freshness = Freshness.Dirty
                queued <- false

                // Clean before the body, for the reason given on
                // `Memo.Recompute`: an invalidation raised by the body itself
                // has to survive the run that raised it.
                freshness <- Freshness.Clean

                if shouldRun then
                    this.Run ()

    member private this.Run() =
        sources.BeginRun ()

        // Discharge the previous run's cleanups before the next run
        // registers its own. Guarded so a cleanup writing one of this effect's
        // own sources does not re-queue the run it is making way for; see
        // `inCleanup`.
        if not (Platform.isNull (box scope)) then
            inCleanup <- true

            try
                graph.Discharge scope
            finally
                inCleanup <- false

        // A cleanup that disposes the effect, directly or through its owner,
        // ends the run here.
        if not disposed then
            this.RunBody ()

    member private this.RunBody() =
        if
            not (Platform.isNull pendingSources)
            && pendingSources.Count > 0
        then
            pendingSources.Clear ()

        status <- Status.None
        failure <- null
        runs <- runs + 1
#if RANVIER_COUNTERS
        Counters.EffectRan ()
#endif
        Tracer.RunStart (graph, id, runs)

        try
            try
                let recorder = graph.Journal

                if Platform.isNull recorder then
                    graph.RunHosted (this :> IComputation, body)
                else
                    recorder.RunWork (fun () -> graph.RunHosted (this :> IComputation, body))
            finally
                if disposed then
                    sources.Clear (this :> IComputation)
                else
                    sources.EndRun (this :> IComputation)
        with
        | NotReadyException source ->
            if Platform.isNull pendingSources then
                pendingSources <- HashSet<INode>(HashIdentity.Reference)

            pendingSources.Add source |> ignore

            status <- Status.Pending
        | ex ->
            // Never rethrow into the flush loop: one failing effect must
            // not strand every effect queued behind it. Solid routes
            // this to the nearest error boundary; we have none yet, so
            // it is recorded and readable.
            failure <- graph.FailureOf (ex, this, null)
            status <- Status.Error

        Tracer.RunEnd (graph, id, status)

    /// <summary>
    /// Detaches from every source and discharges the run scope. Idempotent.
    /// </summary>
    /// <remarks>
    /// Usually called for you: the effect attached itself to the enclosing
    /// owner at construction, so disposing that owner disposes this.
    /// </remarks>
    member this.Dispose() =
        graph.Entered (
            "Disposing a node",
            fun () ->
                if not disposed then
                    disposed <- true
                    Tracer.NodeDispose (graph, id)

                    if not (Platform.isNull link) then
                        link.Detach ()
                        link <- null

                    this.DetachSources ()

                    if not (Platform.isNull (box scope)) then
                        graph.Retire scope
        )

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

    interface IScopeHost with
        member _.Scope =
            if Platform.isNull (box scope) then
                scope <- new Owner (graph.Root)
                Tracer.ScopeNew (scope, graph, id)

                if disposed then
                    scope.Dispose ()

            scope

    member _.Status = status

    /// <summary>
    /// The error from the last run, or null. Effects do not throw out of the
    /// flush loop, so this is the only way to see one.
    /// </summary>
    member _.Error = Failure.ErrorOf failure

    /// <summary>The node the last run's failure originated in, or null when the run did not fail.</summary>
    /// <remarks>
    /// The effect itself when its body raised the exception; the upstream node when the body rethrew the exception of a
    /// failed read.
    /// </remarks>
    member _.ErrorOrigin: INode = Failure.OriginOf failure

    /// <summary>
    /// The source of the last run's last pending read, or empty when the run did not suspend.
    /// </summary>
    /// <remarks>
    /// A body that catches several pending reads lists only the last; a body that does not catch one stops at it.
    /// </remarks>
    member _.PendingSources =
        if Platform.isNull pendingSources then
            Seq.empty
        else
            pendingSources :> seq<INode>

    /// <summary>
    /// How many times the body has executed.
    /// </summary>
    member _.Runs = runs

/// <summary>
/// A side effect split into a tracked, pure <c>compute</c> and an untracked <c>act</c> that runs with each settled value
/// unequal to the last value acted on. This library's <c>createEffectOn</c>.
/// </summary>
/// <remarks>
/// A pending read or a failure in <c>compute</c> keeps the previous action and its cleanups. Nodes and cleanups
/// created by <c>act</c> belong to a reused scope, discharged before the next <c>act</c> and disposed with the effect.
/// </remarks>
[<Sealed>]
type internal EffectOn<'T> private (graph: Graph, compute: unit -> 'T, act: 'T -> unit, ?comparer: IEqualityComparer<'T>) =
    let id = graph.NextId ()
    let sources = SourceList ()
    do Tracer.Bind (sources, graph, id)

    let equal =
        match comparer with
        | Some supplied -> supplied
        | None -> graph.Options.Equality.Comparer<'T>()

    /// <summary>
    /// The value <c>act</c> last ran with: the cutoff baseline once <c>hasActed</c>.
    /// </summary>
    let mutable last = Unchecked.defaultof<'T>
    let mutable hasActed = false

    /// <summary>
    /// The scope of <c>act</c>'s nodes and cleanups, allocated by the first node or cleanup created in <c>act</c>.
    /// </summary>
    let mutable actionOwner: Owner = Unchecked.defaultof<Owner>

    /// <summary>
    /// True while <c>act</c> runs; selects the owning rule of <c>IScopeHost.Scope</c> over the pure one.
    /// </summary>
    let mutable acting = false
    let mutable violated = false
    let mutable freshness = Freshness.Clean
    let mutable status = Status.Uninitialized
    let mutable error: exn = null
    let mutable queued = false
    let mutable disposed = false
    let mutable link: OwnerLink = null

#if RANVIER_COUNTERS
    do Counters.EffectCreated ()
#endif

    /// <summary>An effect owned by the current owner, with its first run queued.</summary>
    static member internal Create(graph: Graph, compute: unit -> 'T, act: 'T -> unit) =
        let node = new EffectOn<'T> (graph, compute, act)
        node.Start ()
        node

    /// <summary>An owned split effect whose cutoff uses <c>comparer</c>, with its first run queued.</summary>
    static member internal CreateWithComparer(graph: Graph, compute: unit -> 'T, act: 'T -> unit, comparer: IEqualityComparer<'T>) =
        let node = new EffectOn<'T> (graph, compute, act, comparer = comparer)
        node.Start ()
        node

    member private this.Start() =
        link <- graph.CurrentOwner.AttachLinked this
        Tracer.EffectNew (graph, id, link.Owner)
        (this :> IComputation).MarkDirty()
        graph.RequestFlush ()

    /// <summary>Runs <c>compute</c>, then <c>act</c> on the first settled value and on each one unequal to <c>last</c>.</summary>
    member private this.Run() =
        sources.BeginRun ()
        status <- Status.None
        error <- null
#if RANVIER_COUNTERS
        Counters.EffectRan ()
#endif
        Tracer.RunStart (graph, id, 0)
        let mutable v = Unchecked.defaultof<'T>
        let mutable settled = false

        try
            try
                v <- graph.RunHosted (this :> IComputation, compute)

                if violated then
                    raise (InvalidOperationException ScopeMessages.effectOn)

                settled <- true
            finally
                if disposed then
                    sources.Clear (this :> IComputation)
                else
                    sources.EndRun (this :> IComputation)
        with
        | ex when violated ->
            violated <- false
            error <- ScopeMessages.failure ScopeMode.EffectOn ex
            status <- Status.Error
        | NotReadyException _ -> status <- Status.Pending
        | ex ->
            error <- ex
            status <- Status.Error

        // A throwing comparer fails the run as a throwing `compute` does.
        let mutable unchanged = false

        if settled && hasActed then
            try
                unchanged <- equal.Equals (last, v)
            with ex ->
                settled <- false
                error <- ex
                status <- Status.Error

        // RunEnd in both branches keeps the untraced IL equal to the unhooked method (tools/verify-trace.fsx, gate 1).
        if settled && not disposed && not unchanged then
            this.Act v
            Tracer.RunEnd (graph, id, status)
        else
            Tracer.RunEnd (graph, id, status)

    /// <summary>
    /// Discharges the previous action's scope, then runs <c>act</c> with <c>v</c>. An exception from <c>act</c> is recorded
    /// and leaves <c>v</c> as the cutoff baseline.
    /// </summary>
    member private this.Act(v: 'T) =
        last <- v
        hasActed <- true

        if not (Platform.isNull (box actionOwner)) then
            graph.Discharge actionOwner

        if not disposed then
            acting <- true

            try
                try
                    let recorder = graph.Journal

                    if Platform.isNull recorder then
                        graph.RunUntrackedHostedWith (this :> IScopeHost, act, v)
                    else
                        recorder.RunWork (fun () -> graph.RunUntrackedHostedWith (this :> IScopeHost, act, v))
                finally
                    acting <- false
            with
            | NotReadyException _ -> status <- Status.Pending
            | ex ->
                error <- ex
                status <- Status.Error

    /// <summary>Detaches from the sources and disposes the action's scope. Idempotent.</summary>
    member this.Dispose() =
        graph.Entered (
            "Disposing a node",
            fun () ->
                if not disposed then
                    disposed <- true
                    Tracer.NodeDispose (graph, id)

                    if not (Platform.isNull link) then
                        link.Detach ()
                        link <- null

                    sources.Clear (this :> IComputation)

                    if not (Platform.isNull (box actionOwner)) then
                        graph.Retire actionOwner
        )

    /// <summary>The error of the last run, from <c>compute</c> or <c>act</c>; null when it did not fail.</summary>
    member _.Error = error

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

    interface IOwned with
        member this.Release() =
            link <- null
            this.Dispose ()

#if RANVIER_TRACE
    interface ITracedNode with
        member _.TraceGraph = box graph
#endif

    interface INode with
        member _.Id = id
        member _.Status = status ||| Staleness.ofFreshness freshness

    interface IComputation with
        member this.AddSource source =
            sources.Add (this :> IComputation, source)

        member _.MovedIn(slot, handle) =
            sources.MovedIn (slot, handle)

        member this.MarkDirty() =
            if not disposed then
                freshness <- Freshness.Dirty

                if not queued then
                    queued <- true
                    graph.Schedule (this :> IScheduled)

        member this.MarkCheck() =
            if not disposed then
                if freshness = Freshness.Clean then
                    freshness <- Freshness.Check

                if not queued then
                    queued <- true
                    graph.Schedule (this :> IScheduled)

    interface IScheduled with
        member this.Execute() =
            if disposed then
                queued <- false
            else
                if freshness = Freshness.Check then
                    Tracer.CheckStart (graph, id)
                    let mutable i = 0

                    while freshness = Freshness.Check && i < sources.Count do
                        sources.SourceAt(i).UpdateIfNecessary()
                        i <- i + 1

                    Tracer.CheckResolved (graph, id, (freshness = Freshness.Dirty))

                    if freshness = Freshness.Check then
                        freshness <- Freshness.Clean

                let shouldRun = freshness = Freshness.Dirty
                queued <- false
                freshness <- Freshness.Clean

                if shouldRun then
                    this.Run ()

    interface IScopeHost with
        member _.Scope =
            if acting then
                if Platform.isNull (box actionOwner) then
                    actionOwner <- new Owner (graph.Root)
                    Tracer.ScopeNew (actionOwner, graph, id)

                    if disposed then
                        actionOwner.Dispose ()

                actionOwner
            else
                violated <- true
                raise (InvalidOperationException ScopeMessages.effectOn)

/// <summary>The async memo a <c>Previous</c> reads.</summary>
type internal IPreviousSource<'T> =
    /// <summary>
    /// The value last published, once <c>position</c> chained results are applied.
    /// </summary>
    abstract Settled: position: int -> Task<'T voption>

/// <summary>The completed <c>ValueNone</c> task shared by every node of one value type.</summary>
[<AbstractClass; Sealed>]
type internal NothingPublished<'T> private () =
    static member val Task: Task<'T voption> = Platform.completedWith ValueNone

/// <summary>
/// The value an async memo published before a flight, handed to the flight's body.
/// </summary>
/// <remarks>
/// <para>
/// Under every policy except <c>Queue</c>, <c>Settled</c> is complete when the body runs. Under <c>Queue</c>, a flight
/// that starts while an earlier flight's result is unapplied receives the value as it stands once that result is
/// applied, so a chain of flights folds in start order. A faulted or dropped predecessor leaves the last settled value,
/// and disposing the memo completes <c>Settled</c> with the value last published.
/// </para>
/// <para>
/// Tracking stops at the body's first await that suspends. Read every input, then await <c>Settled</c>: under
/// <c>Queue</c>, a read made after awaiting it is not tracked.
/// </para>
/// </remarks>
[<Struct; NoComparison; NoEquality>]
type Previous<'T> internal (source: IPreviousSource<'T>, position: int) =
    /// <summary>
    /// Completes with the value last published, <c>ValueNone</c> before the first. Under <c>Queue</c> it completes after
    /// the preceding flight's result is applied. Its awaiters resume outside the graph's apply.
    /// </summary>
    member _.Settled: Task<'T voption> =
        if Platform.isNull (box source) then
            NothingPublished<'T>.Task
        else
            source.Settled position

/// <summary>
/// A derived computation whose value arrives later: the body runs synchronously and returns a <c>Task</c>. This library's
/// <c>createAsync</c>.
/// </summary>
/// <remarks>
/// Reads are tracked up to the body's first await that suspends; a read after it is untracked. On .NET a continuation run
/// inline inside another computation's body is detached from that body. Under Fable every await suspends and an outcome
/// lands on a later microtask. Pure or owning, as a <c>Memo</c> is: an owning scope is discharged before each flight starts.
/// The body receives the value last published as a <c>Previous</c>. Flights follow the <c>FlightPolicy</c> given at
/// creation, or the graph's <c>GraphOptions.FlightPolicy</c> when none was given.
/// </remarks>
type AsyncMemo<'T> private (graph: Graph, compute: Previous<'T> -> CancellationToken -> Task<'T>, mode: ScopeMode, policy: FlightPolicy) =
    let id = graph.NextId ()
    let observers = ObserverSet ()
    do Tracer.Bind (observers, graph, id)
    let sources = SourceList ()
    do Tracer.Bind (sources, graph, id)
    let mutable pendingSources: HashSet<INode> = null

    let mutable freshness = Freshness.Dirty
    let mutable status = Status.Pending ||| Status.Uninitialized
    let mutable value = Unchecked.defaultof<'T>
    let mutable failure: Failure = null
    let mutable runs = 0
    let mutable disposed = false
    let mutable link: OwnerLink = null

    /// <summary>
    /// Owner of the nodes an owning body creates, allocated by the first of
    /// them. Discharged before each flight starts and disposed with the
    /// computation.
    /// </summary>
    let mutable scope: Owner = Unchecked.defaultof<Owner>

    /// <summary>
    /// Set when a pure body creates an owned node, and cleared when the run
    /// fails for it.
    /// </summary>
    let mutable violated = false

    /// <summary>
    /// Which flight is current. A settling task publishes only if it is still
    /// the one the graph is waiting for; anything older carries a value that
    /// belongs to a dependency state the graph has already left, which is the
    /// same stale-pairing the diamond test pins.
    /// </summary>
    let mutable generation = 0

    /// <summary>
    /// The source of the flight token. Under <c>CancelPrevious</c> each flight gets its own. Under the other policies
    /// the flights in progress share one, disposed once every flight holding it has settled.
    /// </summary>
    let mutable cts: CancellationTokenSource = null

    /// <summary>The number of flights started and not yet settled, under <c>KeepLatest</c>.</summary>
    let mutable flying = 0

    /// <summary>The number of bodies executing. The shared source is retained while it is nonzero.</summary>
    let mutable running = 0

    /// <summary>
    /// The end of the serialised chain, under <c>Queue</c>.
    /// </summary>
    let mutable tail: Task = Platform.completedTask

    /// <summary>
    /// Under <c>Queue</c>, the number of results chained onto <c>tail</c> and not yet applied. Under
    /// <c>FinishCurrent</c>, <c>Drop</c> and <c>DropWhilePending</c>, 0 with no flight in progress, 1 with a flight in
    /// progress, 2 with a flight in progress that has deferred or discarded a change. Zero under the other policies.
    /// </summary>
    let mutable queued = 0

    /// <summary>The number of results ever chained onto <c>tail</c>, under <c>Queue</c>.</summary>
    let mutable chained = 0

    /// <summary>The number of chained results applied, under <c>Queue</c>.</summary>
    let mutable applied = 0

    /// <summary>True once a result has written <c>value</c>.</summary>
    let mutable published = false

    /// <summary>The completed task of the value last published, created on first read.</summary>
    let mutable settledTask: Task<'T voption> = null

    /// <summary>
    /// The <c>Previous.Settled</c> tasks still waiting, keyed by the number of applied results that completes each.
    /// </summary>
    let mutable waiters: Dictionary<int, Platform.Deferred<'T voption>> = null

    /// <summary>
    /// True while <c>Launch</c> attaches the settle continuation. A flight already complete
    /// publishes inline, during the read that launched it.
    /// </summary>
    let mutable launching = false

    let wake () =
        Tracer.Moved (graph, id, Failure.Payload (failure, box value))

        if launching then
            observers.NotifyDirtyExcept graph.CurrentComputation
        else
            observers.NotifyDirty ()

        Tracer.Notified graph
        graph.RequestFlush ()

    let lastSettled () =
        if published then ValueSome value else ValueNone

    // Under the lock `Settled` takes, so a read off the graph thread never caches a value this write replaces.
    let write (v: 'T) =
        lock observers (fun () ->
            value <- v
            published <- true
            settledTask <- null)

    /// <summary>
    /// The value last published, as a completed task shared by every read until the next publish. Called under the
    /// <c>observers</c> lock.
    /// </summary>
    let settledNow () =
        if Platform.isNull settledTask then
            settledTask <-
                if published then
                    Platform.completedWith (ValueSome value)
                else
                    NothingPublished<'T>.Task

        settledTask

    /// <summary>
    /// Counts one more chained result applied and completes the waiter keyed on the new count. Called under the
    /// <c>observers</c> lock.
    /// </summary>
    let releaseNext () =
        applied <- applied + 1

        if not (Platform.isNull waiters) then
            let waiter = Platform.findOrDefault waiters applied

            if not (Platform.isNull (box waiter)) then
                waiters.Remove applied |> ignore
                Platform.resolve waiter (lastSettled ())

    /// <summary>Disposes the flight token's source without cancelling it. The next flight allocates another.</summary>
    let retireSource () =
        if not (Platform.isNull cts) then
            cts.Dispose ()
            cts <- null

    /// <summary>
    /// Retires the shared source under every policy except <c>CancelPrevious</c> once no body is executing and every
    /// flight holding it has settled. Registrations left on the token are released with it.
    /// </summary>
    let retireIfQuiet () =
        if running = 0 then
            match policy with
            | FlightPolicy.Queue
            | FinishCurrent
            | Drop
            | DropWhilePending when queued = 0 -> retireSource ()
            | KeepLatest when flying = 0 -> retireSource ()
            | _ -> ()

    /// <summary>
    /// Brings every source current. Clears a stale source left behind by a discarded change, so the source's next
    /// change reaches this node.
    /// </summary>
    let refreshSources () =
        graph.EnterPull ()

        try
            let mutable i = 0

            while i < sources.Count do
                sources.SourceAt(i).UpdateIfNecessary()
                i <- i + 1
        finally
            graph.ExitPull ()

    /// <summary>
    /// Discards the change left by a flight under <c>Drop</c> or a successful flight under <c>DropWhilePending</c>:
    /// the sources are brought current and the node is Clean.
    /// </summary>
    let discardAtSettle (gen: int) =
        refreshSources ()

        if freshness = Freshness.Dirty then
            Tracer.RunDropped (graph, id, gen)

        freshness <- Freshness.Clean

    /// <summary>Applies the result of the run numbered <c>gen</c>. Runs on the graph thread.</summary>
    let applyResult (gen: int) (outcome: Platform.FlightOutcome<'T>) =
        // True under `FinishCurrent` when a change arrived during the flight, and under `DropWhilePending` when a
        // failed flight discarded a change.
        let mutable owed = false

        // `Queue` applies every result in the order the flights started,
        // so it is the one policy that does not discard the superseded.
        let current =
            match policy with
            | FlightPolicy.Queue ->
                queued <- queued - 1
                true
            | KeepLatest ->
                flying <- flying - 1
                gen = generation
            | CancelPrevious -> gen = generation
            | Drop ->
                if
                    not disposed
                    && (queued = 2 || freshness <> Freshness.Clean)
                then
                    discardAtSettle gen

                queued <- 0
                gen = generation
            | DropWhilePending ->
                if
                    not disposed
                    && (queued = 2 || freshness <> Freshness.Clean)
                then
                    if
                        (match outcome with
                         | Platform.FlightOutcome.Completed _ -> true
                         | _ -> false)
                    then
                        discardAtSettle gen
                    else
                        // A Check is a discarded change only once a source resolves it to Dirty.
                        if queued = 1 && freshness = Freshness.Check then
                            refreshSources ()

                        if queued = 2 || freshness = Freshness.Dirty then
                            owed <- true
                        else
                            freshness <- Freshness.Clean

                queued <- 0
                gen = generation
            | FinishCurrent ->
                owed <- queued = 2
                queued <- 0
                gen = generation

        retireIfQuiet ()

        // An owed trailing run holds the node pending as a pending source does.
        let suspended =
            owed
            || not (Platform.isNull pendingSources)
               && pendingSources.Count > 0

        if current && not disposed then
            match outcome with
            // Under `Queue`, the newest run is waiting on a source: an
            // older flight's value is the last settled one, and the node
            // stays pending.
            | Platform.FlightOutcome.Completed v when suspended ->
                Tracer.FlightSettled (graph, id, gen, 0, (if owed then 2 else 1), box v)
                write v
                status <- Status.Pending
            | Platform.FlightOutcome.Faulted _
            | Platform.FlightOutcome.Canceled _ when suspended ->
                // An older flight's failure is dropped; the node stays pending on the source or the run owed.
                Tracer.FlightFailureDrop (
                    graph,
                    id,
                    gen,
                    owed,
                    (match policy with
                     | FlightPolicy.FinishCurrent -> true
                     | _ -> false)
                )
            | Platform.FlightOutcome.Completed v ->
                Tracer.FlightSettled (graph, id, gen, 0, 0, box v)
                write v
                failure <- null
                status <- Status.None
                wake ()
            // A cancellation of the current flight is a failure.
            | Platform.FlightOutcome.Faulted ex
            | Platform.FlightOutcome.Canceled ex ->
                Tracer.FlightSettled (
                    graph,
                    id,
                    gen,
                    (match outcome with
                     | Platform.FlightOutcome.Canceled _ -> 2
                     | _ -> 1),
                    0,
                    ex
                )
                // The owner link is the one reference to this node in reach of a let-bound function.
                failure <- graph.FailureOf (ex, (link.Child :?> INode), null)
                status <- Status.Error
                wake ()
        else
            Tracer.FlightDrop (graph, id, gen, (if disposed then 2 else 1))

        match policy with
        // Every chained result, dropped or failed included, completes the waiter behind it with the last settled value.
        | FlightPolicy.Queue -> lock observers releaseNext
        // The trailing or replacing run starts at the next read: readers are told the value is stale again.
        | FinishCurrent
        | DropWhilePending when owed && not disposed ->
            freshness <- Freshness.Dirty
            Tracer.TrailingRun (graph, id)
            observers.NotifyDirty ()
            Tracer.Notified graph
            graph.RequestFlush ()
        | CancelPrevious
        | KeepLatest
        | FinishCurrent
        | Drop
        | DropWhilePending -> ()

    let publish (gen: int) (outcome: Platform.FlightOutcome<'T>) =
        graph.Post (fun () -> applyResult gen outcome)

    /// <summary>
    /// Publishes a result chained under <c>Queue</c>. The returned task completes once the
    /// result is applied, so the next result in the chain applies after it on any thread.
    /// </summary>
    let publishQueued (gen: int) (outcome: Platform.FlightOutcome<'T>) =
        graph.DispatchApplied (fun () -> applyResult gen outcome)

    // Fable cannot translate a secondary constructor of a type with a
    // self-identifier. Under Fable the `Api` functions construct this type.
#if !FABLE_COMPILER
    /// <summary>
    /// A pure async memo over <c>compute</c>.
    /// </summary>
    new(graph: Graph, compute: Func<Previous<'T>, CancellationToken, Task<'T>>) as this =
        new AsyncMemo<'T> (graph, (fun previous token -> compute.Invoke (previous, token)), ScopeMode.PureAsync, graph.Options.FlightPolicy)
        then this.Attach ()

    /// <summary>
    /// An owning async memo over <c>compute</c> when <c>owning</c> is true, a pure one
    /// otherwise.
    /// </summary>
    new(graph: Graph, compute: Func<Previous<'T>, CancellationToken, Task<'T>>, owning: bool) as this =
        new AsyncMemo<'T> (
            graph,
            (fun previous token -> compute.Invoke (previous, token)),
            (if owning then ScopeMode.Owning else ScopeMode.PureAsync),
            graph.Options.FlightPolicy
        )

        then this.Attach ()
#endif

    /// <summary>
    /// An async memo over <c>compute</c>, owned by the current owner.
    /// </summary>
    static member internal Create(graph: Graph, compute: Previous<'T> -> CancellationToken -> Task<'T>, mode: ScopeMode) =
        AsyncMemo<'T>.Create(graph, compute, mode, graph.Options.FlightPolicy)

    /// <summary>
    /// An async memo over <c>compute</c> under <c>policy</c>, owned by the current owner.
    /// </summary>
    static member internal Create(graph: Graph, compute: Previous<'T> -> CancellationToken -> Task<'T>, mode: ScopeMode, policy: FlightPolicy) =
        let memo = new AsyncMemo<'T> (graph, compute, mode, policy)
        memo.Attach ()
        memo

    member private this.Attach() =
        link <- graph.CurrentOwner.AttachLinked this
        Tracer.AsyncMemoNew (graph, id, link.Owner)

    member private this.DetachSources() =
        sources.Clear (this :> IComputation)

    /// <summary>
    /// Discharges the previous flight's scope, then starts one flight, unless
    /// the async memo was disposed meanwhile. A read from a cleanup serves the
    /// previous state. A flight started by a cleanup's read replaces this one,
    /// and a write after that read starts over, as on <c>Memo.Recompute</c>.
    /// With a flight in progress, under <c>FinishCurrent</c> owes a trailing run instead and keeps the scope, and under
    /// <c>Drop</c> and <c>DropWhilePending</c> discards the change and keeps the scope.
    /// </summary>
    member private this.Start() =
        if
            queued <> 0
            && (match policy with
                | FlightPolicy.Queue -> false
                | _ -> true)
        then
            freshness <- Freshness.Clean
            queued <- 2

            Tracer.RunDeferred (
                graph,
                id,
                generation,
                (match policy with
                 | FlightPolicy.FinishCurrent -> true
                 | _ -> false)
            )
        elif Platform.isNull (box scope) then
            this.Launch ()
        else
            let before = runs
            freshness <- Freshness.Clean
            graph.Discharge scope

            if not disposed then
                if runs = before then
                    this.Launch ()
                elif freshness = Freshness.Dirty then
                    this.Start ()


    member private this.Launch() =
        freshness <- Freshness.Clean
        sources.BeginRun ()

        if
            not (Platform.isNull pendingSources)
            && pendingSources.Count > 0
        then
            pendingSources.Clear ()

        // Bumped before the cancel: a superseded flight settled inline by its
        // own cancellation must already be out of date when it publishes.
        generation <- generation + 1
        let gen = generation

        match policy with
        | CancelPrevious ->
            if not (Platform.isNull cts) then
                cts.Cancel ()
                cts.Dispose ()

            cts <- new CancellationTokenSource ()
        | KeepLatest
        | FlightPolicy.Queue
        | FinishCurrent
        | Drop
        | DropWhilePending ->
            if Platform.isNull cts then
                cts <- new CancellationTokenSource ()

        let token = cts.Token
        // The flight resumes from the value once every result chained before it is applied.
        let previous = Previous<'T>(this :> IPreviousSource<'T>, chained)
        runs <- runs + 1
        Tracer.RunStart (graph, id, runs)
        failure <- null

        status <-
            Status.Pending
            ||| (status &&& Status.Uninitialized)

        let mutable flight = null

        // A synchronous failure is a result like a faulted flight's. Under
        // `Queue` it takes its turn behind the flights started before it.
        let fail (ex: exn) =
            match policy with
            | FlightPolicy.Queue when queued > 0 ->
                queued <- queued + 1
                chained <- chained + 1
                let outcome = Task.FromResult (Platform.FlightOutcome<'T>.Faulted ex)
                tail <- Platform.after tail (fun () -> Platform.apply outcome (publishQueued gen))
            | _ ->
                failure <- graph.FailureOf (ex, this, null)
                status <- Status.Error

        running <- running + 1

        try
            try
                // A flight dropped for a pending read or a violation gets a
                // continuation, so its rejection is handled; under Fable an
                // unhandled rejection ends the node process.
                let run () =
                    let started =
#if FABLE_COMPILER
                        compute previous token
#else
                        Platform.runFlightBody graph.EnterContinuation graph.LeaveContinuation (fun () -> compute previous token)
#endif

                    if
                        (violated || graph.RaisedPending)
                        && not (Platform.isNull started)
                    then
                        Platform.outcomeOf started |> ignore

                    started

                let started = graph.RunHosted (this :> IComputation, run)

                if violated then
                    raise (InvalidOperationException (ScopeMessages.forMode mode))

                flight <- started
            finally
                running <- running - 1

                if disposed then
                    sources.Clear (this :> IComputation)
                else
                    sources.EndRun (this :> IComputation)
        with
        | ex when violated ->
            violated <- false
            fail (ScopeMessages.failure mode ex)
        | NotReadyException source ->
            // The body suspended before it could start a flight: it read a
            // source that is itself pending. There is nothing to await, only
            // another source to wait on.
            if Platform.isNull pendingSources then
                pendingSources <- HashSet<INode>(HashIdentity.Reference)

            pendingSources.Add source |> ignore
        | ex -> fail ex

        if not (Platform.isNull flight) then
            let flight = flight
            Tracer.FlightStart (graph, id, gen)
            let settle = publish gen
            launching <- true

            try
                match policy with
                | FlightPolicy.Queue ->
                    queued <- queued + 1
                    chained <- chained + 1
                    let outcome = Platform.outcomeOf flight
                    tail <- Platform.after tail (fun () -> Platform.apply outcome (publishQueued gen))
                | KeepLatest ->
                    flying <- flying + 1
                    Platform.whenSettled flight settle |> ignore
                | FinishCurrent
                | Drop
                | DropWhilePending ->
                    queued <- 1
                    Platform.whenSettled flight settle |> ignore
                | CancelPrevious -> Platform.whenSettled flight settle |> ignore
            finally
                launching <- false

            Tracer.RunEnd (graph, id, status)
        else
            // A run that failed or suspended before starting a flight leaves no settle to retire the source.
            retireIfQuiet ()
            Tracer.RunEnd (graph, id, status)

    member private this.EnsureCurrent() =
        // One comparison on the cached path, as on `Memo`.
        if freshness <> Freshness.Clean && not disposed then
            this.Pull ()

    /// <summary>
    /// Brings a stale async memo current.
    /// </summary>
    member private this.Pull() =
        graph.EnterPull ()

        try
            // Resolving `Check` matters more here than anywhere: the work it
            // may avoid is a network round trip, not a multiplication.
            if freshness = Freshness.Check then
                Tracer.CheckStart (graph, id)
                let mutable i = 0

                while freshness = Freshness.Check && i < sources.Count do
                    sources.SourceAt(i).UpdateIfNecessary()
                    i <- i + 1

                Tracer.CheckResolved (graph, id, (freshness = Freshness.Dirty))

                if freshness = Freshness.Check then
                    freshness <- Freshness.Clean

            if freshness = Freshness.Dirty then
                this.Start ()
        finally
            graph.ExitPull ()

    interface IOwned with
        member this.Release() =
            link <- null
            this.Dispose ()

    interface IScopeHost with
        member _.Scope =
            if Platform.isNull (box scope) then
                if mode = ScopeMode.Owning then
                    scope <- new Owner (graph.Root)
                    Tracer.ScopeNew (scope, graph, id)

                    if disposed then
                        scope.Dispose ()
                else
                    violated <- true
                    raise (InvalidOperationException (ScopeMessages.forMode mode))

            scope

#if RANVIER_TRACE
    interface ITracedNode with
        member _.TraceGraph = box graph
#endif

    interface INode with
        member _.Id = id
        member _.Status = status ||| Staleness.ofFreshness freshness

    interface ISource with
        member _.AddObserver(c, slot) =
            observers.Add (c, slot)

        member _.TryAddObserver(c, slot) =
            observers.TryAdd (c, slot)

        member _.RemoveObserverAt(c, handle) =
            observers.RemoveAt (c, handle)

        member this.UpdateIfNecessary() =
            this.EnsureCurrent ()

    interface IComputation with
        member this.AddSource s =
            sources.Add (this :> IComputation, s)

        member _.MovedIn(slot, handle) =
            sources.MovedIn (slot, handle)

        member _.MarkDirty() =
            if freshness <> Freshness.Dirty then
                freshness <- Freshness.Dirty

                // `Dirty`, not `Check`, and unlike `Memo` this is not deferred
                // to the recompute: re-running an async memo *always* changes
                // what it publishes, because starting a flight makes it
                // pending. There is no cutoff to wait for. Dependents are told
                // immediately rather than when the flight lands, because they
                // have to learn that what they read is now stale, and an effect
                // has to re-read to start the flight at all.
                observers.NotifyDirty ()

        member _.MarkCheck() =
            if freshness = Freshness.Clean then
                freshness <- Freshness.Check

                observers.NotifyCheck ()

    /// <summary>
    /// Cancels the flight in progress and detaches from every source.
    /// Idempotent. Usually called for you, by the enclosing owner.
    /// </summary>
    /// <remarks>
    /// A settled memo keeps its last value. A memo still pending fails with
    /// <c>ObjectDisposedException</c> and wakes its readers once.
    /// </remarks>
    member this.Dispose() =
        graph.Entered (
            "Disposing a node",
            fun () ->
                if not disposed then
                    disposed <- true
                    Tracer.NodeDispose (graph, id)

                    if not (Platform.isNull link) then
                        link.Detach ()
                        link <- null

                    this.DetachSources ()

                    if not (Platform.isNull (box scope)) then
                        graph.Retire scope

                    if not (Platform.isNull cts) then
                        // Detached before the cancel: a flight settled inline by it runs `retireSource`.
                        let source = cts
                        cts <- null
                        source.Cancel ()
                        source.Dispose ()

                    if status.HasFlag Status.Pending then
                        if not (Platform.isNull pendingSources) then
                            pendingSources.Clear ()

                        failure <- Failure (ObjectDisposedException (this.GetType().Name), (this :> INode))
                        status <- Status.Error
                        wake ()

                    lock observers (fun () ->
                        if not (Platform.isNull waiters) then
                            let settled = lastSettled ()

                            for waiter in waiters.Values do
                                Platform.resolve waiter settled

                            waiters <- null)
        )

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

    // The lock covers a body reading `Settled` from a continuation off the graph thread.
    interface IPreviousSource<'T> with
        member _.Settled position =
            lock observers (fun () ->
                if disposed || applied >= position then
                    settledNow ()
                else
                    if Platform.isNull waiters then
                        waiters <- Dictionary<int, Platform.Deferred<'T voption>>()

                    let waiter = Platform.findOrDefault waiters position

                    if Platform.isNull (box waiter) then
                        let waiter = Platform.deferred<'T voption>()
                        waiters[position] <- waiter
                        Platform.deferredTask waiter
                    else
                        Platform.deferredTask waiter)

    member _.Status = status

    /// <summary>
    /// The source of the last run's last pending read, or empty when the run did not suspend.
    /// </summary>
    /// <remarks>
    /// Under <c>KeepLatest</c> and <c>Queue</c>, an earlier flight may still be in progress while a source is listed.
    /// Under <c>FinishCurrent</c>, <c>Drop</c> and <c>DropWhilePending</c>, a source is listed only by a run that started
    /// no flight.
    /// </remarks>
    member _.PendingSources =
        if Platform.isNull pendingSources then
            Seq.empty
        else
            pendingSources :> seq<INode>

    /// <summary>
    /// How many times the body has run, including runs that suspended or threw before starting a flight.
    /// </summary>
    member _.Runs = runs

    /// <summary>
    /// Transparent, tracked read. Raises <c>NotReadyException</c> while <c>Status</c> is Pending and the recorded
    /// error while it is Error.
    /// </summary>
    /// <remarks>
    /// Pending while a flight is in progress, except under <c>Queue</c> once an earlier flight's outcome is applied.
    /// </remarks>
    member this.Value: 'T =
        this.EnsureCurrent ()
        graph.Track (this :> ISource)

        if status.HasFlag Status.Pending then
            raise (graph.NotReady (this :> INode))

        if status.HasFlag Status.Error then
            graph.Raise failure

        value

    /// <summary>
    /// Non-throwing, tracked read.
    /// </summary>
    member this.TryValue: Reading<'T> =
        this.EnsureCurrent ()
        graph.Track (this :> ISource)

        if status.HasFlag Status.Pending then Pending
        elif status.HasFlag Status.Error then Failed failure.Error
        else Ready value

    /// <summary>
    /// The node the current failure originated in while <c>Status</c> has <c>Error</c>, otherwise null. Untracked.
    /// </summary>
    /// <remarks>
    /// The async memo itself for a faulted or cancelled flight, a throwing body, a purity violation or disposal while
    /// pending; the upstream node when the body rethrew the exception of a failed read before its first await that
    /// suspends. A failure read after that await can report the async memo instead.
    /// </remarks>
    member _.ErrorOrigin: INode = Failure.OriginOf failure

    /// <summary>
    /// The last settled value, untracked, without starting a flight. The one
    /// read that never suspends — Solid's <c>latest</c>.
    /// </summary>
    member _.Peek = value

/// <summary>
/// Where a channel stops.
/// </summary>
/// <remarks>
/// <para>
/// Pending and error both propagate: a suspended source suspends everything
/// that reads it, all the way out. Something has to end that, or the only
/// possible response to one in-flight request is for the whole graph to wait.
/// A boundary is a computation that catches a channel coming out of its body
/// and substitutes a value, so its own dependents see an ordinary value and
/// never learn that anything was in flight.
/// </para>
/// <para>
/// This is control flow over the reactive graph, not rendering. A boundary
/// yields a value of the same type its body yields; whether that value is a
/// view, a message or a number is the caller's business. Solid's <c>Suspense</c> is
/// this plus a DOM fallback, and the DOM half belongs in Partas.Solid.
/// </para>
/// <para>
/// Absorbing a channel does not mean ignoring the source. The read that
/// suspended still linked its edge before throwing, so the settle marks the
/// boundary dirty, its dependents are notified, and the next read runs the body
/// again — from the top, never resumed.
/// </para>
/// <para>
/// Constructed through the static members: the three of them name the three
/// things a boundary can catch.
/// </para>
/// </remarks>
type Boundary<'T>
    private
    (
        graph: Graph,
        body: unit -> 'T,
        onPending: ('T voption -> 'T) voption,
        onError: (exn -> 'T voption -> 'T) voption,
        ?comparer: IEqualityComparer<'T>
    ) =
    let id = graph.NextId ()
    let observers = ObserverSet ()
    do Tracer.Bind (observers, graph, id)
    let sources = SourceList ()
    do Tracer.Bind (sources, graph, id)
    let mutable pendingSources: HashSet<INode> = null

    /// <summary>
    /// The typed cutoff comparer selected at construction.
    /// </summary>
    let equal =
        match comparer with
        | Some supplied -> supplied
        | None -> graph.Options.Equality.Comparer<'T>()

    let mutable freshness = Freshness.Dirty
    let mutable status = Status.Uninitialized
    let mutable value = Unchecked.defaultof<'T>
    let mutable failure: Failure = null
    let mutable caught: Failure = null
    let mutable waiting = false
    let mutable runs = 0
    let mutable disposed = false
    let mutable link: OwnerLink = null

    /// <summary>
    /// True once a run has produced a value: the body's, a fallback's or a recover's.
    /// </summary>
    let mutable shown = false

    /// <summary>
    /// Owner of the nodes the body creates, allocated by the first of them.
    /// Discharged before each run and disposed with the computation.
    /// </summary>
    let mutable scope: Owner = Unchecked.defaultof<Owner>

    /// <summary>
    /// A boundary over <c>body</c>, owned by the current owner.
    /// </summary>
    static member internal Create(graph: Graph, body: unit -> 'T, onPending: ('T voption -> 'T) voption, onError: (exn -> 'T voption -> 'T) voption) =
        let boundary = Boundary<'T>(graph, body, onPending, onError)
        boundary.Attach ()
        boundary

    /// <summary>An owned boundary whose value cutoff uses <c>comparer</c>.</summary>
    static member internal CreateWithComparer
        (
            graph: Graph,
            body: unit -> 'T,
            onPending: ('T voption -> 'T) voption,
            onError: (exn -> 'T voption -> 'T) voption,
            comparer: IEqualityComparer<'T>
        ) =
        let boundary = Boundary<'T>(graph, body, onPending, onError, comparer = comparer)
        boundary.Attach ()
        boundary

    // Owned through `IOwned`, for the reason given on Memo.
    member private this.Attach() =
        link <- graph.CurrentOwner.AttachLinked this
        Tracer.BoundaryNew (graph, id, link.Owner)

    member private this.DetachSources() =
        sources.Clear (this :> IComputation)

    /// <summary>
    /// Discharges the previous run's scope, then runs the body once, unless
    /// the boundary was disposed meanwhile. A read from a cleanup serves the
    /// previous value. A run started by a cleanup's read replaces this one,
    /// and a write after that read starts over, as on <c>Memo.Recompute</c>.
    /// </summary>
    member private this.Recompute() =
        if Platform.isNull (box scope) then
            this.Run ()
        else
            let before = runs
            freshness <- Freshness.Clean
            graph.Discharge scope

            if not disposed then
                if runs = before then
                    this.Run ()
                elif freshness = Freshness.Dirty then
                    this.Recompute ()


    member private this.Run() =
        // Clean before the body runs, for the reason given on Memo.Recompute:
        // an invalidation raised while the body is running has to survive it.
        freshness <- Freshness.Clean
        sources.BeginRun ()

        if
            not (Platform.isNull pendingSources)
            && pendingSources.Count > 0
        then
            pendingSources.Clear ()

        // Captured before the run overwrites them, to decide at the end whether
        // the observers told `Check` are owed a `Dirty`. `waiting` is part of
        // the comparison because a caught fallback can compute the same value
        // the body did, and a dependent asking `IsWaiting` would not see it
        // change. `caught` is compared for the same reason: a recover can
        // return the same value for a different exception.
        let previous = value
        let previousStatus = status
        let previousWaiting = waiting
        let previousFailure = failure
        let previousCaught = caught

        status <- Status.None
        failure <- null
        caught <- null
        waiting <- false
        runs <- runs + 1
        Tracer.RunStart (graph, id, runs)

        let last = if shown then ValueSome previous else ValueNone

        let recordPending (source: INode) =
            if Platform.isNull pendingSources then
                pendingSources <- HashSet<INode>(HashIdentity.Reference)

            pendingSources.Add source |> ignore

        // A handler is ordinary caller code and can do anything the body can,
        // including read a source that has not settled and throw. Running it in
        // the `with` of the body's own `try` put it outside every handler this
        // boundary has, so a fallback that suspended threw straight out of
        // `TryValue` — a boundary failing in the exact way it exists to
        // prevent. Both handlers therefore run under their own guard, and their
        // outcome is classified the way the body's is.
        //
        // They also run inside the tracked region, because the boundary's value
        // *is* the handler's value: whatever the fallback reads has to be able
        // to wake the boundary, or a fallback waiting on its own source waits
        // for ever.
        let rec recoverOrFail (ex: exn) =
            graph.ClearRaised ()

            match onError with
            | ValueSome recover ->
                // Taken before `recover` runs: a failed read inside it replaces the last failed read.
                let from = graph.FailureOf (ex, this, previousCaught)

                try
                    let recovered = recover ex last
                    graph.CheckRaised ()
                    caught <- from
                    recovered
                with
                | NotReadyException inner -> suspend inner
                | rethrown ->
                    graph.ClearRaised ()

                    failure <-
                        if obj.ReferenceEquals (rethrown, ex) then
                            from
                        else
                            graph.FailureOf (rethrown, this, previousFailure)

                    status <- Status.Error
                    value

            | ValueNone ->
                failure <- graph.FailureOf (ex, this, previousFailure)
                status <- Status.Error
                value

        // Nothing to show and nothing to report: what this is, is waiting.
        // Keeps the previous value, which is all `Peek` ever promised.
        and suspend (source: INode) =
            graph.ClearRaised ()
            recordPending source
            status <- Status.Pending
            value

        let run () =
            try
                let result = body ()
                graph.CheckRaised ()
                result
            with
            | NotReadyException source ->
                graph.ClearRaised ()
                recordPending source

                match onPending with
                | ValueSome fallback ->
                    // Caught: the status stays clear, so nothing downstream
                    // ever sees Pending. The edge to `source` is already
                    // linked, which is what brings us back here when it
                    // settles.
                    try
                        let substitute = fallback last
                        graph.CheckRaised ()
                        waiting <- true
                        substitute
                    with
                    | NotReadyException inner -> suspend inner
                    | ex -> recoverOrFail ex

                | ValueNone -> suspend source

            | ex -> recoverOrFail ex

        try
            value <- graph.RunHosted (this :> IComputation, run)
        finally
            if disposed then
                sources.Clear (this :> IComputation)
            else
                sources.EndRun (this :> IComputation)

        // The cutoff. See `Memo.Run`. A throwing comparer fails the boundary
        // without reaching `recover`.
        let mutable moved =
            status <> previousStatus
            || waiting <> previousWaiting
            || Failure.Moved (failure, previousFailure)
            || Failure.Moved (caught, previousCaught)

        if not moved then
            try
                moved <- not (equal.Equals (previous, value))
            with ex ->
                value <- previous
                failure <- graph.FailureOf (ex, this, previousFailure)
                caught <- null
                waiting <- false
                status <- Status.Error
                moved <- true

        if status = Status.None then
            shown <- true

        if moved then
            Tracer.Moved (graph, id, Failure.Payload (failure, box value))
            observers.NotifyDirtyExcept graph.CurrentComputation
            Tracer.Notified graph
            Tracer.RunEnd (graph, id, status)
        else
            Tracer.RunEnd (graph, id, status)

    member private this.EnsureCurrent() =
        // One comparison on the path a cached read takes. Testing `disposed`
        // or `Check` first costs every clean read an extra comparison, which
        // measured at ~1 ns against a ~1.3 ns baseline — the read is small
        // enough that one avoidable test is most of it.
        if freshness <> Freshness.Clean && not disposed then
            this.Pull ()

    /// <summary>
    /// Brings a stale boundary current.
    /// </summary>
    member private this.Pull() =
        graph.EnterPull ()

        try
            if freshness = Freshness.Check then
                Tracer.CheckStart (graph, id)
                let mutable i = 0

                while freshness = Freshness.Check && i < sources.Count do
                    sources.SourceAt(i).UpdateIfNecessary()
                    i <- i + 1

                Tracer.CheckResolved (graph, id, (freshness = Freshness.Dirty))

                if freshness = Freshness.Check then
                    freshness <- Freshness.Clean

            if freshness = Freshness.Dirty then
                this.Recompute ()
        finally
            graph.ExitPull ()

    /// <summary>
    /// Detaches from every source. Idempotent, and usually called for you by
    /// the enclosing owner. A disposed boundary keeps answering reads with the
    /// last value it produced — fallback included — and stops being woken.
    /// </summary>
    member this.Dispose() =
        graph.Entered (
            "Disposing a node",
            fun () ->
                if not disposed then
                    disposed <- true
                    Tracer.NodeDispose (graph, id)

                    if not (Platform.isNull link) then
                        link.Detach ()
                        link <- null

                    this.DetachSources ()

                    if not (Platform.isNull (box scope)) then
                        graph.Retire scope
        )

    /// <summary>
    /// Catches the pending channel. Errors still propagate — a failure is not a
    /// slow success, and conflating them is how a permanent error becomes a
    /// spinner that never stops.
    /// </summary>
    static member Suspense(graph: Graph, body: Func<'T>, fallback: Func<'T voption, 'T>) =
        Boundary<'T>.Create(graph, body.Invoke, ValueSome fallback.Invoke, ValueNone)

    /// <summary>
    /// Catches the error channel. Pending still propagates, so a boundary that
    /// reports failures does not also swallow the fact that something is in
    /// flight.
    /// </summary>
    static member Errors(graph: Graph, body: Func<'T>, recover: Func<exn, 'T voption, 'T>) =
        Boundary<'T>.Create(graph, body.Invoke, ValueNone, ValueSome (fun ex previous -> recover.Invoke (ex, previous)))

    /// <summary>
    /// Catches both.
    /// </summary>
    static member Catching(graph: Graph, body: Func<'T>, fallback: Func<'T voption, 'T>, recover: Func<exn, 'T voption, 'T>) =
        Boundary<'T>.Create(graph, body.Invoke, ValueSome fallback.Invoke, ValueSome (fun ex previous -> recover.Invoke (ex, previous)))

    /// <summary>
    /// <c>Suspense</c> whose <c>fallback</c> receives the boundary's last value, or <c>seed</c> before its first.
    /// </summary>
    static member Suspense(graph: Graph, body: Func<'T>, seed: 'T, fallback: Func<'T, 'T>) =
        Boundary<'T>.Create(graph, body.Invoke, ValueSome (fun previous -> fallback.Invoke (ValueOption.defaultValue seed previous)), ValueNone)

    /// <summary>
    /// <c>Errors</c> whose <c>recover</c> receives the error and the boundary's last value, or <c>seed</c> before its
    /// first.
    /// </summary>
    static member Errors(graph: Graph, body: Func<'T>, seed: 'T, recover: Func<exn, 'T, 'T>) =
        Boundary<'T>.Create(graph, body.Invoke, ValueNone, ValueSome (fun ex previous -> recover.Invoke (ex, ValueOption.defaultValue seed previous)))

    /// <summary>
    /// <c>Catching</c> whose handlers receive the boundary's last value, or <c>seed</c> before its first.
    /// </summary>
    static member Catching(graph: Graph, body: Func<'T>, seed: 'T, fallback: Func<'T, 'T>, recover: Func<exn, 'T, 'T>) =
        Boundary<'T>
            .Create(
                graph,
                body.Invoke,
                ValueSome (fun previous -> fallback.Invoke (ValueOption.defaultValue seed previous)),
                ValueSome (fun ex previous -> recover.Invoke (ex, ValueOption.defaultValue seed previous))
            )

    interface IOwned with
        member this.Release() =
            link <- null
            this.Dispose ()

    interface IScopeHost with
        member _.Scope =
            if Platform.isNull (box scope) then
                scope <- new Owner (graph.Root)
                Tracer.ScopeNew (scope, graph, id)

                if disposed then
                    scope.Dispose ()

            scope

#if RANVIER_TRACE
    interface ITracedNode with
        member _.TraceGraph = box graph
#endif

    interface INode with
        member _.Id = id
        member _.Status = status ||| Staleness.ofFreshness freshness

    interface ISource with
        member _.AddObserver(c, slot) =
            observers.Add (c, slot)

        member _.TryAddObserver(c, slot) =
            observers.TryAdd (c, slot)

        member _.RemoveObserverAt(c, handle) =
            observers.RemoveAt (c, handle)

        member this.UpdateIfNecessary() =
            this.EnsureCurrent ()

    interface IComputation with
        member this.AddSource s =
            sources.Add (this :> IComputation, s)

        member _.MovedIn(slot, handle) =
            sources.MovedIn (slot, handle)

        member _.MarkDirty() =
            if freshness <> Freshness.Dirty then
                let wasClean = freshness = Freshness.Clean
                freshness <- Freshness.Dirty

                // `Check`, for the reason given on `Memo.MarkDirty`: a
                // boundary that re-runs to the same value — the common case
                // while a fallback is standing in and an unrelated source
                // moves — has published nothing new.
                if wasClean then
                    observers.NotifyCheck ()

        member _.MarkCheck() =
            if freshness = Freshness.Clean then
                freshness <- Freshness.Check

                observers.NotifyCheck ()

    member _.Status = status

    /// <summary>True while the body is suspended and the fallback is standing in for it.</summary>
    /// <remarks>
    /// The only way to tell a fallback from a real value, since a caught channel is invisible to dependents by
    /// construction. A tracked read that brings the boundary current, as <c>TryValue</c> does.
    /// </remarks>
    member this.IsWaiting =
        this.EnsureCurrent ()
        graph.Track (this :> ISource)
        waiting

    /// <summary>The error the boundary recovered from on its current run, or null.</summary>
    /// <remarks>A tracked read that brings the boundary current, as <c>TryValue</c> does.</remarks>
    member this.Caught =
        this.EnsureCurrent ()
        graph.Track (this :> ISource)
        Failure.ErrorOf caught

    /// <summary>The node <c>Caught</c> originated in, or null when <c>Caught</c> is null.</summary>
    /// <remarks>
    /// A tracked read that brings the boundary current, as <c>Caught</c> does. The boundary itself when its body raised
    /// the exception; the upstream node when the body rethrew the exception of a failed read.
    /// </remarks>
    member this.CaughtFrom: INode =
        this.EnsureCurrent ()
        graph.Track (this :> ISource)
        Failure.OriginOf caught

    /// <summary>
    /// The source of the last run's last pending read, whether or not a fallback caught it, and the source a fallback or
    /// recover suspended on; empty when the run did not suspend.
    /// </summary>
    member _.PendingSources =
        if Platform.isNull pendingSources then
            Seq.empty
        else
            pendingSources :> seq<INode>

    /// <summary>
    /// How many times the body has run.
    /// </summary>
    member _.Runs = runs

    /// <summary>
    /// Transparent, tracked read. Raises <c>NotReadyException</c> while <c>Status</c> is Pending and the recorded
    /// error while it is Error.
    /// </summary>
    /// <remarks>
    /// A channel the boundary does not catch passes through. A caught channel reaches the reader when its handler
    /// suspends or throws.
    /// </remarks>
    member this.Value: 'T =
        this.EnsureCurrent ()
        graph.Track (this :> ISource)

        if status.HasFlag Status.Pending then
            raise (graph.NotReady (this :> INode))

        if status.HasFlag Status.Error then
            graph.Raise failure

        value

    /// <summary>
    /// Non-throwing, tracked read.
    /// </summary>
    member this.TryValue: Reading<'T> =
        this.EnsureCurrent ()
        graph.Track (this :> ISource)

        if status.HasFlag Status.Pending then Pending
        elif status.HasFlag Status.Error then Failed failure.Error
        else Ready value

    /// <summary>
    /// The node the current failure originated in while <c>Status</c> has <c>Error</c>, otherwise null. Untracked.
    /// </summary>
    /// <remarks>
    /// The boundary itself when its comparer raised the exception, or <c>recover</c> or a fallback raised a new one; the
    /// upstream node for an exception that passed through, including one rethrown by <c>recover</c>.
    /// </remarks>
    member _.ErrorOrigin: INode = Failure.OriginOf failure

    /// <summary>
    /// Untracked read of the cached value, without re-running the body.
    /// </summary>
    member _.Peek = value
