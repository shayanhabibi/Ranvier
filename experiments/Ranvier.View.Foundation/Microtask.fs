module Ranvier.View.Foundation.Microtask

open Fable.Core
open Ranvier.View.Foundation.Scheduling

[<Global>]
let private globalThis: TS.Es.GlobalThis = jsNative

let private post callback =
    globalThis.queueMicrotask callback

let create graph failure =
    Queue (graph, failure, post)

let mode failure =
    QueuedCommits (failure, post)
