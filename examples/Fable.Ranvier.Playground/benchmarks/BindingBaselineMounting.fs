module BindingBaselineMounting

open System
open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation

/// <summary>The renderer scope tree of the baseline, kept with the baseline it measures.</summary>
type internal Scope private (report: exn list -> unit, release: unit -> unit, parent: Scope option) =
    let children = ResizeArray<Scope>()
    let callbacks = ResizeArray<unit -> unit>()
    let mutable disposed = false
    let mutable attached = false
    let mutable failures: exn list = []
    // The active disposal order of this scope's tree. Set only on the topmost scope; null outside a disposal.
    let mutable draining: ResizeArray<Scope> = null

    new(report, release) = Scope (report, release, None)

    member private this.Top: Scope =
        match parent with
        | Some owner -> owner.Top
        | None -> this

    member private _.Draining
        with get () = draining
        and set value = draining <- value

    member private _.RemoveChild(child) =
        children.Remove child |> ignore

    member private _.EnsureLive() =
        if disposed then
            raise (System.InvalidOperationException "Renderer scope is disposed")

    member this.Child(childReport: exn list -> unit, childRelease: unit -> unit) =
        this.EnsureLive ()
        let child = Scope (childReport, childRelease, Some this)
        children.Add child
        child

    member this.Child(childRelease: unit -> unit) =
        this.Child (report, childRelease)

    member this.OnCleanup(callback: unit -> unit) =
        this.EnsureLive ()
        callbacks.Add callback

    member private this.Collect(order: ResizeArray<Scope>) =
        if not disposed then
            disposed <- true

            for index in 0 .. children.Count - 1 do
                children[index].Collect order

            order.Add this

    member private _.Clean(errors: ResizeArray<exn>) =
        for index in 0 .. callbacks.Count - 1 do
            try
                callbacks[index]()
            with error ->
                errors.Add error

        callbacks.Clear ()

    member private this.Release(errors: ResizeArray<exn>) =
        try
            release ()
        with error ->
            errors.Add error

        children.Clear ()

        match parent with
        | Some owner -> owner.RemoveChild this
        | None -> ()

    member this.Dispose() =
        if not disposed then
            let top = this.Top

            match top.Draining with
            | null ->
                let order = ResizeArray<Scope>()
                let errors = ResizeArray<exn>()
                top.Draining <- order

                try
                    this.Collect order
                    let mutable cleaned = 0
                    let mutable released = 0

                    while released < order.Count do
                        while cleaned < order.Count do
                            order[cleaned].Clean errors
                            cleaned <- cleaned + 1

                        order[released].Release errors
                        released <- released + 1

                    if errors.Count > 0 then
                        failures <- List.ofSeq errors
                finally
                    top.Draining <- null

                if errors.Count > 0 then
                    try
                        report failures
                    with error ->
                        failures <- failures @ [ error ]
            | order -> this.Collect order

    member this.MarkAttached() =
        this.EnsureLive ()

        if attached then
            raise (System.InvalidOperationException "Renderer scope is already attached")

        attached <- true

        for index in 0 .. children.Count - 1 do
            let child = children[index]

            if not child.IsDisposed && child.Phase = SettingUp then
                child.MarkAttached ()

    member _.Phase =
        if disposed then Disposed
        elif attached then Attached
        else SettingUp

    member _.IsDisposed = disposed
    member _.Failures = failures

type NativeOwnership =
    | Owned
    | Borrowed

type BuildContext =
    {
        Backend: Backend
        Native: Node * NativeOwnership -> unit
        OnCleanup: (unit -> unit) -> unit
        Phase: unit -> ScopePhase
    }

[<Emit("document.createDocumentFragment()")>]
let private fragment () : Node = jsNative

[<Emit("document.createComment($0)")>]
let private comment (value: string) : Node = jsNative

[<Emit("document.createTextNode($0)")>]
let private text (value: string) : Node = jsNative

[<Emit("document.createElement($0)")>]
let private element (tag: string) : HTMLElement = jsNative

[<Emit("$0.insertBefore($1, $2 == null ? null : $2)")>]
let private insert (parent: Node) (node: Node) (before: Node option) : unit = jsNative

[<Emit("$0.remove()")>]
let private remove (node: Node) : unit = jsNative

[<Emit("$0.contains($1)")>]
let private contains (parent: Node) (node: Node) : bool = jsNative

[<Emit("$0.ownerDocument === $1.ownerDocument")>]
let private sameDocument (a: Node) (b: Node) : bool = jsNative

[<Emit("$0.value = $1")>]
let private inputValue (node: HTMLElement) (value: string) : unit = jsNative

[<Emit("$0.nextSibling")>]
let private nextSibling (node: Node) : Node option = jsNative

[<Emit("$0.nodeType === 11")>]
let private isDocumentFragment (node: Node) : bool = jsNative

let private invalid message =
    raise (InvalidOperationException message)

let private same a b =
    obj.ReferenceEquals (a, b)

[<Emit("new WeakSet()")>]
let private createClaims () : obj = jsNative

let private claims = createClaims ()

[<Emit("$0.add($1)")>]
let private claim (registry: obj) (node: Node) : unit = jsNative

[<Emit("$0.delete($1)")>]
let private unclaim (registry: obj) (node: Node) : unit = jsNative

[<Emit("(() => { for (let p = $1; p; p = p.parentNode) { if ($0.has(p)) return true; } if (!$1.firstChild) return false; const pending = Array.from($1.childNodes); while (pending.length) { const n = pending.pop(); if ($0.has(n)) return true; for (let c = n.firstChild; c; c = c.nextSibling) pending.push(c); } return false; })()")>]
let private overlaps (registry: obj) (node: Node) : bool = jsNative

type private Entry =
    {
        Node: Node
        EndNode: Node
        Validate: unit -> unit
        Relocate: unit -> unit
        Restore: (unit -> unit) option
    }

type private OwnedRange(host: Node) =
    let staging = fragment ()
    let first, last = comment "ranvier:start", comment "ranvier:end"
    let entries = ResizeArray<Entry>()
    let generatedDescendants = ResizeArray<Node>()
    let mutable placement: Node = staging

    let intactAt parent =
        let mutable cursor = Some first
        let mutable intact = true

        for entry in entries do
            entry.Validate ()

            if
                not (
                    entry.Node.parentNode
                    |> Option.exists (same parent)
                )
                || not (cursor |> Option.exists (same entry.Node))
            then
                intact <- false

            let mutable spanCursor = Some entry.Node

            while spanCursor
                  |> Option.exists (fun node -> not (same node entry.EndNode)) do
                spanCursor <- spanCursor |> Option.bind nextSibling

            if
                spanCursor.IsNone
                || not (
                    entry.EndNode.parentNode
                    |> Option.exists (same parent)
                )
            then
                intact <- false

            cursor <- nextSibling entry.EndNode

        intact

    do
        staging.appendChild first |> ignore
        staging.appendChild last |> ignore

        entries.Add
            {
                Node = first
                EndNode = first
                Validate = ignore
                Relocate = ignore
                Restore = None
            }

        entries.Add
            {
                Node = last
                EndNode = last
                Validate = ignore
                Relocate = ignore
                Restore = None
            }

        claim claims first
        claim claims last

    member _.Staging = staging

    member _.AddRegion(start: Node, finish: Node, validate: unit -> unit, relocate: unit -> unit) =
        entries.Insert (
            entries.Count - 1,
            {
                Node = start
                EndNode = finish
                Validate = validate
                Relocate = relocate
                Restore = None
            }
        )

        claim claims start
        claim claims finish
        insert staging start (Some last)
        insert staging finish (Some last)

    member this.AppendGenerated(parent: Node, node: Node) =
        generatedDescendants.Add node
        claim claims node
        parent.appendChild node |> ignore

    member _.Add(node: Node, ownership) =
        if contains node host || contains node staging then
            invalid "A native node cannot contain its mount host or range"

        if overlaps claims node then
            invalid "Native ownership overlaps a live mount"

        let restore =
            match ownership with
            | Owned -> None
            | Borrowed ->
                let originalParent, originalNext = node.parentNode, nextSibling node

                Some (fun () ->
                    match originalParent with
                    | None -> remove node
                    | Some parent ->
                        let before =
                            originalNext
                            |> Option.filter (fun next -> next.parentNode |> Option.exists (same parent))

                        insert parent node before)

        entries.Insert (
            entries.Count - 1,
            {
                Node = node
                EndNode = node
                Validate = ignore
                Relocate = ignore
                Restore = restore
            }
        )

        claim claims node
        insert staging node (Some last)

    member _.Validate() =
        if intactAt placement then
            ()
        elif
            isDocumentFragment placement
            && placement.firstChild.IsNone
        then
            let relocatedParent =
                first.parentNode
                |> Option.defaultWith (fun () -> invalid "Range boundaries or owned roots were externally mutated")

            if not (intactAt relocatedParent) then
                invalid "Range roots were not contiguous after their staging fragment was attached"

            placement <- relocatedParent
        else
            invalid "Range boundaries or owned roots were externally mutated"

    member private _.ValidateDestination(parent: Node, before: Node option) =
        if not (sameDocument first parent) then
            invalid "Cross-document movement requires an explicit adoption policy"

        match before with
        | Some anchor when not (anchor.parentNode |> Option.exists (same parent)) -> invalid "Insertion anchor does not belong to the destination"
        | _ -> ()

        for entry in entries do
            let mutable cursor = Some entry.Node

            while cursor.IsSome do
                let node = cursor.Value

                if contains node parent then
                    invalid "Cannot move a range into itself"

                if
                    before
                    |> Option.exists (fun anchor -> same node anchor && not (same first anchor))
                then
                    invalid "Cannot insert a range inside its own boundaries"

                cursor <- if same node entry.EndNode then None else nextSibling node

    member this.Attach(parent, before) =
        this.Validate ()
        this.ValidateDestination (parent, before)
        insert parent staging before
        placement <- parent

    member _.Relocate() =
        placement <-
            first.parentNode
            |> Option.defaultWith (fun () -> invalid "Range has no parent")

        for entry in entries do
            entry.Relocate ()

    member this.Move(parent, before, rollbackFailed: unit -> unit) =
        this.Validate ()
        this.ValidateDestination (parent, before)

        if not (before |> Option.exists (same first)) then
            let originalParent, originalNext = placement, nextSibling last

            try
                for entry in entries do
                    let mutable cursor = Some entry.Node

                    while cursor.IsSome do
                        let node = cursor.Value
                        cursor <- if same node entry.EndNode then None else nextSibling node
                        staging.appendChild node |> ignore

                insert parent staging before
                placement <- parent

                for entry in entries do
                    entry.Relocate ()
            with original ->
                try
                    for entry in entries do
                        let mutable cursor = Some entry.Node

                        while cursor.IsSome do
                            let node = cursor.Value
                            cursor <- if same node entry.EndNode then None else nextSibling node
                            staging.appendChild node |> ignore

                    insert originalParent staging originalNext
                    placement <- originalParent

                    for entry in entries do
                        entry.Relocate ()
                with rollback ->
                    rollbackFailed ()
                    raise (AggregateException ("Range movement and rollback failed", [| original; rollback |]))

                raise original

    member _.Release() =
        let errors = ResizeArray<exn>()

        for index in generatedDescendants.Count - 1 .. -1 .. 0 do
            let node = generatedDescendants[index]

            try
                remove node
            with error ->
                errors.Add error

            unclaim claims node

        generatedDescendants.Clear ()

        for index in entries.Count - 1 .. -1 .. 0 do
            let entry = entries[index]

            let attempt action =
                try
                    action ()
                with error ->
                    errors.Add error

            match entry.Restore with
            | Some restore -> attempt restore
            | None ->
                attempt (fun () -> remove entry.Node)

                if not (same entry.Node entry.EndNode) then
                    attempt (fun () -> remove entry.EndNode)

            unclaim claims entry.Node
            unclaim claims entry.EndNode

        entries.Clear ()

        if errors.Count = 1 then
            raise errors[0]
        elif errors.Count > 1 then
            raise (AggregateException ("Range release failed", errors))

type Mounted internal (scope: Scope, move: Node * Node option -> unit, validate: unit -> unit, relocate: unit -> unit) =
    member _.Phase = scope.Phase
    member _.Failures = scope.Failures

    /// <summary>Checks that the mount's recorded DOM range remains intact.</summary>
    member internal _.Validate() =
        validate ()

    member internal _.Relocate() =
        relocate ()

    member _.Dispose() =
        scope.Dispose ()

    member _.MoveBefore(parent, before) =
        if scope.Phase <> Attached then
            invalid "Only an attached mount can move"

        move (parent, before)

type private KeyedEntry =
    {
        Key: obj
        Value: Signal<obj>
        First: Node
        Last: Node
        Mount: Mounted
    }

exception private KeyedDisposed
exception private KeyedSetupFailed of exn list

let rec internal mount
    (graph: Graph)
    (parentScope: Scope option)
    (report: exn list -> unit)
    (host: Node)
    (before: Node option)
    (build: BuildContext -> unit)
    =
    let range = OwnedRange host

    let root =
        match parentScope with
        | Some parent -> parent.Child (report, range.Release)
        | None -> Scope (report, range.Release)

    let mutable parent = range.Staging
    let mutable scope = root

    let ensureSetup () =
        if
            root.Phase <> SettingUp
            || scope.Phase <> SettingUp
        then
            invalid "View construction is only valid during live setup"

    let append node =
        ensureSetup ()

        if same parent range.Staging then
            range.Add (node, Owned)
        else
            range.AppendGenerated (parent, node)

    let mutable modifierNode: HTMLElement = Unchecked.defaultof<_>
    let mutable modifierTag = Div

    let currentElement () =
        ensureSetup ()

        if isNull (box modifierNode) then
            invalid "Element modifiers require an active element"

        modifierNode

    let applyModifiers (backend: Backend) tag node modifiers =
        let previousNode = modifierNode
        let previousTag = modifierTag
        modifierNode <- node
        modifierTag <- tag

        try
            for apply in modifiers do
                ensureSetup ()
                apply backend
                ensureSetup ()
        finally
            modifierNode <- previousNode
            modifierTag <- previousTag

    let backend =
        { new Backend() with
            member _.Text value =
                ensureSetup ()
                append (text value)

            member _.Attribute(key, value) =
                let node = currentElement ()
                validateAttribute key
                node.setAttribute (key, value)

            member _.OptionalAttribute(key, value) =
                let node = currentElement ()
                validateAttribute key

                match value with
                | Some value -> node.setAttribute (key, value)
                | None -> node.removeAttribute key

            member _.ReactiveAttribute(_, _) =
                invalidOp "Reactive attributes are outside the frozen text benchmark"

            member _.InputValue value =
                let node = currentElement ()

                if modifierTag <> Input then
                    invalidArg "modifiers" "Input value requires input"

                inputValue node value

            member _.Browser hook =
                let cleanup = hook (box (currentElement ()))

                if scope.IsDisposed then
                    cleanup ()
                    ensureSetup ()

                scope.OnCleanup cleanup

            member this.Listen(eventName, handler) =
                this.Browser (fun node -> Typed.listenDirect node eventName (Typed.ownedHandler handler))

            member _.Property write =
                write (box (currentElement ()))

            member _.ReactiveProperty(_, _) =
                invalidOp "Reactive properties are outside the frozen text benchmark"

            member this.Element(tag, modifiers, children) =
                ensureSetup ()
                let node = element (name tag)
                append node

                applyModifiers this tag node modifiers

                if not (isVoid tag) then
                    let previous = parent

                    parent <-
                        if name tag = "template" then
                            (node :?> HTMLTemplateElement).content :> Node
                        else
                            node

                    try
                        children ()
                    finally
                        parent <- previous

            member _.Bind(equals, read) =
                ensureSetup ()
                let node = text ""
                append node
                let mutable committed: string option = None
                let bindingScope = scope

                Api.createEffect (fun () ->
                    let value = read ()

                    if not root.IsDisposed && not bindingScope.IsDisposed then
                        let changed =
                            committed
                            |> Option.forall (fun old -> not (equals old value))

                        if
                            changed
                            && not root.IsDisposed
                            && not bindingScope.IsDisposed
                        then
                            Api.untrack (fun () -> node.textContent <- Some value)
                            committed <- Some value)

            member _.Keyed(items, render) =
                let read () =
                    items.Read ()
                    |> Array.map (fun item -> items.Key.Invoke item, item)
                    |> Array.toList

                ensureSetup ()
                let initialItems = read ()
                ensureSetup ()

                Projection.ensureUniqueKeys fst initialItems

                let regionStart, regionEnd =
                    comment "ranvier:list:start", comment "ranvier:list:end"

                if not (same parent range.Staging) then
                    range.AppendGenerated (parent, regionStart)
                    range.AppendGenerated (parent, regionEnd)

                let regionScope = scope.Child (report, ignore)
                let mutable entries: KeyedEntry list = []
                let mutable regionOwner: Owner option = None
                let mutable initializing = true
                let mutable setupFailure: exn option = None
                let mutable rowSetupErrors: ResizeArray<exn> option = None

                let reportRow errors =
                    match rowSetupErrors with
                    | Some collected -> collected.AddRange errors
                    | None -> report errors

                let ensureRegionLive () =
                    if root.IsDisposed || regionScope.IsDisposed then
                        raise KeyedDisposed

                let getRegionParent () =
                    let regionParent =
                        regionStart.parentNode
                        |> Option.defaultWith (fun () -> invalid "Keyed-list boundaries have no parent")

                    if
                        not (
                            regionEnd.parentNode
                            |> Option.exists (same regionParent)
                        )
                    then
                        invalid "Keyed-list boundaries were externally mutated"

                    regionParent

                let validateRegion regionParent =
                    if
                        not (
                            regionStart.parentNode
                            |> Option.exists (same regionParent)
                        )
                        || not (
                            regionEnd.parentNode
                            |> Option.exists (same regionParent)
                        )
                    then
                        invalid "Keyed-list boundaries were externally mutated"

                    let mutable cursor = nextSibling regionStart

                    for entry in entries do
                        entry.Mount.Validate ()

                        if not (cursor |> Option.exists (same entry.First)) then
                            invalid "Keyed-list rows or contents were externally mutated"

                        cursor <- nextSibling entry.Last

                    if not (cursor |> Option.exists (same regionEnd)) then
                        invalid "Keyed-list boundaries were externally reordered"

                if same parent range.Staging then
                    range.AddRegion (
                        regionStart,
                        regionEnd,
                        (fun () -> validateRegion (getRegionParent ())),
                        (fun () ->
                            for entry in entries do
                                entry.Mount.Relocate ())
                    )

                let moveToOrder regionParent (ordered: KeyedEntry list) =
                    let mutable anchor = Some regionEnd

                    for entry in List.rev ordered do
                        ensureRegionLive ()

                        let alreadyPlaced =
                            entry.Last.parentNode
                            |> Option.exists (same regionParent)
                            && (nextSibling entry.Last
                                |> Option.exists (fun next -> anchor |> Option.exists (same next)))

                        if not alreadyPlaced then
                            entry.Mount.MoveBefore (regionParent, anchor)

                        ensureRegionLive ()

                        anchor <- Some entry.First

                let createEntry key value =
                    ensureRegionLive ()
                    let item = graph.Run (fun () -> Api.createSignal value)
                    let staging = fragment ()
                    let setupErrors = ResizeArray<exn>()
                    let previousErrors = rowSetupErrors

                    let owner =
                        regionOwner
                        |> Option.defaultWith (fun () -> invalid "Keyed-list owner is unavailable")

                    let mounted =
                        rowSetupErrors <- Some setupErrors

                        try
                            try
                                Api.runWithOwner owner (fun () ->
                                    mount graph (Some regionScope) reportRow staging None (fun context ->
                                        render (fun () -> item.Value) context.Backend))
                            with original ->
                                if setupErrors.Count > 0 then
                                    raise (KeyedSetupFailed (original :: List.ofSeq setupErrors))

                                ensureRegionLive ()
                                reraise ()
                        finally
                            rowSetupErrors <- previousErrors

                    let first =
                        staging.firstChild
                        |> Option.defaultWith (fun () -> invalid "A keyed row has no start boundary")

                    let last =
                        staging.lastChild
                        |> Option.defaultWith (fun () -> invalid "A keyed row has no end boundary")

                    {
                        Key = key
                        Value = item
                        First = first
                        Last = last
                        Mount = mounted
                    }

                let reconcileItems items =
                    ensureRegionLive ()
                    let regionParent = getRegionParent ()
                    validateRegion regionParent
                    Projection.ensureUniqueKeys fst items
                    ensureRegionLive ()

                    let matches =
                        items
                        |> List.map (fun (key, value) ->
                            let matched =
                                entries
                                |> List.tryFind (fun entry -> entry.Key = key)

                            ensureRegionLive ()

                            match matched with
                            | Some entry -> Choice1Of2 (entry, value)
                            | None -> Choice2Of2 (key, value))

                    let created = ResizeArray<KeyedEntry>()
                    let desired = ResizeArray<KeyedEntry * obj>()

                    try
                        for matchResult in matches do
                            match matchResult with
                            | Choice1Of2 (entry, value) -> desired.Add (entry, value)
                            | Choice2Of2 (key, value) ->
                                let entry = createEntry key value
                                created.Add entry
                                ensureRegionLive ()
                                desired.Add (entry, value)
                    with original ->
                        let errors = ResizeArray<exn>()
                        errors.Add original

                        for index in created.Count - 1 .. -1 .. 0 do
                            try
                                created[index].Mount.Dispose()
                            with error ->
                                errors.Add error

                        if errors.Count = 1 then
                            reraise ()
                        else
                            raise (AggregateException ("Keyed row setup and cleanup failed", errors))

                    let nextEntries = desired |> Seq.map fst |> List.ofSeq

                    try
                        moveToOrder regionParent nextEntries
                    with movementError ->
                        ensureRegionLive ()

                        try
                            moveToOrder regionParent entries
                        with rollbackError ->
                            root.Dispose ()
                            raise (AggregateException ("Keyed reconciliation and rollback failed", [| movementError; rollbackError |]))

                        let errors = ResizeArray<exn>()
                        errors.Add movementError

                        for entry in created do
                            try
                                entry.Mount.Dispose ()
                            with error ->
                                errors.Add error

                        if errors.Count = 1 then
                            reraise ()
                        else
                            raise (AggregateException ("Keyed reconciliation and cleanup failed", errors))

                    let removedEntries =
                        entries
                        |> List.filter (fun entry ->
                            nextEntries
                            |> List.exists (fun next -> obj.ReferenceEquals (entry, next))
                            |> not)

                    Api.batch (fun () ->
                        for entry, value in desired do
                            ensureRegionLive ()
                            entry.Value.Value <- value

                        entries <- nextEntries

                        for removed in removedEntries do
                            removed.Mount.Dispose ())

                let reconcile () =
                    try
                        let items = read ()
                        Api.untrack (fun () -> reconcileItems items)
                    with
                    | KeyedDisposed -> ()
                    | error ->
                        let errors =
                            match error with
                            | KeyedSetupFailed errors -> errors
                            | _ -> [ error ]

                        if initializing then
                            setupFailure <-
                                Some (
                                    match errors with
                                    | [ error ] -> error
                                    | _ ->
                                        AggregateException (
                                            "Keyed row setup and cleanup failed: "
                                            + String.concat "; " (errors |> List.map _.Message),
                                            errors
                                        )
                                )
                        else
                            try
                                Api.untrack (fun () -> report errors)
                            with reporterError ->
                                raise (AggregateException ("Keyed update and reporter failed", [| error; reporterError |]))

                            reraise ()

                Api.createRoot (fun owner ->
                    regionOwner <- Some owner
                    Api.onCleanup regionScope.Dispose
                    regionScope.OnCleanup owner.Dispose
                    Api.untrack (fun () -> Api.createEffect reconcile))

                initializing <- false

                match setupFailure with
                | Some error -> raise error
                | None -> ()

            member _.Component setup =
                ensureSetup ()
                let previous = scope
                let child = scope.Child ignore
                scope <- child

                try
                    Api.createRoot (fun owner ->
                        Api.onCleanup child.Dispose
                        child.OnCleanup owner.Dispose
                        Api.untrack (fun () -> setup { OnCleanup = child.OnCleanup }))
                finally
                    scope <- previous
        }

    let context =
        {
            Backend = backend
            Native =
                fun (node, ownership) ->
                    ensureSetup ()

                    if not (same parent range.Staging) then
                        invalid "Native roots require top-level placement"

                    range.Add (node, ownership)
            OnCleanup = root.OnCleanup
            Phase = fun () -> root.Phase
        }

    try
        graph.Run (fun () ->
            Api.createRoot (fun owner ->
                Api.onCleanup root.Dispose
                root.OnCleanup owner.Dispose
                Api.untrack (fun () -> build context)))

        ensureSetup ()
        range.Attach (host, before)
        root.MarkAttached ()

        Mounted (
            root,
            (fun (parent, before) -> range.Move (parent, before, root.Dispose)),
            (fun () -> range.Validate ()),
            (fun () -> range.Relocate ())
        )
    with _ ->
        root.Dispose ()
        reraise ()
