namespace Workload

open Avalonia.FuncUI.Hosts
open Avalonia.FuncUI.Types
open Avalonia.Controls
open Avalonia.LogicalTree
open Avalonia.Threading
open System.Collections.Generic
open Ranvier
open Ranvier.Elmish

type Counters =
    { mutable RootViews: int64
      mutable SectionViews: int64
      mutable RowViews: int64
      mutable HostUpdates: int64
      mutable SelectorEvaluations: int64 }

type Runner =
    { Host: HostControl
      Dispatch: Msg -> unit
      CurrentModel: unit -> Model
      Counters: Counters
      Dispose: unit -> unit }

module Runners =
    let names = [| "elmish"; "elmish-cached"; "ranvier" |]
    let mutable liveCount = 0
    type private LoopMsg = Message of Msg | Stop

    let create variant initial instrument =
        if not (Array.contains variant names) then invalidArg "variant" "Unknown runner"
        Dispatcher.UIThread.VerifyAccess()
        let host = HostControl()
        let counters = { RootViews = 0; SectionViews = 0; RowViews = 0; HostUpdates = 0; SelectorEvaluations = 0 }
        let mutable disposed = false
        let mutable send: Msg -> unit = ignore
        let dispatch msg = if not disposed then send msg
        let row task =
            if instrument then counters.RowViews <- counters.RowViews + 1L
            Views.row task dispatch
        let section build =
            if instrument then counters.SectionViews <- counters.SectionViews + 1L
            build()
        let patch (target: HostControl) view =
            if instrument then counters.HostUpdates <- counters.HostUpdates + 1L
            (target :> IViewHost).Update(Some view)
        let root regions =
            if instrument then counters.RootViews <- counters.RootViews + 1L
            patch host (Views.layout regions)
        let mutable getModel = fun () -> initial
        let mutable stop = ignore

        if variant <> "ranvier" then
            let rowCache = Dictionary<int, TaskItem * IView>()
            let mutable previous: Model option = None
            let mutable oldRegions: Regions option = None
            let mutable current = initial
            let mutable loopDispatch: LoopMsg -> unit = ignore
            let render model wrappedDispatch =
                loopDispatch <- wrappedDispatch
                send <- fun msg -> wrappedDispatch (Message msg)
                let cached = variant = "elmish-cached"
                let unchanged projection =
                    cached && (match previous with Some before -> projection before = projection model | None -> false)
                let reuse condition old build =
                    match oldRegions with
                    | Some regions when condition -> old regions
                    | _ -> section build
                let rows =
                    model.VisibleIds |> List.map (fun id ->
                        let task = model.Tasks[id]
                        let view =
                            match rowCache.TryGetValue id with
                            | true, (before, view) when cached && obj.ReferenceEquals(before, task) -> view
                            | _ -> let view = row task in rowCache[id] <- (task, view); view
                        id, view)
                let regions =
                    { Header = reuse (unchanged _.Query) _.Header (fun () -> Views.header model.Query dispatch)
                      Summary = reuse (unchanged _.Summary) _.Summary (fun () -> Views.summary model.Summary)
                      Paging = reuse (unchanged (fun m -> struct (m.Page, m.MatchingCount))) _.Paging (fun () -> Views.paging (struct (model.Page, model.MatchingCount)) dispatch)
                      Details = reuse (unchanged Views.selected) _.Details (fun () -> Views.details (Views.selected model))
                      Rows = rows }
                let visible = HashSet<int>(model.VisibleIds)
                for id in rowCache.Keys |> Seq.toArray do
                    if not (visible.Contains id) then rowCache.Remove id |> ignore
                root regions
                previous <- Some model
                oldRegions <- Some regions
            let update msg model = match msg with Message msg -> Model.update msg model | Stop -> model
            Elmish.Program.mkSimple (fun () -> initial) update render
            |> Elmish.Program.withSetState (fun model wrappedDispatch ->
                let changed = match previous with None -> true | Some before -> before <> model
                current <- model
                if changed then render model wrappedDispatch)
            |> Elmish.Program.withTermination ((=) Stop) ignore
            |> Elmish.Program.withErrorHandler (fun (_, error) -> raise error)
            |> Elmish.Program.run
            getModel <- fun () -> current
            stop <- fun () -> loopDispatch Stop; rowCache.Clear(); oldRegions <- None; previous <- None
        else
            let ui = SynchronizationContextDispatcher(AvaloniaSynchronizationContext())
            let graph = new Graph(GraphOptions.Default.WithDispatcher ui)
            let owners = Dictionary<int, System.IDisposable>()
            let rowHosts = Dictionary<int, HostControl>()
            let rowViews = Dictionary<int, IView>()
            let findHost name =
                host.GetLogicalDescendants()
                |> Seq.pick (function :? HostControl as control when control.Name = name -> Some control | _ -> None)
            graph.Run(fun () ->
                let app = Mvu.create initial Model.update
                send <- app.Dispatch
                getModel <- fun () -> graph.Run(fun () -> untrack (fun () -> app.Model))
                let select projection =
                    app.Select(fun model ->
                        if instrument then counters.SelectorEvaluations <- counters.SelectorEvaluations + 1L
                        projection model)
                let ids = select _.VisibleIds
                let appOwner = getOwner()
                createEffect(fun () ->
                    let visible = ids.Value
                    let keep = HashSet<int>(visible)
                    for id in owners.Keys |> Seq.toArray do
                        if not (keep.Contains id) then
                            owners[id].Dispose()
                            owners.Remove id |> ignore
                            rowHosts.Remove id |> ignore
                            rowViews.Remove id |> ignore
                    root { Header = Views.empty; Summary = Views.empty; Paging = Views.empty; Details = Views.empty
                           Rows = visible |> List.map (fun id -> id, Views.empty) }
                    for id in visible do
                        let target = findHost (sprintf "row-host-%d" id)
                        if not (owners.ContainsKey id) then
                            rowHosts[id] <- target
                            let dispose =
                                runWithOwner appOwner (fun () -> createRoot(fun dispose ->
                                    let task = select (fun model -> model.Tasks[id])
                                    createEffect(fun () ->
                                        let view = row task.Value
                                        rowViews[id] <- view
                                        patch rowHosts[id] view)
                                    dispose))
                            owners[id] <- dispose
                        elif not (obj.ReferenceEquals(rowHosts[id], target)) then
                            rowHosts[id] <- target
                            patch target rowViews[id])
                let query = select _.Query
                let counts = select _.Summary
                let page = select (fun m -> struct (m.Page, m.MatchingCount))
                let details = select Views.selected
                createEffect(fun () -> patch (findHost "header-host") (section (fun () -> Views.header query.Value dispatch)))
                createEffect(fun () -> patch (findHost "summary-host") (section (fun () -> Views.summary counts.Value)))
                createEffect(fun () -> patch (findHost "paging-host") (section (fun () -> Views.paging page.Value dispatch)))
                createEffect(fun () -> patch (findHost "details-host") (section (fun () -> Views.details details.Value))))
            stop <- fun () -> graph.Dispose(); owners.Clear(); rowHosts.Clear(); rowViews.Clear()

        liveCount <- liveCount + 1
        { Host = host
          Dispatch = dispatch
          CurrentModel = getModel
          Counters = counters
          Dispose = fun () ->
              if not disposed then
                  disposed <- true
                  stop()
                  send <- ignore
                  (host :> IViewHost).Update None
                  liveCount <- liveCount - 1 }
