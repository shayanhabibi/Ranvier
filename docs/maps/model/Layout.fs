namespace Ranvier.Docs.Maps

#if RANVIER_TRACE
/// <summary>Where each node of a map sits.</summary>
[<RequireQualifiedAccess>]
module Layout =

    /// <summary>The (layer, row) of each node: layers run from sources to observers, rows top to bottom.</summary>
    /// <remarks>
    /// A node's layer is the longest path from a node without sources. Rows within a layer follow the mean row of
    /// each node's sources in earlier layers, ties broken by id. The same graph always yields the same placement.
    /// </remarks>
    /// <param name="nodes">The nodes to place.</param>
    /// <param name="sources">Each node's sources; sources outside <c>nodes</c> are ignored.</param>
    let place (nodes: int list) (sources: Map<int, int list>) : Map<int, int * int> =
        let known = Set.ofList nodes

        let sourcesOf id =
            sources.TryFind id
            |> Option.defaultValue []
            |> List.filter (fun s -> s <> id && known.Contains s)

        let layers = System.Collections.Generic.Dictionary<int, int>()

        let rec layerOf (visiting: Set<int>) id =
            match layers.TryGetValue id with
            | true, layer -> layer
            | _ ->
                let layer =
                    sourcesOf id
                    |> List.filter (fun s -> not (visiting.Contains s))
                    |> List.map (layerOf (visiting.Add id) >> (+) 1)
                    |> List.fold max 0

                layers[id] <- layer
                layer

        let byLayer =
            known
            |> Set.toList
            |> List.groupBy (layerOf Set.empty)
            |> List.sortBy fst

        let rows = System.Collections.Generic.Dictionary<int, int>()

        for _, members in byLayer do
            let centre id =
                match
                    sourcesOf id
                    |> List.choose (fun s ->
                        match rows.TryGetValue s with
                        | true, r -> Some (float r)
                        | _ -> None)
                with
                | [] -> infinity
                | placed -> List.average placed

            members
            |> List.sortBy (fun id -> centre id, id)
            |> List.iteri (fun row id -> rows[id] <- row)

        known
        |> Seq.map (fun id -> id, (layers[id], rows[id]))
        |> Map.ofSeq
#endif
