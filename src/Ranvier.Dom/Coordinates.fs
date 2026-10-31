namespace Ranvier.Dom

open System

/// <summary>A point of a coordinate space: CSS pixels, or device pixels in <c>Device</c>.</summary>
[<Struct>]
type CoordinatePoint = { X: float; Y: float }

/// <summary>Four points in the order of <c>DOMQuad</c>'s p1 to p4: the top-left, top-right, bottom-right and bottom-left
/// corners of the box they map.</summary>
type CoordinateQuad =
    {
        P1: CoordinatePoint
        P2: CoordinatePoint
        P3: CoordinatePoint
        P4: CoordinatePoint
    }

/// <summary>A named coordinate space of a document.</summary>
[<RequireQualifiedAccess>]
type CoordinateSpace =
    /// <summary>The local space of the element's CSS box: the border box's top-left corner is 0, 0, before the element's
    /// own transform.</summary>
    | Element of element: Element
    /// <summary>CSS pixels of the document from its top-left corner, independent of scroll and page scale.</summary>
    | Page
    /// <summary>CSS pixels of <c>getBoundingClientRect</c>, <c>elementFromPoint</c> and <c>MouseEvent.clientX</c>.</summary>
    | Client
    /// <summary>CSS pixels of the visible surface, where input arrives: the client point less the visual viewport's
    /// offset, times the page scale. It equals <c>Client</c> at page scale 1 and offset 0.</summary>
    | Host
    /// <summary>Device pixels of the visible surface: <c>Host</c> times the device pixel ratio. A screen point is a device
    /// point plus the surface's origin, which the windowing system reports.</summary>
    | Device

/// <summary>A CSS box of an element.</summary>
[<RequireQualifiedAccess>]
type CoordinateBox =
    | Border
    | Padding
    | Content
    | Margin

/// <summary>Why a conversion has no result.</summary>
[<RequireQualifiedAccess>]
type CoordinateUnavailable =
    /// <summary>An element is outside the document the service shows: removed from it, or of a replaced document.</summary>
    | Detached
    /// <summary>An element belongs to another host or document than the service's.</summary>
    | WrongDocument
    /// <summary>An element space's element has no CSS box: display: none or contents, an inline box, text or an SVG
    /// shape.</summary>
    | NotRendered
    /// <summary>A point maps into an element space whose transforms are singular there, such as scale(0).</summary>
    | NonInvertible
    /// <summary>The provider cannot convert through these spaces, with the provider's reason.</summary>
    | Unsupported of reason: string

/// <summary>The engine conversions behind a coordinate service.</summary>
type CoordinateProvider =
    {
        /// <summary>Converts points from the first space to the second; the array is the provider's to replace.</summary>
        Convert: CoordinateSpace * CoordinateSpace * CoordinatePoint array -> Result<CoordinatePoint array, CoordinateUnavailable>
        /// <summary>The corners of an element's box in a space.</summary>
        BoxQuad: Element * CoordinateBox * CoordinateSpace -> Result<CoordinateQuad, CoordinateUnavailable>
    }

/// <summary>Point and quad conversion between the coordinate spaces of one document.</summary>
/// <remarks>
/// Every conversion reads the current layout, scroll, page scale and device scale; the service holds no geometry
/// between calls. Converting into a space and back returns the point within 1/64 CSS pixel.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type CoordinateService =
    inherit IDisposable
    /// <exception cref="T:System.ArgumentException">A coordinate is not finite.</exception>
    abstract Convert: point: CoordinatePoint * from: CoordinateSpace * ``to``: CoordinateSpace -> Result<CoordinatePoint, CoordinateUnavailable>

    /// <exception cref="T:System.ArgumentException">A coordinate is not finite.</exception>
    abstract ConvertPoints:
        points: CoordinatePoint array * from: CoordinateSpace * ``to``: CoordinateSpace -> Result<CoordinatePoint array, CoordinateUnavailable>

    /// <exception cref="T:System.ArgumentException">A coordinate is not finite.</exception>
    abstract ConvertQuad: quad: CoordinateQuad * from: CoordinateSpace * ``to``: CoordinateSpace -> Result<CoordinateQuad, CoordinateUnavailable>
    /// <summary>The corners of the element's box in <c>to</c>.</summary>
    abstract BoxQuad: element: Element * box: CoordinateBox * ``to``: CoordinateSpace -> Result<CoordinateQuad, CoordinateUnavailable>

/// <summary>Creates coordinate services.</summary>
[<RequireQualifiedAccess>]
module Coordinates =
    let private finite (value: float) =
        not (Double.IsNaN value)
        && not (Double.IsInfinity value)

    let private space (name: string) (value: CoordinateSpace) =
        if isNull (box value) then
            nullArg name

        match value with
        | CoordinateSpace.Element element when isNull (box element) -> nullArg name
        | _ -> ()

    /// <summary>A service over <c>provider</c>.</summary>
    let create (provider: CoordinateProvider) : CoordinateService =
        if isNull (box provider) then
            nullArg "provider"

        let mutable disposed = false

        let live () =
            if disposed then
                raise (ObjectDisposedException (nameof CoordinateService))

        let convert (points: CoordinatePoint array) from target =
            live ()

            if isNull points then
                nullArg "points"

            space "from" from
            space "to" target

            for point in points do
                if not (finite point.X) || not (finite point.Y) then
                    invalidArg "points" "Each coordinate must be finite"

            if points.Length = 0 then
                Ok [||]
            else
                provider.Convert (from, target, Array.copy points)

        { new CoordinateService with
            member _.Convert(point, from, target) =
                convert [| point |] from target
                |> Result.map (fun points -> points[0])

            member _.ConvertPoints(points, from, target) =
                convert points from target

            member _.ConvertQuad(quad, from, target) =
                if isNull (box quad) then
                    nullArg "quad"

                convert [| quad.P1; quad.P2; quad.P3; quad.P4 |] from target
                |> Result.map (fun points ->
                    {
                        P1 = points[0]
                        P2 = points[1]
                        P3 = points[2]
                        P4 = points[3]
                    })

            member _.BoxQuad(element, cssBox, target) =
                live ()

                if isNull (box element) then
                    nullArg "element"

                space "to" target
                provider.BoxQuad (element, cssBox, target)

            member _.Dispose() =
                disposed <- true
        }
