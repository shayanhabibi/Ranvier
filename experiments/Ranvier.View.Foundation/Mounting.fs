module Ranvier.View.Foundation.Mounting

open System
open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Fable.Core.TS
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Scheduling

type NativeOwnership =
    | Owned
    | Borrowed

[<Global>]
let private globalThis: GlobalThis = jsNative

[<Global "document">]
let private document: Document = jsNative

let private fragment () =
    document.createDocumentFragment ()

let private comment (value: string) =
    document.createComment (value)

let private text (value: string) =
    document.createTextNode (value)

let private propertyEquals = Func<obj, obj, bool>(fun left right -> left = right)

let private attributeEquals =
    Func<string option, string option, bool>(fun left right -> left = right)

let inline private insert (parent: #Node) (node: #Node) (before: #Node option) : unit =
    let before: Node option = before |> Option.map (fun x -> x :> Node)

    parent.insertBefore (node, ?child = before)
    |> ignore

let inline private nextSiblingNode (node: #Node) : Node option =
    node.nextSibling
    |> Option.map (fun x -> x :> Node)

let private remove (node: ChildNode) : unit =
    node.remove ()

let private contains (parent: Node) (node: Node) : bool =
    parent.contains node

let private sameDocument (a: Node) (b: Node) : bool =
    match a.ownerDocument, b.ownerDocument with
    | None, None -> true
    | Some left, Some right -> obj.ReferenceEquals (left, right)
    | _ -> false

let private inputValue (node: HTMLInputElement) (value: string) : unit =
    node.value <- value

let private attributeValue (node: Element) name =
    function
    | Some value -> node.setAttribute (name, value)
    | None -> node.removeAttribute name

let private isDocumentFragment (node: Node) : bool =
    node.nodeType = 11.

let private invalid message =
    raise (InvalidOperationException message)

let private same a b =
    obj.ReferenceEquals (a, b)

#if FABLE_COMPILER
let inline private optionSame expected candidate =
    match candidate with
    | Some actual -> obj.ReferenceEquals (expected, actual)
    | None -> false
#endif

let rec private walkPrepared visit (node: Node) =
    visit node

    let children =
        if
            node.nodeType = 1.
            && (node :?> Element).tagName = "TEMPLATE"
        then
            (node :?> HTMLTemplateElement).content :> Node
        else
            node

    let mutable child = children.firstChild

    while child.IsSome do
        let current = child.Value
        child <- current.nextSibling
        walkPrepared visit current

let private createClaims () : WeakSet<Node> =
    globalThis.WeakSet.Create ()

let private claims = createClaims ()

[<Emit("new Set(['beforeinput', 'click', 'contextmenu', 'dblclick', 'focusin', 'focusout', 'input', 'keydown', 'keyup', 'mousedown', 'mousemove', 'mouseout', 'mouseover', 'mouseup', 'pointerdown', 'pointermove', 'pointerout', 'pointerover', 'pointerup', 'touchend', 'touchmove', 'touchstart'])")>]
let private createDelegatedEvents () : obj = jsNative

/// <summary>Bubbling events dispatched from one listener per mount host.</summary>
let private delegatedEvents = createDelegatedEvents ()

[<Emit("$0.has($1)")>]
let private isDelegated (events: obj) (eventName: string) : bool = jsNative

[<Emit("$0['$$ranvier:' + $1] = $2")>]
let private setDelegatedHandler (node: Node) (eventName: string) (handler: obj -> unit) : unit = jsNative

[<Emit("new Map()")>]
let private createSharedListeners () : obj = jsNative

/// <summary>Per event name: the listener shared by every host.</summary>
let private sharedListeners = createSharedListeners ()

[<Emit("new WeakMap()")>]
let private createHostCounts () : obj = jsNative

/// <summary>Per host: the number of mounts using each shared listener.</summary>
let private hostCounts = createHostCounts ()

// The listener runs with its host as `this` and walks from the target to that host, presenting each handled node as currentTarget. A node with a handler runs it; for a template event node, `TemplateDom.dispatch` receives its instance state. An inner host records itself on the event, so an outer host resumes above it.
let private delegateFrom: Func<Node, string, unit> =
    JsInterop.emitJsExpr
        (sharedListeners, TemplateDom.dispatch, hostCounts)
        """(host, eventName) => { let listener = $0.get(eventName); if (!listener) { const key = '$$ranvier:' + eventName; listener = function (event) { const host = this; let node = event.$$ranvierHost ? event.$$ranvierHost.parentNode : event.target; Object.defineProperty(event, 'currentTarget', { configurable: true, get: () => node }); while (node) { const handler = node[key]; if (handler) { handler(event); if (event.cancelBubble) break; } else if (node.$$ranvier !== undefined) { $1(node.$$ranvier, node, event); if (event.cancelBubble) break; } if (node === host) break; node = node.parentNode; } event.$$ranvierHost = host; }; $0.set(eventName, listener); } let counts = $2.get(host); if (!counts) $2.set(host, counts = new Map()); const count = counts.get(eventName) | 0; if (count === 0) host.addEventListener(eventName, listener); counts.set(eventName, count + 1); }"""

let private undelegateFrom: Func<Node, string, unit> =
    JsInterop.emitJsExpr
        (sharedListeners, hostCounts)
        """(host, eventName) => { const counts = $1.get(host); const count = counts ? counts.get(eventName) | 0 : 0; if (count === 1) host.removeEventListener(eventName, $0.get(eventName)); if (count > 0) counts.set(eventName, count - 1); }"""

let private claim (registry: WeakSet<Node>) (node: Node) : unit =
    registry.add node |> ignore

let private unclaim (registry: WeakSet<Node>) (node: Node) : unit =
    registry.delete node |> ignore

[<Emit("(() => { for (let p = $1; p; p = p.parentNode) { if ($0.has(p)) return true; } if (!$1.firstChild) return false; const pending = Array.from($1.childNodes); while (pending.length) { const n = pending.pop(); if ($0.has(n)) return true; for (let c = n.firstChild; c; c = c.nextSibling) pending.push(c); } return false; })()")>]
let private overlaps (registry: WeakSet<Node>) (node: Node) : bool = jsNative

[<Emit("$0.parentNode === $1")>]
let private isChildOf (node: Node) (parent: Node) : bool = jsNative

[<Emit("$0.parentNode == null")>]
let private isDetached (node: Node) : bool = jsNative

/// <summary>The node's next sibling, or <c>null</c> after the last child.</summary>
[<Emit("$0.nextSibling")>]
let private nextOrNull (node: Node) : Node = jsNative

[<Emit("$0.textContent = ''")>]
let private clearChildren (parent: Node) : unit = jsNative

/// <summary>Removes the siblings between <c>first</c> and <c>last</c> in one operation.</summary>
[<Emit("(() => { const range = $0.ownerDocument.createRange(); range.setStartAfter($0); range.setEndBefore($1); range.deleteContents(); })()")>]
let private deleteBetween (first: Node) (last: Node) : unit = jsNative

/// <summary>Reads an element without a bounds check.</summary>
[<Emit("$0[$1]")>]
let private at (items: ResizeArray<'T>) (index: int) : 'T = jsNative

let private skip: unit -> unit = ignore

#if RANVIER_VIEW_COUNTERS
/// <summary>Counts of keyed-list work, defined only in the DOM test build.</summary>
module internal ListCounters =
    /// <summary>Laid-out rows checked by keyed-list validation.</summary>
    let validatedRows = ref 0

    /// <summary>Rows looked up in a keyed list's projection.</summary>
    let lookups = ref 0

    /// <summary>Longest-run computations made by keyed-list layout.</summary>
    let increasingRuns = ref 0
#endif

[<Emit("new Set()")>]
let private nameSet () : obj = jsNative

[<Emit("$0.has($1)")>]
let private setHas (set: obj) (name: string) : bool = jsNative

[<Emit("$0.add($1)")>]
let private setAdd (set: obj) (name: string) : unit = jsNative

[<Emit("$0.forEach($1)")>]
let private setIter (set: obj) (action: string -> unit) : unit = jsNative

/// <summary>For each position of <c>sources</c>, whether it belongs to one longest increasing run of its non-negative values.</summary>
let private increasingRun (sources: int array) : bool array =
#if RANVIER_VIEW_COUNTERS
    ListCounters.increasingRuns.Value <- ListCounters.increasingRuns.Value + 1
#endif
    let keep: bool array = Array.zeroCreate sources.Length
    let tails = ResizeArray<int>()
    let previous = Array.create sources.Length -1

    for index in 0 .. sources.Length - 1 do
        let value = sources[index]

        if value >= 0 then
            let mutable low = 0
            let mutable high = tails.Count

            while low < high do
                let middle = (low + high) >>> 1

                if sources[tails[middle]] < value then
                    low <- middle + 1
                else
                    high <- middle

            if low > 0 then
                previous[index] <- tails[low - 1]

            if low = tails.Count then
                tails.Add index
            else
                tails[low] <- index

    if tails.Count > 0 then
        let mutable cursor = tails[tails.Count - 1]

        while cursor >= 0 do
            keep[cursor] <- true
            cursor <- previous[cursor]

    keep

/// <summary>A region's failure state: set up failures recorded during the first pass, reports after it.</summary>
type private RegionErrors(report: exn list -> unit) =
    member val Initializing = true with get, set
    member val SetupFailure: exn option = None with get, set

    member _.Report(errors: exn list) =
        if not errors.IsEmpty then
            try
                Api.untrack (fun () -> report errors)
            with reporterError ->
                raise (AggregateException ("Keyed update and reporter failed", List.toArray (errors @ [ reporterError ])))

    /// <summary>Records <c>errors</c> as the region's setup failure during the first pass; afterwards reports them and raises <c>error</c>.</summary>
    member this.Fail(error: exn, errors: exn list) =
        if this.Initializing then
            this.SetupFailure <-
                Some (
                    match errors with
                    | [ single ] -> single
                    | _ ->
                        AggregateException (
                            "Keyed row setup and cleanup failed: "
                            + String.concat "; " (errors |> List.map _.Message),
                            errors
                        )
                        :> exn
                )
        else
            this.Report errors
            raise error

type private Entry =
    {
        Node: Node
        EndNode: Node
        Validate: unit -> unit
        Relocate: unit -> unit
        Restore: (unit -> unit) option
    }

type private OwnedRange(host: Node) =
    // Null until the range is first staged or moved.
    let mutable staging: DocumentFragment = Unchecked.defaultof<_>
    let first, last = comment "ranvier:start", comment "ranvier:end"
    let entries = ResizeArray<Entry>()
    let mutable generatedDescendants: ResizeArray<Node> = null
    // The parent of the entries; null while they are detached and unstaged.
    let mutable placement: Node = Unchecked.defaultof<_>
    // False while every root is a detached node created by the mount; `Attach` then inserts each root individually. A keyed region, a native node or a cloned-fragment root moves all roots into `staging`.
    let mutable staged = false
#if LADDER_NAIVE_RANGE
    let naiveRoots = ResizeArray<Node>()
#endif

    let intactAt (parent: Node) =
        let mutable cursor: Node = first
        let mutable intact = true

        for index in 0 .. entries.Count - 1 do
            let entry = at entries index
            entry.Validate ()

            if
                not (isChildOf entry.Node parent)
                || not (same cursor entry.Node)
            then
                intact <- false

            let mutable spanCursor = entry.Node

            while not (isNull (box spanCursor))
                  && not (same spanCursor entry.EndNode) do
                spanCursor <- nextOrNull spanCursor

            if
                isNull (box spanCursor)
                || not (isChildOf entry.EndNode parent)
            then
                intact <- false

            cursor <- nextOrNull entry.EndNode

        intact

    let stagingFragment () =
        if isNull (box staging) then
            staging <- fragment ()

        staging

    let stage () =
        if not staged then
            staged <- true
            let staging = stagingFragment ()

            if isNull (box placement) then
                placement <- staging

            for index in 0 .. entries.Count - 1 do
                let node = (at entries index).Node

                if not (isDetached node || isChildOf node staging) then
                    invalid "Range boundaries or owned roots were externally mutated"

                staging.appendChild node |> ignore

    do
        entries.Add
            {
                Node = first
                EndNode = first
                Validate = skip
                Relocate = skip
                Restore = None
            }

        entries.Add
            {
                Node = last
                EndNode = last
                Validate = skip
                Relocate = skip
                Restore = None
            }

        claim claims first
        claim claims last

    member _.AddRegion(start: Node, finish: Node, validate: unit -> unit, relocate: unit -> unit) =
        stage ()

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

    /// <summary>Joins a region's start and end markers, adjacent roots of the range, into one entry spanning the region's rows.</summary>
    member _.SpanRegion(start: Node, finish: Node, validate: unit -> unit, relocate: unit -> unit) =
        let mutable index = entries.Count - 2

        while index > 1
              && not (same (at entries index).Node finish) do
            index <- index - 1

        if
            index < 2
            || not (same (at entries (index - 1)).Node start)
        then
            invalid "Region markers must be adjacent roots of the range"

        entries[index - 1] <-
            {
                Node = start
                EndNode = finish
                Validate = validate
                Relocate = relocate
                Restore = None
            }

        entries.RemoveAt index

    /// <summary>The parent of the range's entries.</summary>
    member _.Placement: Node = placement

    member this.AppendGenerated(parent: Node, node: Node) =
        if isNull generatedDescendants then
            generatedDescendants <- ResizeArray ()

        generatedDescendants.Add node
        claim claims node
        parent.appendChild node |> ignore

#if LADDER_NAIVE_RANGE
    member _.AddNaive(node: Node) =
        stage ()
        naiveRoots.Add node
        insert staging node (Some last)

#endif
    /// <summary>Adds a root node created by the mount, either detached or a child of a cloned fragment.</summary>
    /// <remarks>The node and its subtree are unclaimed and contain neither the host nor the range.</remarks>
    member _.AddCreated(node: Node) =
        let attachNow = staged || not (isDetached node)

        if attachNow then
            stage ()

        entries.Insert (
            entries.Count - 1,
            {
                Node = node
                EndNode = node
                Validate = skip
                Relocate = skip
                Restore = None
            }
        )

        claim claims node

        if attachNow then
            insert staging node (Some last)

    member _.Add(node: ChildNode, ownership) =
        if
            contains node host
            || (not (isNull (box staging))
                && contains node staging)
        then
            invalid "A native node cannot contain its mount host or range"

        if overlaps claims node then
            invalid "Native ownership overlaps a live mount"

        let restore =
            match ownership with
            | Owned -> None
            | Borrowed ->
                let originalParent, originalNext = node.parentNode, node.nextSibling

                Some (fun () ->
                    match originalParent with
                    | None -> remove node
                    | Some parent ->
                        let before =
                            originalNext
#if FABLE_COMPILER
                            |> Option.filter (fun next -> next.parentNode |> optionSame parent)
#else
                            |> Option.filter (fun next -> next.parentNode |> Option.exists (same parent))
#endif

                        insert parent node before)

        stage ()

        entries.Insert (
            entries.Count - 1,
            {
                Node = node
                EndNode = node
                Validate = skip
                Relocate = skip
                Restore = restore
            }
        )

        claim claims node
        insert staging node (Some last)

    member _.Validate() =
        if isNull (box placement) then
            for index in 0 .. entries.Count - 1 do
                if not (isDetached (at entries index).Node) then
                    invalid "Range boundaries or owned roots were externally mutated"
        elif intactAt placement then
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

        let anchor: Node =
            match before with
            | Some anchor when not (isChildOf anchor parent) -> invalid "Insertion anchor does not belong to the destination"
            | Some anchor -> anchor
            | None -> Unchecked.defaultof<_>

        for index in 0 .. entries.Count - 1 do
            let entry = at entries index
            let mutable cursor = entry.Node

            while not (isNull (box cursor)) do
                let node = cursor

                if contains node parent then
                    invalid "Cannot move a range into itself"

                if same node anchor && not (same first anchor) then
                    invalid "Cannot insert a range inside its own boundaries"

                cursor <-
                    if same node entry.EndNode then
                        Unchecked.defaultof<_>
                    else
                        nextOrNull node

    member this.Attach(parent, before) =
#if !LADDER_NAIVE_RANGE
        this.Validate ()
        this.ValidateDestination (parent, before)
#endif
        if staged then
            insert parent staging before

            for index in 0 .. entries.Count - 1 do
                (at entries index).Relocate()
        else
            let mutable index = 0

            // An insertion that disposes the mount empties `entries`, ending the loop.
            while index < entries.Count do
                insert parent (at entries index).Node before
                index <- index + 1

        placement <- parent

    member _.Relocate() =
        placement <-
            first.parentNode
            |> Option.map (fun node -> node :> Node)
            |> Option.defaultWith (fun () -> invalid "Range has no parent")

        for entry in entries do
            entry.Relocate ()

    member this.Move(parent, before, rollbackFailed: unit -> unit) =
        this.Validate ()
        this.ValidateDestination (parent, before)

        if
#if FABLE_COMPILER
            not (before |> optionSame first)
#else
            not (before |> Option.exists (same first))
#endif
        then
            let originalParent, originalNext = placement, last.nextSibling
            let staging = stagingFragment ()

            try
                for entry in entries do
                    let mutable cursor = Some entry.Node

                    while cursor.IsSome do
                        let node = cursor.Value

                        cursor <-
                            if same node entry.EndNode then
                                None
                            else
                                nextSiblingNode node

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

                            cursor <-
                                if same node entry.EndNode then
                                    None
                                else
                                    nextSiblingNode node

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

#if LADDER_NAIVE_RANGE
        for index in naiveRoots.Count - 1 .. -1 .. 0 do
            remove (naiveRoots[index] :?> ChildNode)

        naiveRoots.Clear ()

#endif
        if not (isNull generatedDescendants) then
            for index in generatedDescendants.Count - 1 .. -1 .. 0 do
                let node = at generatedDescendants index

                try
                    remove (node :?> ChildNode)
                with error ->
                    errors.Add error

                unclaim claims node

            generatedDescendants.Clear ()

        for index in entries.Count - 1 .. -1 .. 0 do
            let entry = at entries index
            let spans = not (same entry.Node entry.EndNode)

            match entry.Restore with
            | Some restore ->
                try
                    restore ()
                with error ->
                    errors.Add error
            | None ->
                try
                    remove (entry.Node :?> ChildNode)
                with error ->
                    errors.Add error

                if spans then
                    try
                        remove (entry.EndNode :?> ChildNode)
                    with error ->
                        errors.Add error

            unclaim claims entry.Node

            if spans then
                unclaim claims entry.EndNode

        entries.Clear ()

        if errors.Count = 1 then
            raise errors[0]
        elif errors.Count > 1 then
            raise (AggregateException ("Range release failed", errors))

/// <summary>How a keyed region's markers join the mount's range.</summary>
type private RegionPlacement =
    /// <summary>Markers created for an authored list at the range's top level.</summary>
    | NewRegion
    /// <summary>Markers already added as adjacent roots of the range.</summary>
    | RootRegion
    /// <summary>Markers inside an element.</summary>
    | NestedRegion

/// <summary>Arm selections are equal when both are null, or when their keys (<c>fst</c>) are equal under <c>=</c>.</summary>
let private sameArm =
    { new System.Collections.Generic.IEqualityComparer<obj> with
        member _.Equals(left, right) =
            if isNull left || isNull right then
                isNull left && isNull right
            else
                fst (unbox<obj * obj> left) = fst (unbox<obj * obj> right)

        member _.GetHashCode _ = 0
    }

/// <summary>The operations a view's build function uses during setup.</summary>
type BuildContext internal (state: MountState) =
    member _.Backend: Backend = state

    /// <summary>Adds a native node as a top-level root of the mount.</summary>
    member _.Native(node: Node, ownership: NativeOwnership) =
        state.Native (node, ownership)

    member _.OnCleanup(callback: unit -> unit) =
        state.Root.OnCleanup callback

    member _.Phase() =
        state.Root.Phase

and Mounted internal (state: MountState) =
    member _.Phase = state.Root.Phase
    member _.Failures = state.Root.Failures
    member _.CommitErrors = state.CommitErrors

    member _.Flush() =
        if not state.Root.IsDisposed then
            state.Flush ()

    /// <summary>Checks that the mount's recorded DOM range remains intact.</summary>
    member internal _.Validate() =
        state.Validate ()

    member internal _.Relocate() =
        state.Relocate ()

    member _.Dispose() =
        state.Root.Dispose ()

    member _.MoveBefore(parent, before) =
        if state.Root.Phase <> Attached then
            invalid "Only an attached mount can move"

        state.Move (parent, before)

/// <summary>The nodes of a row or arm, from its first node to its last.</summary>
and [<Sealed; AllowNullLiteral>] private Row(first: Node, last: Node) =
    member _.First = first
    member _.Last = last
    /// <summary>The row's position among its region's laid-out rows, or -1 before its first layout.</summary>
    member val Index = -1 with get, set
    member val Removed = false with get, set
    /// <summary>The nested mount of an authored row, or null for a template row.</summary>
    member val Mount: Mounted = Unchecked.defaultof<_> with get, set

    static member MoveBefore(row: Row, parent: Node, anchor: Node) =
        let mutable cursor = row.First
        let mutable going = true

        while going do
            let next = nextOrNull cursor

            if same cursor row.Last then
                going <- false

            parent.insertBefore (cursor, anchor) |> ignore
            cursor <- next

    static member Remove(row: Row) =
        if not row.Removed then
            row.Removed <- true
            let mutable cursor = row.First
            let mutable going = true

            while going do
                let next = nextOrNull cursor

                if same cursor row.Last then
                    going <- false

                remove (cursor :?> ChildNode)
                cursor <- next

    /// <summary>Removes the nodes between the markers, which are children of <c>parent</c>; both markers stay in place.</summary>
    static member ClearBetween(parent: Node, regionStart: Node, regionEnd: Node) =
        if
#if FABLE_COMPILER
            parent.firstChild |> optionSame regionStart
            && parent.lastChild |> optionSame regionEnd
#else
            parent.firstChild
            |> Option.exists (same regionStart)
            && parent.lastChild |> Option.exists (same regionEnd)
#endif
        then
            clearChildren parent
            parent.appendChild regionStart |> ignore
            parent.appendChild regionEnd |> ignore
        else
            deleteBetween regionStart regionEnd

    /// <summary>Raises unless the markers share <c>parent</c> and the laid-out rows sit between them in order.</summary>
    static member Validate(regionStart: Node, regionEnd: Node, laidOut: ResizeArray<Row>, parent: Node) =
        Row.ValidateWindow (regionStart, regionEnd, laidOut, parent, 0, laidOut.Count - 1)

    /// <summary>
    /// Requires the markers to share <c>parent</c>, and each non-removed laid-out row from <c>first</c> to <c>last</c>
    /// (clamped to the laid-out range) to start at the node after the preceding row, or after <c>regionStart</c>, and to
    /// pass its nested mount's validation. A window that reaches the final row also requires the next node to be
    /// <c>regionEnd</c>. Raises <c>InvalidOperationException</c> otherwise.
    /// </summary>
    static member ValidateWindow(regionStart: Node, regionEnd: Node, laidOut: ResizeArray<Row>, parent: Node, first: int, last: int) =
        if
#if FABLE_COMPILER
            not (regionStart.parentNode |> optionSame parent)
            || not (regionEnd.parentNode |> optionSame parent)
#else
            not (
                regionStart.parentNode
                |> Option.exists (same parent)
            )
            || not (
                regionEnd.parentNode
                |> Option.exists (same parent)
            )
#endif
        then
            invalid "Keyed-list boundaries were externally mutated"

        let last = min last (laidOut.Count - 1)
        let mutable index = max first 0

        let mutable cursor =
            if index = 0 then
                nextOrNull regionStart
            else
                nextOrNull laidOut[index - 1].Last

        while index <= last do
            let row = laidOut[index]

            if not row.Removed then
#if RANVIER_VIEW_COUNTERS
                ListCounters.validatedRows.Value <- ListCounters.validatedRows.Value + 1
#endif
                if not (isNull (box row.Mount)) then
                    row.Mount.Validate ()

                if not (same cursor row.First) then
                    invalid "Keyed-list rows or contents were externally mutated"

                cursor <- nextOrNull row.Last

            index <- index + 1

        if
            last = laidOut.Count - 1
            && not (same cursor regionEnd)
        then
            invalid "Keyed-list boundaries were externally reordered"

    static member Relocate(laidOut: ResizeArray<Row>) =
        for index in 0 .. laidOut.Count - 1 do
            let row = laidOut[index]

            if not (isNull (box row.Mount)) then
                row.Mount.Relocate ()

    static member private Place(row: Row, parent: Node, anchor: Node) =
        Row.MoveBefore (row, parent, anchor)

        if not (isNull (box row.Mount)) then
            row.Mount.Relocate ()

    /// <summary>
    /// Moves the region's rows into the order of <c>next</c> and stores it as the laid-out rows. A window that exchanges
    /// its first and last rows moves those two rows; any other window moves the rows outside one longest increasing run.
    /// Stops when <c>live</c> turns false.
    /// </summary>
    static member Layout(live: unit -> bool, laidOut: ResizeArray<Row>, staging: DocumentFragment, parent: Node, regionEnd: Node, next: Row array) =
        let previous = ResizeArray<Row>(laidOut.Count)

        for index in 0 .. laidOut.Count - 1 do
            let row = laidOut[index]

            if not row.Removed then
                row.Index <- previous.Count
                previous.Add row

        let mutable aStart = 0
        let mutable bStart = 0
        let mutable aEnd = previous.Count
        let mutable bEnd = next.Length

        while aStart < aEnd
              && bStart < bEnd
              && same previous[aStart] next[bStart] do
            aStart <- aStart + 1
            bStart <- bStart + 1

        while aEnd > aStart
              && bEnd > bStart
              && same previous[aEnd - 1] next[bEnd - 1] do
            aEnd <- aEnd - 1
            bEnd <- bEnd - 1

        if bStart < bEnd && live () then
            let anchor = if bEnd < next.Length then next[bEnd].First else regionEnd

            if aStart = aEnd then
                let staged =
#if FABLE_COMPILER
                    staging.firstChild
                    |> optionSame next[bStart].First
                    && staging.lastChild
                       |> optionSame next[bEnd - 1].Last
#else
                    staging.firstChild
                    |> Option.exists (same next[bStart].First)
                    && staging.lastChild
                       |> Option.exists (same next[bEnd - 1].Last)
#endif

                if staged then
                    parent.insertBefore (staging, anchor) |> ignore
                else
                    let mutable index = bStart

                    while index < bEnd && live () do
                        Row.Place (next[index], parent, anchor)
                        index <- index + 1
            else
                let count = bEnd - bStart

                let mutable swapped =
                    count >= 2
                    && aEnd - aStart = count
                    && same previous[aStart] next[bEnd - 1]
                    && same previous[aEnd - 1] next[bStart]

                let mutable offset = 1

                while swapped && offset < count - 1 do
                    swapped <- same previous[aStart + offset] next[bStart + offset]
                    offset <- offset + 1

                if swapped then
                    Row.Place (next[bStart], parent, previous[aStart].First)

                    if count > 2 && live () then
                        Row.Place (next[bEnd - 1], parent, anchor)
                else
                    let sources = Array.create count -1

                    for offset in 0 .. count - 1 do
                        let index = next[bStart + offset].Index

                        if
                            index >= aStart
                            && index < aEnd
                            && same previous[index] next[bStart + offset]
                        then
                            sources[offset] <- index

                    let keep = increasingRun sources
                    let mutable before = anchor
                    let mutable offset = count - 1

                    while offset >= 0 && live () do
                        let row = next[bStart + offset]

                        if not keep[offset] then
                            Row.Place (row, parent, before)

                        before <- row.First
                        offset <- offset - 1

        laidOut.Clear ()

        for index in 0 .. next.Length - 1 do
            next[index].Index <- index
            laidOut.Add next[index]

/// <summary>Materialises the template rows and arms of one region into the region's staging fragment and serves their setup.</summary>
and private RowBuilder(state: MountState) =
    let roots = ResizeArray<Node>()
    let mutable staging: DocumentFragment = Unchecked.defaultof<_>
    let mutable settingUp = false
    let mutable setupFailure: exn option = None

    let setup: Setup =
        {
            OnCleanup = fun callback -> state.OwnedCleanup (Api.getOwner (), callback)
        }

    member _.Staging =
        if isNull (box staging) then
            staging <- fragment ()

        staging

    member this.Build(materialise: Func<(unit -> obj), TemplateDom.MountServices, unit>, current: unit -> obj) : Row =
        roots.Clear ()
        setupFailure <- None
        settingUp <- true

        try
            try
                materialise.Invoke (current, this :> TemplateDom.MountServices)
            with _ ->
                for index in 0 .. roots.Count - 1 do
                    remove (roots[index] :?> ChildNode)

                reraise ()
        finally
            settingUp <- false

        if roots.Count = 0 then
            let marker = comment "ranvier:row"
            this.Staging.appendChild marker |> ignore
            roots.Add marker

        Row (roots[0], roots[roots.Count - 1])

    interface TemplateDom.MountServices with
        member this.Append(node) =
            this.Staging.appendChild node |> ignore
            roots.Add node

        member _.Setup = setup

        member _.SettingUp() = settingUp

        member _.SetupFailed(error) =
            setupFailure <- Some error

        member _.RaiseSetupFailure() =
            setupFailure |> Option.iter raise

        member _.IsDelegated(eventName) =
            isDelegated delegatedEvents eventName

        member _.Delegate(eventName) =
            state.DelegateLate eventName

        member _.ListenDirect(node, eventName, invoke) =
            state.ListenDirect (node, eventName, invoke)

        member _.ReportFailure(error) =
            state.ReportReaderFailure error

        member _.ClearFailure() =
            state.ClearReported ()

        member _.Region(start, finish, _, kind, items, render, rows) =
            state.NestedRegion (start, finish, kind, items, render, rows)

/// <summary>
/// The markers and laid-out rows of one region, with the region's owner, row builder and failure state.
/// </summary>
and private RegionSite(state: MountState, regionStart: Node, regionEnd: Node, report: exn list -> unit, releaseNodes: Row -> unit) =
    let laidOut = ResizeArray<Row>()
    let errors = RegionErrors report
    // Template rows released while `Deferring` is set, with their nodes still in place.
    let pending = ResizeArray<Row>()
    let mutable owner: Owner = Unchecked.defaultof<_>
    let mutable builder: RowBuilder = Unchecked.defaultof<_>

    member _.LaidOut = laidOut
    member _.Errors = errors

    /// <summary>The region's owner, set by <c>Own</c>.</summary>
    member _.Owner = owner

    member _.Builder =
        if isNull (box builder) then
            builder <- RowBuilder state

        builder

    /// <summary>Whether the mount and the region's owner are both live.</summary>
    member _.Live() =
        not state.Root.IsDisposed && not owner.IsDisposed

    /// <summary>The markers' shared parent. Raises when the markers have no parent or different parents.</summary>
    member _.Parent() : Node =
        let regionParent =
            regionStart.parentNode
            |> Option.defaultWith (fun () -> invalid "Keyed-list boundaries have no parent")

        if
#if FABLE_COMPILER
            not (regionEnd.parentNode |> optionSame regionParent)
#else
            not (
                regionEnd.parentNode
                |> Option.exists (same regionParent)
            )
#endif
        then
            invalid "Keyed-list boundaries were externally mutated"

        regionParent

    member this.Validate() =
        Row.Validate (regionStart, regionEnd, laidOut, this.Parent ())

    /// <summary>Whether <c>ReleaseRow</c> holds released template rows for <c>FlushReleased</c> while the mount is live.</summary>
    member val Deferring = false with get, set

    /// <summary>
    /// Marks an authored row removed, and a template row removed while the region's owner is disposed in a live mount:
    /// its nodes leave with the enclosing row. Holds a template row for <c>FlushReleased</c> while <c>Deferring</c> is
    /// set and the mount is live, and otherwise releases its nodes through the mount.
    /// </summary>
    member this.ReleaseRow(row: Row) =
        if not (isNull row) then
            if
                not (isNull (box row.Mount))
                || not (isNull (box owner))
                   && owner.IsDisposed
                   && not state.Root.IsDisposed
            then
                row.Removed <- true
            elif this.Deferring && not state.Root.IsDisposed then
                pending.Add row
            else
                releaseNodes row

    /// <summary>Whether the held rows are exactly the region's live laid-out rows.</summary>
    /// <remarks>Guards an invariant: a successful pass that empties the list holds every live laid-out row.</remarks>
    member _.ReleasedAll() =
        let mutable live = 0

        for index in 0 .. laidOut.Count - 1 do
            if not laidOut[index].Removed then
                live <- live + 1

        let mutable all = live = pending.Count
        let mutable index = 0

        while all && index < pending.Count do
            let row = pending[index]

            all <-
                row.Index >= 0
                && row.Index < laidOut.Count
                && same laidOut[row.Index] row

            index <- index + 1

        all

    /// <summary>
    /// Releases the held rows. When <c>cleared</c> and the markers share a parent, removes the region's contents in one
    /// operation and marks the rows removed; otherwise releases each row's nodes through the mount, in release order.
    /// </summary>
    member _.FlushReleased(cleared: bool) =
        if pending.Count > 0 then
            let rows = pending.ToArray ()
            pending.Clear ()

            let parent =
                match regionStart.parentNode with
                | Some parent when isChildOf regionEnd parent -> parent
                | _ -> Unchecked.defaultof<_>

            if cleared && not (isNull (box parent)) then
                Row.ClearBetween (parent, regionStart, regionEnd)

                for index in 0 .. rows.Length - 1 do
                    rows[index].Removed <- true
            else
                let errors = ResizeArray<exn>()

                for index in 0 .. rows.Length - 1 do
                    try
                        releaseNodes rows[index]
                    with error ->
                        errors.Add error

                if errors.Count = 1 then
                    raise errors[0]
                elif errors.Count > 1 then
                    raise (AggregateException ("Row release failed", errors))

    /// <summary>
    /// Runs <c>body</c> in a new root owner of the region, then raises the setup failure recorded during that first pass.
    /// </summary>
    member _.Own(body: unit -> unit) =
        Api.createRoot (fun created ->
            owner <- created
            body ())

        errors.Initializing <- false
        errors.SetupFailure |> Option.iter raise

    /// <summary>
    /// Ends a pass. A region disposed during the pass reports <c>recorded</c>; otherwise a failure is recorded or
    /// reported with <c>recorded</c>, and a success runs <c>settle recorded</c>.
    /// </summary>
    member this.Settle(recorded: exn list, failure: exn option, settle: exn list -> unit) =
        if not (this.Live ()) then
            errors.Report recorded
        else
            match failure with
            | Some error -> errors.Fail (error, error :: recorded)
            | None -> settle recorded

/// <summary>The state of a mounted view and the backend that builds it.</summary>
and internal MountState(scheduling: Scheduling, graph: Graph, parent: MountLifetime, parentOwner: Owner, report: exn list -> unit, host: Node) =
    inherit Prepared.Backend()

    let range = OwnedRange host

    // Template rows retired while the mount tears down, before its release; their nodes leave in the release, before
    // the range's. A row retired after the release removes its nodes at once. A row held for a region's flush leaves
    // in that flush, which can follow the release.
    let mutable releasedRows: ResizeArray<Row> = null
    let mutable rowsReleased = false

    // Removes the nodes of a template row. A row whose first node is detached left with an enclosing row's nodes and is
    // only marked removed.
    let removeRow (row: Row) =
        if isDetached row.First then
            row.Removed <- true
        else
            Row.Remove row

    let release () =
        rowsReleased <- true
        let errors = ResizeArray<exn>()

        if not (isNull releasedRows) then
            let rows = releasedRows
            releasedRows <- null

            for index in 0 .. rows.Count - 1 do
                try
                    removeRow rows[index]
                with error ->
                    errors.Add error

        try
            range.Release ()
        with error ->
            errors.Add error

        if errors.Count = 1 then
            raise errors[0]
        elif errors.Count > 1 then
            raise (AggregateException ("Range release failed", errors))

    let ownedCleanup (owner: Owner) (callback: unit -> unit) =
        if owner.IsDisposed then
            invalid "Component cleanup registration requires a live component"

        owner.OnCleanup (Action callback)

    let mutable root: MountLifetime = null

    // Removes the nodes of a template row; while the mount tears down, before its release, queues them for the release.
    let releaseNodes (row: Row) =
        if root.IsDisposed && not rowsReleased then
            if isNull releasedRows then
                releasedRows <- ResizeArray ()

            releasedRows.Add row
        else
            removeRow row

    // The element receiving created nodes; null at the top level of the range.
    let mutable target: Node = Unchecked.defaultof<_>
    let mutable bindingSetupFailure: exn option = None
    let mutable modifierNode: HTMLElement = Unchecked.defaultof<_>
    let mutable modifierTag = Div

    let ensureSetup () =
#if LADDER_NAIVE_CHECKS
        ()
#else
        if root.Phase <> SettingUp then
            invalid "View construction is only valid during live setup"
#endif

    let append node =
        ensureSetup ()

#if LADDER_NAIVE_RANGE
        if isNull (box target) then
            range.AddNaive node
        else
            target.appendChild node |> ignore
#else
        if isNull (box target) then
            range.AddCreated node
        else
            target.appendChild node |> ignore
#endif

    let currentElement () =
#if !LADDER_NAIVE_CHECKS
        ensureSetup ()

        if isNull (box modifierNode) then
            invalid "Element modifiers require an active element"

#endif
        modifierNode

    let applyModifiers (backend: Backend) tag node (modifiers: ModifierAction array) =
        let previousNode = modifierNode
        let previousTag = modifierTag
        modifierNode <- node
        modifierTag <- tag

        try
            for index in 0 .. modifiers.Length - 1 do
                ensureSetup ()
                modifiers[index] backend
                ensureSetup ()
        finally
            modifierNode <- previousNode
            modifierTag <- previousTag

    let openElement (backend: Backend) tag modifiers =
        ensureSetup ()
        let node = document.createElement (name tag)
        append node

        if tag <> Select then
            applyModifiers backend tag node modifiers

        node

    let childParent tag (node: HTMLElement) : Node =
        if name tag = "template" then
            (node :?> HTMLTemplateElement).content :> Node
        else
            node

    let mutable lastReported: exn = null

    // Sends a reader failure to `report`, untracked, once per exception object until `clearReported` runs.
    // A pending read (`NotReadyException`) is not reported.
    let reportReaderFailure (error: exn) =
        match error with
        | NotReadyException _ -> ()
        | _ when obj.ReferenceEquals (error, lastReported) -> ()
        | _ ->
            lastReported <- error

            try
                Api.untrack (fun () -> report [ error ])
            with reporterError ->
                raise (AggregateException ("Binding reader and reporter failed", [| error; reporterError |]))

    // Called when a failed reader recovers; the same exception object is reported again if it recurs.
    let clearReported () =
        lastReported <- null

    let startBinding (equals: Func<_, _, bool>) read write =
#if LADDER_NAIVE_BINDINGS
        Api.createEffect (fun () -> write (read ()))
#else
        match scheduling with
        | Synchronous ->
            let mutable hasCommitted = false
            let mutable committed = Unchecked.defaultof<_>
            let mutable pending = Unchecked.defaultof<_>

            let commit () =
                let changed =
                    not hasCommitted
                    || not (equals.Invoke (committed, pending))

                if changed && not root.IsDisposed then
                    try
                        write pending
                        committed <- pending
                        hasCommitted <- true
                    with error when root.Phase = SettingUp ->
                        bindingSetupFailure <- Some error
                        reraise ()

            let mutable failed = false

            Api.createEffect (fun () ->
                let value =
                    try
                        read ()
                    with error ->
                        failed <- true
                        reportReaderFailure error
                        reraise ()

                if failed then
                    failed <- false
                    clearReported ()

                if not root.IsDisposed then
                    pending <- value
                    Api.untrack commit)
        | Queued queue ->
            let live () =
                not root.IsDisposed

            let guardedWrite value =
                try
                    write value
                with error when root.Phase = SettingUp ->
                    bindingSetupFailure <- Some error
                    reraise ()

            let mutable failed = false

            let reportingRead () =
                let value =
                    try
                        read ()
                    with error ->
                        failed <- true
                        reportReaderFailure error
                        reraise ()

                if failed then
                    failed <- false
                    clearReported ()

                value

            Binding.startQueued root.OnCleanup live queue (fun left right -> equals.Invoke (left, right)) reportingRead guardedWrite

        bindingSetupFailure |> Option.iter raise
#endif

    // A set of event names; null until a delegated event is registered.
    let mutable delegatedNames: obj = null
    let mutable delegatedHost: Node = Unchecked.defaultof<_>

    let delegateAt (host: Node) =
        if not (isNull delegatedNames) then
            setIter delegatedNames (fun eventName -> delegateFrom.Invoke (host, eventName))

        delegatedHost <- host

    let releaseDelegation () =
        if
            not (isNull (box delegatedHost))
            && not (isNull delegatedNames)
        then
            let releasedHost = delegatedHost
            setIter delegatedNames (fun eventName -> undelegateFrom.Invoke (releasedHost, eventName))
            delegatedHost <- Unchecked.defaultof<_>

    let mutable preparedRoot: Node = Unchecked.defaultof<_>

    let ensurePrepared (node: Node) =
        ensureSetup ()

        if isNull (box preparedRoot) then
            invalid "Prepared node operations require active clone setup"

        let mutable belongs = contains preparedRoot node

        if not belongs then
            walkPrepared
                (fun current ->
                    if same current node then
                        belongs <- true)
                preparedRoot

        if not belongs then
            invalid "Prepared node operations require a node from the current clone"

    let registerDelegated (eventName: string) =
        if isNull delegatedNames then
            delegatedNames <- nameSet ()

        setAdd delegatedNames eventName

    let mutable delegationReleased = false

    // A row registers delegated events after its mount attached: the name joins the mount's host at once.
    let delegateLate (eventName: string) =
        if
            isNull delegatedNames
            || not (setHas delegatedNames eventName)
        then
            registerDelegated eventName

            if not (isNull (box delegatedHost)) then
                delegateFrom.Invoke (delegatedHost, eventName)

                if not delegationReleased then
                    delegationReleased <- true
                    root.OnCleanup releaseDelegation

    let listenDirect (node: Node) eventName (invoke: obj -> unit) =
        let cleanup = Typed.listenDirect (box node) eventName invoke

        if root.IsDisposed then
            cleanup ()
            ensureSetup ()

        ownedCleanup (Api.getOwner ()) cleanup

    let mutable templateSetup: Setup = Unchecked.defaultof<_>

    member _.Root: MountLifetime = root

    member internal _.OwnedCleanup(owner: Owner, callback: unit -> unit) =
        ownedCleanup owner callback

    member internal _.DelegateLate(eventName: string) =
        delegateLate eventName

    member internal _.ListenDirect(node: Node, eventName: string, invoke: obj -> unit) =
        listenDirect node eventName invoke

    member internal _.ReportReaderFailure(error: exn) =
        reportReaderFailure error

    member internal _.ClearReported() =
        clearReported ()

    member _.Native(node: Node, ownership: NativeOwnership) : unit =
        ensureSetup ()

        if not (isNull (box target)) then
            invalid "Native roots require top-level placement"

        range.Add (node :?> ChildNode, ownership)

    member _.Move(parent: Node, before: Node option) : unit =
        range.Move (parent, before, root.Dispose)

        if not (same parent delegatedHost) then
            releaseDelegation ()
            delegateAt parent

    member _.Validate() : unit =
        range.Validate ()

    member _.Relocate() : unit =
        range.Relocate ()

        if
            not (isNull delegatedNames)
            && not (same range.Placement delegatedHost)
        then
            releaseDelegation ()
            delegateAt range.Placement

    member _.Flush() : unit =
        match scheduling with
        | Synchronous -> graph.Run Api.flush
        | Queued queue -> queue.Flush ()

    member _.CommitErrors: exn array =
        match scheduling with
        | Synchronous -> [||]
        | Queued queue -> queue.Errors

    /// <summary>Builds the view, attaches it before <c>before</c> in the host, and returns its handle.</summary>
    /// <remarks>A build or attachment failure disposes the mount and rethrows.</remarks>
    member this.Run(before: Node option, build: BuildContext -> unit) =
        let context = BuildContext this

        try
            graph.Run (fun () ->
                let create () =
                    Api.createRoot (fun owner ->
                        root <- MountLifetime.Create (graph, owner, report, release, parent)
                        Api.untrack (fun () -> build context))

                if isNull (box parentOwner) then
                    create ()
                else
                    Api.runWithOwner parentOwner create)

            ensureSetup ()
            range.Attach (host, before)
            root.MarkAttached ()
            delegateAt host

            if not (isNull delegatedNames) then
                delegationReleased <- true
                root.OnCleanup releaseDelegation

            Mounted this
        with _ ->
            if not (isNull root) then
                root.Abandon ()

            reraise ()

    override _.Clone(template, setup) =
        ensureSetup ()
        let node = template.cloneNode true :?> HTMLElement
        append node
        let previous = preparedRoot
        preparedRoot <- node

        try
            setup node
            ensureSetup ()
        finally
            preparedRoot <- previous

    override this.Apply(node, modifiers) =
        ensurePrepared node

        let tag =
            if node.tagName = "INPUT" then
                Input
            else
                HtmlTag node.localName

        applyModifiers this tag node modifiers

    override _.BindText(node, read) =
        ensurePrepared node

        if node.nodeType <> 3. then
            raise (ArgumentException ("Prepared text bindings require a text node", "node"))

        startBinding (Func<_, _, _>(fun left right -> left = right)) read (fun value -> node.textContent <- Some value)
        ensureSetup ()

    override _.Text(value) =
        ensureSetup ()
        append (text value)

    override _.Attribute(key, value) =
        let node = currentElement ()
#if !LADDER_NAIVE_CHECKS && !LADDER_UNCHECKED_ATTRIBUTES
        validateAttribute key
#endif
        node.setAttribute (key, value)

    override _.OptionalAttribute(key, value) =
        let node = currentElement ()
#if !LADDER_NAIVE_CHECKS && !LADDER_UNCHECKED_ATTRIBUTES
        validateAttribute key
#endif
        attributeValue node key value

    override _.ReactiveAttribute(key, read) =
        let node = currentElement ()
#if !LADDER_NAIVE_CHECKS && !LADDER_UNCHECKED_ATTRIBUTES
        validateAttribute key
#endif
        startBinding attributeEquals read (attributeValue node key)

    override _.InputValue(value) =
        let node = currentElement ()

        if modifierTag <> Input then
            invalidArg "modifiers" "Input value requires input"

        inputValue (node :?> HTMLInputElement) value

    override _.Browser(hook) =
        let cleanup = hook (box (currentElement ()))

        if root.IsDisposed then
            cleanup ()
            ensureSetup ()

        ownedCleanup (Api.getOwner ()) cleanup

    override _.Listen(eventName, handler) =
        let node = currentElement ()
        let invoke = Typed.ownedHandler handler

        if isDelegated delegatedEvents eventName then
            setDelegatedHandler node eventName invoke
            registerDelegated eventName
        else
            listenDirect node eventName invoke

    override _.Property(write) =
        write (box (currentElement ()))

    override _.ReactiveProperty(read, write) =
        let node = currentElement ()
        startBinding propertyEquals read (fun value -> write.Invoke (box node, value))

    override this.Element(tag, modifiers, children) =
        let node = openElement this tag modifiers

        if not (isVoid tag) then
            let previous = target
            target <- childParent tag node

            try
                children ()
            finally
                target <- previous

        if tag = Select then
            applyModifiers this tag node modifiers

    override this.ElementWith(tag, modifiers, children) =
        let node = openElement this tag modifiers

        if not (isVoid tag) then
            let previous = target
            target <- childParent tag node

            try
                for index in 0 .. children.Length - 1 do
                    children[index] this
            finally
                target <- previous

        if tag = Select then
            applyModifiers this tag node modifiers

    override _.Bind(equals, read) =
        ensureSetup ()
        let node = text ""
        append node
        startBinding (Func<_, _, _>(fun left right -> equals left right)) read (fun value -> node.textContent <- value)

    override this.Keyed(items, render) =
        this.AuthoredRegion (TemplateDom.ListRows, items, render)

    override this.Switch(read, render) =
        this.AuthoredRegion (TemplateDom.SelectedArm, KeyedItems.ofPairs read, render)

    /// <summary>Adds boundary nodes at the current position and mounts a region of <c>kind</c> between them.</summary>
    member private this.AuthoredRegion(kind, items, render) =
        ensureSetup ()

        let regionStart, regionEnd =
            comment "ranvier:list:start", comment "ranvier:list:end"

        let placement =
            if isNull (box target) then
                NewRegion
            else
                range.AppendGenerated (target, regionStart)
                range.AppendGenerated (target, regionEnd)
                NestedRegion

        this.RegionOf (regionStart, regionEnd, placement, kind, items, render, null)

    member internal this.NestedRegion
        (
            start: Node,
            finish: Node,
            kind: TemplateDom.RegionKind,
            items: KeyedItems,
            render: (unit -> obj) -> Backend -> unit,
            rows: Func<(unit -> obj), TemplateDom.MountServices, unit>
        ) =
        this.RegionOf (start, finish, NestedRegion, kind, items, render, rows)

    member private _.PlaceRegion(regionStart: Node, regionEnd: Node, placement: RegionPlacement, validate: unit -> unit, relocate: unit -> unit) =
        match placement with
        | NestedRegion -> ()
        | NewRegion -> range.AddRegion (regionStart, regionEnd, validate, relocate)
        | RootRegion -> range.SpanRegion (regionStart, regionEnd, validate, relocate)

    /// <summary>The site of a region between <c>regionStart</c> and <c>regionEnd</c>, placed in the mount's range.</summary>
    member private this.Site(regionStart: Node, regionEnd: Node, placement: RegionPlacement) : RegionSite =
        let site = RegionSite (this, regionStart, regionEnd, report, releaseNodes)

        this.PlaceRegion (regionStart, regionEnd, placement, (fun () -> site.Validate ()), (fun () -> Row.Relocate site.LaidOut))

        site

    /// <summary>Mounts an authored row as a nested mount and returns its nodes.</summary>
    member private _.MountRow(current: unit -> obj, render: (unit -> obj) -> Backend -> unit) : Row =
        let rowHost = fragment ()

        let mounted =
            MountState(scheduling, graph, root, Unchecked.defaultof<_>, report, rowHost).Run(None, (fun context -> render current context.Backend))

        let first =
            rowHost.firstChild
            |> Option.defaultWith (fun () -> invalid "A keyed row has no start boundary")

        let last =
            rowHost.lastChild
            |> Option.defaultWith (fun () -> invalid "A keyed row has no end boundary")

        Row (first, last, Mount = mounted)

    /// <summary>
    /// Mounts one row per keyed item between the region's markers, on a core projection that creates new rows before it
    /// retires removed ones. Template rows (<c>rows</c>) are cloned into the region; authored rows (<c>render</c>) are
    /// nested mounts.
    /// </summary>
    member private this.ListRegion
        (
            regionStart: Node,
            regionEnd: Node,
            placement: RegionPlacement,
            items: KeyedItems,
            render: (unit -> obj) -> Backend -> unit,
            rows: Func<(unit -> obj), TemplateDom.MountServices, unit>
        ) =
        let site = this.Site (regionStart, regionEnd, placement)
        let mutable projection: Projection<obj, Row> = Unchecked.defaultof<_>
        // The items of the latest reconcile, the projection's only input; each write is followed by a bump of `version`,
        // which marks the projection stale.
        let mutable version: Signal<int> = Unchecked.defaultof<_>
        let mutable snapshot: obj array = [||]
        // The items and rows of the last successful pass; empty until a pass succeeds, and after a failed pass. The items
        // are the region's own copy.
        let mutable previousItems: obj array = [||]
        let mutable previousRows: Row array = [||]

        let buildRow (current: unit -> obj) : Row =
            let mutable row: Row = null

            // Runs after the row's own cleanups.
            Api.onCleanup (fun () -> site.ReleaseRow row)

            row <-
                if isNull rows then
                    this.MountRow (current, render)
                else
                    site.Builder.Build (rows, current)

            row

        let reconcile () =
            if site.Live () then
                let parent = ref Unchecked.defaultof<Node>
                let keys = ref Unchecked.defaultof<obj array>
                let priorItems = previousItems
                let priorRows = previousRows
                previousItems <- [||]
                previousRows <- [||]
                // Items before `start` and after `end0` (previous) or `end1` (current) are the same references in both passes.
                let start = ref 0
                let end0 = ref -1
                let end1 = ref -1
                let passItems = ref ([||]: obj array)

                let recorded, failure =
                    MountLifetime.Capture (
                        graph,
                        report,
                        fun () ->
                            let current = items.Read ()
                            snapshot <- current
                            passItems.Value <- current
                            Api.untrack (fun () -> version.Value <- version.Value + 1)
                            parent.Value <- site.Parent ()
                            let n0 = priorItems.Length
                            let n1 = current.Length

                            while start.Value < n0
                                  && start.Value < n1
                                  && obj.ReferenceEquals (priorItems[start.Value], current[start.Value]) do
                                start.Value <- start.Value + 1

                            end0.Value <- n0 - 1
                            end1.Value <- n1 - 1

                            while end0.Value >= start.Value
                                  && end1.Value >= start.Value
                                  && obj.ReferenceEquals (priorItems[end0.Value], current[end1.Value]) do
                                end0.Value <- end0.Value - 1
                                end1.Value <- end1.Value - 1

                            if priorRows.Length = site.LaidOut.Count then
                                Row.ValidateWindow (regionStart, regionEnd, site.LaidOut, parent.Value, start.Value - 1, end0.Value + 1)
                            else
                                Row.Validate (regionStart, regionEnd, site.LaidOut, parent.Value)
                            // Runs the pass inside the window: its row setup, rollback and retirement errors are reported with this update.
                            // Rows retired by the pass keep their nodes until the flush.
                            site.Deferring <- true
                            let errors = ResizeArray<exn>()

                            try
                                try
                                    keys.Value <- Api.untrack (fun () -> projection.Keys)
                                finally
                                    site.Deferring <- false
                            with error ->
                                errors.Add error

                            try
                                site.FlushReleased (
                                    not (isNull keys.Value)
                                    && keys.Value.Length = 0
                                    && site.Live ()
                                    && site.ReleasedAll ()
                                )
                            with error ->
                                errors.Add error

                            if errors.Count = 1 then
                                raise errors[0]
                            elif errors.Count > 1 then
                                raise (AggregateException ("Keyed update and row release failed", errors))
                    )

                site.Settle (
                    recorded,
                    failure,
                    fun recorded ->
                        let layout =
                            try
                                Api.untrack (fun () ->
                                    let keys = keys.Value
                                    let next: Row array = Array.zeroCreate keys.Length

                                    for index in 0 .. keys.Length - 1 do
                                        let kept =
                                            if index < start.Value then
                                                priorRows[index]
                                            elif index > end1.Value then
                                                priorRows[end0.Value + index - end1.Value]
                                            else
                                                null

                                        next[index] <-
                                            if isNull kept || kept.Removed then
#if RANVIER_VIEW_COUNTERS
                                                ListCounters.lookups.Value <- ListCounters.lookups.Value + 1
#endif
                                                projection.Get keys[index]
                                            else
                                                kept

                                    Row.Layout ((fun () -> site.Live ()), site.LaidOut, site.Builder.Staging, parent.Value, regionEnd, next)
                                    next)
                                |> Ok
                            with error ->
                                Error error

                        match layout with
                        // A layout that disposed its own region reports the pass's errors and ends the update.
                        | Error _ when not (site.Live ()) -> site.Errors.Report recorded
                        | Error error ->
                            root.Dispose ()
                            site.Errors.Fail (error, recorded @ [ error ])
                        | Ok next ->
                            if site.Live () then
                                previousItems <- Array.copy passItems.Value
                                previousRows <- next

                            site.Errors.Report recorded
                )

        site.Own (fun () ->
            version <- Api.createSignal 0

            projection <-
                Api.createProjectionOnce ProjectionOptions.CreateFirst items.Key.Invoke buildRow (fun () ->
                    version.Value |> ignore
                    snapshot :> seq<_>)

            Api.untrack (fun () -> Api.createEffect reconcile))

    /// <summary>
    /// Mounts the arm selected by the region's first item between its markers. An arm stays while its key is unchanged
    /// under <c>=</c> and receives the item's new value; another key disposes the arm, its cleanups and its nodes, before
    /// the next arm is set up.
    /// </summary>
    member private this.ArmRegion
        (
            regionStart: Node,
            regionEnd: Node,
            placement: RegionPlacement,
            read: unit -> obj array,
            render: (unit -> obj) -> Backend -> unit,
            rows: Func<(unit -> obj), TemplateDom.MountServices, unit>
        ) =
        let site = this.Site (regionStart, regionEnd, placement)
        let mutable items: Memo<obj array> = Unchecked.defaultof<_>
        let mutable selection: Memo<obj> = Unchecked.defaultof<_>
        // The mounted arm's root owner, item signal and key; null while the region is empty.
        let mutable armOwner: Owner = Unchecked.defaultof<_>
        let mutable armItem: Signal<obj> = Unchecked.defaultof<_>
        let mutable armKey: obj = null

        let disposeArm () =
            if not (isNull (box armOwner)) then
                let owner = armOwner
                armOwner <- Unchecked.defaultof<_>
                armItem <- Unchecked.defaultof<_>
                armKey <- null
                site.LaidOut.Clear ()
                Api.batch (fun () -> owner.Dispose ())

        let mountArm (selected: obj) =
            if not (isNull selected) then
                let key, value = unbox<obj * obj> selected
                let item = Api.createSignal value
                let mutable created: Owner = Unchecked.defaultof<_>
                let mutable row: Row = null

                try
                    Api.runWithOwner site.Owner (fun () ->
                        Api.createRoot (fun owner ->
                            created <- owner
                            root.Adopt owner

                            // Runs after the arm's own cleanups.
                            Api.onCleanup (fun () -> site.ReleaseRow row)

                            let current () =
                                item.Value

                            row <-
                                if isNull rows then
                                    this.MountRow (current, render)
                                else
                                    site.Builder.Build (rows, current)))

                    // An arm whose setup disposed the region leaves with the region's owner.
                    if site.Live () then
                        Row.MoveBefore (row, site.Parent (), regionEnd)

                        if not (isNull (box row.Mount)) then
                            row.Mount.Relocate ()

                        site.LaidOut.Add row
                        armOwner <- created
                        armItem <- item
                        armKey <- key
                with _ ->
                    if not (isNull (box created)) then
                        created.Dispose ()

                    reraise ()

        let reconcile () =
            if site.Live () then
                let recorded, failure =
                    MountLifetime.Capture (
                        graph,
                        report,
                        fun () ->
                            let selected = selection.Value

                            Api.untrack (fun () ->
                                site.Validate ()
                                disposeArm ()
                                mountArm selected)
                    )

                site.Settle (recorded, failure, (fun recorded -> site.Errors.Report recorded))

        let updatePayload () =
            let current = items.Value

            Api.untrack (fun () ->
                if current.Length > 0 && not (isNull (box armItem)) then
                    let key, value = unbox<obj * obj> current[0]

                    if key = armKey then
                        armItem.Value <- value)

        site.Own (fun () ->
            items <- Api.createMemo (fun _ -> read ())

            selection <-
                Api.createMemoWithComparer sameArm (fun _ ->
                    let current = items.Value

                    if current.Length = 0 then null else current[0])

            Api.untrack (fun () -> Api.createEffect reconcile)
            Api.untrack (fun () -> Api.createEffect updatePayload))

    /// <summary>Mounts the view read once from the region's single item between its markers.</summary>
    member private this.HoleRegion
        (regionStart: Node, regionEnd: Node, placement: RegionPlacement, read: unit -> obj array, render: (unit -> obj) -> Backend -> unit)
        =
        let site = this.Site (regionStart, regionEnd, placement)
        let items = read ()
        let view = KeyedItems.value (items[0])
        let row = this.MountRow ((fun () -> view), render)
        Row.MoveBefore (row, site.Parent (), regionEnd)
        row.Mount.Relocate ()
        site.LaidOut.Add row

    member private this.RegionOf
        (
            start: Node,
            finish: Node,
            placement: RegionPlacement,
            kind: TemplateDom.RegionKind,
            items: KeyedItems,
            render: (unit -> obj) -> Backend -> unit,
            rows: Func<(unit -> obj), TemplateDom.MountServices, unit>
        ) =
        match kind with
        | TemplateDom.ListRows -> this.ListRegion (start, finish, placement, items, render, rows)
        | TemplateDom.SelectedArm -> this.ArmRegion (start, finish, placement, items.Read, render, rows)
        | TemplateDom.ViewHole -> this.HoleRegion (start, finish, placement, items.Read, render)

    override _.Component(setup) =
        ensureSetup ()
#if LADDER_NAIVE_COMPONENTS
        setup
            {
                OnCleanup = ownedCleanup (Api.getOwner ())
            }
#else
        Api.createRoot (fun owner ->
            root.Adopt owner

            Api.untrack (fun () ->
                setup
                    {
                        OnCleanup = fun callback -> ownedCleanup owner callback
                    })

            if owner.IsDisposed then
                invalid "View construction is only valid during live setup")
#endif

    override this.Template(definition, props) =
        ensureSetup ()

        match scheduling with
        | Synchronous -> TemplateDom.materialise definition props (this :> TemplateDom.MountServices)
        | Queued _ -> definition.Expand (this, props)

    interface TemplateDom.MountServices with
        member _.Append(node) =
            append (node :?> ChildNode)

        member _.Setup =
            if isNull (box templateSetup) then
                templateSetup <-
                    {
                        OnCleanup = fun callback -> ownedCleanup (Api.getOwner ()) callback
                    }

            templateSetup

        member _.SettingUp() =
            root.Phase = SettingUp

        member _.SetupFailed(error) =
            bindingSetupFailure <- Some error

        member _.RaiseSetupFailure() =
            bindingSetupFailure |> Option.iter raise

        member _.IsDelegated(eventName) =
            isDelegated delegatedEvents eventName

        member _.Delegate(eventName) =
            registerDelegated eventName

        member _.ListenDirect(node, eventName, invoke) =
            listenDirect node eventName invoke

        member _.ReportFailure(error) =
            reportReaderFailure error

        member _.ClearFailure() =
            clearReported ()

        member this.Region(start, finish, rootLevel, kind, items, render, rows) =
            ensureSetup ()

            let placement =
                if rootLevel && isNull (box target) then
                    RootRegion
                else
                    NestedRegion

            this.RegionOf (start, finish, placement, kind, items, render, rows)

/// <summary>Mounts a view with an owned commit queue when deferred scheduling is selected.</summary>
/// <remarks>Nested keyed mounts share the containing mount's scheduler.</remarks>
let mountWith mode graph (parentOwner: Owner option) report host before build =
    let parentOwner =
        match parentOwner with
        | Some owner -> owner
        | None -> Unchecked.defaultof<_>

    match mode with
    | SynchronousCommits -> MountState(Synchronous, graph, null, parentOwner, report, host).Run(before, build)
    | QueuedCommits (failure, post) ->
        let queue = Queue (graph, failure, post)

        try
            let state = MountState (Queued queue, graph, null, parentOwner, report, host)
            let mounted = state.Run (before, build)
            state.Root.OnCleanup queue.Dispose
            mounted
        with _ ->
            queue.Dispose ()
            reraise ()

let mount graph (parentOwner: Owner option) report host before build =
    mountWith SynchronousCommits graph parentOwner report host before build
