module Docs.Site

open System.IO
open Feliz.ViewEngine
open Nacara.Core
open Nacara.Plugins
open Partas.Nacara.Theme

let versions = [ SiteVersion.root "1.0" ]

/// The public API of the Ranvier, Ranvier.CSharp and Ranvier.Elmish assemblies, built beside the site.
let apiOptions =
    let beside =
        System.Reflection.Assembly.GetExecutingAssembly().Location
        |> Path.GetDirectoryName

    { FSharpApi.defaults with
        Root = "reference"
        Title = "API reference"
        Sources =
            [
                for name in [ "Ranvier"; "Ranvier.CSharp"; "Ranvier.Elmish" ] -> FSharpApiSource.create (Path.Combine (beside, $"%s{name}.dll"))
            ]
    }

let theme = Theme.theme (
    Theme.navbar
        [
            NavbarSection ("Guide", "guide", "/guide/")
            NavbarSection ("Concepts", "concepts", "/concepts/")
            NavbarSection ("Benchmarks", "benchmarks", "/benchmarks/")
            NavbarSection ("Fable", "fable", "/fable/")
            NavbarDivider
            NavbarSection ("Reference", "reference", "/reference/")
        ]
    >> Theme.menu
        "guide"
        [
            Menu.section
                "Getting started"
                [
                    Menu.page "guide/index.md"
                    Menu.page "guide/installation.md"
                    Menu.page "guide/getting-started.md"
                    Menu.page "guide/csharp.md"
                    Menu.page "guide/blazor-server.md"
                    Menu.page "guide/elmish.md"
                ]
            Menu.section
                "Core concepts"
                [
                    Menu.page "guide/async-and-pending.md"
                    Menu.page "guide/testing.md"
                    Menu.page "guide/collections.fsx"
                    Menu.page "guide/aggregates.fsx"
                    Menu.page "guide/forms.md"
                ]
            Menu.section
                "Reference"
                [
                    Menu.page "guide/tracing.md"
                    Menu.page "guide/signal-maps.md"
                    Menu.page "guide/troubleshooting.md"
                ]
        ]
    >> Theme.menu
        "concepts"
        [
            Menu.section
                "Concepts"
                [
                    Menu.page "concepts/index.md"
                    Menu.page "concepts/suspension.md"
                    Menu.page "concepts/async-graph.md"
                    Menu.page "concepts/contracts.md"
                    Menu.page "concepts/ecosystem.md"
                    Menu.page "concepts/roadmap.md"
                ]
        ]
    >> Theme.menu
        "benchmarks"
        [
            Menu.section
                "Benchmarks"
                [
                    Menu.page "benchmarks/index.md"
                    Menu.page "benchmarks/signals.md"
                    Menu.page "benchmarks/memos.md"
                    Menu.page "benchmarks/effects.md"
                    Menu.page "benchmarks/lifetimes.md"
                    Menu.page "benchmarks/projections.md"
                    Menu.page "benchmarks/suspension.md"
                    Menu.page "benchmarks/counters.md"
                ]
        ]
    >> Theme.menu "fable" [ Menu.section "Fable" [ Menu.page "fable/index.md" ] ]
    >> Theme.navbarEnd
        [
            NavbarIcon ("GitHub", "https://github.com/shayanhabibi/Ranvier", Icons.github)
        ]
    >> Theme.editUrl "https://github.com/shayanhabibi/Ranvier/edit/main/docs"
    >> Theme.footer (
        Html.p
            [
                Html.text "Ranvier · Preview · "
                Html.a [ prop.href "https://github.com/shayanhabibi/Ranvier"; prop.text "GitHub" ]
                Html.text " · Built with Nacara"
            ]
    ))

/// Live Partas.Solid components on the pages (the landing page's animated state mark and the signal maps), compiled
/// against the Partas.Solid 3 build committed under feed/. The generated project inherits
/// docs/nuget.config, whose partas-local source serves it.
let private solidExamples (options: SolidExamplesOptions) =
    options
    |> SolidExamples.partasVersion "3.0.0-local.e08ad85"
    |> SolidExamples.npm "animejs" "4.5.0"
    |> SolidExamples.project "maps/Ranvier.Docs.Maps.fsproj"
    |> SolidExamples.property "RanvierTrace" "true"
    |> SolidExamples.targetFramework "net10.0"
    |> SolidExamples.transform "map" Maps.transform

/// The og card of each section, keyed by the path segment that names it, with the card's alt text.
let private ogCards =
    [
        "async-and-pending", "async", "Async and pending: boundary states along a nerve fibre"
        "tracing", "tracing", "Tracing: a cause chain traced back along a nerve fibre"
        "signal-maps", "signal-maps", "Signal maps: a traced graph drawn live, a write travelling its edges"
        "guide", "guide", "Ranvier guide: build with signals"
        "concepts", "concepts", "Ranvier concepts: how the graph works"
        "fable", "fable", "Ranvier on Fable: the JavaScript target"
        "benchmarks", "benchmarks", "Ranvier benchmarks: measured per commit"
    ]

/// The card of the most specific section named by a page's path.
let private sectionCard (page: Page) =
    let segments =
        page.Id.Split ([| '/'; '\\'; ':' |])
        |> Array.map (fun s -> s.Replace (".md", ""))

    ogCards
    |> List.tryFind (fun (segment, _, _) -> Array.contains segment segments)
    |> Option.map (fun (_, card, alt) ->
        OgImage.image $"/og/%s{card}.png"
        |> OgImage.withAlt alt
        |> OgImage.withSize 1200 630)

let private ogImages (options: OgImageOptions) =
    options
    |> OgImage.fallback sectionCard
    |> OgImage.defaultImage (
        OgImage.image "/og/home.png"
        |> OgImage.withAlt "Ranvier: fine-grained reactive computation for .NET"
        |> OgImage.withSize 1200 630
    )

/// The shipped C# grammar (<c>c_sharp</c>, after its wasm's <c>tree_sitter_c_sharp</c> export), also written with
/// <c>csharp</c>, <c>cs</c> or <c>c#</c> fences.
let private csharpGrammar (options: TreeSitterOptions) =
    let grammar = TreeSitter.bundled "c_sharp"

    options
    |> TreeSitter.grammars (
        TreeSitter.aliases [ "csharp"; "cs"; "c#" ] grammar
        :: options.Grammars
    )

let reference =
    FSharpApi.collection "reference" DocFrontMatter.decoder apiOptions
    |> Collection.title _.Title
    |> Collection.layout (Theme.layout theme)

let site =
    Site.create "Ranvier"
    |> Site.baseUrl Spec.baseUrl
    |> Site.origin "https://shayanhabibi.github.io"
    |> Site.output "output"
    |> Site.staticFiles "static"
    |> Markdown.register
    |> Literate.registerWith (fun options -> { options with Extensions = [ ".fsx" ] })
    |> TreeSitter.registerWith csharpGrammar
    |> Docs.Directives.register
    |> Sitemap.register
    |> FSharpApi.register apiOptions
    |> LinkValidator.register
    |> SolidExamples.registerWith solidExamples
    |> LiveExample.registerWith (
        LiveExample.preset (
            LiveExamplePreset.create "maps"
            |> LiveExamplePreset.project "maps/Ranvier.Docs.Maps.fsproj"
            |> LiveExamplePreset.property "RanvierTrace" "true"
            |> LiveExamplePreset.css "../brand/tokens/ranvier-brand-tokens.css"
            |> LiveExamplePreset.css "theme/maps.css"
            |> LiveExamplePreset.template "live/snippet.html"
            |> LiveExamplePreset.asDefault
        )
    )
    |> OgImage.registerWith ogImages
    |> AgentFriendly.registerWith (
        AgentFriendly.summary "Fine-grained reactive computation for .NET, with a Fable target"
        >> AgentFriendly.details "Start with the guide. Concepts explains the engine's model; Benchmarks holds measured results."
    )
    |> LightningCss.register
    |> Esbuild.register
    |> Nuglify.minifyHtml
    |> Versions.register versions
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")
    |> Site.collection reference

[<EntryPoint>]
let main argv =
    Nacara.run site argv
