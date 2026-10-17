module RipplePerf

open Fable.Ripple
open Fable.Ripple.Dom

/// <summary>Mounts signal-backed rows in an owned scope and returns update and disposal callbacks.</summary>
let mount hostId width =
    let source = Var.create "a"

    let mounted =
        Html.mount hostId (fun () ->
            let disabled = Signal.computed (fun () -> source.Value = "blocked")

            let row index =
                let item, scope =
                    Signal.root (fun () ->
                        let local = Var.create 0

                        Html.div
                            [
                                attr.id ("row-" + string index)
                                attr.className "panel"
                                attr.custom ("title", fun () -> source.Value)
                                Html.input [ attr.value (fun () -> source.Value) ]
                                Html.button
                                    [
                                        attr.disabled disabled
                                        on.click (fun _ -> local.Value <- local.Value + 1)
                                        Html.text (fun () -> string local.Value)
                                    ]
                            ])

                Signal.onCleanup (fun () -> scope.Dispose ())
                item

            Html.section [ for index in 1..width -> row index ])

    (fun value -> Signal.batch (fun () -> source.Value <- value)), (fun () -> mounted.Dispose ())
