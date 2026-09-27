---
title: Suspension
order: 2
---

:::warning
**Preview.** Ranvier is pre-release; APIs follow Partas.Signals and may change.
:::

This page describes how a computation handles a value that has not arrived: what the graph records, how
the reading computation stops, where the channel ends, and how failures use the same path.

## The problem

A memo that reads an async value has to do something before the value exists:

```fsharp
let displayName (u: User) = u.FirstName + " " + u.LastName   // an ordinary helper
let greeting = createMemo (fun () -> "Hi " + displayName (user.Value))
```

`displayName` knows nothing about reactivity. If `user` is still loading, the read has to stop the memo
body, including frames like `displayName` that sit between the read and the memo, and the graph has to
know to run the memo again when `user` settles.

## The pending channel

*Implemented.*

Every node reports a `Status`, a flags enum that is separate from the node's dirty state:

| Flag | Meaning |
| --- | --- |
| `Status.None` | The node holds a settled value. |
| `Status.Pending` | The node is waiting on a value that has not arrived. |
| `Status.Error` | The node's last run failed. |
| `Status.Uninitialized` | The node has not produced a value yet. |

Pending and dirty are independent. A dirty node has an out-of-date value and recomputes on its next read. A
pending node has already run and is waiting on a source. A node can be both.

Two mechanisms work together. Each one needs the other:

- **Node status** lives on the source and persists. It records that a flight is in progress, and it
  propagates. A memo, effect or boundary that reads a pending node becomes pending too.
- **A thrown `NotReadyException`** is raised by the read and is caught immediately. It stops the reading
  computation's body.

Status without the throw would require every consumer to check a flag by hand. The throw without status
would leave the graph with no record of when to retry.

### What a pending read does

When a body reads `.Value` on a pending node, the read does the following, in this order:

1. It links the dependency edge.
2. It throws `NotReadyException`, which carries the source.

The engine catches the exception around the body. It adds the source to the computation's pending sources,
sets the computation's status to Pending, and publishes no value. When the source settles, the linked edge
wakes the computation, and the body runs again **from the top**.

The order is required. If the read threw before linking the edge, the computation would never be woken and
would stay pending indefinitely.

Pending sources are kept as a set. A later write can reach a computation through a source that has
already settled while another source is still in flight. In that case the computation stays Pending, and
the unsettled source remains in its set. A settled branch that changes does not make a join readable while
another branch is still in flight:

```fsharp
let a = createAsyncSource<int> ()
let b = createSignal 1
let c = createMemo (fun () -> a.Value + b.Value)

c.TryValue        // Pending
b.Value <- 2
c.TryValue        // still Pending, and no value was published
a.Settle 10
c.TryValue        // Ready 12
```

### Two ways to read

- `.Value` is the transparent read. It returns the value, raises `NotReadyException` while the node is
  Pending, and raises the recorded error while it is Failed. It works from inside any helper.
- `TryValue` returns a `Reading<'T>`, which is one of `Ready value`, `Pending` or `Failed error`. It links
  the edge exactly as `.Value` does, so a `Pending` result still wakes the reader when the source settles.
- `Peek` is an untracked read of the last settled value. It never suspends. On an async memo it returns the
  value held from before the current flight. These docs call that value the **retained value**.

The engine itself uses the non-throwing path when it traverses the graph. The only reads that throw are
`.Value` reads of pending sources inside user bodies.

A body that catches `NotReadyException` in its own `try ... with` is still Pending, whatever it returns. The
graph records the pending read itself, not only the exception. The one exception is a read inside
`untrack`: there, a caught read lets the body's result stand. To substitute a value while something is
pending, use a [boundary](#boundaries), or `TryValue`.

## Why a throw

*Considered and rejected alternatives.*

A throw is expensive on .NET compared with a return. Four other mechanisms were evaluated before the throw
was kept.

**A result type on every read.** `Reading<'T>` with a computation expression that stops at `Pending` is
sound, does not allocate, and compiles under Fable. However, every helper between the read and the memo
would then have to return `Reading<_>` as well. This is the call-site colouring that Solid 2.0 removed when
it made async a property of every computation. `Reading<'T>` therefore ships as the result of `TryValue`,
for code that wants to handle Pending explicitly, but it is not the default read.

**F# resumable code.** Resumable state machines (the machinery behind `task { }`) could in principle pause
a body and resume it instead of re-running it. In practice, three results rule them out:

- A resumption point has to be visible to the compiler after inlining. A pending read inside a
  non-`inline` helper does not compile (error FS3501). That makes resumable code a faster implementation of
  the result-type approach, not transparent suspension.
- Fable 5.13 does not compile resumable code. A test builder produced seven distinct errors. Fable's own
  `task { }` support maps `task` to promises by name and does not support resumable code in general.
- Resuming breaks consistency (see [Re-running versus resuming](#re-running-versus-resuming)).

**A message queue per node.** A `MailboxProcessor` round trip took about 4.9 µs on .NET 10, which is more
than a throw. It also cannot settle synchronously. A queue belongs at the edge of the graph, where
completions from other threads arrive. It does not belong between nodes. See
[The async graph on .NET](async-graph.md#threads-and-dispatch).

**A marker value inside `'T`.** A read that returns `'T` has no spare value to mean "pending", because
`Unchecked.defaultof<'T>` is a legitimate value for every type. Changing the return type to carry the
marker is the result-type approach again.

### What a throw costs

On one machine (.NET 10, Release build, no debugger), throwing through ten frames took about 3 µs. A
sentinel return through the same depth took about 41 ns. Entering a `try ... with` block without throwing
added about 4 ns over a plain call. Throw cost grows roughly linearly with stack depth. Throws were about
30% cheaper on .NET 10 and 11 than on .NET 9.

Reusing a cached exception object does not help on .NET. Fresh throws measured slightly cheaper than cached
ones on .NET 9, 10 and 11, so `NotReadyException` is allocated fresh for each throw. (In JavaScript, Solid
reduces the cost of its throw by disabling stack capture. .NET has no equivalent.)

The number of throws depends on how many transparent reads hit pending sources, not on the size of the
graph. The engine's own traversals, `TryValue`, and boundary checks do not throw. In a page-sized graph where
a handful of reads are waiting on data, that comes to a handful of throws per settle.

### A variant that was removed

*Considered and rejected.* One variant, the tolerant read, returned a default value from a pending read
instead of throwing. The body was allowed to finish, and its result was discarded. The variant shipped
briefly as an option and was then removed. Its savings applied only to value types. For reference types,
the default value is `null`, and the resulting `NullReferenceException` cost more than the throw it
replaced. The variant also ran side effects and loops on placeholder data. Code that wants to avoid the throw
on a hot path can use `TryValue`.

## Re-running versus resuming

*Implemented. The alternative was tested and rejected.*

A body re-runs from the start after it suspends. It never resumes from the point where it stopped. Resuming
can produce a result that corresponds to no state the graph was ever in.

Take a memo `c = s + a`, where `a` is an async value computed from `s`:

| Step | Event | Re-run from the top | Naive resume |
| --- | --- | --- | --- |
| 1 | `s = 1`. `c` reads `s = 1`, then suspends on `a`. | Pending | Pending, holding `s = 1` |
| 2 | `s` is written to `2`. A new flight for `a` starts. | Pending, no value published | Pending, still holding `s = 1` |
| 3 | `a` settles with `20`, the result for `s = 2`. | `c` re-reads both values: **22** | `c` adds the held `1` to `20`: **21** |

The graph only ever held `(s = 1, a = 10)` or `(s = 2, a = 20)`. The value `21` combines an old `s` with a
new `a`. This was reproduced in code, not just reasoned about. Conditional bodies fail the same way. If a
condition changes while a branch is suspended, a resumed body publishes the branch the condition no longer
selects.

Resuming could be made correct by recording a version for every read before the pause and re-running
whenever any version changed. At that point the saving is the cost of re-reading a few signals minus the
cost of comparing a few versions, which is close to zero for typical small bodies. It would also run the
prefix's side effects a different number of times than Solid does.

## Boundaries

*Implemented.*

Pending and error both propagate outward. Without something to stop them, one request in flight would put
every downstream node into the pending state. A **boundary** is a computation that stops a channel coming out
of its body. In place of the channel, it returns a value of the same type, so its readers see an ordinary
value.

A boundary is control flow over the graph, not rendering. What the substituted value represents is up to
the caller: a view, a message or a number.

| Constructor | Body pending | Body failed |
| --- | --- | --- |
| `createSuspense fallback body` | Shows `fallback ()` | Passes through as Failed |
| `createErrorBoundary recover body` | Passes through as Pending | Shows `recover ex` |
| `createBoundary fallback recover body` | Shows `fallback ()` | Shows `recover ex` |

```fsharp
let data = createAsyncSource<string> ()
let view = createSuspense (fun () -> "loading") (fun () -> "loaded " + data.Value)

view.TryValue, view.IsWaiting   // Ready "loading", true
data.Settle "report"
view.TryValue, view.IsWaiting   // Ready "loaded report", false
```

The suspended read inside the body linked its edge before it threw. When the source settles, the boundary is
marked dirty and its readers are notified. On the next read, the body runs again from the top.

All three constructors return a `Boundary<'T>`:

- `IsWaiting` is true while the fallback stands in for the body. A caught channel is invisible to the
  boundary's readers by design, so this is the only way to tell a fallback from a real value.
- `Caught` holds the exception that `recover` handled on the current run, or `null`.
- Both are tracked reads, so an effect that reads only `IsWaiting` still wakes when the body settles.

Boundary rules:

- A boundary catches what its body reads, directly or through memos. A node that the body creates but does
  not read is outside the boundary's reach.
- A fallback can read reactive values. A fallback that suspends leaves the boundary Pending. A fallback that
  throws leaves it Failed.
- A `recover` that re-raises leaves the boundary Failed. This narrows the boundary to the errors that
  `recover` handles.
- A boundary owns the nodes its body creates and replaces them on every re-run. Create an async value
  **outside** the boundary and read it inside. An async value created and read inside the body starts a new
  flight on every settle, and the boundary shows its fallback indefinitely.

## Errors and recovery

*Implemented.*

A failure is a settled outcome, not a slow success. When a source fails, a memo that reads it becomes Failed,
and its readers see the error, not Pending. The exception passes through intermediate memos unchanged.

```fsharp
let price = createAsyncSource<int> ()
let shown = createMemo (fun () -> price.Value)

price.Fail (exn "offline")
shown.TryValue        // Failed "offline"
price.Settle 12
shown.TryValue        // Ready 12
```

Recovery is driven by re-reads, and no reset call exists. A failed async source can be settled later, which
clears its Error flag. An error boundary shows `recover ex` while its body fails, and shows the body's value
again once a source the body read changes and the re-run succeeds. A throwing effect does not stop a flush:
every effect queued behind it still runs, and the failure is recorded on that effect's `Status` and `Error`.

## State labels

These docs use the following plain-text labels for what a reader can observe. Where the interface shows a
state mark, the mark follows the same names. A mark identifies a state; it does not describe event order or
API behaviour.

| Label | What the reader sees |
| --- | --- |
| **Ready** | A settled value. `TryValue` returns `Ready`. |
| **Pending** | No usable value yet. `TryValue` returns `Pending`. |
| **Retained value** | Pending, but `Peek` still returns the last settled value. |
| **Fallback** | A boundary is showing its fallback. `IsWaiting` is true. |
| **Failed** | `TryValue` returns `Failed`. |
| **Recovered** | A boundary is showing `recover ex`. `Caught` holds the error. |

## Open questions

*Exploratory.*

- **Real-world throw counts.** The estimate that a settle involves a handful of throws has not been measured
  in a real application.
- **Solid's later pending-channel changes.** Parts of Solid 2.0's signal core, such as transitions,
  optimistic lanes and the pending/error classification module, have not been reviewed in full. Full
  behavioural parity may need them.
