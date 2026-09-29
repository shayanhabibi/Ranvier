module Docs.Site

open System.IO
open Feliz.ViewEngine
open Nacara.Core
open Nacara.Plugins
open Partas.Nacara.Theme

let versions = [ SiteVersion.root "1.0" ]

let baseUrl = "/Ranvier/"

// Stylesheets are read once at startup; under `watch`, CSS edits take effect after a restart.

/// A stylesheet from docs/theme, with the `__BASE__` placeholder in asset URLs replaced by baseUrl.
let private themeCss name =
    File.ReadAllText(Path.Combine (__SOURCE_DIRECTORY__, "theme", name)).Replace("__BASE__", baseUrl)

/// brand/tokens/ranvier-brand-tokens.css, the single source of the --rv-* colours, with its
/// [data-ranvier-theme] selectors mapped to the docs theme's :root[data-theme].
let private brandTokensCss =
    File
        .ReadAllText(Path.Combine (__SOURCE_DIRECTORY__, "..", "brand", "tokens", "ranvier-brand-tokens.css"))
        .Replace("[data-ranvier-theme=", ":root[data-theme=")

/// Colour tokens shared by both schemes. Each resolves to a --rv-* brand token for the active scheme.
let private brandColours (t: Tokens) =
    { t with
        Bg = "var(--rv-canvas)"
        BgSubtle = "var(--rv-surface)"
        BgRaised = "var(--rv-raised)"
        Border = "var(--rv-border)"
        Text = "var(--rv-text)"
        TextMuted = "var(--rv-muted)"
        Heading = "var(--rv-text)"
        Primary = "var(--rv-accent-text)"
        PrimaryContrast = "var(--rv-on-accent)"
        PrimarySubtle = "color-mix(in oklab, var(--rv-contour) 14%, var(--rv-canvas))"
        Note = "var(--rv-accent-text)"
        Tip = "var(--rv-value)"
        Warning = "var(--rv-pending)"
        Danger = "var(--rv-error)"
        CodeInlineBg = "var(--rv-raised)"
        CodeInlineBorder = "var(--rv-border)"
        CodeInlineText = "var(--rv-text)"
    }

/// Syntax colours from the contour/blue/violet axis and the text tokens. Value and error mark
/// diff inserts, deletes and invalid tokens only.
let private brandSyntax (s: SyntaxTokens) =
    { s with
        Comment = "var(--rv-muted)"
        Number = "var(--rv-violet)"
        Constant = "var(--rv-violet)"
        Constructor = "var(--rv-grad-b)"
        Property = "var(--rv-text)"
        Escape = "var(--rv-accent-text)"
        Keyword = "var(--rv-violet)"
        Operator = "var(--rv-muted)"
        Function = "var(--rv-grad-b)"
        Type = "var(--rv-accent-text)"
        Namespace = "var(--rv-accent-text)"
        Variable = "var(--rv-text)"
        Parameter = "var(--rv-text)"
        Punctuation = "var(--rv-muted)"
        Tag = "var(--rv-accent-text)"
        Attribute = "var(--rv-violet)"
        Preprocessor = "var(--rv-violet)"
        Invalid = "var(--rv-error)"
        Inserted = "var(--rv-value)"
        Deleted = "var(--rv-error)"
    }

/// Non-colour tokens. Applied to both schemes: the dark record is independent of the light one,
/// so any value left at its default would be re-emitted under :root[data-theme="dark"].
let private sizing (t: Tokens) =
    { t with
        FontSans = "system-ui, -apple-system, \"Segoe UI\", Roboto, \"Helvetica Neue\", \"Noto Sans\", \"Liberation Sans\", Arial, sans-serif"
        FontMono =
            "\"Geist Mono\", ui-monospace, \"Cascadia Code\", \"JetBrains Mono\", SFMono-Regular, Menlo, Consolas, \"Liberation Mono\", monospace"
        Radius = "0.625rem"
        RadiusSm = "0.375rem"
        ContentWidth = "46rem"
        SidebarWidth = "16.5rem"
        TocWidth = "14rem"
        NavbarOpacity = "76%"
        NavbarBlur = "12px"
    }

/// Geist and Geist Mono from Google Fonts. Geist sets headings, navigation and the wordmark;
/// body copy stays on the system stack.
let private fontLinks =
    [
        Html.link [ prop.rel "preconnect"; prop.href "https://fonts.googleapis.com" ]
        Html.link
            [
                prop.rel "preconnect"
                prop.href "https://fonts.gstatic.com"
                prop.custom ("crossorigin", "")
            ]
        Html.link
            [
                prop.rel "stylesheet"
                prop.href "https://fonts.googleapis.com/css2?family=Geist:wght@400..700&family=Geist+Mono:wght@400..600&display=swap"
            ]
    ]

/// Makes dark the default scheme. With no stored choice, the page renders dark before first paint
/// and the scheme picker shows "Dark" once the DOM is loaded.
let private darkByDefault =
    Html.script
        [
            prop.dangerouslySetInnerHTML
                "(()=>{try{if(!localStorage.getItem(\"nacara-theme\")){var d=document.documentElement;d.dataset.theme=\"dark\";d.dataset.themeSetting=\"dark\";document.addEventListener(\"DOMContentLoaded\",()=>{for(const s of document.querySelectorAll(\"[data-nacara-theme]\"))s.value=d.dataset.themeSetting})}}catch{}})();"
        ]

let theme =
    Theme.defaults
    |> Theme.favIcon "favicon.svg"
    |> Theme.navbar
        [
            NavbarSection ("Guide", "guide", "/guide/")
            NavbarSection ("Concepts", "concepts", "/concepts/")
            NavbarSection ("Benchmarks", "benchmarks", "/benchmarks/")
            NavbarSection ("Fable", "fable", "/fable/")
        ]
    |> Theme.menu
        "guide"
        [
            Menu.section
                "Getting started"
                [
                    Menu.page "guide/index.md"
                    Menu.page "guide/installation.md"
                    Menu.page "guide/getting-started.md"
                    Menu.page "guide/csharp.md"
                ]
            Menu.section
                "Core concepts"
                [
                    Menu.page "guide/async-and-pending.md"
                    Menu.page "guide/collections.fsx"
                    Menu.page "guide/aggregates.fsx"
                ]
            Menu.section
                "Reference"
                [
                    Menu.page "guide/tracing.md"
                    Menu.page "guide/signal-maps.md"
                    Menu.page "guide/troubleshooting.md"
                ]
        ]
    |> Theme.menu
        "concepts"
        [
            Menu.section
                "Concepts"
                [
                    Menu.page "concepts/index.md"
                    Menu.page "concepts/suspension.md"
                    Menu.page "concepts/async-graph.md"
                    Menu.page "concepts/ecosystem.md"
                ]
        ]
    |> Theme.menu
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
    |> Theme.menu "fable" [ Menu.section "Fable" [ Menu.page "fable/index.md" ] ]
    |> Theme.navbarEnd
        [
            NavbarIcon ("GitHub", "https://github.com/shayanhabibi/Ranvier", Icons.github)
        ]
    |> Theme.editUrl "https://github.com/shayanhabibi/Ranvier/edit/main/docs"
    |> Theme.lightTokens (fun t ->
        { brandColours (sizing t) with
            Shadow = "0 1px 2px rgb(23 35 45 / 6%), 0 2px 8px rgb(23 35 45 / 5%)"
            ShadowFloating = "0 12px 32px -8px rgb(23 35 45 / 18%), 0 2px 6px rgb(23 35 45 / 6%)"
        })
    |> Theme.darkTokens (fun t ->
        { brandColours (sizing t) with
            Shadow = "0 1px 2px rgb(0 0 0 / 45%), 0 4px 16px rgb(0 0 0 / 30%)"
            ShadowFloating = "0 16px 40px -8px rgb(0 0 0 / 55%), 0 0 0 1px var(--rv-border)"
        })
    // Strings keep a code-only green per scheme; everything else is a brand token.
    |> Theme.lightSyntax (fun s ->
        { brandSyntax s with
            String = "#1F7A5A"
        })
    |> Theme.darkSyntax (fun s ->
        { brandSyntax s with
            String = "#9FDDBF"
        })
    |> Theme.layerAfter
        "responsive"
        "brand"
        (brandTokensCss
         + System.Environment.NewLine
         + themeCss "brand.css")
    |> Theme.layerAfter "brand" "landing" (themeCss "landing.css")
    |> Theme.layerAfter "landing" "maps" (themeCss "maps.css")
    |> Theme.headExtra (darkByDefault :: fontLinks)
    |> Theme.footer (
        Html.p
            [
                Html.text "Ranvier · Preview · "
                Html.a [ prop.href "https://github.com/shayanhabibi/Ranvier"; prop.text "GitHub" ]
                Html.text " · Built with Nacara"
            ]
    )

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

/// The shipped C# grammar, loaded as <c>c_sharp</c> to match its wasm's <c>tree_sitter_c_sharp</c> export, and
/// written with <c>csharp</c>, <c>cs</c> or <c>c#</c> fences.
let private csharpGrammar (options: TreeSitterOptions) =
    let dir = Path.Combine (__SOURCE_DIRECTORY__, "obj", "tree-sitter", "c_sharp")
    let wasm = Path.Combine (dir, "grammar.wasm.gz")
    let queries = Path.Combine (dir, "highlights.scm")
    Directory.CreateDirectory dir |> ignore
    File.WriteAllBytes (wasm, TreeSitter.bundledGrammar "csharp")
    File.WriteAllBytes (queries, TreeSitter.bundledQueries "csharp")

    let grammar =
        {
            TreeSitter.bundled "csharp" with
                Language = "c_sharp"
                Source = TreeSitterGrammarSource.Files (wasm, queries)
        }

    options
    |> TreeSitter.grammars (TreeSitter.aliases [ "csharp"; "cs"; "c#" ] grammar :: options.Grammars)

let site =
    Site.create "Ranvier"
    |> Site.baseUrl baseUrl
    |> Site.origin "https://shayanhabibi.github.io"
    |> Site.output "output"
    |> Site.staticFiles "static"
    |> Markdown.register
    |> Literate.registerWith (fun options -> { options with Extensions = [ ".fsx" ] })
    |> TreeSitter.registerWith csharpGrammar
    |> Directives.register []
    |> Sitemap.register
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
        AgentFriendly.summary "Fine-grained reactive computation for .NET, with a Fable target planned"
        >> AgentFriendly.details "Start with the guide. Concepts explains the engine's model; Benchmarks holds measured results."
    )
    |> LightningCss.register
    |> Esbuild.register
    |> Nuglify.minifyHtml
    |> Versions.register versions
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")

[<EntryPoint>]
let main argv =
    Nacara.run site argv
