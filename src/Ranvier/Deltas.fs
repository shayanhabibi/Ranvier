namespace Ranvier

open System
open System.Collections.Generic

/// <summary>What happened to a key between two reads by one <c>ProjectionReader</c>.</summary>
type KeyChange =
    /// <summary>Live at this read, absent at the previous one.</summary>
    | Added = 0
    /// <summary>Absent at this read, live at the previous one.</summary>
    | Removed = 1
    /// <summary>Live at both reads, removed and re-added between them: a new row, and a new scope in the factory form.</summary>
    | Replaced = 2
    /// <summary>Reserved for value readers. A key reader reports membership only.</summary>
    | Changed = 3

/// <summary>
/// The changes recorded for one reader, one pair per key, merged as they arrive. Enumerates in an unspecified order.
/// </summary>
#if FABLE_COMPILER
[<Sealed; Fable.Core.AttachMembers>]
#else
[<Sealed>]
#endif
type internal KeyChanges<'K when 'K: equality>() =
    let pairs = ResizeArray<KeyValuePair<'K, KeyChange>>()

    /// <summary>One past each key's index in <c>pairs</c>, and 0 for an absent key.</summary>
    let slots = Platform.KeyMap<'K, int>()

    member _.Size = pairs.Count

    /// <summary>
    /// Merges <c>change</c> into the key's pending change: <c>Added</c> then <c>Removed</c> drops the key, and <c>Removed</c>
    /// then <c>Added</c> gives <c>Replaced</c>.
    /// </summary>
    member _.Record(key: 'K, change: KeyChange) =
        let slot = slots.Find key

        if slot > 0 then
            let previous = pairs[slot - 1].Value

            if
                change = KeyChange.Removed
                && previous = KeyChange.Added
            then
                let last = pairs.Count - 1

                if slot - 1 < last then
                    let moved = pairs[last]
                    pairs[slot - 1] <- moved
                    slots.Set (moved.Key, slot)

                pairs.RemoveAt last
                slots.Remove key
            elif
                change = KeyChange.Added
                && previous = KeyChange.Removed
            then
                pairs[slot - 1] <- KeyValuePair (key, KeyChange.Replaced)
            else
                pairs[slot - 1] <- KeyValuePair (key, change)
        else
            pairs.Add (KeyValuePair (key, change))
            slots.Set (key, pairs.Count)

    member _.Clear() =
        if pairs.Count > 0 then
            pairs.Clear ()
            slots.Clear ()

    interface IReadOnlyCollection<KeyValuePair<'K, KeyChange>> with
        member _.Count = pairs.Count

        member _.GetEnumerator() : IEnumerator<KeyValuePair<'K, KeyChange>> =
            (pairs :> seq<_>).GetEnumerator()

        member _.GetEnumerator() : Collections.IEnumerator =
            (pairs :> Collections.IEnumerable).GetEnumerator()

/// <summary>The changes to a projection's membership and order between two reads by one <c>ProjectionReader</c>.</summary>
#if FABLE_COMPILER
[<Sealed; Fable.Core.AttachMembers>]
#else
[<Sealed>]
#endif
type ProjectionDelta<'K when 'K: equality> internal (changes: KeyChanges<'K>, keys: 'K[], previousKeys: 'K[], isReset: bool) =
    let mutable positional: IReadOnlyList<PositionalChange<'K>> = null

    /// <summary>One entry per key whose membership moved, in an unspecified order. Empty on a reset.</summary>
    member _.Changes: IReadOnlyCollection<KeyValuePair<'K, KeyChange>> = changes

    /// <summary>The key order at this read: the array the projection's <c>Keys</c> returned. Empty after the projection is disposed.</summary>
    member _.Keys = keys

    /// <summary>The key order at the previous read, and empty on the reader's first read.</summary>
    member _.PreviousKeys = previousKeys

    /// <summary>
    /// True whenever the order or membership of <c>Keys</c> differs from <c>PreviousKeys</c>. Also true after an order that moved
    /// and moved back between the reads; <c>Positional</c> is then empty.
    /// </summary>
    member _.OrderChanged = not (obj.ReferenceEquals (keys, previousKeys))

    /// <summary>
    /// True on the reader's first read, after the reader fell behind, and after the projection is disposed. <c>Changes</c> is
    /// then empty, and the consumer rebuilds from <c>Keys</c>.
    /// </summary>
    member _.IsReset = isReset

    /// <summary>True when the delta reports no reset, no change and the same key order.</summary>
    member this.IsEmpty =
        not isReset
        && changes.Size = 0
        && not this.OrderChanged

    /// <summary>
    /// The edits that turn <c>PreviousKeys</c> into <c>Keys</c> when applied in sequence: removals from back to front, then the
    /// moves and inserts in key order. Empty while <c>OrderChanged</c> is false.
    /// </summary>
    /// <remarks>Computed by the first read of the property, O(N log N), and cached.</remarks>
    member this.Positional: IReadOnlyList<PositionalChange<'K>> =
        if isNull positional then
            positional <-
                if this.OrderChanged then
                    Positional.diff previousKeys keys
                else
                    ResizeArray<PositionalChange<'K>>()

        positional

/// <summary>The projection behind a <c>ProjectionReader</c>.</summary>
type internal IKeyLogHost<'K when 'K: equality> =
    /// <summary>The projection's <c>Keys</c>, as a tracked read.</summary>
    abstract ReadKeys: unit -> 'K[]

    /// <summary>How many keys are live.</summary>
    abstract LiveCount: int

    /// <summary>Stops recording into <c>reader</c>.</summary>
    abstract Detach: reader: ProjectionReader<'K> -> unit

/// <summary>A cursor over a projection's membership and order. Disposed with the scope that created it.</summary>
/// <remarks>
/// Each reader keeps its own changes, at most <c>max(64, N)</c> of them; past that its next read reports a reset. Created by
/// <c>Projection.NewKeyReader</c>.
/// </remarks>
and
#if FABLE_COMPILER
    [<Sealed; Fable.Core.AttachMembers>]
#else
    [<Sealed>]
#endif
    ProjectionReader<'K when 'K: equality> internal (host: IKeyLogHost<'K>) =
    let empty = KeyChanges<'K>()
    let mutable changes = KeyChanges<'K>()

    /// <summary>The keys the previous read returned.</summary>
    let mutable cursor: 'K[] = Array.empty

    /// <summary>
    /// Set until the first read, after the reader falls behind and after the projection is disposed. The reader records
    /// nothing while it is set.
    /// </summary>
    let mutable reset = true

    /// <summary>The delta a read returns while nothing changed since the previous read, or null.</summary>
    let mutable idle: ProjectionDelta<'K> = Unchecked.defaultof<ProjectionDelta<'K>>
    let mutable disposed = false
    let mutable link: OwnerLink = null

    member internal _.Link
        with set (value: OwnerLink) = link <- value

    /// <summary>Records <c>change</c> to <c>key</c>, or flags a reset once the pending changes exceed the cap.</summary>
    member internal _.Record(key: 'K, change: KeyChange) =
        if not reset then
            changes.Record (key, change)

            if changes.Size > max 64 host.LiveCount then
                changes.Clear ()
                reset <- true

    member internal _.MarkReset() =
        changes.Clear ()
        reset <- true

    /// <summary>
    /// The changes since this reader's previous read, as a tracked read of the projection's <c>Keys</c>. Advances the cursor.
    /// </summary>
    /// <remarks>
    /// Wakes its reader on every change it reports. While the projection's pass is pending or failed, raises what the pass
    /// raised, as <c>Keys</c> does, and keeps the changes for the next read. Reads with no change in between return one cached delta.
    /// </remarks>
    /// <exception cref="T:System.ObjectDisposedException">The reader was disposed.</exception>
    member _.Read() : ProjectionDelta<'K> =
        if disposed then
            raise (ObjectDisposedException "The projection reader was disposed.")

        let current = host.ReadKeys ()

        if reset then
            reset <- false
            changes.Clear ()
            let delta = ProjectionDelta (empty, current, cursor, true)
            cursor <- current
            idle <- Unchecked.defaultof<ProjectionDelta<'K>>
            delta
        elif changes.Size > 0 then
            //FOR-REVIEW A non-empty read allocates a fresh accumulator (ResizeArray + KeyMap, about 400 B on .NET) because the delta keeps the old one. Pooling would need a delta lifetime rule; left as allocation until the A/B gate says otherwise.
            let delta = ProjectionDelta (changes, current, cursor, false)
            changes <- KeyChanges<'K>()
            cursor <- current
            idle <- Unchecked.defaultof<ProjectionDelta<'K>>
            delta
        elif obj.ReferenceEquals (current, cursor) then
            if isNull (box idle) then
                idle <- ProjectionDelta (empty, cursor, cursor, false)

            idle
        else
            let delta = ProjectionDelta (empty, current, cursor, false)
            cursor <- current
            idle <- Unchecked.defaultof<ProjectionDelta<'K>>
            delta

    member private this.Stop() =
        if not disposed then
            disposed <- true

            if not (isNull link) then
                link.Detach ()
                link <- null

            host.Detach this
            changes.Clear ()
            cursor <- Array.empty
            idle <- Unchecked.defaultof<ProjectionDelta<'K>>

#if !FABLE_COMPILER
    // Under Fable the interface member below is the attached `Dispose`.
    /// <summary>Stops the reader and releases its changes. Idempotent.</summary>
    member this.Dispose() =
        this.Stop ()
#endif

    interface IDisposable with
        member this.Dispose() =
            this.Stop ()

    interface IOwned with
        member this.Release() =
            link <- null
            this.Stop ()

/// <summary>The readers of one projection, each recording every membership change.</summary>
[<Sealed; AllowNullLiteral>]
type internal KeyLog<'K when 'K: equality>() =
    let readers = ResizeArray<ProjectionReader<'K>>()

    member _.Count = readers.Count

    member _.Add(reader: ProjectionReader<'K>) =
        readers.Add reader

    member _.Remove(reader: ProjectionReader<'K>) =
        readers.Remove reader |> ignore

    member _.Record(key: 'K, change: KeyChange) =
        for i in 0 .. readers.Count - 1 do
            readers[i].Record(key, change)

    /// <summary>Flags every reader for a reset.</summary>
    member _.Reset() =
        for i in 0 .. readers.Count - 1 do
            readers[i].MarkReset()
