---
title: Drafts
---

`createDraft seed` returns an `Editable<'T>` whose local edit stays in force until `Reset ()`.
Use it for input that should survive a reload, and use [editable values](editable.md) when a new
upstream value should replace the edit.

```fsharp
let upstream = createSignal "Ada"
let name = createDraft (fun _ -> upstream.Value)
name.Value <- "Grace"
upstream.Value <- "Katherine"
// name.Value is "Grace"; name.Upstream is "Katherine".
name.Reset ()
// name.Value is now "Katherine".
```

`Value` and `IsEdited` are tracked reads. `Upstream` exposes the seed independently of the edit;
`Peek` reads the stored value without refreshing it. The seed is a pure derivation and receives
its own previous value, as on [Memos](memos.md#the-previous-value).

## Pending and failed seeds

An edited draft continues to display its edit when the seed is pending or failed. Without an
edit, it follows the seed's pending or failed state. Resetting reveals that upstream state.

The [editable replay](editable.md#when-an-edit-is-dropped) compares both behaviors on the same
upstream changes. See [Forms](forms.md) for multiple fields and batched resets.

## C#

`Reactive.Draft(() => …)` creates a draft over a `Func<T>` seed.
