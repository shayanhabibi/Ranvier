module Fable.Ranvier.Global

open Fable.Core
open Fable.Core.TS.Dom
open Fable.Core.TS

[<Global "globalThis">]
let globalThis: GlobalThis = jsNative

[<Global "window">]
let window: Window = jsNative

[<Global "document">]
let document: Document = jsNative

let isBrowser = lazy (Option.ofObj document |> Option.isSome)
