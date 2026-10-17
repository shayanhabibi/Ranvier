module Ranvier.View.Foundation.Components

open global.Ranvier
open global.Ranvier.View.Foundation.Representation

type Prop<'T> =
    | Constant of 'T
    | Reactive of (unit -> 'T)

module Prop =
    let private ensure name value =
        if isNull (box value) then
            invalidArg name "A prop source cannot be null"

    let read prop =
        match prop with
        | Constant value -> value
        | Reactive reader ->
            ensure "reader" reader
            reader ()

    let ofSignal (source: Signal<'T>) =
        ensure "source" source
        Reactive (fun () -> source.Value)

    let ofMemo (source: Memo<'T>) =
        ensure "source" source
        Reactive (fun () -> source.Value)

    let text (ops: Operations<'view>) equals prop =
        match prop with
        | Constant value -> ops.Text value
        | Reactive reader ->
            ensure "reader" reader
            ops.Bind equals reader

let inline create (ops: Operations<'view>) (setup: Setup -> 'props -> 'view) (props: 'props) =
    ops.Component (fun context -> setup context props)
