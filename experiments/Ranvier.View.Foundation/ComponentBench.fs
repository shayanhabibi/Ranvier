module ComponentBench

open global.Ranvier.View.Foundation.Representation
open global.Ranvier.View.Foundation.Components

type Props = { Title: Prop<string> }

let definitions representation helper reactive count =
    let props =
        {
            Title =
                if reactive then
                    Reactive (fun () -> "title")
                else
                    Constant "title"
        }

    let run (ops: Operations<'view>) =
        let setup (_: Setup) (p: Props) =
            Prop.text ops (=) p.Title

        Array.init count (fun _ ->
            if helper then
                box (create ops setup props)
            else
                box (ops.Component (fun context -> setup context props)))

    if representation = "tree" then run Tree.ops
    elif representation = "program" then run Program.ops
    else invalidArg "representation" representation
