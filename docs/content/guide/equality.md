---
title: Equality
---

`GraphOptions.Equality` sets the comparison for every signal and memo in the graph.

- `JsIdentityPolicy`, the default, follows JavaScript `===`.
- `StructuralPolicy` uses `EqualityComparer<'T>.Default`.

:::details How the policies compare values on .NET

| Value kind | `JsIdentityPolicy` (default) | `StructuralPolicy` |
| --- | --- | --- |
| Primitives, strings, structs, struct tuples | By value | By value |
| Records, unions, `Some x`, other reference types | By reference: an equal copy propagates | By value: an equal copy is cut off |
| `None` over `None` | Cut off (`None` is `null`) | Cut off |
| `nan` over `nan` | Propagates (IEEE: `nan` equals nothing) | Cut off |
| `0.0` over `-0.0` | Cut off | Cut off |

:::

:::tip Write a new object to propagate a change
Under `JsIdentityPolicy`, mutating a referenced object in place and writing the same reference back
is cut off. Write a new value to propagate a change.
:::

:::details Compare the policies on .NET

This example counts the effect runs caused by each write, excluding the initial run.

```fsharp
type Point = { X: int; Y: int }

let countWakes (options: GraphOptions) (first: 'T) (second: 'T) =
    use graph = new Graph (options)
    use _ = graph.Activate ()
    let source = createSignal first
    let mutable runs = 0
    createEffect (fun () -> source.Value |> ignore; runs <- runs + 1)
    source.Value <- second
    runs - 1

let structural = { GraphOptions.Default with Equality = StructuralPolicy () }

let equalityRows =
    [ "equal record", countWakes GraphOptions.Default { X = 1; Y = 2 } { X = 1; Y = 2 },
                      countWakes structural { X = 1; Y = 2 } { X = 1; Y = 2 }
      "Some 1 over Some 1", countWakes GraphOptions.Default (Some 1) (Some 1),
                            countWakes structural (Some 1) (Some 1)
      "None over None", countWakes GraphOptions.Default (None: int option) None,
                        countWakes structural (None: int option) None
      "nan over nan", countWakes GraphOptions.Default nan nan, countWakes structural nan nan
      "0.0 over -0.0", countWakes GraphOptions.Default 0.0 -0.0, countWakes structural 0.0 -0.0 ]

for name, js, st in equalityRows do
    printfn "%-20s default wakes: %d, structural wakes: %d" name js st
```

```text
equal record         default wakes: 1, structural wakes: 0
Some 1 over Some 1   default wakes: 1, structural wakes: 0
None over None       default wakes: 0, structural wakes: 0
nan over nan         default wakes: 1, structural wakes: 0
0.0 over -0.0        default wakes: 0, structural wakes: 0
```

:::

:::details Differences between .NET and Fable

The table below shows whether writing an equal but separately built value is cut off. Some value
types, including `DateTime` and `decimal`, compile to objects under Fable, so the default comparison
can differ between targets.

Writing the same object instance back is cut off on both targets.

| Type | Default on .NET | Default under Fable | `StructuralPolicy`, both targets |
| --- | --- | --- | --- |
| `int`, `float`, `string` | Cut off | Cut off | Cut off |
| `nan` | Propagates | Propagates | Cut off on .NET, propagates under Fable |
| `DateTime`, `DateTimeOffset`, `decimal` | Cut off | Propagates | Cut off |
| Struct records, struct tuples | Cut off | Propagates | Cut off |
| `Some 1` | Propagates | Cut off | Cut off |
| `None` | Cut off | Cut off | Cut off |
| Records, tuples, lists, `Some` of a record | Propagates | Propagates | Cut off |
| Class without custom equality | Propagates | Propagates | Propagates |

:::

:::details Define a custom equality policy

Pass an `IEqualityPolicy` as `GraphOptions.Equality`. Its `Comparer<'T>` supplies the comparer for
each value type and is called once when a node is created. Individual nodes cannot take their own
comparers.

For example, this graph treats strings that differ only in case as equal:

```fsharp
type CaseInsensitivePolicy() =
    interface IEqualityPolicy with
        member _.Comparer<'T>() =
            { new System.Collections.Generic.IEqualityComparer<'T> with
                member _.Equals(a, b) =
                    System.String.Equals (string (box a), string (box b), System.StringComparison.OrdinalIgnoreCase)
                member _.GetHashCode a = (string (box a)).ToLowerInvariant().GetHashCode () }

let caseInsensitiveWakes =
    countWakes { GraphOptions.Default with Equality = CaseInsensitivePolicy () } "abc" "ABC"

printfn "case-insensitive wakes: %d" caseInsensitiveWakes
```

```text
case-insensitive wakes: 0
```

:::
