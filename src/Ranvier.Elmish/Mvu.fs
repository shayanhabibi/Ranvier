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
/// Selectors belong to the scope current when <c>Select</c> is called, not to a call position.
/// </para>
/// </remarks>
[<Sealed>]
type Mvu<'Model, 'Msg>
    internal
    (graph: Graph, init: 'Model, update: 'Msg -> 'Model -> 'Model, step: 'Msg -> 'Model -> 'Model * (('Msg -> unit) -> unit) list, commanded: bool) =
    let root = Signal<'Model>(graph, init)

    member private this.Apply(msg: 'Msg) =
        if not commanded then
            root.Value <- update msg root.Peek
        else
            let model, commands = step msg root.Peek
            root.Value <- model

            for command in commands do
                command this.Dispatch

    /// <summary>A tracked read of the model.</summary>
    member _.Model = root.Value

    /// <summary>
    /// Applies <c>update msg</c> to the model and writes the result, then runs the commands it returned, in order, with
    /// <c>Dispatch</c> as their argument.
    /// </summary>
    /// <remarks>
    /// <c>update</c> and the commands run untracked. A call on the graph's thread runs inline, and a dispatch from an effect
    /// joins the running flush. A call from another thread is queued, as <c>Graph.Dispatch</c> queues work. Under
    /// <c>Serialised</c>, a call from outside the graph is queued and waits for the next drain; with no
    /// <c>SynchronizationContext</c> at construction, that drain is <c>Graph.Pump</c>.
    /// </remarks>
    member this.Dispatch(msg: 'Msg) : unit =
        //FOR-REVIEW Routes off-thread calls through the inbox (one thread-id test per dispatch) and allocates a closure per
        //FOR-REVIEW dispatch to run update untracked. In core the closure was allocated only inside a computation; outside
        //FOR-REVIEW Ranvier the test for a running computation is internal, so the bridge calls Graph.Untrack on every
        //FOR-REVIEW on-thread dispatch. Graph.Untrack on the graph's thread takes no hold, so writes still flush as before.
        if graph.IsOnGraphThread then
            graph.Untrack (fun () -> this.Apply msg)
        else
            graph.Dispatch (fun () -> this.Apply msg)

    /// <summary>A memo holding <c>select model</c>, owned by the current scope.</summary>
    /// <remarks>
    /// The memo re-runs <c>select</c> on each model write while it is observed, and wakes its readers only when the
    /// result changes under the graph's equality policy.
    /// </remarks>
    /// <exception cref="T:System.InvalidOperationException">Called inside a pure body, such as a <c>createMemo</c> body.</exception>
    member _.Select(select: 'Model -> 'A) : Memo<'A> =
#if FABLE_COMPILER
        //FOR-REVIEW Fable has no public Memo constructor, so the selector memo belongs to Graph.Current, which is the
        //FOR-REVIEW bridge's graph unless Select is called while another graph is current.
        createMemo (fun _ -> select root.Value)
#else
        new Memo<'A> (graph, Func<'A voption, 'A>(fun _ -> select root.Value))
#endif

/// <summary>Constructors for <c>Mvu</c> over the current graph.</summary>
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Mvu =
    /// <summary>A model holding <c>init</c>, updated by <c>update msg model</c> on each dispatch.</summary>
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
    /// A model from <c>init</c> whose <c>update</c> also returns commands. Each command receives <c>Dispatch</c>, after the
    /// model write it came with.
    /// </summary>
    /// <remarks>
    /// A command list has the shape of Elmish's <c>Cmd</c>, so Elmish commands pass unchanged. The initial commands run
    /// before <c>withCmd</c> returns.
    /// </remarks>
    let withCmd
        (init: 'Model * (('Msg -> unit) -> unit) list)
        (update: 'Msg -> 'Model -> 'Model * (('Msg -> unit) -> unit) list)
        : Mvu<'Model, 'Msg> =
        let model, commands = init

        let mvu =
            Mvu<'Model, 'Msg>(Graph.Current, model, Unchecked.defaultof<_>, update, true)

        for command in commands do
            command mvu.Dispatch

        mvu
