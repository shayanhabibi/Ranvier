namespace Ranvier.Elmish

open System
open Ranvier

/// <summary>
/// An Elmish-style model held in a signal: <c>Dispatch</c> applies <c>update</c>, and <c>Select</c> reads a part of the
/// model through a memo that wakes its readers only when that part changes.
/// </summary>
/// <remarks>
/// <para>
/// A dispatch writes the model as a signal write does: readers wake only when the new model differs from the current one
/// under the graph's equality policy, so an <c>update</c> that returns its argument wakes nothing. A write re-runs every
/// observed selector over the model; a selector over another selector's memo re-runs only when that memo changes.
/// </para>
/// <para>
/// An <c>Mvu</c> belongs to the scope current at construction. Once the scope is torn down, at its disposal or before
/// the re-run of the effect or owning memo whose body created the <c>Mvu</c>, <c>Dispatch</c> drops every message
/// unapplied, and the commands of an earlier dispatch that have not yet run are skipped. An <c>Mvu</c> constructed in the
/// graph's root scope stays live for the graph's lifetime.
/// </para>
/// <para>
/// Selectors belong to the scope current when <c>Select</c> is called, not to a call position.
/// </para>
/// </remarks>
[<Sealed>]
type Mvu<'Model, 'Msg>
    internal
    (graph: Graph, init: 'Model, update: 'Msg -> 'Model -> 'Model, step: 'Msg -> 'Model -> 'Model * (('Msg -> unit) -> unit) list, commanded: bool) =
    let root = Signal<'Model>(graph, init)
    let mutable disposed = false
    let mutable site: JournalSite<'Msg> = null

    do
        if not (obj.ReferenceEquals (getOwner (), graph.Root)) then
            onCleanup (fun () -> disposed <- true)

    member private this.Apply(msg: 'Msg) =
        if not disposed then
            if not commanded then
                root.Value <- update msg root.Peek
            else
                let model, commands = step msg root.Peek
                root.Value <- model
                this.Run commands

    /// <summary>
    /// Runs <c>commands</c> in order with a dispatch of their shared <c>JournalWork</c>, stopping once the owning scope
    /// is torn down.
    /// </summary>
    member internal this.Run(commands: (('Msg -> unit) -> unit) list) =
        if not commands.IsEmpty then
            let work = Journal.work graph

            let dispatch (msg: 'Msg) =
                Journal.produce work (fun () -> this.Arrive msg)

            let mutable rest = commands

            while not disposed && not rest.IsEmpty do
                rest.Head dispatch
                rest <- rest.Tail

    /// <summary>A tracked read of the model.</summary>
    member _.Model = root.Value

    /// <summary>
    /// Applies <c>update msg</c> to the model and writes the result, then runs the commands it returned, in order. Each
    /// command receives a dispatch that delivers as <c>Dispatch</c> does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>update</c> and the commands run untracked. A call on the graph's thread runs inline, and a dispatch from an effect
    /// joins the running flush. A call from another thread is queued, as <c>Graph.Dispatch</c> queues work. Under
    /// <c>Serialised</c>, a call from outside the graph is queued and waits for the next drain; with no
    /// <c>SynchronizationContext</c> at construction, that drain is <c>Graph.Pump</c>.
    /// </para>
    /// <para>
    /// Once the scope current at construction is torn down, a call drops its message, as does a queued call that drains
    /// after the teardown.
    /// </para>
    /// </remarks>
    member this.Dispatch(msg: 'Msg) : unit =
        if graph.IsOnGraphThread then
            this.Arrive msg
        else
            graph.Dispatch (fun () -> this.Arrive msg)

    member private this.Arrive(msg: 'Msg) =
        if isNull site then
            graph.Untrack (fun () -> this.Apply msg)
        else
            Journal.recordMessage site msg (fun () -> graph.Untrack (fun () -> this.Apply msg))

    /// <summary>Registers the messages on the graph's journal under <c>key</c>.</summary>
    member internal this.Journal(key: string, codec: JournalCodec<'Msg>) =
        site <- Journal.messages key codec this.Dispatch graph

    /// <summary>A memo holding <c>select model</c>, owned by the current scope.</summary>
    /// <remarks>
    /// The memo re-runs <c>select</c> on each model write while it is observed, and wakes its readers only when the
    /// result changes under the graph's equality policy.
    /// </remarks>
    /// <exception cref="T:System.InvalidOperationException">Called inside a pure body, such as a <c>createMemo</c> body.</exception>
    member _.Select(select: 'Model -> 'A) : Memo<'A> =
#if FABLE_COMPILER
        createMemo (fun _ -> select root.Value)
#else
        new Memo<'A> (graph, Func<'A voption, 'A>(fun _ -> select root.Value))
#endif

/// <summary>Constructors for <c>Mvu</c> over the current graph.</summary>
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Mvu =
    /// <summary>A model holding <c>init</c>, updated by <c>update msg model</c> on each dispatch.</summary>
    /// <remarks>
    /// The model belongs to the current scope: once the scope is torn down, a dispatch drops its message. In the graph's
    /// root scope the model stays live for the graph's lifetime.
    /// </remarks>
    /// <exception cref="T:System.InvalidOperationException">Called inside a pure body, such as a <c>createMemo</c> body.</exception>
    /// <example>
    /// <code lang="fsharp">
    /// let app = Mvu.create { Count = 0 } (fun msg model -> match msg with Increment -> { model with Count = model.Count + 1 })
    /// let count = app.Select _.Count
    /// app.Dispatch Increment
    /// </code>
    /// </example>
    let create (init: 'Model) (update: 'Msg -> 'Model -> 'Model) : Mvu<'Model, 'Msg> =
        Mvu<'Model, 'Msg>(Graph.Current, init, update, Unchecked.defaultof<_>, false)

    /// <summary>
    /// A model from <c>init</c> whose <c>update</c> also returns commands. Each command runs after the model write it came
    /// with, and receives a dispatch that delivers as <c>Dispatch</c> does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A command list has the shape of Elmish's <c>Cmd</c>, so Elmish commands pass unchanged. The initial commands run
    /// before <c>withCmd</c> returns.
    /// </para>
    /// <para>
    /// The model belongs to the current scope: once the scope is torn down, a dispatch drops its message, including a
    /// dispatch from a command, and a command list stops at the teardown. In the graph's root
    /// scope the model stays live for the graph's lifetime.
    /// </para>
    /// </remarks>
    /// <exception cref="T:System.InvalidOperationException">Called inside a pure body, such as a <c>createMemo</c> body.</exception>
    let withCmd
        (init: 'Model * (('Msg -> unit) -> unit) list)
        (update: 'Msg -> 'Model -> 'Model * (('Msg -> unit) -> unit) list)
        : Mvu<'Model, 'Msg> =
        let model, commands = init

        let mvu =
            Mvu<'Model, 'Msg>(Graph.Current, model, Unchecked.defaultof<_>, update, true)

        mvu.Run commands
        mvu

    /// <summary>
    /// Registers <c>mvu</c>'s messages on its graph's journal under the application key <c>key</c>, encoded by
    /// <c>codec</c>, and returns <c>mvu</c>. Does nothing when the graph has no journal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A message dispatched from outside every computation records a <c>Message</c> entry. The model write and the
    /// synchronous commands are derived from the message and record nothing. A message a command dispatches later, after
    /// an <c>await</c>, records a <c>Produced</c> entry: <c>Reenact</c> skips it and the re-run command dispatches it
    /// again, and under <c>Diagnostic</c> the entry is applied and the re-run command's matching dispatch is dropped. A message
    /// dispatched by an effect or other computation records nothing.
    /// </para>
    /// <para>
    /// <c>Journal.replay</c> applies a <c>Message</c> entry by dispatching the decoded message. The registration belongs
    /// to the current scope, as <c>Journal.key</c>'s does.
    /// </para>
    /// </remarks>
    /// <exception cref="T:System.ArgumentException">
    /// <c>key</c> is null or empty, or the graph already has an input registered under <c>key</c>.
    /// </exception>
    let journal (key: string) (codec: JournalCodec<'Msg>) (mvu: Mvu<'Model, 'Msg>) : Mvu<'Model, 'Msg> =
        mvu.Journal (key, codec)
        mvu
