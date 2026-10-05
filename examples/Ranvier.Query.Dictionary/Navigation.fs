module Ranvier.Examples.QueryDictionary

open System
open System.Threading
open System.Threading.Tasks
open Ranvier
open Ranvier.Query

type WordPreview = { Id: int; Def1: string; Def2: string }
type SectionPreview = { Id: int; Name: string }

type Section =
    {
        Id: int
        Name: string
        Words: WordPreview list
    }

type Word =
    {
        Id: int
        Def1: string
        Def2: string
        OtherFields: Map<string, string>
    }

type Index =
    {
        TotalWordCount: int
        Sections: SectionPreview list
    }

type Saved =
    {
        Word: Word
        SectionId: int
        TotalWordCount: int
    }

type Api =
    {
        LoadIndex: unit -> CancellationToken -> Task<Index>
        LoadSection: int -> CancellationToken -> Task<Section>
        LoadWord: int -> CancellationToken -> Task<Word>
        SaveWord: int * Word -> CancellationToken -> Task<Saved>
    }

type Editor =
    {
        SectionId: int
        Detail: QueryLease<Word> option
        Draft: Word option
        Saving: bool
    }

type Content =
    | IndexPage of QueryLease<Index>
    | SectionPage of QueryLease<Section>
    | EditorPage of Editor

type Page =
    {
        Owner: Owner
        Content: Content
        Loading: bool
        Error: exn option
    }

/// <summary>A dictionary session; its owner retains pages until Back or Home removes them.</summary>
type Session(graph: Graph, api: Api) =
    let owner, client =
        graph.Run (fun () -> createRoot (fun owner -> owner, new QueryClient (graph)))

    let index = client.Define api.LoadIndex
    let sections = client.Define api.LoadSection
    let words = client.Define api.LoadWord
    let mutable disposed = false
    member _.Graph = graph
    member _.Api = api
    member _.Client = client
    member _.Index = index
    member _.Sections = sections
    member _.Words = words
    member _.IsDisposed = disposed

    member _.Page(content: Owner -> Content) =
        let pageOwner = new Owner ()
        owner.Attach pageOwner

        {
            Owner = pageOwner
            Content = content pageOwner
            Loading = true
            Error = None
        }

    member _.Dispose() =
        if not disposed then
            disposed <- true
            owner.Dispose ()

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

type Model =
    { Session: Session; History: Page list }

type Msg =
    | OpenSection of int
    | OpenWord of sectionId: int * wordId: int
    | AddWord of sectionId: int
    | DraftChanged of Word
    | Save
    | Loaded of Page * Word option
    | LoadFailed of Page * exn
    | Saved of Page * MutationOutcome<Saved>
    | Back
    | Home

type Command = (Msg -> unit) -> unit

let private post (session: Session) page dispatch message =
    session.Graph.Dispatch (fun () ->
        if
            not session.IsDisposed
            && not page.Owner.IsDisposed
        then
            dispatch message)

let private load (session: Session) page : Command =
    fun dispatch ->
        session.Graph.Dispatch (fun () ->
            if
                not session.IsDisposed
                && not page.Owner.IsDisposed
            then
                let pending =
                    task {
                        match page.Content with
                        | IndexPage query -> let! _ = query.EnsureAsync () in return None
                        | SectionPage query -> let! _ = query.EnsureAsync () in return None
                        | EditorPage { Detail = Some query; Draft = None } ->
                            let! word = query.EnsureAsync ()
                            return Some word
                        | EditorPage _ -> return None
                    }

                task {
                    try
                        let! draft = pending
                        post session page dispatch (Loaded (page, draft))
                    with
                    | :? OperationCanceledException -> ()
                    | error -> post session page dispatch (LoadFailed (page, error))
                }
                |> ignore)

let private reconcile (session: Session) (saved: Saved) =
    [
        session.Index.UpdateIfLoaded (
            (),
            fun old ->
                { old with
                    TotalWordCount = saved.TotalWordCount
                }
        )
        session.Sections.UpdateIfLoaded (
            saved.SectionId,
            fun old ->
                let preview: WordPreview =
                    {
                        Id = saved.Word.Id
                        Def1 = saved.Word.Def1
                        Def2 = saved.Word.Def2
                    }

                let exists =
                    old.Words
                    |> List.exists (fun word -> word.Id = preview.Id)

                { old with
                    Words =
                        if exists then
                            old.Words
                            |> List.map (fun word -> if word.Id = preview.Id then preview else word)
                        else
                            old.Words @ [ preview ]
                }
        )
        session.Words.SetIfLoaded (saved.Word.Id, saved.Word)
    ]

let private save (session: Session) page (editor: Editor) draft : Command =
    fun dispatch ->
        session.Graph.Dispatch (fun () ->
            if not session.IsDisposed then
                let pending =
                    session.Client.Mutate ((editor.SectionId, draft), session.Api.SaveWord, reconcile session)

                task {
                    try
                        let! outcome = pending
                        post session page dispatch (Saved (page, outcome))
                    with :? OperationCanceledException ->
                        ()
                }
                |> ignore)

let private replace page change model =
    { model with
        History =
            model.History
            |> List.map (fun current ->
                if obj.ReferenceEquals (current.Owner, page.Owner) then
                    change current
                else
                    current)
    }

/// <summary>Creates Home and a command that reports its initial loading result.</summary>
let init graph api =
    let session = new Session (graph, api)

    let home =
        session.Page (fun owner -> IndexPage (session.Index.AcquireOwned ((), owner)))

    {
        Session = session
        History = [ home ]
    },
    [ load session home ]

/// <summary>Applies an Elmish-style message and returns commands; invoke it on the graph thread.</summary>
let update message model : Model * Command list =
    let push content =
        let page = model.Session.Page content

        { model with
            History = page :: model.History
        },
        [ load model.Session page ]

    match message, model.History with
    | OpenSection id, _ -> push (fun owner -> SectionPage (model.Session.Sections.AcquireOwned (id, owner)))
    | OpenWord (sectionId, wordId), _ ->
        push (fun owner ->
            EditorPage
                {
                    SectionId = sectionId
                    Detail = Some (model.Session.Words.AcquireOwned (wordId, owner))
                    Draft = None
                    Saving = false
                })
    | AddWord sectionId, _ ->
        push (fun _ ->
            EditorPage
                {
                    SectionId = sectionId
                    Detail = None
                    Draft =
                        Some
                            {
                                Id = 0
                                Def1 = ""
                                Def2 = ""
                                OtherFields = Map.empty
                            }
                    Saving = false
                })
    | DraftChanged draft, ({ Content = EditorPage editor } as page) :: _ when not editor.Saving ->
        replace
            page
            (fun current ->
                { current with
                    Content = EditorPage { editor with Draft = Some draft }
                })
            model,
        []
    | Save,
      ({
           Content = EditorPage ({ Draft = Some draft; Saving = false } as editor)
       } as page) :: _ ->
        replace
            page
            (fun current ->
                { current with
                    Content = EditorPage { editor with Saving = true }
                    Error = None
                })
            model,
        [ save model.Session page editor draft ]
    | Loaded (page, draft), _ ->
        replace
            page
            (fun current ->
                let content =
                    match current.Content, draft with
                    | EditorPage editor, Some word when editor.Draft.IsNone -> EditorPage { editor with Draft = Some word }
                    | content, _ -> content

                { current with
                    Content = content
                    Loading = false
                    Error = None
                })
            model,
        []
    | LoadFailed (page, error), _ ->
        replace
            page
            (fun current ->
                { current with
                    Loading = false
                    Error = Some error
                })
            model,
        []
    | Saved (page, outcome), _ ->
        replace
            page
            (fun current ->
                match current.Content with
                | EditorPage editor ->
                    let draft, error =
                        match outcome with
                        | MutationOutcome.Applied saved -> Some saved.Word, None
                        | MutationOutcome.RequestFailed error -> editor.Draft, Some error
                        | MutationOutcome.ReconciliationFailed (saved, error) -> Some saved.Word, Some error

                    { current with
                        Content =
                            EditorPage
                                { editor with
                                    Draft = draft
                                    Saving = false
                                }
                        Error = error
                    }
                | _ -> current)
            model,
        []
    | Back, removed :: returning :: rest ->
        removed.Owner.Dispose ()

        { model with
            History = returning :: rest
        },
        [ load model.Session returning ]
    | Home, _ ->
        let graph, api = model.Session.Graph, model.Session.Api
        model.Session.Dispose ()
        init graph api
    | _ -> model, []
