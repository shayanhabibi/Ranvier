module LifetimeChecks

open System
open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Fable.Ranvier
open Ranvier.View.Foundation

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private same expected actual =
    if not (obj.ReferenceEquals (expected, actual)) then
        failwith "Node identity changed"

let private remove (node: ChildNode) : unit =
    node.remove ()

let private attach (node: Node) : unit =
    globalThis.document.body.appendChild node
    |> ignore

let private listen (node: HTMLElement) (handler: unit -> unit) : unit -> unit =
    let fn: Event -> obj = fun _ -> handler ()
    node.addEventListener ("click", fn)
    fun () -> node.removeEventListener ("click", fn)

type private Config =
    {
        CleanupFails: bool
        ReleaseFails: bool
        ReporterFails: bool
        SetupFailure: exn option
        Reenter: bool
        Acquired: HTMLElement -> (unit -> int) -> (int -> unit) -> (unit -> int) -> unit
    }

let private normal =
    {
        CleanupFails = false
        ReleaseFails = false
        ReporterFails = false
        SetupFailure = None
        Reenter = false
        Acquired = fun _ _ _ _ -> ()
    }


type private Fixture =
    {
        Parent: HTMLElement
        Child: HTMLElement
        Scope: MountLifetime
        ChildScope: MountLifetime
        Owner: Owner
        Set: int -> unit
        Writes: unit -> int
        Calls: unit -> int
        Reports: ResizeArray<exn list>
        Dispose: unit -> unit
    }

let private expected label =
    [
        label + ":child-clean-attached"
        label + ":parent-clean-attached"
        label + ":remove-child"
        label + ":remove-parent"
    ]

let private createFixture (graph: Graph) (host: HTMLElement) (trace: ResizeArray<string>) label config =
    let parent = Dom.element "div" [] []
    let child = Dom.element "button" [] []
    parent.appendChild (child) |> ignore
    host.appendChild (parent) |> ignore
    let reports = ResizeArray<exn list>()
    let mutable calls = 0
    let mutable writes = 0
    let mutable scope: MountLifetime = null
    let mutable childScope: MountLifetime = null
    let mutable ownerForFailure: Owner option = None
    let signal = graph.Run (fun () -> Api.createSignal 0)

    let report errors =
        reports.Add errors

        if config.ReporterFails then
            failwith "reporter"

    let owner =
        try
            graph.Run (fun () ->
                Api.createRoot (fun owner ->
                    ownerForFailure <- Some owner

                    scope <-
                        MountLifetime.Root (
                            graph,
                            report,
                            fun () ->
                                trace.Add (label + ":remove-parent")
                                remove parent
                        )

                    let unsubscribe = listen child (fun () -> calls <- calls + 1)

                    // Runs after the child lifetime's teardown.
                    scope.OnCleanup (fun () ->
                        same host parent.parentNode.Value
                        same parent child.parentNode.Value
                        trace.Add (label + ":parent-clean-attached"))

                    childScope <-
                        scope.Child (
                            (fun () ->
                                trace.Add (label + ":remove-child")
                                remove child

                                if config.ReleaseFails then
                                    failwith "release"),
                            fun nested ->
                                nested.OnCleanup (fun () ->
                                    same parent child.parentNode.Value
                                    same host parent.parentNode.Value
                                    trace.Add (label + ":child-clean-attached")
                                    unsubscribe ()

                                    if config.Reenter then
                                        scope.Dispose ()

                                    if config.CleanupFails then
                                        failwith "cleanup")

                                Api.createEffect (fun () ->
                                    let value = signal.Value
                                    writes <- writes + 1
                                    child.textContent <- string value)
                        )

                    config.Acquired child (fun () -> calls) (fun value -> signal.Value <- value) (fun () -> writes)

                    match config.SetupFailure with
                    | Some error -> raise error
                    | None -> owner))
        with _ ->
            if not (isNull scope) then
                scope.Dispose ()

            ownerForFailure |> Option.iter _.Dispose()
            reraise ()

    {
        Parent = parent
        Child = child
        Scope = scope
        ChildScope = childScope
        Owner = owner
        Set = fun value -> graph.Run (fun () -> signal.Value <- value)
        Writes = fun () -> writes
        Calls = fun () -> calls
        Reports = reports
        Dispose =
            fun () ->
                scope.Dispose ()
                owner.Dispose ()
    }

let private withHost check =
    let host = Dom.element "section" [] []
    let sibling = Dom.element "aside" [] []
    host.appendChild (sibling) |> ignore
    attach host

    try
        check host sibling
        same host sibling.parentNode.Value
    finally
        remove host

let private disposalPath path =
    withHost (fun host _ ->
        use graph = new Graph ()
        let trace = ResizeArray<string>()
        let fixture = createFixture graph host trace "root" normal
        fixture.Child.click ()
        equal 1 (fixture.Calls ())
        fixture.Set 1
        equal 2 (fixture.Writes ())

        match path with
        | "explicit" -> fixture.Dispose ()
        | "owner" -> fixture.Owner.Dispose ()
        | "graph" -> graph.Dispose ()
        | _ -> invalidArg "path" path

        equal (expected "root") (List.ofSeq trace)
        equal 1.0 host.childNodes.length
        fixture.Child.click ()
        equal 1 (fixture.Calls ())

        if path <> "graph" then
            fixture.Set 2
            equal 2 (fixture.Writes ())

        fixture.Dispose ()
        graph.Dispose ()
        equal (expected "root") (List.ofSeq trace))

let private sharedGraph () =
    withHost (fun host _ ->
        use graph = new Graph ()
        let trace = ResizeArray<string>()
        let first = createFixture graph host trace "first" normal
        let second = createFixture graph host trace "second" normal
        first.Dispose ()
        equal (expected "first") (List.ofSeq trace)
        same host second.Parent.parentNode.Value
        second.Set 1
        equal 2 (second.Writes ())
        second.Child.click ()
        equal 1 (second.Calls ())
        graph.Dispose ()
        equal (expected "first" @ expected "second") (List.ofSeq trace)
        equal 1.0 host.childNodes.length)

let private effectRerun () =
    withHost (fun host _ ->
        use graph = new Graph ()
        let trace = ResizeArray<string>()
        let version = graph.Run (fun () -> Api.createSignal 0)
        let mutable latest: Fixture option = None

        graph.Run (fun () ->
            Api.createEffect (fun () ->
                let value = version.Value
                equal 1.0 host.childNodes.length
                let fixture = createFixture graph host trace (string value) normal
                Api.onCleanup fixture.Scope.Dispose
                latest <- Some fixture))

        let old = latest.Value
        graph.Run (fun () -> version.Value <- 1)
        equal (expected "0") (List.ofSeq trace)
        equal true old.Scope.IsDisposed
        same host latest.Value.Parent.parentNode.Value
        old.Child.click ()
        equal 0 (old.Calls ())
        graph.Dispose ()
        equal (expected "0" @ expected "1") (List.ofSeq trace)
        equal 1.0 host.childNodes.length)

let private setupFailure () =
    withHost (fun host _ ->
        use graph = new Graph ()
        let trace = ResizeArray<string>()
        let failure = InvalidOperationException "setup"
        let mutable caught = false
        let mutable acquired = None

        try
            createFixture
                graph
                host
                trace
                "failed"
                { normal with
                    SetupFailure = Some failure
                    Acquired = fun child calls set writes -> acquired <- Some (child, calls, set, writes)
                }
            |> ignore
        with error ->
            same failure error
            caught <- true

        equal true caught
        equal (expected "failed") (List.ofSeq trace)
        equal 1.0 host.childNodes.length
        let child, calls, set, writes = acquired.Value
        let before = writes ()
        child.click ()
        equal 0 (calls ())
        graph.Run (fun () -> set 1)
        equal before (writes ())
        graph.Dispose ()
        equal (expected "failed") (List.ofSeq trace))

let private teardownFailures () =
    withHost (fun host _ ->
        use graph = new Graph ()
        let trace = ResizeArray<string>()

        let first =
            createFixture
                graph
                host
                trace
                "broken"
                { normal with
                    CleanupFails = true
                    ReleaseFails = true
                    ReporterFails = true
                }

        let second = createFixture graph host trace "sibling" normal
        graph.Dispose ()

        equal
            (expected "broken")
            (trace
             |> Seq.filter _.StartsWith("broken:")
             |> List.ofSeq)

        equal
            (expected "sibling")
            (trace
             |> Seq.filter _.StartsWith("sibling:")
             |> List.ofSeq)

        equal 8 trace.Count
        equal [ "cleanup"; "release"; "reporter" ] (first.Scope.Failures |> List.map _.Message)
        equal 1 first.Reports.Count
        equal [ "cleanup"; "release" ] (first.Reports[0] |> List.map _.Message)
        first.Child.click ()
        second.Child.click ()
        equal 0 (first.Calls ())
        equal 0 (second.Calls ())
        first.Dispose ()
        equal 1 first.Reports.Count
        equal 1.0 host.childNodes.length)


let private reentrantAndRejected () =
    withHost (fun host _ ->
        use graph = new Graph ()
        let trace = ResizeArray<string>()
        let fixture = createFixture graph host trace "root" { normal with Reenter = true }
        let mutable rejected = 0

        fixture.Scope.OnCleanup (fun () ->
            try
                fixture.Scope.Child (ignore) |> ignore
            with :? InvalidOperationException ->
                rejected <- rejected + 1

            try
                fixture.Scope.OnCleanup (ignore)
            with :? InvalidOperationException ->
                rejected <- rejected + 1)

        fixture.Owner.Dispose ()
        equal (expected "root") (List.ofSeq trace)
        equal 2 rejected
        equal true fixture.Scope.IsDisposed
        fixture.Dispose ()
        equal (expected "root") (List.ofSeq trace))

let private childrenReleaseOnce () =
    use graph = new Graph ()
    let mutable released = 0
    let parent = MountLifetime.Root (graph, ignore, ignore)

    graph.Run (fun () ->
        for _ in 1..1000 do
            parent.Child(fun () -> released <- released + 1).Dispose())

    equal 1000 released
    equal false parent.IsDisposed
    parent.Dispose ()
    equal 1000 released

let private ancestorReentry () =
    withHost (fun host _ ->
        use graph = new Graph ()
        let trace = ResizeArray<string>()
        let fixture = createFixture graph host trace "root" normal
        fixture.ChildScope.OnCleanup (fixture.Scope.Dispose)
        fixture.ChildScope.OnCleanup (fun () -> same host fixture.Parent.parentNode.Value)
        fixture.ChildScope.Dispose ()

        // Every cleanup of the tree runs before its first release.
        equal
            [
                "root:parent-clean-attached"
                "root:child-clean-attached"
                "root:remove-child"
                "root:remove-parent"
            ]
            (List.ofSeq trace)

        equal [] fixture.ChildScope.Failures
        equal true fixture.Scope.IsDisposed
        fixture.Dispose ()
        equal 1.0 host.childNodes.length)

let private attachmentPhases () =
    use graph = new Graph ()
    let root = MountLifetime.Root (graph, ignore, ignore)
    let child = graph.Run (fun () -> root.Child ignore)
    equal SettingUp root.Phase
    root.MarkAttached ()
    equal Attached child.Phase
    let later = graph.Run (fun () -> root.Child ignore)
    equal SettingUp later.Phase
    later.MarkAttached ()
    root.Dispose ()
    equal Disposed child.Phase
    let mutable rejected = false

    try
        child.MarkAttached ()
    with :? InvalidOperationException ->
        rejected <- true

    equal true rejected

let private crossTreeDisposal () =
    use graph = new Graph ()
    let trace = ResizeArray<string>()
    let aReports, bReports = ResizeArray<exn list>(), ResizeArray<exn list>()
    let a = MountLifetime.Root (graph, aReports.Add, (fun () -> trace.Add "a:release"))
    // Runs after the teardown of a's child.
    a.OnCleanup (fun () -> trace.Add "a:clean")
    let b = MountLifetime.Root (graph, bReports.Add, (fun () -> trace.Add "b:release"))

    let aChild, bChild =
        graph.Run (fun () -> a.Child (fun () -> trace.Add "a-child:release"), b.Child (fun () -> trace.Add "b-child:release"))

    bChild.OnCleanup (fun () ->
        trace.Add "b-child:clean"
        failwith "b")

    aChild.OnCleanup (fun () ->
        trace.Add "a-child:clean"
        bChild.Dispose ())

    a.Dispose ()

    equal
        [
            "a-child:clean"
            "b-child:clean"
            "b-child:release"
            "a:clean"
            "a-child:release"
            "a:release"
        ]
        (List.ofSeq trace)

    equal 0 aReports.Count

    equal
        [ "b" ]
        (bReports
         |> Seq.concat
         |> Seq.map _.Message
         |> List.ofSeq)

    equal [] a.Failures
    equal false b.IsDisposed
    b.Dispose ()

let private crossTreeOwnerDisposal () =
    use graph = new Graph ()
    let aReports, bReports = ResizeArray<exn list>(), ResizeArray<exn list>()
    let mutable bReleased = false
    let a = MountLifetime.Root (graph, aReports.Add, ignore)
    let bParent = graph.Run (fun () -> Api.createRoot id)

    let b =
        graph.Run (fun () -> Api.runWithOwner bParent (fun () -> MountLifetime.Root (graph, bReports.Add, (fun () -> bReleased <- true))))

    b.OnCleanup (fun () -> failwith "b")
    a.OnCleanup bParent.Dispose
    a.Dispose ()

    let messages (reports: exn list seq) =
        reports
        |> Seq.concat
        |> Seq.map _.Message
        |> List.ofSeq

    equal ([], [ "b" ], [], [ "b" ], true) (messages aReports, messages bReports, messages [ a.Failures ], messages [ b.Failures ], bReleased)

let cases: (string * (unit -> unit)) array =
    [|
        "attachment cannot revive disposed scopes", attachmentPhases
        "child cleanup may dispose a live ancestor while attached", ancestorReentry
        "explicit mount disposal preserves attached cleanup", fun () -> disposalPath "explicit"
        "core owner disposal preserves attached cleanup", fun () -> disposalPath "owner"
        "graph disposal preserves attached cleanup", fun () -> disposalPath "graph"
        "shared graph mounts remain independent", sharedGraph
        "effect rerun releases the previous mounted subtree", effectRerun
        "failed setup unwinds before rethrowing", setupFailure
        "cleanup release and reporter failures do not strand siblings", teardownFailures
        "teardown rejects registration and survives reentry", reentrantAndRejected
        "disposed children release once and leave the parent live", childrenReleaseOnce
        "a cleanup disposing a scope in another tree reports on that tree and keeps its own order", crossTreeDisposal
        "a cleanup disposing another mount's parent owner reports that mount's errors on its own reporter", crossTreeOwnerDisposal
    |]
