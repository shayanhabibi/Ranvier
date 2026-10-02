---
title: Forms
---

Represent each field with its own signal, [editable value](editable.md), or [draft](drafts.md).
A record of reactive fields lets each edit update only the readers of that field.

## A form as a record of signals

A record of reactive fields lets each edit update the readers of that field alone.

:::details A two-field person form

Both fields use drafts so that upstream reloads preserve local edits. The helpers collect a
snapshot, reset both fields in a batch, and report whether either has been edited.

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

:::

- A field is a `Signal<'T>` when the form owns the value, and an `Editable<'T>` when it comes from upstream.
- `snapshot` and a whole-record set are written by hand once per type. Put a multi-field set in a `batch`, so an
  effect that reads several fields runs once.
- `isDirty` is tracked: a memo or an effect over it wakes when any field's edit starts or ends.

:::details Compare with a whole-record signal
Holding the whole record in one signal and reading fields through selectors costs a record copy
and a selector run per field on each write. See
[Migrating from Elmish](elmish.md#selectors-and-their-cost).
:::
