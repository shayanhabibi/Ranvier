# Signal maps: inputs, queued desks and flight policy

**Status.** Design approved in conversation on 2026-09-28. Extends
[the signal maps design](2026-09-28-signal-maps-design.md).

## 1. Goal

A reader of a map sets values, not only presses fixed buttons: a slider drives a quantity, a text field feeds a
name, a toggle flips a flag, and the map shows each write travel through the graph. A map can also show flights
that overlap and apply in order under `FlightPolicy.Queue`.

Success:

1. A `map` fence declares buttons and inputs in one `controls` list; the list order is the row order and the replay
   order.
2. A `replay` map replays each input's scripted values.
3. A queued `Desk` holds several pending requests, answered oldest first or newest first.
4. A fence chooses its graph's flight policy with `policy=`.
5. Every existing map fence works after moving to `button`.

## 2. Decisions

| Question | Decision |
| --- | --- |
| What a replay does with an input | Each input carries a list of values to replay |
| How controls are written | Constructor functions returning `Control`; buttons move from tuples to `button` |
| Which request a queued desk answers | `Settle`/`Fail` the oldest; `SettleNewest`/`FailNewest` the newest |
| How a map picks `Queue` | A `policy=` fence flag, passed to `SignalMap`, which builds the graph with it |

## 3. Authoring API (`docs/maps/model/Helpers.fs`)

### 3.1 Controls

| Function | Signature | Widget |
| --- | --- | --- |
| `button` | `string -> (unit -> unit) -> Control` | button |
| `slider` | `string -> int * int -> int -> int list -> (int -> unit) -> Control` | range over integers, (min, max) inclusive |
| `number` | `string -> float -> float list -> (float -> unit) -> Control` | number field |
| `text` | `string -> string -> string list -> (string -> unit) -> Control` | text field |
| `toggle` | `string -> bool -> bool list -> (bool -> unit) -> Control` | checkbox |

Arguments in order: label, range (slider only), starting value, replay values, action.

- A slider's action runs on every movement (`input`). A number or text field's action runs when the value is
  committed (`change`: Enter or blur). A toggle's action runs on each flip.
- The starting value sets the widget only; it is not written to the graph. The author keeps it equal to the
  signal's initial value.
- Numbers are `float`; a decimal signal converts in the action.
- `controls: Control list -> Control list` returns its argument; it stays the fence's last expression.
- `Control` exposes its label, the widget to render, and its replay steps. A step is an action paired with the
  text it logs: a button has one step (its action, no log line); an input has one step per replay value, which
  applies the value to the action and to the widget, and logs `set <label> = <value>`.

### 3.2 Desk

`Desk<'T>(?queued: bool)`. Without `queued`, or with `queued = false`, a new request cancels the pending one, as
before. With `queued = true` every request stays pending, in the order made.

| Member | Latest-wins | Queued |
| --- | --- | --- |
| `Quote x` | a new request; the pending one is cancelled | a new request, appended |
| `Settle v` / `Fail msg` | the pending request | the oldest request |
| `SettleNewest v` / `FailNewest msg` | the pending request | the newest request |
| `Pending` | 0 or 1 | the number waiting |

Every answering member leaves the desk unchanged when no request is pending.

## 4. Flight policy flag (`docs/maps/authoring/MapFence.fs`)

- `policy=cancel-previous` (default), `policy=keep-latest` or `policy=queue`.
- `MapFlags` gains `Policy: FlightPolicy`. An unknown value is a diagnostic on the fence's opening line.
- The render passes the policy to `SignalMap`, which gains a `FlightPolicy` argument and creates its graph with
  `{ GraphOptions.Default with FlightPolicy = policy }`. Reset keeps the policy.
- The bespoke `SignalMap` example in the guide passes `FlightPolicy.CancelPrevious`.

## 5. Rendering (`docs/maps/SignalMap.fs`)

- Live maps render each control in the control row in declaration order. An input is a `<label>` holding its
  caption and widget, so the caption is its accessible name; a slider also shows its current value.
- Reset rebuilds the row: inputs return to their starting values with the graph.
- An action that throws logs `<label> threw: <message>`, for buttons and inputs alike.
- Replay maps render no controls. After setup, every step of every control runs in declaration order, one step per
  task, under the guard the replay uses today. An input step's log line precedes the events its write causes.
- Styles sit in the maps stylesheet beside `rv-map__button`: `rv-map__input` with `--slider`, `--number`, `--text`
  and `--toggle` modifiers, the same border, radius and focus ring, and `--rv-*` tokens only.

## 6. Docs

- `docs/content/guide/signal-maps.md`:
  - The helper list documents `button`, the four inputs, the queued desk and `policy=`; the flag table gains
    `policy=`.
  - The limit "Buttons are the only input" is removed.
  - A `map timeline` example: a quantity slider and a gift-wrap toggle feeding a total.
  - A `map replay policy=queue` example: a queued desk, two writes start two flights, `SettleNewest` answers the
    second, which waits; `Settle` answers the first, and both apply in order.
- Every other map fence (guide pages, concepts pages, `index.md`) moves to `button`.
- The first signal maps spec gets an "Amended:" note in §5 pointing here.

## 7. Testing

- Expecto under .NET (`docs/maps/tests`):
  - Desk: latest-wins cancels the older request; queued `Settle` answers the oldest and `SettleNewest` the newest;
    `Pending` counts; every answering member on an empty desk leaves it unchanged.
  - Control steps: a button has one step; `slider … [3; 5]` has two steps passing 3 then 5; an empty replay list
    has none.
  - Authoring: `policy=queue` renders `FlightPolicy.Queue`; the default renders `FlightPolicy.CancelPrevious`; an
    unknown policy is a diagnostic on the opening line; a fence using `button` and `slider` generates.
- Browser, over CDP as before: a slider write moves the map; Reset restores the inputs; the queued replay shows two
  flights in progress and applies them in order.
- The docs build passes.

## 8. Out of scope

- Selects, dates and other widgets.
- Inputs bound directly to signals.
- A replay script that interleaves controls in an order other than declaration order.
