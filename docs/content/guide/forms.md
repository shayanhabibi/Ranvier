---
title: Editable values and forms
order: 6
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

A form field shows a value that comes from upstream, such as a loaded record, and lets the user change it locally.
`createEditable` and `createDraft` hold such a value. For a form over several fields, a record of signals gives each
field its own reactive value without code generation.

## Editable values

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

An editable made by `createEditable` drops its edit when the seed publishes a value unequal to its previous one, under
the graph's equality policy (Solid 2.0's writable memo). Every published value counts: if the seed goes from A to B
and back to A, and B was published, the edit is gone. Writes that end at an equal value before the seed runs again
leave the edit in force, for example two writes inside one [`batch`](getting-started.md#batch).

Under the default policy records compare by reference. A seed that builds a new record on every run publishes a new
value on every run, and drops the edit even when the new record is equal. Select a field, or return the previous
value from `seed` when nothing changed.

A draft, made by `createDraft`, keeps its edit until `Reset`, whatever the seed publishes. It suits a form that must not
lose what the user typed when the record reloads. `Upstream` still shows the reloaded value, so a form can offer to
take it.

### Pending and failed seeds

- An edit made while the seed is pending belongs to the seed's last settled value. It stays in force until the seed
  settles on an unequal value.
- An editable whose seed fails reads the failure. A draft with an edit in force reads the edit.

## A form as a record of signals

A record whose fields are signals is a per-field store. Each write costs one signal write, and wakes the readers of
that field only:

```fsharp
type Person = { Name: string; Age: int }

type PersonForm =
    {
        Name: Editable<string>
        Age: Editable<int>
    }

module PersonForm =
    let create (person: Signal<Person>) =
        {
            Name = createDraft (fun _ -> person.Value.Name)
            Age = createDraft (fun _ -> person.Value.Age)
        }

    let snapshot (form: PersonForm) : Person =
        { Name = form.Name.Value; Age = form.Age.Value }

    let reset (form: PersonForm) =
        batch (fun () ->
            form.Name.Reset ()
            form.Age.Reset ())

    let isDirty (form: PersonForm) =
        form.Name.IsEdited || form.Age.IsEdited
```

- A field is a `Signal<'T>` when the form owns the value, and an `Editable<'T>` when it comes from upstream.
- `snapshot` and a whole-record set are written by hand once per type. Put a multi-field set in a `batch`, so an
  effect that reads several fields runs once.
- `isDirty` is tracked: a memo or an effect over it wakes when any field's edit starts or ends.

The alternative, one signal holding the whole record read through selectors, costs a record copy and a selector run
per field on each write. [Migrating from Elmish](elmish.md#selectors-and-their-cost) compares the two.

## C#

`Reactive.Editable(() => …)` and `Reactive.Draft(() => …)` create the same values over a `Func<T>` seed.
