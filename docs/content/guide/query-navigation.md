---
title: Dictionary navigation
order: 25
---

The [compiled example](https://github.com/shayanhabibi/Ranvier/blob/a2b9c30/examples/Ranvier.Query.Dictionary/Navigation.fs)
loads the index, visited sections, and edited words separately. One session owns
the client; each page owns its leases. The editor owns an independent draft.

```fsharp
type Editor = {
    SectionId: int
    Detail: QueryLease<Word> option // None when adding a word
    Draft: Word option
    Saving: bool
}
type Content =
    | IndexPage of QueryLease<Index>
    | SectionPage of QueryLease<Section>
    | EditorPage of Editor
type Page = { Owner: Owner; Content: Content; Loading: bool; Error: exn option }
type Model = { Session: Session; History: Page list }
```

## Back releases the draft, not the save

**Play**: edit → save → Back → server replies. The map uses cache signals, a page
owner, and an async receipt as macros for query entries, page lifetime, and `Mutate`.
`view` switches its dependency from the editor draft to the retained section.

```fsharp map replay code=collapsed code-max-height=24rem
let sectionQuery = createSignal "Tea"
let saved = createAsyncSource<string> ()
let currentPage = createSignal "editor"
let editorOwner, wordQuery, draft =
    createRoot (fun owner ->
        let wordQuery = Trace.named "wordQuery" (fun () -> createSignal "Tea")
        let draft = Trace.named "draft" (fun () -> createSignal wordQuery.Peek)
        owner, wordQuery, draft)
let mutable submitted = "Tea"
let commit =
    createEffect (fun () ->
        let value = saved.Value
        batch (fun () ->
            sectionQuery.Value <- value
            if not editorOwner.IsDisposed then wordQuery.Value <- value))
let view =
    createMemo (fun _ ->
        if currentPage.Value = "editor" then "Draft: " + draft.Value
        else "Section: " + sectionQuery.Value)
createEffect (fun () -> printfn "%s" view.Value)

controls [
    button "Edit draft" (fun () -> if not editorOwner.IsDisposed then draft.Value <- "Green tea")
    |> describe "Typing changes only the editor draft; shared fetched records stay unchanged."
    |> expect "draft is independent" (fun () -> sectionQuery.Peek = "Tea" && wordQuery.Peek = "Tea")
    button "Save" (fun () -> if not editorOwner.IsDisposed then submitted <- draft.Peek)
    |> describe "The client owns the pending save; it has captured the draft."
    button "Back" (fun () ->
        batch (fun () ->
            currentPage.Value <- "section"
            editorOwner.Dispose ()))
    |> describe "The draft and final detail lease disappear. The section and client-owned save remain."
    |> expect "editor released" (fun () -> editorOwner.IsDisposed && view.Peek = "Section: Tea")
    button "Server replies" (fun () -> saved.Settle submitted)
    |> describe "Reconciliation updates the retained section, and its view follows the cache."
    |> expect "view sees save after Back" (fun () -> view.Peek = "Section: Green tea" && sectionQuery.Peek = "Green tea")
]
```

## Consume the model in a view

Keep the Elmish model in a signal. A view reads **both** navigation/drafts and the
current lease's `State`. It then updates on either messages or query publication.

```fsharp
open Ranvier
open Ranvier.Query
open Ranvier.Examples.QueryDictionary

use _ = graph.Activate()
let initial, initialCommands = init graph api
let model = createSignal initial

let rec dispatch message =
    let next, commands = update message model.Peek
    model.Value <- next
    for command in commands do command dispatch

for command in initialCommands do command dispatch
```

Here is a minimal text view; replace the strings with your framework's UI elements:

```fsharp
let queryText render (state: QuerySnapshot<_>) =
    match state.Data, state.Error with
    | Some data, error ->
        let status =
            match error with
            | Some e -> " (" + e.Message + ")"
            | None when state.FetchStatus = FetchStatus.Fetching -> " (refreshing)"
            | None -> ""
        render data + status
    | None, Some error -> "Error: " + error.Message
    | None, None -> "Loading…"

let view = createMemo (fun _ ->
    let page = model.Value.History.Head
    match page.Content with
    | IndexPage query ->
        queryText
            (fun index ->
                let names = index.Sections |> List.map (fun section -> section.Name) |> String.concat ", "
                sprintf "%d words: %s" index.TotalWordCount names)
            query.State
    | SectionPage query ->
        queryText
            (fun section ->
                let definitions = section.Words |> List.map (fun word -> word.Def1) |> String.concat ", "
                section.Name + ": " + definitions)
            query.State
    | EditorPage editor ->
        let status =
            match editor.Saving, page.Error with
            | true, _ -> " (saving)"
            | false, Some e -> " (" + e.Message + ")"
            | false, None -> ""
        match editor.Draft with
        | Some word -> word.Def1 + " / " + word.Def2 + status
        | None ->
            match page.Error with
            | Some e -> "Error: " + e.Message
            | None -> "Loading…")

createEffect (fun () -> printfn "%s" view.Value)
```

UI events dispatch `OpenSection id`, `OpenWord(sectionId, wordId)`, `AddWord sectionId`,
`DraftChanged word`, `Save`, `Back`, or `Home`. Run this adapter on the graph thread.

## Lifetime and commands

- Open: create a page owner; `AcquireOwned(key, owner)`; await `EnsureAsync` and
  send `Loaded`/`LoadFailed`. Copy detail into the draft only if it has no draft.
- Back: dispose the removed owner; ensure the returning page. Fresh data needs no IO.
- Home/logout/shutdown: dispose the session. Home starts a fresh index.
- Save: await `Mutate`; [handle all outcomes](queries.md#outcomes-and-contracts).
  Repair reconciliation failure by refreshing, not by repeating the write.
- Commands catch cancellation and ignore removed page/session identities. A save
  can update retained queries after its editor closes; it cannot update a new session.

`Command = (Msg -> unit) -> unit` fits Elmish's command subscription shape. The
example and its navigation behavior are compiled and tested; no UI dependency is required.
