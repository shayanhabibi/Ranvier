module Ranvier.Tests.QueryDictionary

open System.Collections.Generic
open Expecto
open Ranvier
open Ranvier.Query
open Ranvier.Tests.QuerySupport

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

type Page =
    | IndexPage of QueryLease<Index>
    | SectionPage of QueryLease<Section>
    | EditorPage of Word

type SectionKey =
    {
        SectionId: int
        Page: int
        Filter: string
    }

let toPreview (word: Word) : WordPreview =
    {
        Id = word.Id
        Def1 = word.Def1
        Def2 = word.Def2
    }

let upsert (word: Word) (items: WordPreview list) =
    let preview = toPreview word

    if
        items
        |> List.exists (fun item -> item.Id = word.Id)
    then
        items
        |> List.map (fun item -> if item.Id = word.Id then preview else item)
    else
        items @ [ preview ]

[<Tests>]
let tests =
    testList
        "QueryDictionary"
        [
            testCaseAsync "save reconciles retained pages without loading unvisited dictionary data"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let calls = ResizeArray<string>()

                let word =
                    {
                        Id = 42
                        Def1 = "first"
                        Def2 = "second"
                        OtherFields = Map.empty
                    }

                let index =
                    client.Define (
                        EqualityComparer<unit>.Default,
                        fun () _ ->
                            calls.Add "index"

                            completed
                                {
                                    TotalWordCount = 10000
                                    Sections = [ { Id = 7; Name = "A" }; { Id = 8; Name = "B" } ]
                                }
                    )

                let sections =
                    client.Define (
                        EqualityComparer<int>.Default,
                        fun id _ ->
                            calls.Add $"section:{id}"

                            completed
                                {
                                    Id = id
                                    Name = "A"
                                    Words = if id = 7 then [ toPreview word ] else []
                                }
                    )

                let words =
                    client.Define (
                        EqualityComparer<int>.Default,
                        fun id _ ->
                            calls.Add $"word:{id}"
                            completed { word with Id = id }
                    )

                use home = index.Acquire ()
                use section = sections.Acquire 7
                use unrelated = sections.Acquire 8
                use detail = words.Acquire 42

                do!
                    waitUntil (fun () ->
                        home.State.Data.IsSome
                        && section.State.Data.IsSome
                        && unrelated.State.Data.IsSome
                        && detail.State.Data.IsSome)

                let draft = { detail.Value with Def1 = "edited" }
                let history = [ EditorPage draft; SectionPage section; IndexPage home ]

                let reconcile (saved: Saved) =
                    [
                        index.UpdateIfLoaded (
                            (),
                            fun old ->
                                { old with
                                    TotalWordCount = saved.TotalWordCount
                                }
                        )
                        sections.UpdateIfLoaded (
                            saved.SectionId,
                            fun old ->
                                { old with
                                    Words = upsert saved.Word old.Words
                                }
                        )
                        words.UpdateIfLoaded (saved.Word.Id, fun _ -> saved.Word)
                    ]

                let saved =
                    client.Mutate (
                        draft,
                        (fun draft _ ->
                            completed
                                {
                                    Word = draft
                                    SectionId = 7
                                    TotalWordCount = 10000
                                }),
                        reconcile
                    )

                let! _ = awaitOutcome saved
                Expect.equal detail.Value.Def1 "edited" "accepted detail"
                Expect.equal section.Value.Words[0].Def1 "edited" "back shows updated preview"
                Expect.equal home.Value.TotalWordCount 10000 "edit doesn't increment total"
                Expect.equal unrelated.Value.Words [] "unrelated section untouched"
                let created = { draft with Id = 99; Def1 = "new" }

                let add =
                    client.Mutate (
                        created,
                        (fun draft _ ->
                            completed
                                {
                                    Word = draft
                                    SectionId = 7
                                    TotalWordCount = 10001
                                }),
                        reconcile
                    )

                let! _ = awaitOutcome add

                client.Commit (
                    reconcile
                        {
                            Word = created
                            SectionId = 7
                            TotalWordCount = 10001
                        }
                )

                Expect.equal section.Value.Words.Length 2 "receipt is idempotent by ID"
                Expect.equal home.Value.TotalWordCount 10001 "server authoritative total"
                section.Ensure ()
                Expect.equal calls.Count 4 "back reads reconciled page without fetch"
                Expect.equal history.Length 3 "history doesn't need a traversal"
                use addedDetail = words.Acquire 99
                do! waitUntil (fun () -> addedDetail.State.Data.IsSome)
                Expect.sequenceEqual calls [ "index"; "section:7"; "section:8"; "word:42"; "word:99" ] "absent detail was never manufactured"
            }
            testCaseAsync "paged membership is invalidated only for loaded matching pages"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let calls = ResizeArray<SectionKey>()

                let pages =
                    client.Define (
                        EqualityComparer<SectionKey>.Default,
                        fun key _ ->
                            calls.Add key
                            completed ([]: WordPreview list)
                    )

                let key section page =
                    {
                        SectionId = section
                        Page = page
                        Filter = ""
                    }

                use first = pages.Acquire (key 7 1)
                use second = pages.Acquire (key 7 2)
                use unrelated = pages.Acquire (key 8 1)

                do!
                    waitUntil (fun () ->
                        first.State.Data.IsSome
                        && second.State.Data.IsSome
                        && unrelated.State.Data.IsSome)

                client.Commit [ pages.InvalidateWhere (fun key -> key.SectionId = 7) ]
                Expect.equal calls.Count 3 "no hidden or unvisited page fetched"
                Expect.isTrue first.State.IsStale "first matching page"
                Expect.isTrue second.State.IsStale "second matching page"
                Expect.isFalse unrelated.State.IsStale "other section stays fresh"
                first.Ensure ()
                do! waitUntil (fun () -> not first.State.IsStale)
                Expect.equal calls.Count 4 "only returning page fetched"
            }
        ]
