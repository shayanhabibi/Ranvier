namespace Ranvier.View.Foundation

open System
open Ranvier

type ScopePhase =
    | SettingUp
    | Attached
    | Disposed

/// <summary>
/// The errors of one view-initiated teardown or list pass: the errors the graph root records while it runs, less those
/// of nested windows, followed by the errors routed to it by lifetimes that report to <c>report</c>.
/// </summary>
[<Sealed; AllowNullLiteral>]
type internal TeardownWindow(root: Owner, start: int, report: exn list -> unit) =
    let routed = ResizeArray<exn>()
    // Start and end positions in the root's errors of nested windows, in pairs.
    let nested = ResizeArray<int>()

    member _.Root = root
    member _.Start = start
    member _.Report = report

    member _.Add(errors: exn[]) =
        routed.AddRange errors

    member _.Exclude(first: int, finish: int) =
        nested.Add first
        nested.Add finish

    member _.Errors() : exn list =
        let recorded = root.ErrorsFrom start
        let result = ResizeArray<exn>()

        for offset in 0 .. recorded.Length - 1 do
            let index = start + offset
            let mutable inner = false
            let mutable pair = 0

            while not inner && pair < nested.Count do
                if index >= nested[pair] && index < nested[pair + 1] then
                    inner <- true

                pair <- pair + 2

            if not inner then
                result.Add recorded[offset]

        result.AddRange routed
        List.ofSeq result

/// <summary>
/// The lifetime of a mounted view: a core owner, the release of the view's DOM nodes and the reporter of its teardown
/// errors.
/// </summary>
/// <remarks>
/// <para>
/// The release runs after every cleanup of the owner. While an enclosing lifetime tears down, a release waits in the
/// outermost one's queue and runs before that lifetime's own release, so every cleanup of a disposed tree runs while its
/// nodes are attached. A release that runs while a descendant's <c>Dispose</c> is in progress waits for that call to
/// finish.
/// </para>
/// <para>
/// A teardown's errors reach the reporter once: the errors of the owner and of adopted owners, release failures and,
/// for a teardown started by <c>Dispose</c>, the errors the graph root records meanwhile. An open teardown window
/// receives a lifetime's errors only when the window serves the lifetime's reporter; a lifetime torn down inside a
/// window of another reporter reports its errors through its own reporter when its teardown completes.
/// </para>
/// </remarks>
[<Sealed; AllowNullLiteral>]
type MountLifetime private (graph: Graph, owner: Owner, report: exn list -> unit, release: unit -> unit, parent: MountLifetime) =
    let inherits = not (isNull parent) && parent.Phase = SettingUp
    // The teardown reports the owner's errors recorded at or after this position.
    let ownerStart = owner.ErrorCount
    let mutable attached = false
    let mutable released = false
    let mutable failures: exn list = []
    let mutable queued: ResizeArray<unit -> unit> = null
    let mutable held: ResizeArray<exn> = null
    let mutable deferred: ResizeArray<unit -> unit> = null

    static member val private Windows = ResizeArray<TeardownWindow>()
    static member val private Frames = ResizeArray<MountLifetime>()

    static member private CurrentWindow: TeardownWindow =
        let windows = MountLifetime.Windows

        if windows.Count = 0 then
            null
        else
            windows[windows.Count - 1]

    /// <summary>
    /// Wraps <c>owner</c> and registers the release as its first cleanup. Call before anything else registers with
    /// <c>owner</c>.
    /// </summary>
    static member internal Create(graph: Graph, owner: Owner, report: exn list -> unit, release: unit -> unit, parent: MountLifetime) =
        let lifetime = MountLifetime (graph, owner, report, release, parent)
        owner.OnCleanup (Action (fun () -> lifetime.Finish ()))
        lifetime

    /// <summary>A lifetime whose owner is a root created under the current owner of <c>graph</c>.</summary>
    static member internal Root(graph: Graph, report: exn list -> unit, release: unit -> unit) =
        graph.Run (fun () -> Api.createRoot (fun owner -> MountLifetime.Create (graph, owner, report, release, null)))

    /// <summary>
    /// Runs <c>body</c> in a new teardown window serving <c>report</c>, and returns the window's errors with the
    /// exception <c>body</c> raised.
    /// </summary>
    static member internal Capture(graph: Graph, report: exn list -> unit, body: unit -> unit) : exn list * exn option =
        let root = graph.Root
        let windows = MountLifetime.Windows
        let window = TeardownWindow (root, root.ErrorCount, report)
        windows.Add window
        let mutable failure = None

        try
            try
                body ()
            with error ->
                failure <- Some error
        finally
            windows.RemoveAt (windows.Count - 1)

        let outer = MountLifetime.CurrentWindow

        if
            not (isNull outer)
            && obj.ReferenceEquals (outer.Root, root)
        then
            outer.Exclude (window.Start, root.ErrorCount)

        window.Errors (), failure

    static member private DescendantFrame(lifetime: MountLifetime) : MountLifetime =
        let frames = MountLifetime.Frames
        let mutable result: MountLifetime = null
        let mutable index = frames.Count - 1

        while isNull result && index >= 0 do
            let frame = frames[index]

            if not frame.Released && frame.DescendsFrom lifetime then
                result <- frame

            index <- index - 1

        result

    member _.Owner = owner
    member _.Parent = parent
    member _.IsDisposed = owner.IsDisposed
    member _.Failures = failures
    member internal _.Released = released

    member _.Phase =
        if owner.IsDisposed then
            Disposed
        elif attached || (inherits && parent.Phase = Attached) then
            Attached
        else
            SettingUp

    member private _.EnsureLive() =
        if owner.IsDisposed then
            raise (InvalidOperationException "Renderer scope is disposed")

    member this.OnCleanup(callback: unit -> unit) =
        this.EnsureLive ()
        owner.OnCleanup (Action callback)

    member this.Child(childRelease: unit -> unit) : MountLifetime =
        this.Child (childRelease, ignore)

    /// <summary>A lifetime nested in this one, its owner a root under this lifetime's owner, with <c>body</c> run inside that root.</summary>
    member this.Child(childRelease: unit -> unit, body: MountLifetime -> unit) : MountLifetime =
        this.EnsureLive ()
        let mutable child: MountLifetime = null

        graph.Run (fun () ->
            Api.runWithOwner owner (fun () ->
                Api.createRoot (fun childOwner ->
                    child <- MountLifetime.Create (graph, childOwner, report, childRelease, this)
                    body child)))

        child

    member this.MarkAttached() =
        this.EnsureLive ()

        if attached then
            raise (InvalidOperationException "Renderer scope is already attached")

        attached <- true

    /// <summary>
    /// Routes the errors recorded by <c>adopted</c> after this call into this lifetime's teardown report.
    /// <c>adopted</c> is a root owner nested in this lifetime. Call before anything else registers with it.
    /// </summary>
    member this.Adopt(adopted: Owner) =
        let start = adopted.ErrorCount
        adopted.OnCleanup (Action (fun () -> this.Route (adopted.ErrorsFrom start)))

    member private this.DescendsFrom(ancestor: MountLifetime) =
        let mutable cursor = parent
        let mutable found = false

        while not found && not (isNull cursor) do
            if obj.ReferenceEquals (cursor, ancestor) then
                found <- true

            cursor <- cursor.Parent

        found

    /// <summary>The highest lifetime, this one included, that is tearing down and has not released its nodes.</summary>
    member internal this.Outermost() : MountLifetime =
        let mutable result = this
        let mutable cursor = parent

        while not (isNull cursor) do
            if cursor.IsDisposed && not cursor.Released then
                result <- cursor

            cursor <- cursor.Parent

        result

    member private _.Hold(errors: exn[]) =
        if isNull held then
            held <- ResizeArray ()

        held.AddRange errors

    member private _.Enqueue(action: unit -> unit) =
        if isNull queued then
            queued <- ResizeArray ()

        queued.Add action

    member private _.Defer(action: unit -> unit) =
        if isNull deferred then
            deferred <- ResizeArray ()

        deferred.Add action

    member private _.RunDeferred() =
        if not (isNull deferred) then
            let pending = deferred
            deferred <- null

            for index in 0 .. pending.Count - 1 do
                pending[index]()

    /// <summary>
    /// Adds <c>errors</c> to the open teardown window when it serves this lifetime's reporter, or else to the teardown
    /// of the outermost tearing-down lifetime, or reports them.
    /// </summary>
    member internal this.Route(errors: exn[]) =
        if errors.Length > 0 then
            let window = MountLifetime.CurrentWindow

            if this.Serves window then
                window.Add errors
            else
                let target = this.Outermost ()

                if target.IsDisposed then
                    target.Hold errors
                else
                    this.Report (List.ofArray errors)

    member private _.Serves(window: TeardownWindow) =
        not (isNull window)
        && obj.ReferenceEquals (window.Report, report)

    member private this.Report(errors: exn list) =
        if not errors.IsEmpty then
            failures <- errors

            try
                graph.Run (fun () -> Api.untrack (fun () -> report errors))
            with error ->
                failures <- failures @ [ error ]

    member private this.RunRelease() =
        try
            release ()
        with error ->
            this.Route [| error |]

        released <- true

    member private this.Complete() =
        if not (isNull queued) then
            let pending = queued
            queued <- null

            for index in 0 .. pending.Count - 1 do
                pending[index]()

        this.RunRelease ()

        if not (isNull held) then
            let errors = held.ToArray ()
            held <- null
            let window = MountLifetime.CurrentWindow

            if this.Serves window then
                window.Add errors
            else
                this.Report (List.ofArray errors)

    member private this.Finish() =
        let outer = this.Outermost ()
        this.Route (owner.ErrorsFrom ownerStart)

        if obj.ReferenceEquals (outer, this) then
            let frame = MountLifetime.DescendantFrame this

            if isNull frame then
                this.Complete ()
            else
                frame.Defer (fun () -> this.Complete ())
        else
            outer.Enqueue (fun () -> this.RunRelease ())

    member private this.TearDown() =
        let frames = MountLifetime.Frames
        frames.Add this

        try
            owner.Dispose ()
        finally
            frames.RemoveAt (frames.Count - 1)

        this.RunDeferred ()

    /// <summary>Tears the lifetime down and reports the teardown's errors once.</summary>
    member this.Dispose() =
        if not owner.IsDisposed then
            let errors, failure =
                MountLifetime.Capture (graph, report, (fun () -> this.TearDown ()))

            this.Report errors
            failure |> Option.iter raise

    /// <summary>Tears the lifetime down. Inside a teardown window serving this lifetime's reporter the errors join that window.</summary>
    member internal this.Abandon() =
        if not (this.Serves MountLifetime.CurrentWindow) then
            this.Dispose ()
        elif not owner.IsDisposed then
            this.TearDown ()
