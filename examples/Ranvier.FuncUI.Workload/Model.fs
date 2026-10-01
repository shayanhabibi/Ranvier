namespace Workload

type TaskStatus =
    | Ready
    | Running
    | Done

type TaskItem =
    {
        Id: int
        Title: string
        Owner: string
        Status: TaskStatus
        Progress: int
    }

type Model =
    {
        Tasks: Map<int, TaskItem>
        Query: string
        Page: int
        SelectedId: int option
        Summary: struct (int * int * int)
        VisibleIds: int list
        MatchingCount: int
    }

type Msg =
    | Search of string
    | Page of int
    | Select of int
    | SetProgress of int * int
    | SetStatus of int * TaskStatus

module Model =
    let pageSize = 50

    let private statusCounts (tasks: Map<int, TaskItem>) =
        let count status =
            tasks.Values
            |> Seq.filter (fun task -> task.Status = status)
            |> Seq.length

        struct (count Ready, count Running, count Done)

    let private matchingIds (tasks: Map<int, TaskItem>) (query: string) =
        tasks.Values
        |> Seq.filter (fun task ->
            task.Title.Contains (query, System.StringComparison.OrdinalIgnoreCase)
            || task.Owner.Contains (query, System.StringComparison.OrdinalIgnoreCase))
        |> Seq.map _.Id
        |> Seq.toList

    let private setPage page ids model =
        let maxPage = max 0 ((List.length ids - 1) / pageSize)
        let page = max 0 (min maxPage page)

        { model with
            Page = page
            VisibleIds =
                ids
                |> List.skip (min ids.Length (page * pageSize))
                |> List.truncate pageSize
            MatchingCount = ids.Length
        }

    let init size =
        if size < 1 then
            invalidArg "size" "At least one task is required"

        let owners = [| "Ada"; "Grace"; "Linus"; "Margaret" |]

        let tasks =
            [ 0 .. size - 1 ]
            |> List.map (fun id ->
                id,
                {
                    Id = id
                    Title = sprintf "Task %04d" id
                    Owner = owners[id % owners.Length]
                    Status = [| Ready; Running; Done |][id % 3]
                    Progress = (id * 17) % 101
                })
            |> Map.ofList

        let model =
            {
                Tasks = tasks
                Query = ""
                Page = 0
                SelectedId = Some 0
                Summary = statusCounts tasks
                VisibleIds = []
                MatchingCount = size
            }

        setPage 0 (matchingIds tasks "") model

    let update msg model =
        let edit id change =
            match Map.tryFind id model.Tasks with
            | None -> model
            | Some before ->
                let after = change before

                if after = before then
                    model
                else
                    let struct (ready, running, doneCount) = model.Summary

                    let delta status =
                        (if after.Status = status then 1 else 0)
                        - (if before.Status = status then 1 else 0)

                    { model with
                        Tasks = Map.add id after model.Tasks
                        Summary = struct (ready + delta Ready, running + delta Running, doneCount + delta Done)
                    }

        match msg with
        | SetProgress (id, progress) ->
            edit id (fun task ->
                { task with
                    Progress = max 0 (min 100 progress)
                })
        | SetStatus (id, status) -> edit id (fun task -> { task with Status = status })
        | Select id when
            model.Tasks.ContainsKey id
            && model.SelectedId <> Some id
            ->
            { model with SelectedId = Some id }
        | Select _ -> model
        | Search query ->
            let query = if isNull query then "" else query.Trim ()

            if query = model.Query then
                model
            else
                setPage 0 (matchingIds model.Tasks query) { model with Query = query }
        | Page page ->
            let next = setPage page (matchingIds model.Tasks model.Query) model
            if next.Page = model.Page then model else next

    let visibleIds (model: Model) =
        List.toArray model.VisibleIds

    let summary (model: Model) =
        model.Summary

module Workloads =
    let names = [| "background"; "selected-edit"; "search-page"; "mixed" |]

    let trace name size =
        if size < 1 then
            invalidArg "size" "At least one task is required"

        let progress i =
            SetProgress ((i * 37 + 73) % size, (i * 7 + 13) % 101)

        let status i =
            SetStatus ((i * 19 + 5) % size, [| Ready; Running; Done |][(i / 3) % 3])

        let search i =
            match i % 6 with
            | 0 -> Search "Ada"
            | 1 -> Page 1
            | 2 -> Search "Task 00"
            | 3 -> Search "no matching task"
            | 4 -> Search ""
            | _ -> Page 1

        Array.init 600 (fun i ->
            match name with
            | "background" -> if i % 5 = 0 then status i else progress i
            | "selected-edit" ->
                if i % 4 = 0 then
                    SetStatus (0, [| Ready; Running; Done |][i / 4 % 3])
                else
                    SetProgress (0, i * 7 % 101)
            | "search-page" -> search i
            | "mixed" ->
                match i % 10 with
                | 0 -> Select (i * 11 % size)
                | 1 -> search (i / 10)
                | 2 -> status i
                | _ -> progress i
            | _ -> invalidArg "name" "Unknown workload")
