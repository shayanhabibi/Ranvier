namespace Ranvier.CSharp

open System
open System.Collections
open System.Collections.Generic
open System.ComponentModel
open System.Runtime.ExceptionServices
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open System.Windows.Input
open Ranvier

/// <summary>
/// The handlers of one event, each paired with the <c>SynchronizationContext</c> current when it subscribed.
/// </summary>
/// <remarks>Safe to subscribe and unsubscribe from any thread.</remarks>
[<Sealed>]
type internal ContextHandlers<'H when 'H: null and 'H :> Delegate>() =
    let gate = obj ()
    let mutable items: struct ('H * SynchronizationContext)[] = [||]

    member _.Add(handler: 'H) =
        if not (isNull handler) then
            let entry = struct (handler, SynchronizationContext.Current)
            lock gate (fun () -> items <- Array.append items [| entry |])

    /// <summary>Removes the latest subscription of <c>handler</c>.</summary>
    member _.Remove(handler: 'H) =
        if not (isNull handler) then
            lock gate (fun () ->
                let index =
                    Array.FindLastIndex (items, (fun (struct (h, _)) -> obj.Equals (h, handler)))

                if index >= 0 then
                    items <- Array.removeAt index items)

    member _.Clear() =
        lock gate (fun () -> items <- [||])

    /// <summary>
    /// Invokes each handler inline when it captured no context or the current context is the captured one, and posts it
    /// to the captured context otherwise.
    /// </summary>
    /// <remarks>
    /// An exception from an inline handler is added to <c>errors</c>, and the remaining handlers still run. A posted
    /// invocation runs only while <c>live ()</c> is true.
    /// </remarks>
    member _.Raise(invoke: 'H -> unit, live: unit -> bool, errors: ResizeArray<exn>) =
        let current = SynchronizationContext.Current

        for struct (handler, context) in items do
            if
                isNull context
                || obj.ReferenceEquals (context, current)
            then
                try
                    invoke handler
                with ex ->
                    errors.Add ex
            else
                context.Post (
                    (fun _ ->
                        if live () then
                            invoke handler),
                    null
                )

/// <summary>A bound property's state as last notified.</summary>
[<Sealed; AllowNullLiteral>]
type internal BoundState<'T>(value: 'T, hasValue: bool, loading: bool, error: exn) =
    member _.Value = value
    member _.HasValue = hasValue
    member _.Loading = loading
    member _.Error = error

    /// <summary>The state after <c>reading</c>. <c>Pending</c> and <c>Failed</c> keep the last settled value.</summary>
    member this.Next(reading: Reading<'T>) =
        match reading with
        | Ready v -> BoundState (v, true, false, null)
        | Pending -> BoundState (value, hasValue, true, null)
        | Failed ex -> BoundState (value, hasValue, false, ex)

/// <summary>The notifications of one pass over the bound properties.</summary>
type internal Notifications =
    {
        Changed: ResizeArray<PropertyChangedEventArgs>
        ErrorsChanged: ResizeArray<DataErrorsChangedEventArgs>
    }

/// <summary>A property registered with a <c>ReactiveBindings</c>.</summary>
type internal IBoundSlot =
    abstract Name: string
    abstract Loading: bool
    abstract Error: exn

    /// <summary>
    /// Reads the node, tracked, stores the new state, and adds the notifications the change requires.
    /// </summary>
    abstract Refresh: Notifications -> unit

/// <summary>
/// A read-only view-model property backed by a memo. <c>Value</c> is the last settled value; <c>IsLoading</c> and
/// <c>Error</c> describe the current reading.
/// </summary>
/// <remarks>
/// <c>Value</c> is <c>default</c> before the first settled value. <c>IsLoading</c> and <c>Error</c> are safe to read from
/// any thread.
/// </remarks>
[<Sealed>]
type BoundValue<'T> internal (graph: Graph, owner: Owner, name: string, loadingName: string, memo: Memo<'T>) =
    let equal = graph.Options.Equality.Comparer<'T>()
    let changedArgs = PropertyChangedEventArgs name
    let errorsArgs = DataErrorsChangedEventArgs name

    let loadingArgs =
        if String.IsNullOrEmpty loadingName then
            null
        else
            PropertyChangedEventArgs loadingName

    [<VolatileField>]
    let mutable state =
        BoundState(Unchecked.defaultof<'T>, false, false, null).Next(graph.Untrack (Func<_>(fun () -> memo.TryValue)))

    /// <summary>The property name raised with <c>PropertyChanged</c> and <c>ErrorsChanged</c>.</summary>
    member _.Name = name

    /// <summary>The memo behind the property, disposed with the bindings.</summary>
    member _.Memo = memo

    /// <summary>
    /// The memo's value when it is ready, otherwise the last settled value. On the graph's thread the read is tracked.
    /// </summary>
    /// <remarks>
    /// Off the graph's thread, or after disposal, <c>Value</c> is the value last notified. A <c>Computed</c> body that
    /// reads it tracks the memo, and sees the last settled value in place of a pending or failed reading; reading
    /// <c>Memo.Value</c> propagates the pending or failed reading instead.
    /// </remarks>
    member _.Value: 'T =
        if graph.IsOnGraphThread && not owner.IsDisposed then
            match memo.TryValue with
            | Ready v -> v
            | _ -> state.Value
        else
            state.Value

    /// <summary>True while the memo is pending.</summary>
    member _.IsLoading = state.Loading

    /// <summary>The memo's error while it is failed, otherwise null.</summary>
    member _.Error: exn = state.Error

    interface IBoundSlot with
        member _.Name = name
        member _.Loading = state.Loading
        member _.Error = state.Error

        member _.Refresh(notes) =
            let old = state
            let next = old.Next memo.TryValue
            state <- next

            if
                next.HasValue
                && (not old.HasValue
                    || not (equal.Equals (old.Value, next.Value)))
            then
                notes.Changed.Add changedArgs

            if
                next.Loading <> old.Loading
                && not (isNull loadingArgs)
            then
                notes.Changed.Add loadingArgs

            if not (obj.ReferenceEquals (next.Error, old.Error)) then
                notes.ErrorsChanged.Add errorsArgs

/// <summary>A two-way view-model property backed by a signal.</summary>
[<Sealed>]
type BoundSignal<'T> internal (graph: Graph, owner: Owner, name: string, signal: Signal<'T>) =
    let equal = graph.Options.Equality.Comparer<'T>()
    let changedArgs = PropertyChangedEventArgs name

    [<VolatileField>]
    let mutable state = BoundState (signal.Peek, true, false, null)

    /// <summary>The property name raised with <c>PropertyChanged</c>.</summary>
    member _.Name = name

    member _.Signal = signal

    /// <summary>
    /// Reads the signal, tracked, on the graph's thread, and the value last notified elsewhere. Setting it writes the
    /// signal through <c>Graph.Dispatch</c>: inline on the graph's thread, marshalled from any other.
    /// </summary>
    member _.Value
        with get (): 'T =
            if graph.IsOnGraphThread && not owner.IsDisposed then
                signal.Value
            else
                state.Value
        and set (v: 'T) = graph.Dispatch (Action (fun () -> signal.Value <- v))

    interface IBoundSlot with
        member _.Name = name
        member _.Loading = false
        member _.Error = null

        member _.Refresh(notes) =
            let old = state
            let next = BoundState (signal.Value, true, false, null)
            state <- next

            if not (equal.Equals (old.Value, next.Value)) then
                notes.Changed.Add changedArgs

/// <summary>What a <c>ReactiveCommand</c> does when an execution is requested while another runs.</summary>
type CommandPolicy =
    /// <summary>The command is disabled while an execution runs, and a request made meanwhile is ignored.</summary>
    | Disable = 0
    /// <summary>The request cancels the token of every execution in flight, then starts a new execution.</summary>
    | CancelPrevious = 1

/// <summary>
/// An <c>ICommand</c> whose <c>CanExecute</c> is derived from graph nodes and whose busy state is observable. Create one
/// with <c>ReactiveBindings.Command</c> or <c>Reactive.Command</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>CanExecute</c> is true while <c>Enabled</c> is ready and true: false while the predicate reads a pending or failed
/// node, and, under <c>CommandPolicy.Disable</c>, false while an execution runs. <c>CanExecuteChanged</c> and
/// <c>PropertyChanged</c> (<c>"CanRun"</c>, <c>"IsRunning"</c>, <c>"Error"</c>) are raised on the graph's thread, or
/// posted to the <c>SynchronizationContext</c> current when the handler subscribed.
/// </para>
/// <para>
/// The predicate is first evaluated at the first <c>CanExecute</c> call, event subscription or execution, so it can
/// read commands created after this one.
/// </para>
/// <para>
/// An execution runs the body untracked on the graph's thread, with a token that <c>Cancel</c>, <c>Dispose</c> and
/// <c>CommandPolicy.CancelPrevious</c> cancel. Its failure is stored in <c>Error</c>; an
/// <c>OperationCanceledException</c> after its token is cancelled clears <c>Error</c> instead. <c>ICommand.Execute</c>
/// discards the execution's task, so a failure reaches only <c>Error</c>.
/// </para>
/// </remarks>
/// <example>
/// <code lang="csharp">
/// save = bindings.Command((_, token) =&gt; repo.SaveAsync(draft.Value, token), () =&gt; IsValid &amp;&amp; !load.IsRunning);
/// load = bindings.Command((_, token) =&gt; repo.LoadAsync(token), () =&gt; !save.IsRunning);
/// busy = bindings.Computed("IsBusy", () =&gt; save.IsRunning || load.IsRunning);
/// </code>
/// </example>
[<Sealed>]
type ReactiveCommand
    internal (graph: Graph, execute: obj -> CancellationToken -> Task, canExecute: Func<bool>, policy: CommandPolicy, sync: bool, standalone: bool) as self
    =
    let changedHandlers = ContextHandlers<PropertyChangedEventHandler>()
    let canExecuteHandlers = ContextHandlers<EventHandler>()
    let canRunArgs = PropertyChangedEventArgs "CanRun"
    let isRunningArgs = PropertyChangedEventArgs "IsRunning"
    let errorArgs = PropertyChangedEventArgs "Error"

    /// <summary>The token sources of the executions in flight. Graph thread only.</summary>
    let flights = ResizeArray<CancellationTokenSource>()

    /// <summary>The token source of the latest execution; only that execution writes <c>error</c>.</summary>
    let mutable latest: CancellationTokenSource = null

    let mutable detach: unit -> unit = ignore

    [<VolatileField>]
    let mutable canRun = false

    [<VolatileField>]
    let mutable isRunning = false

    [<VolatileField>]
    let mutable lastError: exn = null

    [<VolatileField>]
    let mutable armRequested = false

    [<VolatileField>]
    let mutable disposed = false

    (*FOR-REVIEW `armed` is one signal per command beyond the note's three nodes. It keeps the notify pass from
      evaluating `Enabled` before the view model's constructor has assigned every command the predicate reads (the
      reviewer's sample hazard). The alternative, arming through the bindings' `version` signal, re-runs every slot
      of the bindings on each arm. *)
    let struct (owner, armed, running, error, enabled) =
        graph.CreateRoot (
            Func<Owner, _>(fun owner ->
                let armed = Signal<bool>(graph, false)
                let running = Signal<int>(graph, 0)
                let error = Signal<exn>(graph, null)

                let enabled =
                    new Memo<bool> (
                        graph,
                        Func<bool voption, bool>(fun _ ->
                            (isNull canExecute || canExecute.Invoke ())
                            && (policy <> CommandPolicy.Disable
                                || running.Value = 0))
                    )

                struct (owner, armed, running, error, enabled))
        )

    let readEnabled () =
        match enabled.TryValue with
        | Ready v -> v
        | _ -> false

    let live () =
        not disposed

    let arm () =
        if not armRequested && not disposed then
            armRequested <- true

            graph.Dispatch (
                Action (fun () ->
                    if not disposed then
                        armed.Value <- true)
            )

    (*FOR-REVIEW A callback registered on an execution's token that throws makes `Cancel` throw an AggregateException.
      It is dropped here, so `ExecuteAsync`, `Cancel` and `Dispose` never fail because of a user's registration. The
      alternative is to surface it as the command's `Error`. *)
    let cancelAll () =
        for cts in flights.ToArray () do
            try
                cts.Cancel ()
            with _ ->
                ()

    let release () =
        disposed <- true
        changedHandlers.Clear ()
        canExecuteHandlers.Clear ()
        detach ()

    do
        owner.OnCleanup (
            Action (fun () ->
                release ()
                cancelAll ())
        )

    /// <summary>The failure recorded for an execution of <c>task</c> under <c>cts</c>, or null.</summary>
    let failureOf (task: Task) (cts: CancellationTokenSource) : exn =
        let failure =
            if task.IsFaulted then
                let inner = task.Exception.InnerExceptions

                if inner.Count = 1 then inner[0] else task.Exception :> exn
            elif task.IsCanceled then
                try
                    task.GetAwaiter().GetResult()
                    null
                with ex ->
                    ex
            else
                null

        match failure with
        | :? OperationCanceledException when cts.IsCancellationRequested -> null
        | _ -> failure

    /// <summary>Applies the outcome of <c>task</c>, the execution under <c>cts</c>. Graph thread only.</summary>
    let finish (cts: CancellationTokenSource) (completion: TaskCompletionSource<obj>) (task: Task) =
        flights.Remove cts |> ignore

        if not disposed then
            let failure = failureOf task cts

            graph.Batch (
                Func<unit>(fun () ->
                    running.Value <- running.Peek - 1

                    if obj.ReferenceEquals (latest, cts) then
                        error.Value <- failure)
            )
        else
            // Observes the exception, so it reaches no UnobservedTaskException handler.
            task.Exception |> ignore

        if obj.ReferenceEquals (latest, cts) then
            latest <- null

        cts.Dispose ()

        if not (isNull completion) then
            if task.IsFaulted then
                completion.TrySetException task.Exception.InnerExceptions
                |> ignore
            elif task.IsCanceled then
                completion.TrySetCanceled () |> ignore
            else
                completion.TrySetResult null |> ignore

    /// <summary>Starts an execution when the command is enabled. Graph thread only.</summary>
    let launch (parameter: obj) (completion: TaskCompletionSource<obj>) =
        if policy = CommandPolicy.CancelPrevious then
            cancelAll ()

        let cts = new CancellationTokenSource ()
        flights.Add cts
        latest <- cts
        running.Value <- running.Peek + 1

        let task =
            try
                match graph.Untrack (Func<Task>(fun () -> execute parameter cts.Token)) with
                | null -> Task.CompletedTask
                | task -> task
            with ex ->
                Task.FromException ex

        if task.IsCompleted then
            finish cts completion task
        else
            task.ContinueWith (
                Action<Task>(fun t -> graph.Dispatch (Action (fun () -> finish cts completion t))),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            )
            |> ignore

    let start (parameter: obj) (completion: TaskCompletionSource<obj>) =
        try
            arm ()

            if
                disposed
                || not (graph.Untrack (Func<bool>(readEnabled)))
            then
                if not (isNull completion) then
                    completion.TrySetResult null |> ignore
            elif sync then
                graph.Batch (Func<unit>(fun () -> launch parameter completion))
            else
                launch parameter completion
        with ex ->
            if not (isNull completion) then
                completion.TrySetException ex |> ignore

    /// <summary>
    /// Reads the command's nodes, tracked, stores the new state, and returns the changed properties as flags. Graph
    /// thread only.
    /// </summary>
    let refresh () =
        if disposed || not armed.Value then
            0
        else
            let nextCanRun = readEnabled ()
            let nextRunning = running.Value > 0
            let nextError = error.Value
            let mutable changes = 0

            if nextCanRun <> canRun then
                canRun <- nextCanRun
                changes <- changes ||| 1

            if nextRunning <> isRunning then
                isRunning <- nextRunning
                changes <- changes ||| 2

            if not (obj.ReferenceEquals (nextError, lastError)) then
                lastError <- nextError
                changes <- changes ||| 4

            changes

    let raiseChanges (changes: int) (errors: ResizeArray<exn>) =
        let from = box self

        if changes &&& 1 <> 0 then
            canExecuteHandlers.Raise ((fun h -> h.Invoke (from, EventArgs.Empty)), live, errors)
            changedHandlers.Raise ((fun h -> h.Invoke (from, canRunArgs)), live, errors)

        if changes &&& 2 <> 0 then
            changedHandlers.Raise ((fun h -> h.Invoke (from, isRunningArgs)), live, errors)

        if changes &&& 4 <> 0 then
            changedHandlers.Raise ((fun h -> h.Invoke (from, errorArgs)), live, errors)

    let notify () =
        let changes = refresh ()

        if changes <> 0 then
            graph.Untrack (
                Func<unit>(fun () ->
                    let errors = ResizeArray<exn>()
                    raiseChanges changes errors

                    if errors.Count > 0 then
                        ExceptionDispatchInfo.Capture(errors[0]).Throw())
            )

    do
        if standalone then
            Api.runWithOwner owner (fun () -> new Effect (graph, Action notify))
            |> ignore

    /// <summary>An execution body running <c>execute</c> and returning a completed task.</summary>
    static member internal Synchronous(execute: Action<obj>) : obj -> CancellationToken -> Task =
        fun parameter _ ->
            execute.Invoke parameter
            Task.CompletedTask

    member internal _.Refresh() =
        refresh ()

    member internal _.RaiseChanges(changes: int, errors: ResizeArray<exn>) =
        raiseChanges changes errors

    /// <summary>Called when the command is disposed, on the disposing thread.</summary>
    member internal _.Detach
        with set (value: unit -> unit) = detach <- value

    member _.Graph = graph

    /// <summary>The root scope of the command's nodes; disposed by <c>Dispose</c>.</summary>
    member _.Owner = owner

    /// <summary>
    /// The derived predicate: <c>canExecute ()</c>, and under <c>CommandPolicy.Disable</c> no execution in flight.
    /// </summary>
    member _.Enabled = enabled

    /// <summary>
    /// <c>Enabled</c> when it is ready, and false while it is pending or failed. On the graph's thread the read is
    /// tracked.
    /// </summary>
    /// <remarks>Off the graph's thread, or after disposal, <c>CanRun</c> is the value last notified.</remarks>
    member _.CanRun: bool =
        if graph.IsOnGraphThread && not disposed then
            readEnabled ()
        else
            canRun

    /// <summary>True while an execution is in flight. On the graph's thread the read is tracked.</summary>
    /// <remarks>Off the graph's thread, or after disposal, <c>IsRunning</c> is the value last notified.</remarks>
    member _.IsRunning: bool =
        if graph.IsOnGraphThread && not disposed then
            running.Value > 0
        else
            isRunning

    /// <summary>
    /// The latest execution's failure, or null after it succeeds or is cancelled. On the graph's thread the read is
    /// tracked.
    /// </summary>
    /// <remarks>Off the graph's thread, or after disposal, <c>Error</c> is the value last notified.</remarks>
    member _.Error: exn =
        if graph.IsOnGraphThread && not disposed then
            error.Value
        else
            lastError

    /// <summary>
    /// Starts an execution with <c>parameter</c> when the command is enabled, through <c>Graph.Dispatch</c>. The task
    /// completes with the execution, after <c>IsRunning</c> and <c>Error</c> are updated.
    /// </summary>
    /// <remarks>
    /// A disabled or disposed command returns a completed task. A failed execution faults the task, and a cancelled one
    /// cancels it.
    /// </remarks>
    member _.ExecuteAsync([<Optional; DefaultParameterValue(null: obj)>] parameter: obj) : Task =
        if disposed then
            Task.CompletedTask
        else
            let completion =
                TaskCompletionSource<obj>(TaskCreationOptions.RunContinuationsAsynchronously)

            graph.Dispatch (Action (fun () -> start parameter completion))
            completion.Task :> Task

    /// <summary>
    /// Starts an execution with <c>parameter</c> as <c>ExecuteAsync</c> does, and discards its task. A failure is stored
    /// in <c>Error</c> only.
    /// </summary>
    member _.Execute([<Optional; DefaultParameterValue(null: obj)>] parameter: obj) : unit =
        if not disposed then
            graph.Dispatch (Action (fun () -> start parameter null))

    /// <summary>Cancels the token of every execution in flight, through <c>Graph.Dispatch</c>.</summary>
    member _.Cancel() =
        graph.Dispatch (Action cancelAll)

    /// <summary>The value of <c>CanRun</c> last notified. Safe to call from any thread.</summary>
    member _.CanExecute(_parameter: obj) : bool =
        arm ()
        canRun

    /// <summary>
    /// Raised when <c>CanExecute</c> changes, on the graph's thread or posted to the context current when the handler
    /// subscribed.
    /// </summary>
    [<CLIEvent>]
    member _.CanExecuteChanged =
        { new IDelegateEvent<EventHandler> with
            member _.AddHandler h =
                canExecuteHandlers.Add h
                arm ()

            member _.RemoveHandler h =
                canExecuteHandlers.Remove h
        }

    /// <summary>
    /// Raised as <c>"CanRun"</c>, <c>"IsRunning"</c> and <c>"Error"</c>, on the graph's thread or posted to the context
    /// current when the handler subscribed.
    /// </summary>
    [<CLIEvent>]
    member _.PropertyChanged =
        { new IDelegateEvent<PropertyChangedEventHandler> with
            member _.AddHandler h =
                changedHandlers.Add h
                arm ()

            member _.RemoveHandler h =
                changedHandlers.Remove h
        }

    /// <summary>
    /// Cancels the executions in flight, disposes the command's nodes and drops every handler. Idempotent; dispatched to
    /// the graph's thread when called from another.
    /// </summary>
    member _.Dispose() =
        if not disposed then
            release ()
            graph.Dispatch (Action owner.Dispose)

    interface ICommand with
        member this.CanExecute(parameter) =
            this.CanExecute parameter

        member this.Execute(parameter) =
            this.Execute parameter

        [<CLIEvent>]
        member this.CanExecuteChanged = this.CanExecuteChanged

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member this.PropertyChanged = this.PropertyChanged

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

/// <summary>
/// <c>INotifyPropertyChanged</c> and <c>INotifyDataErrorInfo</c> over graph nodes, for XAML view models. Each registered
/// property raises <c>PropertyChanged</c> once per settled change, and errors of failed properties surface through
/// <c>GetErrors</c>.
/// </summary>
/// <remarks>
/// <para>
/// Events carry <c>sender</c>, or the bindings when <c>sender</c> is null. Each handler runs on the
/// <c>SynchronizationContext</c> current when it subscribed: inline when it subscribed without one or when the raise
/// already runs on it, posted otherwise. The notifying effect runs on the graph's thread.
/// </para>
/// <para>
/// The bindings own a root scope created under the scope current at construction. <c>Dispose</c>, or disposing that
/// scope, disposes every node the bindings created and drops every handler. Register properties on the graph's thread.
/// </para>
/// </remarks>
/// <example>
/// <code lang="csharp">
/// public sealed class OrderViewModel : ObservableObject, IDisposable
/// {
///     readonly ReactiveBindings bindings;
///     readonly BoundSignal&lt;int&gt; quantity;
///     readonly BoundValue&lt;decimal&gt; total;
///
///     public OrderViewModel(Graph graph, decimal price)
///     {
///         bindings = new ReactiveBindings(this, graph);
///         bindings.PropertyChanged += (_, e) =&gt; OnPropertyChanged(e);
///         quantity = bindings.Writable(nameof(Quantity), 1);
///         total = bindings.Computed(nameof(Total), () =&gt; Quantity * price);
///     }
///
///     public int Quantity { get =&gt; quantity.Value; set =&gt; quantity.Value = value; }
///     public decimal Total =&gt; total.Value;
///     public void Dispose() =&gt; bindings.Dispose();
/// }
/// </code>
/// </example>
[<Sealed>]
type ReactiveBindings(sender: obj, graph: Graph) as self =
    let changedHandlers = ContextHandlers<PropertyChangedEventHandler>()
    let errorsHandlers = ContextHandlers<EventHandler<DataErrorsChangedEventArgs>>()
    let gate = obj ()
    let mutable slots: IBoundSlot[] = [||]
    let mutable commands: ReactiveCommand[] = [||]

    [<VolatileField>]
    let mutable loading = false

    [<VolatileField>]
    let mutable hasErrors = false

    [<VolatileField>]
    let mutable disposed = false

    let loadingArgs = PropertyChangedEventArgs "IsLoading"
    let hasErrorsArgs = PropertyChangedEventArgs "HasErrors"
    let version = Signal<int>(graph, 0)
    let owner = graph.CreateRoot (Func<Owner, Owner> id)

    let source () =
        if isNull sender then box self else sender

    let live () =
        not disposed

    let release () =
        disposed <- true
        changedHandlers.Clear ()
        errorsHandlers.Clear ()

    do owner.OnCleanup (Action release)

    /// <summary>Runs <c>body</c> with <c>graph</c> active and <c>owner</c> as the scope new nodes attach to.</summary>
    let inScope (body: unit -> 'R) : 'R =
        let activation =
            match Graph.TryCurrent with
            | ValueSome current when obj.ReferenceEquals (current, graph) -> null
            | _ -> graph.Activate ()

        try
            Api.runWithOwner owner body
        finally
            if not (isNull activation) then
                activation.Dispose ()

    let aggregate (current: IBoundSlot[]) =
        struct (current |> Array.exists (fun s -> s.Loading),
                current
                |> Array.exists (fun s -> not (isNull s.Error)))

    let raiseAll (notes: Notifications) (commandChanges: ResizeArray<struct (ReactiveCommand * int)>) =
        let errors = ResizeArray<exn>()
        let from = source ()

        for args in notes.Changed do
            changedHandlers.Raise ((fun h -> h.Invoke (from, args)), live, errors)

        for args in notes.ErrorsChanged do
            errorsHandlers.Raise ((fun h -> h.Invoke (from, args)), live, errors)

        if not (isNull commandChanges) then
            for struct (command, changes) in commandChanges do
                command.RaiseChanges (changes, errors)

        if errors.Count > 0 then
            ExceptionDispatchInfo.Capture(errors[0]).Throw()

    let notify () =
        version.Value |> ignore
        let current = slots

        let notes =
            {
                Changed = ResizeArray ()
                ErrorsChanged = ResizeArray ()
            }

        for slot in current do
            slot.Refresh notes

        let struct (nextLoading, nextErrors) = aggregate current

        if nextLoading <> loading then
            loading <- nextLoading
            notes.Changed.Add loadingArgs

        if nextErrors <> hasErrors then
            hasErrors <- nextErrors
            notes.Changed.Add hasErrorsArgs

        let currentCommands = commands
        let mutable commandChanges = null

        if currentCommands.Length > 0 then
            for command in currentCommands do
                let changes = command.Refresh ()

                if changes <> 0 then
                    if isNull commandChanges then
                        commandChanges <- ResizeArray ()

                    commandChanges.Add (struct (command, changes))

        if
            not disposed
            && (notes.Changed.Count > 0
                || notes.ErrorsChanged.Count > 0
                || not (isNull commandChanges))
        then
            graph.Untrack (Func<unit>(fun () -> raiseAll notes commandChanges))

    do
        inScope (fun () -> new Effect (graph, Action notify))
        |> ignore

    let register (slot: IBoundSlot) =
        if disposed then
            raise (ObjectDisposedException (nameof ReactiveBindings))

        lock gate (fun () ->
            if
                slots
                |> Array.exists (fun s -> s.Name = slot.Name)
            then
                raise (ArgumentException ($"A property named '%s{slot.Name}' is already bound.", "name"))

            slots <- Array.append slots [| slot |])

        let struct (nextLoading, nextErrors) = aggregate slots
        loading <- nextLoading
        hasErrors <- nextErrors
        version.Value <- version.Peek + 1

    let registerCommand (command: ReactiveCommand) =
        lock gate (fun () -> commands <- Array.append commands [| command |])

        command.Detach <-
            fun () ->
                lock gate (fun () ->
                    commands <-
                        commands
                        |> Array.filter (fun c -> not (obj.ReferenceEquals (c, command))))

        version.Value <- version.Peek + 1
        command

    let createCommand (execute: obj -> CancellationToken -> Task) (canExecute: Func<bool>) policy sync =
        if disposed then
            raise (ObjectDisposedException (nameof ReactiveBindings))

        inScope (fun () -> new ReactiveCommand (graph, execute, canExecute, policy, sync, false))
        |> registerCommand

    let bindSignal (name: string) (signal: Signal<'T>) =
        let bound = BoundSignal<'T>(graph, owner, name, signal)
        register bound
        bound

    /// <summary>Bindings on <c>Graph.Current</c> whose events carry <c>sender</c>.</summary>
    /// <exception cref="T:System.InvalidOperationException">No graph is active on the calling thread.</exception>
    new(sender: obj) = new ReactiveBindings (sender, Graph.Current)

    /// <summary>Bindings on <c>Graph.Current</c> whose events carry the bindings themselves.</summary>
    /// <exception cref="T:System.InvalidOperationException">No graph is active on the calling thread.</exception>
    new() = new ReactiveBindings (null, Graph.Current)

    member _.Graph = graph

    /// <summary>The root scope of every node the bindings create; disposed by <c>Dispose</c>.</summary>
    member _.Owner = owner

    /// <summary>
    /// A read-only property <c>name</c> holding <c>compute ()</c>. <c>compute</c> re-runs when a value it read changes,
    /// as a memo's body does.
    /// </summary>
    /// <remarks>
    /// <c>compute</c> is pure: creating a node in it fails the property. A pending or failed read in <c>compute</c>
    /// makes the property loading or failed, and it keeps showing its last settled value.
    /// </remarks>
    /// <exception cref="T:System.ArgumentException">A property named <c>name</c> is already bound.</exception>
    /// <exception cref="T:System.ObjectDisposedException">The bindings are disposed.</exception>
    member this.Computed<'T>(name: string, compute: Func<'T>) : BoundValue<'T> =
        this.Computed (name, compute, null)

    /// <summary>
    /// <c>Computed</c>, also raising <c>PropertyChanged</c> for <c>loadingName</c> when the property starts or stops
    /// loading.
    /// </summary>
    /// <exception cref="T:System.ArgumentException">A property named <c>name</c> is already bound.</exception>
    /// <exception cref="T:System.ObjectDisposedException">The bindings are disposed.</exception>
    member _.Computed<'T>(name: string, compute: Func<'T>, loadingName: string) : BoundValue<'T> =
        if disposed then
            raise (ObjectDisposedException (nameof ReactiveBindings))

        let memo =
            inScope (fun () -> new Memo<'T> (graph, Func<'T voption, 'T>(fun _ -> compute.Invoke ())))

        let bound = BoundValue<'T>(graph, owner, name, loadingName, memo)
        register bound
        bound

    /// <summary>A two-way property <c>name</c> over a new signal holding <c>initial</c>.</summary>
    /// <exception cref="T:System.ArgumentException">A property named <c>name</c> is already bound.</exception>
    /// <exception cref="T:System.ObjectDisposedException">The bindings are disposed.</exception>
    member _.Writable<'T>(name: string, initial: 'T) : BoundSignal<'T> =
        bindSignal name (Signal<'T>(graph, initial))

    /// <summary>A two-way property <c>name</c> over <c>signal</c>, which stays usable after the bindings are disposed.</summary>
    /// <exception cref="T:System.ArgumentException">A property named <c>name</c> is already bound.</exception>
    /// <exception cref="T:System.ObjectDisposedException">The bindings are disposed.</exception>
    member _.Writable<'T>(name: string, signal: Signal<'T>) : BoundSignal<'T> =
        bindSignal name signal

    /// <summary>
    /// A command running <c>execute</c>, enabled while <c>canExecute ()</c> is true and <c>policy</c> allows. The
    /// bindings' notifying effect raises its events, and <c>Dispose</c> disposes it.
    /// </summary>
    /// <remarks>
    /// <c>canExecute</c> re-runs when a value it read changes, as a memo's body does; a null <c>canExecute</c> is always
    /// true. <c>execute</c> receives the parameter and a token cancelled by <c>Cancel</c>, <c>Dispose</c> and
    /// <c>CommandPolicy.CancelPrevious</c>.
    /// </remarks>
    /// <exception cref="T:System.ArgumentNullException"><c>execute</c> is null.</exception>
    /// <exception cref="T:System.ObjectDisposedException">The bindings are disposed.</exception>
    member _.Command
        (
            execute: Func<obj, CancellationToken, Task>,
            [<Optional; DefaultParameterValue(null: Func<bool>)>] canExecute: Func<bool>,
            [<Optional; DefaultParameterValue(CommandPolicy.Disable)>] policy: CommandPolicy
        ) : ReactiveCommand =
        if isNull execute then
            nullArg "execute"

        createCommand (fun parameter token -> execute.Invoke (parameter, token)) canExecute policy false

    /// <summary>
    /// A command running <c>execute</c> synchronously, enabled while <c>canExecute ()</c> is true. The writes
    /// <c>execute</c> makes are batched.
    /// </summary>
    /// <exception cref="T:System.ArgumentNullException"><c>execute</c> is null.</exception>
    /// <exception cref="T:System.ObjectDisposedException">The bindings are disposed.</exception>
    member _.Command(execute: Action<obj>, [<Optional; DefaultParameterValue(null: Func<bool>)>] canExecute: Func<bool>) : ReactiveCommand =
        if isNull execute then
            nullArg "execute"

        createCommand (ReactiveCommand.Synchronous execute) canExecute CommandPolicy.Disable true

    /// <summary>
    /// Runs <c>body</c> with the graph active and <c>Owner</c> as its scope, so the nodes it creates are disposed with
    /// the bindings.
    /// </summary>
    member _.Run<'T>(body: Func<'T>) : 'T =
        inScope body.Invoke

    /// <summary>
    /// Runs <c>body</c> with the graph active and <c>Owner</c> as its scope, so the nodes it creates are disposed with
    /// the bindings.
    /// </summary>
    member _.Run(body: Action) : unit =
        inScope body.Invoke

    /// <summary>True while any bound property is loading. Raises <c>PropertyChanged</c> as <c>"IsLoading"</c>.</summary>
    member _.IsLoading = loading

    /// <summary>True while any bound property is failed. Raises <c>PropertyChanged</c> as <c>"HasErrors"</c>.</summary>
    member _.HasErrors = hasErrors

    /// <summary>
    /// The error message of property <c>propertyName</c> while it is failed, or of every failed property for a null or
    /// empty name.
    /// </summary>
    member _.GetErrors(propertyName: string) : IEnumerable =
        let current = slots

        current
        |> Array.filter (fun s ->
            not (isNull s.Error)
            && (String.IsNullOrEmpty propertyName
                || s.Name = propertyName))
        |> Array.map (fun s -> s.Error.Message)
        :> IEnumerable

    /// <summary>Raised on the graph's thread, or posted to the context current when the handler subscribed.</summary>
    [<CLIEvent>]
    member _.PropertyChanged =
        { new IDelegateEvent<PropertyChangedEventHandler> with
            member _.AddHandler h =
                changedHandlers.Add h

            member _.RemoveHandler h =
                changedHandlers.Remove h
        }

    /// <summary>Raised on the graph's thread, or posted to the context current when the handler subscribed.</summary>
    [<CLIEvent>]
    member _.ErrorsChanged =
        { new IDelegateEvent<EventHandler<DataErrorsChangedEventArgs>> with
            member _.AddHandler h =
                errorsHandlers.Add h

            member _.RemoveHandler h =
                errorsHandlers.Remove h
        }

    /// <summary>
    /// Disposes every node the bindings created and drops every handler. Idempotent; dispatched to the graph's thread
    /// when called from another.
    /// </summary>
    /// <remarks>Signals passed to <c>Writable</c> stay usable.</remarks>
    member _.Dispose() =
        if not disposed then
            release ()
            graph.Dispatch (Action owner.Dispose)

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member this.PropertyChanged = this.PropertyChanged

    interface INotifyDataErrorInfo with
        member this.HasErrors = this.HasErrors

        member this.GetErrors(propertyName) =
            this.GetErrors propertyName

        [<CLIEvent>]
        member this.ErrorsChanged = this.ErrorsChanged

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

/// <summary>
/// A view model whose <c>PropertyChanged</c>, <c>ErrorsChanged</c>, <c>HasErrors</c>, <c>GetErrors</c> and
/// <c>Dispose</c> come from <c>Bindings</c>. Register properties with <c>Bindings.Computed</c> and
/// <c>Bindings.Writable</c>.
/// </summary>
/// <example>
/// <code lang="csharp">
/// public sealed class Cart : ReactiveObject
/// {
///     readonly BoundSignal&lt;int&gt; count;
///     readonly BoundValue&lt;int&gt; total;
///
///     public Cart(Graph graph) : base(graph)
///     {
///         count = Bindings.Writable(nameof(Count), 1);
///         total = Bindings.Computed(nameof(Total), () =&gt; Count * 5);
///     }
///
///     public int Count { get =&gt; count.Value; set =&gt; count.Value = value; }
///     public int Total =&gt; total.Value;
/// }
/// </code>
/// </example>
[<AbstractClass>]
type ReactiveObject(graph: Graph) as self =
    let bindings = new ReactiveBindings (self, graph)

    /// <summary>A view model on <c>Graph.Current</c>.</summary>
    /// <exception cref="T:System.InvalidOperationException">No graph is active on the calling thread.</exception>
    new() = new ReactiveObject (Graph.Current)

    member _.Bindings = bindings

    /// <summary>True while any bound property is loading.</summary>
    member _.IsLoading = bindings.IsLoading

    /// <summary>True while any bound property is failed.</summary>
    member _.HasErrors = bindings.HasErrors

    /// <summary>
    /// The error message of property <c>propertyName</c> while it is failed, or of every failed property for a null or
    /// empty name.
    /// </summary>
    member _.GetErrors(propertyName: string) =
        bindings.GetErrors propertyName

    [<CLIEvent>]
    member _.PropertyChanged = bindings.PropertyChanged

    [<CLIEvent>]
    member _.ErrorsChanged = bindings.ErrorsChanged

    /// <summary>Disposes <c>Bindings</c>.</summary>
    member _.Dispose() =
        bindings.Dispose ()

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member _.PropertyChanged = bindings.PropertyChanged

    interface INotifyDataErrorInfo with
        member _.HasErrors = bindings.HasErrors

        member _.GetErrors(propertyName) =
            bindings.GetErrors propertyName

        [<CLIEvent>]
        member _.ErrorsChanged = bindings.ErrorsChanged

    interface IDisposable with
        member _.Dispose() =
            bindings.Dispose ()
