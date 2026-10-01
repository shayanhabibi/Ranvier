namespace Workload

open Avalonia.Controls
open Avalonia.Layout
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Hosts
open Avalonia.FuncUI.Types

type Regions =
    { Header: IView
      Summary: IView
      Paging: IView
      Details: IView
      Rows: (int * IView) list }

module Views =
    let empty = TextBlock.create [TextBlock.text ""] :> IView

    let header query dispatch =
        StackPanel.create [
            StackPanel.spacing 8.
            StackPanel.children [
                TextBlock.create [TextBlock.text "Task dashboard"; TextBlock.fontSize 24.]
                TextBox.create [
                    TextBox.name "query"
                    TextBox.placeHolderText "Search task title or owner"
                    TextBox.text query
                    TextBox.onTextChanged (fun text -> dispatch (Msg.Search text))
                ]
            ]
        ] :> IView

    let summary (struct (ready, running, doneCount)) =
        TextBlock.create [
            TextBlock.name "summary"
            TextBlock.fontSize 18.
            TextBlock.text (sprintf "Ready %d   Running %d   Done %d" ready running doneCount)
        ] :> IView

    let paging (struct (page, matching)) dispatch =
        StackPanel.create [
            StackPanel.orientation Orientation.Horizontal
            StackPanel.spacing 12.
            StackPanel.children [
                Button.create [Button.name "previous"; Button.content "Previous"; Button.onClick ((fun _ -> dispatch (Msg.Page(page - 1))), SubPatchOptions.OnChangeOf page)]
                TextBlock.create [TextBlock.name "paging"; TextBlock.text (sprintf "Page %d | %d matching tasks" (page + 1) matching)]
                Button.create [Button.name "next"; Button.content "Next"; Button.onClick ((fun _ -> dispatch (Msg.Page(page + 1))), SubPatchOptions.OnChangeOf page)]
            ]
        ] :> IView

    let detailText selected =
        match selected with
        | ValueSome task -> sprintf "%s | %s | %O | %d%%" task.Title task.Owner task.Status task.Progress
        | ValueNone -> "Select a task"

    let details selected =
        StackPanel.create [
            StackPanel.spacing 12.
            StackPanel.children [
                TextBlock.create [TextBlock.text "Selected task"; TextBlock.fontSize 20.]
                TextBlock.create [TextBlock.name "detail"; TextBlock.text (detailText selected); TextBlock.textWrapping Avalonia.Media.TextWrapping.Wrap]
            ]
        ] :> IView

    let row task dispatch =
        StackPanel.create [
            StackPanel.orientation Orientation.Horizontal
            StackPanel.spacing 10.
            StackPanel.margin 4.
            StackPanel.children [
                Button.create [
                    Button.name (sprintf "select-%d" task.Id)
                    Button.content "Select"
                    Button.onClick (fun _ -> dispatch (Msg.Select task.Id))
                ]
                TextBlock.create [TextBlock.name (sprintf "task-%d" task.Id); TextBlock.width 110.; TextBlock.text task.Title]
                TextBlock.create [TextBlock.width 85.; TextBlock.text task.Owner]
                TextBlock.create [TextBlock.name (sprintf "status-%d" task.Id); TextBlock.width 65.; TextBlock.text (string task.Status)]
                TextBlock.create [TextBlock.name (sprintf "progress-%d" task.Id); TextBlock.width 45.; TextBlock.text (sprintf "%d%%" task.Progress)]
                Button.create [
                    Button.name (sprintf "advance-%d" task.Id)
                    Button.content "+10%"
                    Button.onClick ((fun _ -> dispatch (SetProgress(task.Id, task.Progress + 10))), SubPatchOptions.OnChangeOf task.Progress)
                ]
                Button.create [Button.name (sprintf "done-%d" task.Id); Button.content "Done"; Button.onClick (fun _ -> dispatch (SetStatus(task.Id, Done)))]
            ]
        ] :> IView

    let selected (model: Model) =
        match model.SelectedId with
        | Some id -> ValueSome model.Tasks[id]
        | None -> ValueNone

    let private wrap name (child: IView) =
        View.createGeneric<HostControl> [ContentControl.name name; ContentControl.content child]
        |> View.withKey name

    let layout regions =
        Grid.create [
            Grid.margin 16.
            Grid.rowDefinitions "Auto,Auto,Auto,*"
            Grid.columnDefinitions "3*,2*"
            Grid.children [
                wrap "header-host" regions.Header |> View.withAttrs [Grid.row 0; Grid.columnSpan 2]
                wrap "summary-host" regions.Summary |> View.withAttrs [Grid.row 1; Grid.columnSpan 2; Control.margin (0., 12.)]
                wrap "paging-host" regions.Paging |> View.withAttrs [Grid.row 2; Grid.columnSpan 2; Control.margin (0., 12.)]
                ScrollViewer.create [
                    Grid.row 3
                    Grid.column 0
                    ScrollViewer.content (StackPanel.create [StackPanel.children (regions.Rows |> List.map (fun (id, view) -> wrap (sprintf "row-host-%d" id) view :> IView))])
                ]
                wrap "details-host" regions.Details |> View.withAttrs [Grid.row 3; Grid.column 1; Control.margin (16., 0.)]
            ]
        ] :> IView
