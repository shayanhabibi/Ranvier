namespace Ranvier

open System
open System.Collections.Generic

/// <summary>
/// The text form of a journaled value. <c>Name</c> and <c>Version</c> are recorded in each entry, and replay requires
/// the target's codec to carry the same pair.
/// </summary>
/// <remarks>
/// A Ranvier.Wire <c>WireCodec&lt;'T&gt;</c> adapts as
/// <c>{ Name = name; Version = 1; Encode = wire.Encode; Decode = wire.Decode &gt;&gt; Result.mapError string }</c>.
/// </remarks>
type JournalCodec<'T> =
    {
        Name: string
        Version: int
        Encode: 'T -> string
        Decode: string -> Result<'T, string>
    }

/// <summary>The failure a replay publishes for a journaled <c>Fail</c> entry, carrying the recorded message.</summary>
type JournalReplayedFailure(message: string) =
    inherit Exception(message)

/// <summary>A journal entry that a replay cannot apply to its target graph.</summary>
type JournalReplayException(seq: int, key: string option, payloadType: string, reason: string) =
    inherit
        Exception(
            "Journal entry "
            + string seq
            + (match key with
               | Some key -> " (key \"" + key + "\", "
               | None -> " (unkeyed, ")
            + payloadType
            + ") cannot be replayed: "
            + reason
        )

    /// <summary>The entry's <c>Seq</c>.</summary>
    member _.Seq = seq

    /// <summary>The entry's <c>Key</c>.</summary>
    member _.Key = key

    /// <summary>The entry's <c>PayloadType</c>.</summary>
    member _.PayloadType = payloadType

    /// <summary>Why the entry cannot be applied.</summary>
    member _.Reason = reason

/// <summary>The journal binding of an input of value type <c>'T</c>; <c>apply</c> and <c>fail</c> replay entries.</summary>
type internal CodecBinding<'T>(key: string, codec: JournalCodec<'T>, kinds: JournalEntryKind list, apply: 'T -> unit, fail: exn -> unit) =
    inherit JournalBinding(key, codec.Name, codec.Version)

    override _.Encode value =
        codec.Encode (unbox<'T> value)

    override _.Accepts kind =
        List.contains kind kinds

    override _.Prepare(kind, data) =
        if kind = JournalEntryKind.Fail then
            Ok (fun () -> fail (JournalReplayedFailure data))
        else
            let decoded =
                try
                    codec.Decode data
                with ex ->
                    Error ex.Message

            match decoded with
            | Ok value -> Ok (fun () -> apply value)
            | Error error -> Error error

/// <summary>An MVU loop's message input, registered on its graph's journal.</summary>
[<Sealed; AllowNullLiteral>]
type JournalSite<'Msg> internal (graph: Graph, id: int) =
    member internal _.Graph = graph
    member internal _.Id = id

/// <summary>
/// The application's async work on a graph, captured where the work starts. The inputs passed through it to
/// <c>Journal.produce</c> record <c>Produced</c> entries.
/// </summary>
[<Sealed>]
type JournalWork internal (graph: Graph, attribution: JournalAttribution) =
    member internal _.Graph = graph
    member internal _.Attribution = attribution

/// <summary>An input journal that keeps its entries in memory, in recording order.</summary>
type MemoryJournal(stream: string) =
    let entries = List<JournalEntry>()

    /// <summary>The entries recorded so far, oldest first.</summary>
    member _.Entries: JournalEntry list = List.ofSeq entries

    /// <summary>Removes every recorded entry.</summary>
    member _.Clear() =
        entries.Clear ()

    interface IInputJournal with
        member _.Stream = stream

        member _.Record entry =
            entries.Add entry

    // AoT compat
    /// <summary>The type name, the stream and the entry count.</summary>
    override _.ToString() =
        "MemoryJournal "
        + stream
        + " ("
        + string entries.Count
        + " entries)"

/// <summary>Registers input nodes on their graph's journal.</summary>
[<RequireQualifiedAccess>]
module Journal =
    let private checkKey (key: string) =
        if String.IsNullOrEmpty key then
            raise (ArgumentException ("A journal key must be a non-empty string.", nameof key))

    let private bind (graph: Graph) (recorder: JournalRecorder) (id: int) (key: string) (binding: JournalBinding) =
        graph.AssertOnGraphThread "Registering a journal key"
        graph.AddCleanup (fun () -> recorder.Unbind (id, key))
        recorder.Bind (id, binding)

    let private register (graph: Graph) (node: INode) (key: string) (binding: unit -> JournalBinding) =
        checkKey key
        let recorder = graph.Journal

        if not (Platform.isNull recorder) then
            bind graph recorder node.Id key (binding ())
#if RANVIER_TRACE
            let log = ((box graph) :?> ITraced).TraceLog

            if not (log.HasLabel node.Id) then
                log.Append (TraceEventKind.Label, node.Id, 0, 1, 0, 0, key)
                |> ignore
#endif

    /// <summary>
    /// Registers <c>signal</c> on its graph's journal under the application key <c>key</c>, so its external writes
    /// record values encoded by <c>codec</c>, and returns <c>signal</c>. Does nothing when the graph has no journal.
    /// </summary>
    /// <remarks>
    /// The key is the node's stable name across runs. The registration belongs to the innermost enclosing scope: when
    /// that scope is disposed or its computation re-runs, the key is free for another node. The traced build also labels
    /// an unlabelled node with the key.
    /// </remarks>
    /// <exception cref="T:System.InvalidOperationException">
    /// The graph has a journal, and the call runs off the graph's thread or in the body of a pure computation, such as
    /// a <c>createMemo</c> memo, as <c>onCleanup</c> does.
    /// </exception>
    /// <exception cref="T:System.ArgumentException">
    /// <c>key</c> is null or empty, the graph already has a node registered under <c>key</c>, or <c>signal</c> is
    /// already registered under a key.
    /// </exception>
    let key (key: string) (codec: JournalCodec<'T>) (signal: Signal<'T>) : Signal<'T> =
        register signal.Graph signal key (fun () ->
            CodecBinding<'T>(key, codec, [ JournalEntryKind.Write ], (fun value -> signal.Value <- value), ignore))

        signal

    /// <summary>
    /// Registers <c>source</c> on its graph's journal under the application key <c>key</c>, as <c>key</c> registers a
    /// signal, and returns <c>source</c>. Its external settles record values encoded by <c>codec</c>; its external
    /// failures record the exception's message.
    /// </summary>
    /// <exception cref="T:System.ArgumentException">
    /// <c>key</c> is null or empty, the graph already has a node registered under <c>key</c>, or <c>source</c> is
    /// already registered under a key.
    /// </exception>
    let keySource (key: string) (codec: JournalCodec<'T>) (source: AsyncSource<'T>) : AsyncSource<'T> =
        register source.Graph source key (fun () ->
            CodecBinding<'T>(
                key,
                codec,
                [ JournalEntryKind.Settle; JournalEntryKind.Fail ],
                (fun value -> source.Settle value),
                (fun error -> source.Fail error)
            ))

        source

    /// <summary>
    /// Registers an MVU loop's messages on <c>graph</c>'s journal under the application key <c>key</c>, encoded by
    /// <c>codec</c>, and returns the site <c>recordMessage</c> takes. A replay applies a <c>Message</c> entry by calling
    /// <c>dispatch</c>. With no journal on the graph, the site records nothing.
    /// </summary>
    /// <remarks>The registration belongs to the innermost enclosing scope, as <c>key</c>'s does.</remarks>
    /// <exception cref="T:System.ArgumentException">
    /// <c>key</c> is null or empty, or the graph already has an input registered under <c>key</c>.
    /// </exception>
    let messages (key: string) (codec: JournalCodec<'Msg>) (dispatch: 'Msg -> unit) (graph: Graph) : JournalSite<'Msg> =
        checkKey key
        let recorder = graph.Journal

        if Platform.isNull recorder then
            JournalSite<'Msg>(graph, 0)
        else
            let id = recorder.NextSiteId ()
            bind graph recorder id key (CodecBinding<'Msg>(key, codec, [ JournalEntryKind.Message ], dispatch, ignore))
            JournalSite<'Msg>(graph, id)

    /// <summary>
    /// Records <c>msg</c> as a <c>Message</c> entry of <c>site</c> when it arrives from outside every computation, then
    /// runs <c>apply</c>. The writes and messages made while <c>apply</c> runs are derived from <c>msg</c> and are not
    /// recorded. A message a diagnostic replay drops, as it drops a re-run work's input, is not recorded and does not
    /// run <c>apply</c>.
    /// </summary>
    let recordMessage (site: JournalSite<'Msg>) (msg: 'Msg) (apply: unit -> unit) : unit =
        let graph = site.Graph
        let recorder = graph.Journal

        if Platform.isNull recorder then
            apply ()
        elif graph.RecordInput (JournalEntryKind.Message, site.Id, box msg) then
            recorder.EnterDerived ()

            try
                apply ()
            finally
                recorder.LeaveDerived ()

    /// <summary>
    /// A new async work on <c>graph</c>, for <c>produce</c>, identified by the last entry recorded before it and its
    /// position among the works started after that entry. Captured in the synchronous part of the work, such as an
    /// effect's body before its first <c>await</c>.
    /// </summary>
    /// <exception cref="T:System.InvalidOperationException">
    /// The graph has a journal, and the call runs off the graph's thread.
    /// </exception>
    let work (graph: Graph) : JournalWork =
        let recorder = graph.Journal

        if Platform.isNull recorder then
            JournalWork (graph, null)
        else
            graph.AssertOnGraphThread "Journal.work"
            JournalWork (graph, recorder.NextWork ())

    /// <summary>
    /// Runs <c>input</c> on the graph's thread as an input of <c>work</c>: its writes, settles, failures and messages
    /// record <c>Produced</c> entries carrying the work's identity. Called off the graph's thread, it is queued, as
    /// <c>Graph.Dispatch</c> queues work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each call that makes an input is one call of <c>work</c>, numbered in the work's <c>Produced</c> origin.
    /// <c>Reenact</c> skips the <c>Produced</c> entries, and the re-run work produces them again. Under
    /// <c>Diagnostic</c>, an input of the call is dropped while the replayed entries hold an unmatched
    /// <c>Produced</c> entry of the same work and call, and <c>input</c> still runs.
    /// </para>
    /// <para>
    /// Inside a computation, a flush or another derived section, <c>input</c>'s inputs are unrecorded, and applied
    /// unless a replayed entry of the same call stands for them.
    /// On .NET, the continuations of an effect's body need no <c>produce</c>: their inputs are attributed to the
    /// effect's run. Under Fable, which has no async context, pass them through <c>produce</c>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code lang="fsharp">
    /// createEffect (fun () ->
    ///     let q = query.Value
    ///     let work = Journal.work graph
    ///
    ///     task {
    ///         let! found = service.Search q
    ///         Journal.produce work (fun () -> results.Settle found)
    ///     }
    ///     |> ignore)
    /// </code>
    /// </example>
    let produce (work: JournalWork) (input: unit -> unit) : unit =
        let graph = work.Graph

        let run () =
            let recorder = graph.Journal

            if
                Platform.isNull recorder
                || Platform.isNull work.Attribution
            then
                input ()
            else
                recorder.RunAttributed (work.Attribution, input)

        if graph.IsOnGraphThread then
            run ()
        else
            graph.Dispatch (fun () -> run ())

    /// <summary>The mode of the replay running on <c>graph</c>, or <c>None</c> outside a replay.</summary>
    /// <remarks>
    /// An effect that calls an external service reads it to skip the call under <c>Diagnostic</c>, which applies the
    /// recorded outcome.
    /// </remarks>
    let replaying (graph: Graph) : ReplayMode option =
        let recorder = graph.Journal

        if Platform.isNull recorder then
            None
        else
            recorder.Replaying

    let private prepare (mode: ReplayMode) (recorder: JournalRecorder) (entry: JournalEntry) : (unit -> unit) option =
        let failWith reason =
            raise (JournalReplayException (entry.Seq, entry.Key, entry.PayloadType, reason))

        match entry.Kind, entry.Origin with
        | JournalEntryKind.Flush, _ -> None
        | _, JournalOrigin.Produced _ when mode = ReplayMode.Reenact -> None
        | kind, _ ->
            match entry.Payload with
            | JournalPayload.NonReplayable payloadType ->
                failWith (
                    "the entry is non-replayable: no codec encoded its "
                    + payloadType
                    + " value"
                )
            | JournalPayload.Empty -> failWith "the entry carries no payload"
            | JournalPayload.Encoded (codec, version, data) ->
                let key =
                    match entry.Key with
                    | Some key -> key
                    | None -> failWith "the entry has no key"

                let binding = recorder.BindingOfKey key

                if Platform.isNull (box binding) then
                    failWith "the key is not registered on the target graph"

                if not (binding.Accepts kind) then
                    failWith (
                        "the input registered under the key takes no "
                        + string kind
                        + " entries"
                    )

                let expected, expectedVersion =
                    if kind = JournalEntryKind.Fail then
                        "exn", 1
                    else
                        binding.Codec, binding.Version

                if codec <> expected || version <> expectedVersion then
                    failWith (
                        "the entry was encoded by codec "
                        + codec
                        + " version "
                        + string version
                        + ", and the key's codec is "
                        + expected
                        + " version "
                        + string expectedVersion
                    )

                match binding.Prepare (kind, data) with
                | Ok run ->
                    let attribution = JournalAttribution (entry.Origin, entry.Seq, null)
                    Some (fun () -> recorder.RunReplayed (attribution, run))
                | Error error -> failWith ("decode error: " + error)

    /// <summary>
    /// Applies <c>entries</c> to <c>graph</c> in <c>Seq</c> order: the entries between two <c>Flush</c> entries in one
    /// batch, and a flush at each <c>Flush</c> entry, so the replay schedules effects as the recorded run did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A segment of one entry runs unbatched, as an unbatched write in the recorded run did, so a traced replay of a
    /// run whose batches each end in a flush records the recorded run's trace events.
    /// </para>
    /// <para>
    /// The keys are resolved on <c>graph</c>'s journal, so the target graph needs a journal and the inputs registered
    /// under the recorded keys. The replay's own inputs are recorded on that journal.
    /// </para>
    /// <para>
    /// <c>Diagnostic</c> applies every entry, and settles and fails async sources with the recorded outcomes; a failure
    /// arrives as a <c>JournalReplayedFailure</c> with the recorded message. The application's async work runs again,
    /// and each of its inputs is dropped while the replayed entries hold an unmatched <c>Produced</c> entry of the same
    /// work and call, during the replay or after it returns; its other inputs, such as a synchronous dispatch the
    /// recorded run left unrecorded, are live. A work started during the replay is identified by the replayed entry
    /// applied before it and its position after that entry, whatever the target ran before the replay. Setup work,
    /// started before the target's first entry, is matched by its position from construction when the replay is the
    /// target's first input; other work started outside the replay is live. <c>Reenact</c> applies the <c>External</c> entries and skips the
    /// <c>Produced</c> entries, and the re-run work produces its inputs. In both modes each recorded input reaches the
    /// graph once when every input of the re-run work is attributed: on .NET, the inputs of an <c>Mvu</c> command, of a
    /// continuation of an effect's body, and those passed to <c>produce</c>; under Fable, the inputs of an <c>Mvu</c>
    /// command and those passed to <c>produce</c>. An unattributed input of re-run work is recorded as
    /// <c>External</c> and is applied twice. An applied entry records an entry of the same origin. <c>replaying</c>
    /// returns the mode while the replay runs.
    /// </para>
    /// <para>
    /// The entries of a batch are checked and decoded before the batch runs, so a failing entry leaves the preceding
    /// batches applied and its own batch unapplied.
    /// </para>
    /// </remarks>
    /// <exception cref="T:System.InvalidOperationException">
    /// <c>graph</c> has no journal, or the call runs off the graph's thread.
    /// </exception>
    /// <exception cref="T:Ranvier.JournalReplayException">
    /// An entry is non-replayable, its key is not registered on <c>graph</c>, its codec name or version differs from
    /// the key's codec, or its payload fails to decode.
    /// </exception>
    let replay (mode: ReplayMode) (graph: Graph) (entries: JournalEntry seq) : unit =
        let recorder = graph.Journal

        if Platform.isNull recorder then
            raise (InvalidOperationException "A journal replay requires a graph with a journal: the recorded keys are resolved on it.")

        graph.AssertOnGraphThread "A journal replay"

        let sorted =
            entries
            |> Seq.sortBy (fun entry -> entry.Seq)
            |> Seq.toArray

        let produced (entry: JournalEntry) =
            match entry.Origin with
            | JournalOrigin.Produced (started, index, call) when entry.Kind <> JournalEntryKind.Flush -> ValueSome (started, index, call)
            | _ -> ValueNone

        let previous = recorder.Replaying
        let previousSession = recorder.BeginReplay mode
        recorder.Replaying <- Some mode

        if mode = ReplayMode.Diagnostic then
            for entry in sorted do
                match produced entry with
                | ValueSome work -> recorder.Expect work
                | ValueNone -> ()
        // The entries before this index are applied.
        let applied = ref 0

        try
            try
                let pending = List<unit -> unit>()

                let applyPending (upTo: int) =
                    if pending.Count = 1 then
                        let run = pending[0]
                        pending.Clear ()
                        run ()
                    elif pending.Count > 1 then
                        let runs = pending.ToArray ()
                        pending.Clear ()

                        graph.RunBatch (fun () ->
                            for run in runs do
                                run ())

                    applied.Value <- upTo

                for index in 0 .. sorted.Length - 1 do
                    let entry = sorted[index]

                    if entry.Kind = JournalEntryKind.Flush then
                        applyPending index

                        // A segment that queued effects has already flushed.
                        if recorder.Unflushed then
                            graph.Flush ()

                        applied.Value <- index + 1
                    else
                        match prepare mode recorder entry with
                        | Some run -> pending.Add run
                        | None -> ()

                applyPending sorted.Length
            with _ ->
                if mode = ReplayMode.Diagnostic then
                    for index in applied.Value .. sorted.Length - 1 do
                        match produced sorted[index] with
                        | ValueSome work -> recorder.Unexpect work
                        | ValueNone -> ()

                reraise ()
        finally
            recorder.Replaying <- previous
            recorder.EndReplay previousSession
