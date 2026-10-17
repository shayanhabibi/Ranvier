module RippleSemantics

open Fable.Ripple
open Fable.Ripple.Dom

/// <summary>Mounts an input whose value is bound to a signal written by its own input handler, and returns its disposal.</summary>
let caret hostId =
    let source = Var.create ""

    let mounted =
        Html.mount hostId (fun () ->
            Html.input
                [
                    attr.value (fun () -> source.Value)
                    on.input (fun (text: string) -> source.Value <- text)
                ])

    fun () -> mounted.Dispose ()

/// <summary>Mounts four rows whose third row fails during mount, by its property setter or by its title reader.</summary>
let failure hostId (kind: string) =
    let source = Var.create "a"
    let mutable cleaned = 0
    let mutable reads = 0
    let mutable mounted = None
    let mutable threw = false
    let mutable message = ""

    let title index () =
        reads <- reads + 1

        if kind = "reader" && index = 3 then
            failwith "reader"

        source.Value

    let row index =
        let item, scope =
            Signal.root (fun () ->
                Signal.onCleanup (fun () -> cleaned <- cleaned + 1)

                Html.div
                    [
                        attr.custom ("title", title index)
                        if kind = "setter" && index = 3 then
                            Base.property "explode" (box "boom")
                    ])

        Signal.onCleanup (fun () -> scope.Dispose ())
        item

    try
        mounted <- Some (Html.mount hostId (fun () -> Html.section [ for index in 1..4 -> row index ]))
    with error ->
        threw <- true
        message <- error.Message

    {|
        threw = threw
        message = message
        update =
            fun () ->
                let before = reads
                source.Value <- "b"
                reads - before
        dispose = fun () -> mounted |> Option.iter _.Dispose()
        cleaned = fun () -> cleaned
    |}
