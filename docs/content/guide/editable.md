---
title: Editable values
---

An `Editable<'T>` starts with a value derived from upstream state and can be edited locally.
`createEditable` drops the edit when the seed publishes a different value. Use [Drafts](drafts.md)
when local edits should survive upstream changes.

This quantity starts at `1`. A local edit sets it to `3`; a new upstream quantity of `5` replaces
that edit.

```fsharp
open Ranvier

let graph = new Graph ()

graph.Run (fun () ->
    let order = createSignal {| Quantity = 1 |}
    let quantity = createEditable (fun _ -> order.Value.Quantity)
    createEffect (fun () -> printfn "quantity = %d (edited: %b)" quantity.Value quantity.IsEdited)

    quantity.Value <- 3                  // quantity = 3 (edited: true)
    order.Value <- {| Quantity = 5 |}    // quantity = 5 (edited: false)
)
```

`createEditable seed` returns an `Editable<'T>`:

| Member | Reads |
| --- | --- |
| `Value` | The edit while one is in force, the seed's value otherwise. Setting it records an edit. |
| `IsEdited` | Whether an edit is in force. Tracked, so an effect can show a "modified" marker. |
| `Upstream` | The seed's current value, whether or not an edit is in force. |
| `Reset ()` | Drops the edit. |
| `TryValue`, `Peek`, `Status` | As on a memo. |

`seed` is a pure derivation, like a `createMemo` body, and receives the seed's own last value.

### When an edit is dropped

`createEditable` drops its edit when the seed publishes a different value under the graph's
equality policy. This follows Solid 2.0's writable memo behaviour.

Edit both fields to `3`, then publish an upstream `5`. The editable follows the new seed;
the draft keeps `3` until reset.

```fsharp map replay code=collapsed
let upstream = createSignal 1
let editable = createEditable (fun _ -> upstream.Value)
let draft = createDraft (fun _ -> upstream.Value)
createEffect (fun () -> printfn "editable = %d, draft = %d" editable.Value draft.Value)

controls [
    button "Edit both to 3" (fun () -> batch (fun () -> editable.Value <- 3; draft.Value <- 3))
    |> describe "Both fields now hold a local edit of 3."
    |> expect "Both fields now hold a local edit of 3." (fun () -> editable.Peek = 3 && draft.Peek = 3)
    button "Upstream to 5" (fun () -> upstream.Value <- 5)
    |> describe "The editable follows upstream to 5; the draft preserves its local 3."
    |> expect "The editable follows upstream to 5; the draft preserves its local 3." (fun () -> editable.Peek = 5 && draft.Peek = 3)
    button "Reset draft" (fun () -> draft.Reset ())
    |> describe "Reset drops the draft edit and reveals the upstream 5."
    |> expect "Reset drops the draft edit and reveals the upstream 5." (fun () -> draft.Peek = 5)
]
```

::::details Test your understanding

The seed starts at A, and the user edits it. What happens if the seed publishes B, then A again?
What if both writes happen in one batch before the seed runs?

:::details Answer
Publishing B drops the edit; returning to A does not restore it. If both writes happen before the
seed runs and its result is still equal to A, the edit stays in force. See
[Batch](batch.md).
:::
::::

:::warning Record seeds compare by reference by default
A seed that builds a fresh record on every run drops the edit even if the contents are equal.
Select a field, or return the previous seed value when nothing changed.
:::

:::tip Keep user input through a reload
Use `createDraft` when a reload should preserve what the user typed. `Upstream` still exposes
the reloaded value, so the form can offer to accept it. `Reset` drops the edit.
:::

### Pending and failed seeds

:::details Editing while upstream data is pending or failed

- An edit made while the seed is pending belongs to the seed's last settled value. It stays in force until the seed
  settles on an unequal value.
- An editable whose seed fails reads the failure. A draft with an edit in force reads the edit.

:::

## C#

`Reactive.Editable(() => …)` creates the same value over a `Func<T>` seed.
