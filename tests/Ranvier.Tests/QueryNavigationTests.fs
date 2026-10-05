module Ranvier.Tests.QueryNavigationTests

open System.Collections.Generic
open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.Tests.QuerySupport
open Ranvier.Examples.QueryDictionary

type private Host(graph, api) =
    let initial, commands = init graph api
    let mutable model = initial
    let inbox = System.Collections.Generic.Queue<Msg>()

    let run commands =
        for command in commands do
            command inbox.Enqueue

    do run commands
    member _.Model = model

    member _.Send message =
        let next, commands = update message model
        model <- next
        run commands

    member this.Pump() =
        while inbox.Count > 0 do
            this.Send (inbox.Dequeue ())

    interface System.IDisposable with
        member _.Dispose() =
            model.Session.Dispose ()

let private settle (host: Host) =
    waitUntil (fun () ->
        host.Pump ()
        not host.Model.History.Head.Loading)

let private word: Word =
    {
        Id = 42
        Def1 = "first"
        Def2 = "second"
        OtherFields = Map.empty
    }

let private api (calls: ResizeArray<string>) reply : Api =
    {
        LoadIndex =
            fun () _ ->
                calls.Add "index"

                completed
                    {
                        TotalWordCount = 100
                        Sections = [ { Id = 7; Name = "A" } ]
                    }
        LoadSection =
            fun id _ ->
                calls.Add "section"

                completed
                    {
                        Id = id
                        Name = "A"
                        Words =
                            [
                                {
                                    Id = 42
                                    Def1 = "first"
                                    Def2 = "second"
                                }
                            ]
                    }
        LoadWord =
            fun _ _ ->
                calls.Add "word"
                completed word
        SaveWord =
            fun _ _ ->
                calls.Add "save"
                reply
    }

[<Tests>]
let tests =
    testList
        "QueryNavigation"
        [
            testCaseAsync "Back disposes the editor while its client-owned save reconciles retained pages"
            <| async {
                use graph = newGraph ()
                let calls = ResizeArray<string>()
                let reply = TaskCompletionSource<Saved>()
                use host = new Host (graph, api calls reply.Task)
                do! settle host
                host.Send (OpenSection 7)
                do! settle host
                host.Send (OpenWord (7, 42))
                do! settle host
                let editor = host.Model.History.Head
                host.Send (DraftChanged { word with Def1 = "edited" })
                host.Send Save
                host.Send Back
                Expect.isTrue editor.Owner.IsDisposed "editor lease released"
                do! settle host

                reply.SetResult
                    {
                        Word = { word with Def1 = "edited" }
                        SectionId = 7
                        TotalWordCount = 100
                    }

                do!
                    waitUntil (fun () ->
                        host.Pump ()

                        match host.Model.History.Head.Content with
                        | SectionPage query -> query.Value.Words.Head.Def1 = "edited"
                        | _ -> false)

                host.Send Back
                do! settle host
                Expect.sequenceEqual calls [ "index"; "section"; "word"; "save" ] "returning pages use accepted records"
                Expect.equal host.Model.History.Length 1 "only Home remains"
            }
            testCaseAsync "adding a word stores the saved draft without fetching full detail"
            <| async {
                use graph = newGraph ()
                let calls = ResizeArray<string>()
                let reply = TaskCompletionSource<Saved>()
                use host = new Host (graph, api calls reply.Task)
                do! settle host
                host.Send (OpenSection 7)
                do! settle host
                host.Send (AddWord 7)
                do! settle host
                host.Send (DraftChanged { word with Id = 0; Def1 = "new" })
                host.Send Save

                reply.SetResult
                    {
                        Word = { word with Id = 99; Def1 = "new" }
                        SectionId = 7
                        TotalWordCount = 101
                    }

                do!
                    waitUntil (fun () ->
                        host.Pump ()

                        match host.Model.History.Head.Content with
                        | EditorPage editor -> not editor.Saving && editor.Draft.Value.Id = 99
                        | _ -> false)

                host.Send Back
                do! settle host

                match host.Model.History.Head.Content with
                | SectionPage query -> Expect.equal query.Value.Words.Length 2 "preview appended"
                | _ -> failtest "expected section"

                host.Send Back
                do! settle host

                match host.Model.History.Head.Content with
                | IndexPage query -> Expect.equal query.Value.TotalWordCount 101 "authoritative total"
                | _ -> failtest "expected index"

                Expect.sequenceEqual calls [ "index"; "section"; "save" ] "no full dictionary or absent detail loaded"
            }
            testCaseAsync "Home cancels the old load and ignores messages for removed pages"
            <| async {
                use graph = newGraph ()
                let old = TaskCompletionSource<Index>()
                let mutable loads = 0

                let source =
                    api
                        (ResizeArray ())
                        (completed
                            {
                                Word = word
                                SectionId = 7
                                TotalWordCount = 100
                            })

                let source =
                    { source with
                        LoadIndex =
                            fun () _ ->
                                loads <- loads + 1

                                if loads = 1 then
                                    old.Task
                                else
                                    completed { TotalWordCount = 200; Sections = [] }
                    }

                use host = new Host (graph, source)
                let oldHome = host.Model.History.Head
                let oldSession = host.Model.Session
                host.Send Home
                do! settle host
                Expect.isTrue oldSession.IsDisposed "previous client closed"
                Expect.isTrue oldHome.Owner.IsDisposed "previous page released"
                old.SetResult { TotalWordCount = 55; Sections = [] }
                host.Send (Loaded (oldHome, None))
                host.Pump ()

                match host.Model.History.Head.Content with
                | IndexPage query -> Expect.equal query.Value.TotalWordCount 200 "fresh session unaffected"
                | _ -> failtest "expected index"
            }
        ]
