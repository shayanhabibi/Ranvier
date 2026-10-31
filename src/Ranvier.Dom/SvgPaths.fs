namespace Ranvier.Dom

/// <summary>Reads and writes authored SVG path data through element attributes.</summary>
[<RequireQualifiedAccess>]
module SvgPaths =
    /// <summary>Reads the authored <c>d</c> attribute without normalizing its path syntax.</summary>
    /// <param name="path">The nonnull SVG path element.</param>
    /// <returns><c>None</c> when absent; an empty authored value remains <c>Some ""</c>.</returns>
    /// <exception cref="T:System.ArgumentNullException">The path is null.</exception>
    let getData (path: SVGPathElement) : string option =
        if isNull (box path) then
            nullArg "path"

        path.getAttribute "d" |> Option.ofObj

    /// <summary>Updates the authored <c>d</c> attribute for the engine to parse.</summary>
    /// <param name="path">The nonnull SVG path element.</param>
    /// <param name="value"><c>None</c> and <c>Some null</c> remove the attribute; <c>Some ""</c> keeps a present empty value.</param>
    /// <exception cref="T:System.ArgumentNullException">The path is null.</exception>
    let setData (path: SVGPathElement) (value: string option) : unit =
        if isNull (box path) then
            nullArg "path"

        match value with
        | Some data when not (isNull data) -> path.setAttribute ("d", data)
        | _ -> path.removeAttribute "d"
