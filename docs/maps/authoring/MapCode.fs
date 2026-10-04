namespace Ranvier.Docs.Maps.Authoring

open System.Net
open System.Text.RegularExpressions

/// <summary>Native code disclosures for the HTML cards emitted by the Solid map transform.</summary>
[<RequireQualifiedAccess>]
module MapCode =
    let private card =
        Regex (
            "(?<start><div class=\"partas-solid-card partas-solid-card--map\">\\s*)(?<code>(?:<figure\\b[^>]*>(?:(?!</figure>).)*</figure>|<nacara-tabs\\b[^>]*>(?:(?!</nacara-tabs>).)*</nacara-tabs>)\\s*)(?<mount><div (?<attributes>[^>]*class=\"partas-solid\"[^>]*)>)",
            RegexOptions.Singleline ||| RegexOptions.Compiled
        )

    let private attribute name attributes =
        let matched = Regex.Match (attributes, "(?:^|\\s)" + name + "=\"(?<value>[^\"]*)\"")

        if matched.Success then
            Some (WebUtility.HtmlDecode matched.Groups["value"].Value)
        else
            None

    /// <summary>Wraps registered map cells' code in a disclosure, preserving the highlighted markup and map mount.</summary>
    /// <remarks>Accepts the Solid plugin's rendered HTML before HTML minification; repeated calls preserve existing disclosures.</remarks>
    let rewrite (lookup: string -> string -> MapFlags option) (html: string) =
        card.Replace (
            html,
            MatchEvaluator (fun matched ->
                let code = matched.Groups["code"].Value
                let attributes = matched.Groups["attributes"].Value

                let options =
                    match attribute "data-partas-page" attributes, attribute "data-partas-cell" attributes with
                    | Some page, Some cell -> lookup page cell
                    | _ -> None

                match options with
                | Some options ->
                    let opened = if options.CodeOpen then " open" else ""

                    let height =
                        match options.CodeMaxHeight with
                        | Some height ->
                            " style=\"max-height:"
                            + WebUtility.HtmlEncode height
                            + "\" tabindex=\"0\" role=\"region\" aria-label=\"Signal map code\""
                        | None -> ""

                    matched.Groups["start"].Value
                    + "<details class=\"rv-map-code\""
                    + opened
                    + "><summary>Example code</summary>"
                    + "<div class=\"rv-map-code__body\""
                    + height
                    + ">"
                    + code
                    + "</div></details>"
                    + matched.Groups["mount"].Value
                | _ -> matched.Value)
        )
