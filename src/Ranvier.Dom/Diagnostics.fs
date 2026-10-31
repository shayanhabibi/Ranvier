namespace Ranvier.Dom

open System

/// <summary>Whether a provider implements a diagnostics feature.</summary>
[<RequireQualifiedAccess>]
type DiagnosticsAvailability =
    | Supported
    /// <summary>The provider has no equivalent, with the provider's reason.</summary>
    | Unsupported of reason: string

/// <summary>The diagnostics features of a provider.</summary>
type DiagnosticsFeatures =
    {
        RenderTree: DiagnosticsAvailability
        MatchedRules: DiagnosticsAvailability
        BoxModel: DiagnosticsAvailability
        /// <summary>A browser developer-tools frontend attached to the document.</summary>
        DeveloperToolsFrontend: DiagnosticsAvailability
    }

/// <summary>Why a diagnostics read has no result.</summary>
[<RequireQualifiedAccess>]
type DiagnosticsUnavailable =
    /// <summary>The service has diagnostics disabled.</summary>
    | Disabled
    /// <summary>The element has no box the read can describe: it is display: none, an inline box, or detached.</summary>
    | NotRendered
    /// <summary>The provider has no such read, with the provider's reason.</summary>
    | Unsupported of reason: string

/// <summary>The cascade origin of a matched style rule.</summary>
type CascadeOrigin =
    | UserAgent = 0
    | User = 1
    | Author = 2
    /// <summary>The element's style attribute.</summary>
    | Inline = 3

/// <summary>A selector specificity (a, b, c).</summary>
[<Struct>]
type Specificity = { A: int; B: int; C: int }

/// <summary>A style rule that matches an element, as the engine serialises it.</summary>
type MatchedStyleRule =
    {
        Origin: CascadeOrigin
        /// <summary>The rule's selector list; empty for an inline entry.</summary>
        SelectorText: string
        /// <summary>The most specific selector of the list that matches the element; empty for an inline entry.</summary>
        MatchedSelector: string
        /// <summary>The specificity of <c>MatchedSelector</c>: <c>:where()</c> adds (0, 0, 0) and <c>:is()</c> its most specific argument.</summary>
        Specificity: Specificity
        SelectorCount: int
        MatchedSelectorCount: int
        /// <summary>The declaration block, without braces.</summary>
        Declarations: string
    }

/// <summary>A rectangle in CSS pixels of the viewport after scrolling, as <c>getBoundingClientRect</c> reports one.</summary>
type BoxRect =
    {
        X: float
        Y: float
        Width: float
        Height: float
    }

/// <summary>The four boxes of an element's CSS box, each the bounding rectangle after transforms.</summary>
type BoxModel =
    {
        Content: BoxRect
        Padding: BoxRect
        /// <summary>Equals <c>getBoundingClientRect()</c>.</summary>
        Border: BoxRect
        Margin: BoxRect
    }

/// <summary>Annotations of a render tree dump, numbered as the native <c>RV_RENDER_TREE_*</c> flags.</summary>
[<Flags>]
type RenderTreeOptions =
    | None = 0
    /// <summary>Every layer, including those that paint nothing; a document dump only.</summary>
    | AllLayers = 1
    /// <summary>The layer lists; a document dump only.</summary>
    | LayerNesting = 2
    | Overflow = 4
    | SvgGeometry = 8
    | IdAndClass = 16
    | LayoutState = 32

/// <summary>The engine reads behind a diagnostics service.</summary>
type DiagnosticsProvider =
    {
        Features: DiagnosticsFeatures
        /// <summary>Switches the engine's own opt-in, where it has one.</summary>
        SetEnabled: bool -> unit
        /// <summary>The render tree text of the document (<c>None</c>) or of an element.</summary>
        RenderTree: Element option * RenderTreeOptions -> Result<string, DiagnosticsUnavailable>
        MatchedRules: Element -> Result<MatchedStyleRule array, DiagnosticsUnavailable>
        BoxModel: Element -> Result<BoxModel, DiagnosticsUnavailable>
    }

/// <summary>
/// Opt-in diagnostics of one document: its render tree, the style rules matching an element and an element's box
/// model, without a developer-tools frontend or script evaluation.
/// </summary>
/// <remarks>
/// A service starts disabled, and every read returns <c>Disabled</c> until <c>Enable</c>. A read returns copied values:
/// the service holds no engine object between calls. Disposing the service disables it.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type DiagnosticsService =
    inherit IDisposable
    abstract Features: DiagnosticsFeatures
    abstract Enabled: bool
    abstract Enable: unit -> unit
    abstract Disable: unit -> unit
    /// <summary>The document's render tree text: renderers and layers nested by two-space indentation.</summary>
    /// <exception cref="T:System.ArgumentException"><c>options</c> has an unknown flag.</exception>
    abstract DocumentRenderTree: options: RenderTreeOptions -> Result<string, DiagnosticsUnavailable>
    /// <summary>The element's renderer subtree, one line per renderer.</summary>
    /// <exception cref="T:System.ArgumentException"><c>options</c> has an unknown or a layer flag.</exception>
    abstract RenderTree: element: Element * options: RenderTreeOptions -> Result<string, DiagnosticsUnavailable>
    /// <summary>The style rules matching the element in cascade order, the inline entry last.</summary>
    abstract MatchedRules: element: Element -> Result<MatchedStyleRule array, DiagnosticsUnavailable>
    abstract BoxModel: element: Element -> Result<BoxModel, DiagnosticsUnavailable>

/// <summary>Creates diagnostics services.</summary>
[<RequireQualifiedAccess>]
module Diagnostics =
    let private known =
        RenderTreeOptions.AllLayers
        ||| RenderTreeOptions.LayerNesting
        ||| RenderTreeOptions.Overflow
        ||| RenderTreeOptions.SvgGeometry
        ||| RenderTreeOptions.IdAndClass
        ||| RenderTreeOptions.LayoutState

    let private layers =
        RenderTreeOptions.AllLayers
        ||| RenderTreeOptions.LayerNesting

    /// <summary>The <c>Unsupported</c> reason of a provider without a developer-tools frontend.</summary>
    let noFrontend =
        DiagnosticsAvailability.Unsupported "No developer-tools frontend is attached or available."

    /// <summary>A service over <c>provider</c>.</summary>
    let create (provider: DiagnosticsProvider) : DiagnosticsService =
        if isNull (box provider) then
            nullArg "provider"

        let mutable enabled = false
        let mutable disposed = false

        let live () =
            if disposed then
                raise (ObjectDisposedException (nameof DiagnosticsService))

        let element (value: Element) =
            if isNull (box value) then
                nullArg "element"

        let flags (options: RenderTreeOptions) forbidden =
            if options &&& ~~~known <> RenderTreeOptions.None then
                invalidArg "options" $"Unknown render tree options {int options}."

            if options &&& forbidden <> RenderTreeOptions.None then
                invalidArg "options" "Layer options apply to a document dump only."

        let gated read =
            if enabled then
                read ()
            else
                Error DiagnosticsUnavailable.Disabled

        let set value =
            if enabled <> value then
                provider.SetEnabled value
                enabled <- value

        { new DiagnosticsService with
            member _.Features =
                live ()
                provider.Features

            member _.Enabled = enabled && not disposed

            member _.Enable() =
                live ()
                set true

            member _.Disable() =
                live ()
                set false

            member _.DocumentRenderTree options =
                live ()
                flags options RenderTreeOptions.None
                gated (fun () -> provider.RenderTree (None, options))

            member _.RenderTree(target, options) =
                live ()
                element target
                flags options layers
                gated (fun () -> provider.RenderTree (Some target, options))

            member _.MatchedRules target =
                live ()
                element target
                gated (fun () -> provider.MatchedRules target)

            member _.BoxModel target =
                live ()
                element target
                gated (fun () -> provider.BoxModel target)

            member _.Dispose() =
                if not disposed then
                    set false
                    disposed <- true
        }
