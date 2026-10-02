namespace Fable.Ranvier

open System
open Fable.Core.TS.Dom
open global.Ranvier

module Mount =
    let mount (graph: Graph) (host: Node) (factory: unit -> Node) : IDisposable =
        graph.Run(fun () ->
            let mutable createdOwner: Owner option = None
            try
                let owner =
                    createRoot (fun owner ->
                        createdOwner <- Some owner
                        let root = factory ()
                        if root.nodeType = root.DOCUMENT_FRAGMENT_NODE then
                            raise (ArgumentException("A mount requires one persistent root node; DocumentFragment roots are unsupported.", "factory"))
                        onCleanup (fun () ->
                            root.parentNode |> Option.iter (fun parent -> parent.removeChild root |> ignore))
                        host.appendChild root |> ignore
                        owner)
                { new IDisposable with
                    member _.Dispose() = owner.Dispose() }
            with _ ->
                createdOwner |> Option.iter (fun owner -> owner.Dispose())
                reraise())
