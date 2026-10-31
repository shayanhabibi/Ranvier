namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>HTMLCanvasElement.getContext and CanvasRenderingContext2D.getContextAttributes of the active document realm.</summary>
module BrowserCanvas =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.ownerDocument === $1")>]
    let private ownedBy (canvas: HTMLCanvasElement) (document: Document) : bool = jsNative

    [<Emit("$0.getContext('2d', {alpha: true, desynchronized: $1, willReadFrequently: $2, colorSpace: 'srgb', colorType: 'unorm8'})")>]
    let private context2d (canvas: HTMLCanvasElement) (desynchronized: bool) (willReadFrequently: bool) : CanvasRenderingContext2D = jsNative

    [<Emit("(() => { const a = $0.getContextAttributes(); return [a.alpha !== false, !!a.desynchronized, !!a.willReadFrequently, String(a.colorSpace ?? 'srgb'), String(a.colorType ?? 'unorm8')]; })()")>]
    let private attributes (context: CanvasRenderingContext2D) : obj array = jsNative

    /// <summary>A canvas context service for canvases of <c>document</c>.</summary>
    /// <remarks>Raises NotSupportedException without an active document Window, and ArgumentException for a canvas of another document.</remarks>
    let forDocument (document: Document) : CanvasContextService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Canvas contexts require an active document Window")

        let disposed = ref false

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof CanvasContextService))

        { new CanvasContextService with
            member _.GetContext(canvas, contextType, settings) =
                check ()

                if isNull (box canvas) then
                    nullArg "canvas"

                if not (ownedBy canvas document) then
                    invalidArg "canvas" "The canvas belongs to another document"

                match CanvasContract.unsupported contextType settings with
                | Some reason -> CanvasContextOutcome.Unsupported reason
                | None ->
                    let context = context2d canvas settings.Desynchronized settings.WillReadFrequently

                    if isNull (box context) then
                        CanvasContextOutcome.OtherContext
                    else
                        CanvasContextOutcome.Context context

            member _.Attributes context =
                check ()

                if isNull (box context) then
                    nullArg "context"

                let values = attributes context

                {
                    Alpha = unbox values[0]
                    Desynchronized = unbox values[1]
                    WillReadFrequently = unbox values[2]
                    ColorSpace =
                        (if unbox<string> values[3] = "display-p3" then
                             CanvasColorSpace.DisplayP3
                         else
                             CanvasColorSpace.Srgb)
                    ColorType =
                        (if unbox<string> values[4] = "float16" then
                             CanvasColorType.Float16
                         else
                             CanvasColorType.Unorm8)
                }

          interface IDisposable with
              member _.Dispose() =
                  disposed.Value <- true
        }
