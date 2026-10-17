module Ranvier.View.Foundation.Fixtures

open Ranvier.View.Foundation.Representation

let create (ops: Operations<'view>) workload sugar read =
    let width =
        match workload with
        | "wide" -> 256
        | "static"
        | "fragments"
        | "reactive" -> 64
        | _ -> invalidArg "workload" workload

    let children =
        [|
            for i in 1..width do
                let text = string i

                yield
                    match sugar with
                    | "functions" -> functionSugar ops text
                    | "direct"
                    | "builder" -> ops.Element Div [| Attribute ("title", "sample") |] [| ops.Text text |]
                    | _ -> invalidArg "sugar" sugar
        |]

    let children =
        if workload = "reactive" then
            Array.append children [| ops.Bind (=) read |]
        else
            children

    let fragment =
        if sugar = "builder" then
            let builder = FragmentBuilder ops

            builder {
                yield ops.Text "prefix"
                yield! children
            }
        else
            ops.Fragment (Array.append [| ops.Text "prefix" |] children)

    let body =
        if workload = "fragments" then
            let rec nest depth view =
                if depth = 0 then
                    view
                else
                    nest (depth - 1) (ops.Fragment [| view |])

            nest 20 fragment
        else
            fragment

    let view = ops.Element Div [| Attribute ("id", "fixture") |] [| body |]
    ops.Component (fun _ -> view)
