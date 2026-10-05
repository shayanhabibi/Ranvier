module Ranvier.Benchmarks.Queries

open System.Collections.Generic
open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open Ranvier
open Ranvier.Query

type WordPreview = { Id: int; Def1: string; Def2: string }
type SectionPreview = { Id: int; Name: string }

type Word =
    {
        Id: int
        Def1: string
        Def2: string
        OtherFields: Map<string, string>
    }

type Section =
    {
        Id: int
        Name: string
        Words: WordPreview list
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
    | IndexPage of Index
    | SectionPage of Section
    | WordPage of Word

let private preview (word: Word) : WordPreview =
    {
        Id = word.Id
        Def1 = word.Def1
        Def2 = word.Def2
    }

let private patchSection (saved: Saved) (section: Section) =
    { section with
        Words =
            section.Words
            |> List.map (fun item -> if item.Id = saved.Word.Id then preview saved.Word else item)
    }

let private update (saved: Saved) history =
    history
    |> List.map (function
        | WordPage old when old.Id = saved.Word.Id -> WordPage saved.Word
        | SectionPage old when old.Id = saved.SectionId -> SectionPage (patchSection saved old)
        | IndexPage old ->
            IndexPage
                { old with
                    TotalWordCount = saved.TotalWordCount
                }
        | other -> other)

let private consume (index: Index) (section: Section) (word: Word) =
    index.TotalWordCount
    + int section.Words.Head.Def1[7]
    + int word.Def1[7]

[<MemoryDiagnoser; BenchmarkCategory "Query">]
type QueryReconciliationBenchmarks() =
    let initialWord =
        {
            Id = 1
            Def1 = "initial-A"
            Def2 = "second"
            OtherFields = Map.empty
        }

    let initialIndex =
        {
            TotalWordCount = 100000
            Sections = [ { Id = 7; Name = "A" } ]
        }

    let mutable graph = Unchecked.defaultof<Graph>
    let mutable client = Unchecked.defaultof<QueryClient>
    let mutable index = Unchecked.defaultof<QueryFamily<unit, Index>>
    let mutable sections = Unchecked.defaultof<QueryFamily<int, Section>>
    let mutable words = Unchecked.defaultof<QueryFamily<int, Word>>
    let mutable home = Unchecked.defaultof<QueryLease<Index>>
    let mutable section = Unchecked.defaultof<QueryLease<Section>>
    let mutable detail = Unchecked.defaultof<QueryLease<Word>>
    let mutable history: Page list = []
    let mutable counter = 0

    [<Params(10, 1000, 10000)>]
    member val Previews = 10 with get, set

    member private _.NextSave() =
        counter <- counter + 1

        {
            Word =
                { initialWord with
                    Def1 = if counter % 2 = 0 then "edited-A" else "edited-B"
                }
            SectionId = 7
            TotalWordCount = initialIndex.TotalWordCount
        }

    member private _.Edits(saved: Saved) =
        [
            index.UpdateIfLoaded (
                (),
                fun old ->
                    { old with
                        TotalWordCount = saved.TotalWordCount
                    }
            )
            sections.UpdateIfLoaded (saved.SectionId, patchSection saved)
            words.UpdateIfLoaded (saved.Word.Id, fun _ -> saved.Word)
        ]

    member private _.QueryHistory() =
        [ WordPage detail.Value; SectionPage section.Value; IndexPage home.Value ]

    [<GlobalSetup>]
    member this.Setup() =
        let initialSection =
            {
                Id = 7
                Name = "A"
                Words = List.init this.Previews (fun i -> preview { initialWord with Id = i + 1 })
            }

        history <- [ WordPage initialWord; SectionPage initialSection; IndexPage initialIndex ]

        graph <-
            new Graph (
                { GraphOptions.Default with
                    Dispatcher = Some (ImmediateDispatcher ())
                }
            )

        client <- new QueryClient (graph)
        index <- client.Define (EqualityComparer<unit>.Default, fun () _ -> Task.FromResult initialIndex)
        sections <- client.Define (EqualityComparer<int>.Default, fun _ _ -> Task.FromResult initialSection)
        words <- client.Define (EqualityComparer<int>.Default, fun _ _ -> Task.FromResult initialWord)
        home <- index.Acquire ()
        section <- sections.Acquire 7
        detail <- words.Acquire 1

        for _ in 1..4 do
            let saved = this.NextSave ()
            history <- update saved history
            client.Commit (this.Edits saved)

            if history <> this.QueryHistory () then
                failwith "Hand-written and Query updates produced different records."

        if initialSection.Words.Head.Def1 <> initialWord.Def1 then
            failwith "The shared initial list was mutated."

    [<GlobalCleanup>]
    member _.Cleanup() =
        detail.Dispose ()
        section.Dispose ()
        home.Dispose ()
        client.Dispose ()
        graph.Dispose ()

    [<Benchmark(Baseline = true)>]
    member this.HandWrittenUpdate() =
        history <- update (this.NextSave ()) history

        match history with
        | [ WordPage word; SectionPage section; IndexPage index ] -> consume index section word
        | _ -> failwith "Unexpected history shape."

    [<Benchmark>]
    member this.QueryCommit() =
        client.Commit (this.Edits (this.NextSave ()))
        consume home.Value section.Value detail.Value
