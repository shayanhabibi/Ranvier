module Docs.Directives

open Feliz.ViewEngine
open Nacara.Core
open Nacara.Plugins

let private centerDirective =
    Directive.create "center" Decode.node
    |> Directive.render (fun _ _ contents ->
        Html.div [
            prop.style [ style.display.flex; style.justifyContent.center ]
            prop.children contents
        ])

// TODO - layer doesnt carry padding, gap, et al
let private cardsDirective =
    Directive.create "cards" Decode.node
    |> Directive.render (fun _ _ contents ->
        Html.div [ prop.className "rv-cards"; prop.children contents ]
        )

let private cardDirective =
    Directive.create
        "card"
        (Decode.object (fun get ->
            {|
                title = get.Required.Field "title" Decode.string
                href = get.Optional.Field "href" Decode.string
            |}))
    |> Directive.render (fun _ args contents ->
        Html.a
            [
                prop.className "rv-card"
                if args.href.IsSome then
                    prop.href args.href.Value
                prop.children
                    [
                        Html.strong [ prop.className "rv-card__title"; prop.text args.title ]
                        contents
                    ]
            ])

let private directives = [ centerDirective; cardsDirective; cardDirective ]

let register = Directives.register directives
