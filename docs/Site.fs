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
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "theme", name)).Replace("__BASE__", baseUrl)

/// brand/tokens/ranvier-brand-tokens.css, the single source of the --rv-* colours, with its
/// [data-ranvier-theme] selectors mapped to the docs theme's :root[data-theme].
let private brandTokensCss =
    File
        .ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "..", "brand", "tokens", "ranvier-brand-tokens.css"))
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
        FontSans =
            "system-ui, -apple-system, \"Segoe UI\", Roboto, \"Helvetica Neue\", \"Noto Sans\", \"Liberation Sans\", Arial, sans-serif"
        FontMono =
            "ui-monospace, \"Cascadia Code\", \"JetBrains Mono\", SFMono-Regular, Menlo, Consolas, \"Liberation Mono\", monospace"
        Radius = "0.625rem"
        RadiusSm = "0.375rem"
        ContentWidth = "46rem"
        SidebarWidth = "16.5rem"
        TocWidth = "14rem"
        NavbarOpacity = "76%"
        NavbarBlur = "12px"
    }

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
            NavbarSection("Guide", "guide", "/guide/")
            NavbarSection("Concepts", "concepts", "/concepts/")
            NavbarSection("Benchmarks", "benchmarks", "/benchmarks/")
            NavbarSection("Fable", "fable", "/fable/")
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
                ]
            Menu.section "Core concepts" [ Menu.page "guide/async-and-pending.md"; Menu.page "guide/collections.md" ]
            Menu.section "Reference" [ Menu.page "guide/troubleshooting.md" ]
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
                ]
        ]
    |> Theme.menu "fable" [ Menu.section "Fable" [ Menu.page "fable/index.md" ] ]
    |> Theme.menu "about" [ Menu.section "About" [ Menu.page "about/provenance.md" ] ]
    |> Theme.navbarEnd
        [
            NavbarDynamicWidget(Versions.switcher (Versions.versions versions Versions.defaults))
            NavbarIcon("GitHub", "https://github.com/shayanhabibi/Ranvier", Icons.github)
        ]
    |> Theme.editUrl "https://github.com/shayanhabibi/Ranvier/edit/main/docs"
    |> Theme.lightTokens (fun t ->
        { brandColours (sizing t) with
            Shadow = "0 1px 2px rgb(23 35 45 / 6%), 0 2px 8px rgb(23 35 45 / 5%)"
            ShadowFloating = "0 12px 32px -8px rgb(23 35 45 / 18%), 0 2px 6px rgb(23 35 45 / 6%)"
        }
    )
    |> Theme.darkTokens (fun t ->
        { brandColours (sizing t) with
            Shadow = "0 1px 2px rgb(0 0 0 / 45%), 0 4px 16px rgb(0 0 0 / 30%)"
            ShadowFloating = "0 16px 40px -8px rgb(0 0 0 / 55%), 0 0 0 1px var(--rv-border)"
        }
    )
    // Strings keep a code-only green per scheme; everything else is a brand token.
    |> Theme.lightSyntax (fun s -> { brandSyntax s with String = "#1F7A5A" })
    |> Theme.darkSyntax (fun s -> { brandSyntax s with String = "#9FDDBF" })
    |> Theme.layerAfter "responsive" "brand" (brandTokensCss + System.Environment.NewLine + themeCss "brand.css")
    |> Theme.layerAfter "brand" "landing" (themeCss "landing.css")
    |> Theme.headExtra [ darkByDefault ]
    |> Theme.footer (
        Html.p
            [
                Html.text "Ranvier · Preview · "
                Html.a [ prop.href "https://github.com/shayanhabibi/Ranvier"; prop.text "GitHub" ]
                Html.text " · "
                Html.a [ prop.href (baseUrl + "about/provenance/"); prop.text "Provenance" ]
                Html.text " · Built with Nacara"
            ]
    )

let site =
    Site.create "Ranvier"
    |> Site.baseUrl baseUrl
    |> Site.origin "https://shayanhabibi.github.io"
    |> Site.output "output"
    |> Site.staticFiles "static"
    |> Markdown.register
    |> TreeSitter.register
    |> Directives.register []
    |> Sitemap.register
    |> LinkValidator.register
    |> LightningCss.register
    |> Esbuild.register
    |> Nuglify.minifyHtml
    |> Versions.register versions
    |> GitHubPages.register
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")

[<EntryPoint>]
let main argv = Nacara.run site argv
