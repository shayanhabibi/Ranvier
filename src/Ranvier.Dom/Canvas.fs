namespace Ranvier.Dom

open System

/// <summary>A canvas context type of HTMLCanvasElement.getContext, numbered as the native <c>RV_CANVAS_CONTEXT_*</c> types.</summary>
type CanvasContextType =
    | TwoD = 1
    | BitmapRenderer = 2
    | WebGL = 3
    | WebGL2 = 4
    | WebGpu = 5

/// <summary>A PredefinedColorSpace of a 2D context, numbered as the native <c>RV_CANVAS_COLOR_SPACE_*</c> values.</summary>
type CanvasColorSpace =
    | Srgb = 0
    | DisplayP3 = 1

/// <summary>A CanvasColorType of a 2D context, numbered as the native <c>RV_CANVAS_COLOR_TYPE_*</c> values.</summary>
type CanvasColorType =
    | Unorm8 = 0
    | Float16 = 1

/// <summary>CanvasRenderingContext2DSettings: the settings a 2D context is created with and reports.</summary>
type Canvas2DSettings =
    {
        Alpha: bool
        Desynchronized: bool
        WillReadFrequently: bool
        ColorSpace: CanvasColorSpace
        ColorType: CanvasColorType
    }

    /// <summary>The getContext defaults: alpha, synchronized, sRGB and unorm8.</summary>
    static member Default =
        {
            Alpha = true
            Desynchronized = false
            WillReadFrequently = false
            ColorSpace = CanvasColorSpace.Srgb
            ColorType = CanvasColorType.Unorm8
        }

/// <summary>The result of a getContext request.</summary>
[<RequireQualifiedAccess>]
type CanvasContextOutcome =
    /// <summary>The canvas's one 2D context: the same object on every request.</summary>
    | Context of context: CanvasRenderingContext2D
    /// <summary>The canvas already has a context of another type, so getContext returns null.</summary>
    | OtherContext
    /// <summary>The provider renders no context of the requested type or settings; the canvas is unchanged.</summary>
    | Unsupported of reason: string

/// <summary>Canvas context creation for one document.</summary>
/// <remarks>
/// Every provider renders 2D contexts only, with alpha, sRGB and unorm8. Other context types and settings read as
/// <c>CanvasContextOutcome.Unsupported</c> on every provider, before the canvas is touched, so a canvas keeps no
/// context from an unsupported request. A context keeps its canvas alive.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type CanvasContextService =
    inherit IDisposable
    /// <summary>HTMLCanvasElement.getContext for <c>contextType</c>, creating the 2D context on the first request.</summary>
    /// <remarks>Settings of a later request are ignored when they are supported.</remarks>
    abstract GetContext: canvas: HTMLCanvasElement * contextType: CanvasContextType * settings: Canvas2DSettings -> CanvasContextOutcome
    /// <summary>CanvasRenderingContext2D.getContextAttributes.</summary>
    abstract Attributes: context: CanvasRenderingContext2D -> Canvas2DSettings

/// <summary>The supported canvas profile shared by canvas providers.</summary>
module CanvasContract =
    /// <summary>Why a provider rejects <c>contextType</c> with <c>settings</c>, or None for a supported request.</summary>
    let unsupported (contextType: CanvasContextType) (settings: Canvas2DSettings) : string option =
        match contextType with
        | CanvasContextType.TwoD ->
            if not settings.Alpha then
                Some "2D contexts without alpha are not rendered"
            elif settings.ColorSpace <> CanvasColorSpace.Srgb then
                Some (
                    "2D contexts render sRGB only, not "
                    + string settings.ColorSpace
                )
            elif settings.ColorType <> CanvasColorType.Unorm8 then
                Some (
                    "2D contexts render unorm8 only, not "
                    + string settings.ColorType
                )
            else
                None
        | CanvasContextType.BitmapRenderer -> Some "bitmaprenderer contexts are not provided"
        | CanvasContextType.WebGL
        | CanvasContextType.WebGL2 -> Some "WebGL contexts are not provided"
        | CanvasContextType.WebGpu -> Some "WebGPU contexts are not provided"
        | _ -> Some ("unknown context type " + string (int contextType))
