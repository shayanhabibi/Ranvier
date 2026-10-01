namespace Workload

open System
open Avalonia.Controls
open Avalonia.Interactivity
open Avalonia.Threading
open Avalonia.VisualTree

module Verification =
    let ensure condition message = if not condition then failwith message

    let control<'T when 'T :> Control> (host: Control) name =
        host.GetVisualDescendants()
        |> Seq.pick (function :? 'T as item when (item :> Control).Name = name -> Some item | _ -> None)

    let attach runner =
        let window = Window(Content = runner.Host, Width = 1100., Height = 800.)
        window.Show()
        Dispatcher.UIThread.RunJobs()
        window

    let checkRendered (runner: Runner) (expected: Model) =
        Dispatcher.UIThread.RunJobs()
        let rows =
            runner.Host.GetVisualDescendants()
            |> Seq.choose (function :? TextBlock as item when not (isNull item.Name) && item.Name.StartsWith("task-") -> Some item.Name | _ -> None)
            |> Seq.toArray
        let expectedRows = expected.VisibleIds |> List.map (sprintf "task-%d") |> List.toArray
        ensure (rows = expectedRows) (sprintf "rendered row IDs differ: actual %A, expected %A" rows expectedRows)
        for id in expected.VisibleIds do
            let task = expected.Tasks[id]
            ensure ((control<TextBlock> runner.Host (sprintf "task-%d" id)).Text = task.Title) "row title is stale"
            ensure ((control<TextBlock> runner.Host (sprintf "progress-%d" id)).Text = sprintf "%d%%" task.Progress) "row progress is stale"
            ensure ((control<TextBlock> runner.Host (sprintf "status-%d" id)).Text = string task.Status) "row status is stale"
        let struct (ready, running, doneCount) = expected.Summary
        ensure ((control<TextBlock> runner.Host "summary").Text = sprintf "Ready %d   Running %d   Done %d" ready running doneCount) "summary is stale"
        let detail =
            match expected.SelectedId with
            | Some id -> let task = expected.Tasks[id] in sprintf "%s | %s | %O | %d%%" task.Title task.Owner task.Status task.Progress
            | None -> "Select a task"
        ensure ((control<TextBlock> runner.Host "detail").Text = detail) "selected-task details are stale"
        ensure ((control<TextBox> runner.Host "query").Text = expected.Query) "query input is stale"
        ensure ((control<TextBlock> runner.Host "paging").Text = sprintf "Page %d | %d matching tasks" (expected.Page + 1) expected.MatchingCount) "pagination is stale"
        ensure (runner.CurrentModel() = expected) "runner model diverged"

    let run () =
        ModelChecks.run()
        for size in [100; 1000] do
            for workload in Workloads.names do
                for variant in Runners.names do
                    let initial = Model.init size
                    let runner = Runners.create variant initial true
                    let window = attach runner
                    try
                        checkRendered runner initial
                        let stable = control<TextBlock> runner.Host "task-1"
                        runner.Dispatch(SetProgress(0, 33))
                        checkRendered runner (Model.update (SetProgress(0, 33)) initial)
                        ensure (obj.ReferenceEquals(stable, control<TextBlock> runner.Host "task-1")) "unchanged row lost its native identity"
                        let mutable expected = runner.CurrentModel()
                        for msg in Workloads.trace workload size do
                            expected <- Model.update msg expected
                            runner.Dispatch msg
                            checkRendered runner expected
                        for msg in [SetProgress(-1, 10); Msg.Select -1; Msg.Search ""; Msg.Page 0; Msg.Select 0; SetProgress(0, 0)] do
                            expected <- Model.update msg expected
                            runner.Dispatch msg
                            checkRendered runner expected
                        let button = control<Button> runner.Host "advance-0"
                        button.RaiseEvent(RoutedEventArgs(Button.ClickEvent))
                        expected <- Model.update (SetProgress(0, 10)) expected
                        checkRendered runner expected
                        button.RaiseEvent(RoutedEventArgs(Button.ClickEvent))
                        expected <- Model.update (SetProgress(0, 20)) expected
                        checkRendered runner expected
                        let query = control<TextBox> runner.Host "query"
                        let survivor = control<TextBlock> runner.Host "task-0"
                        query.Text <- "Ada"
                        expected <- Model.update (Search "Ada") expected
                        checkRendered runner expected
                        ensure (obj.ReferenceEquals(survivor, control<TextBlock> runner.Host "task-0")) "overlapping search lost a surviving row"
                        runner.Dispatch(Msg.Search "")
                        expected <- Model.update (Msg.Search "") expected
                        checkRendered runner expected
                        for name, msg in ["select-1", Msg.Select 1; "done-1", SetStatus(1, Done); "next", Msg.Page 1; "previous", Msg.Page 0] do
                            (control<Button> runner.Host name).RaiseEvent(RoutedEventArgs(Button.ClickEvent))
                            expected <- Model.update msg expected
                            checkRendered runner expected
                        printfn "PASS: %s %s %d tasks" variant workload size
                    finally
                        runner.Dispose()
                        runner.Dispose()
                        window.Close()
                    ensure (Runners.liveCount = 0) "disposed runner retained ownership"
        for variant in Runners.names do
            let first = Runners.create variant (Model.init 100) false
            let second = Runners.create variant (Model.init 100) false
            let w1, w2 = attach first, attach second
            first.Dispatch(SetProgress(0, 42))
            checkRendered first (Model.update (SetProgress(0, 42)) (Model.init 100))
            checkRendered second (Model.init 100)
            first.Dispose()
            first.Dispatch(SetProgress(0, 99))
            second.Dispose()
            w1.Close()
            w2.Close()
        ensure (Runners.liveCount = 0) "independent runners leaked"
        printfn "PASS: rendered equivalence after every message, callbacks, stable IDs and disposal"
