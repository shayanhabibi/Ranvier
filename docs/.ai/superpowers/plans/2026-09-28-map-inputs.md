# Signal map inputs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Map fences declare sliders, number and text fields and toggles beside buttons, a queued `Desk` holds several
requests, and a `policy=` flag picks the map graph's flight policy.

**Architecture:** `Control` becomes a record of a label, a `Widget` (what the live map renders) and replay `Step`s
(what a replay runs), built by `button`/`slider`/`number`/`text`/`toggle` in `Helpers.fs`. `Desk` gains a queued
mode. `MapFence.compile` parses flags (now fallible) and renders `SignalMap source policy bindings timeline`.
`SignalMap` renders widgets, runs steps on replay behind a `Said` frame that logs the input, and builds its graph
with the policy.

**Tech Stack:** F#, Fable 4 → JS, Partas.Solid, Expecto (.NET), Nacara docs, headless Chrome over CDP.

**Spec:** `docs/.ai/superpowers/specs/2026-09-28-map-inputs-design.md`

## Global Constraints

- Fences stay plain F# that Fable compiles; every existing map fence works after moving to `button`.
- `Desk<'T>()` without `queued` behaves exactly as before.
- `policy=cancel-previous` (default), `policy=keep-latest`, `policy=queue`; anything else is a diagnostic on the
  fence's opening line.
- Styles use `--rv-*` tokens only, in `docs/theme/maps.css`, beside `rv-map__button`.
- Doc comments follow `.claude/rules/comments.md` and the `fsharp-xml-docs` skill; reviewer-only notes use `//FOR-REVIEW`.
- Shell commands are prefixed with `rtk`. Never stage `.serena/`, `brand/og/__pycache__/`, `.claude/.headroom_wrap_marker.json`.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Work on branch `docs/map-inputs`, cut from `master` (9c6e833).

## Plan notes (deviations from the spec's letter)

- `MapFlags.Policy` is a `string` holding the `FlightPolicy` case name (`"CancelPrevious"`, `"KeepLatest"`,
  `"Queue"`), not a `FlightPolicy`: the authoring project is .NET-only and has no Ranvier reference. The render
  emits `Ranvier.FlightPolicy.<name>`.
- An input's replay step runs the action only: a replay renders no widgets (spec §5), so there is none to move.
- The replay's `set <label> = <value>` line is a timeline frame (`Cue.Said`), so it plays, scrubs and logs in step
  with the write that follows it instead of printing at page load.

## Review Focus

1. A number field cleared or holding non-numeric text: no write, the widget keeps what was typed. → `Controls.number` test in Task 1 (`parseNumber`).
2. `SettleNewest`/`FailNewest` on a latest-wins desk answer the one pending request, identical to `Settle`/`Fail`. → Task 1 test.
3. An input action that throws, live or replayed, logs `<label> threw: …` and the map keeps working. → Task 3 browser check.
4. Reset on a `policy=queue` map rebuilds the graph with `Queue`, not the default. → Task 3 browser check.
5. `policy=` with an empty value, or given with an unknown value, is rejected at the opening line, not silently defaulted. → Task 2 test.

---

## File map

| File | Change |
| --- | --- |
| `docs/maps/model/Helpers.fs` | `Widget`, `Step`, `Control`; `button`, `slider`, `number`, `text`, `toggle`, `parseNumber`; queued `Desk` |
| `docs/maps/model/MapModel.fs` | `Cue.Said`; `MapModel.said` |
| `docs/maps/authoring/MapFence.fs` | `MapFlags.Policy`; fallible `MapFlags.parse`; `MapFence.compile`; render with policy |
| `docs/Maps.fs` | calls `MapFence.compile` |
| `docs/maps/SignalMap.fs` | policy argument; widget rendering; replay steps with `Said` frames |
| `docs/theme/maps.css` | `rv-map__input` styles |
| `docs/maps/tests/ControlTests.fs` (new), `ModelTests.fs`, `AuthoringTests.fs`, test fsproj | tests |
| `docs/content/**/*.md`, `docs/content/index.md` | fences move to `button`; guide examples and limits |
| `docs/.ai/superpowers/specs/2026-09-28-signal-maps-design.md` | §5 amended note |

Test command for every task: `rtk dotnet run --project docs/maps/tests/Ranvier.Docs.Maps.Tests.fsproj` (Debug, so
`RANVIER_TRACE` is defined). Docs build: `rtk dotnet fsi build.fsx docs`.

---

### Task 0: Branch and spec

- [ ] `rtk git switch -c docs/map-inputs`
- [ ] Commit the spec and this plan:

```bash
rtk git add docs/.ai/superpowers/specs/2026-09-28-map-inputs-design.md docs/.ai/superpowers/plans/2026-09-28-map-inputs.md
rtk git commit -m "docs: spec and plan for signal map inputs"
```

---

### Task 1: Controls and the queued desk (`Helpers.fs`)

**Files:**
- Modify: `docs/maps/model/Helpers.fs`
- Create: `docs/maps/tests/ControlTests.fs`
- Modify: `docs/maps/tests/Ranvier.Docs.Maps.Tests.fsproj` (add `ControlTests.fs` before `Main.fs`)

**Interfaces:**
- Produces:
  - `type Widget = Button of (unit -> unit) | Slider of min: int * max: int * start: int * set: (int -> unit) | Number of start: float * set: (float -> unit) | Text of start: string * set: (string -> unit) | Toggle of start: bool * set: (bool -> unit)`
  - `type Step = { Log: string option; Run: unit -> unit }`
  - `type Control = { Label: string; Widget: Widget; Steps: Step list }`
  - `button: string -> (unit -> unit) -> Control`
  - `slider: string -> int * int -> int -> int list -> (int -> unit) -> Control`
  - `number: string -> float -> float list -> (float -> unit) -> Control`
  - `text: string -> string -> string list -> (string -> unit) -> Control`
  - `toggle: string -> bool -> bool list -> (bool -> unit) -> Control`
  - `controls: Control list -> Control list`
  - `Controls.parseNumber: string -> float option`
  - `Desk<'T>(?queued: bool)` with `Quote`, `Settle`, `Fail`, `SettleNewest`, `FailNewest`, `Pending`

- [ ] **Step 1: Write the failing tests** in `docs/maps/tests/ControlTests.fs`:

```fsharp
module Ranvier.Docs.Maps.Tests.ControlTests

open Expecto

#if RANVIER_TRACE
open System.Threading.Tasks
open Ranvier.Docs.Maps

let private outcome (t: Task<'T>) =
    if t.IsCanceled then "cancelled"
    elif t.IsFaulted then "failed: " + t.Exception.InnerException.Message
    elif t.IsCompleted then $"settled: %A{t.Result}"
    else "pending"

[<Tests>]
let tests =
    testList
        "Controls"
        [
            test "a button has one silent step that runs its action" {
                let mutable pressed = 0
                let c = button "Add" (fun () -> pressed <- pressed + 1)
                Expect.equal c.Label "Add" "the label"
                Expect.equal (c.Steps |> List.map _.Log) [ None ] "one step, no log line"
                c.Steps |> List.iter (fun s -> s.Run ())
                Expect.equal pressed 1 "the step presses the button"
            }

            test "a slider has a step per replay value, in order" {
                let written = ResizeArray<int>()
                let c = slider "Qty" (1, 10) 1 [ 3; 5 ] written.Add
                Expect.equal (c.Steps |> List.map _.Log) [ Some "set Qty = 3"; Some "set Qty = 5" ] "one line per value"
                c.Steps |> List.iter (fun s -> s.Run ())
                Expect.equal (List.ofSeq written) [ 3; 5 ] "3 then 5"

                match c.Widget with
                | Slider (1, 10, 1, _) -> ()
                | other -> failtestf "widget: %A" other
            }

            test "an input with no replay values has no steps" {
                Expect.isEmpty (text "Name" "Ada" [] ignore).Steps "text"
                Expect.isEmpty (toggle "Gift" false [] ignore).Steps "toggle"
                Expect.isEmpty (number "Price" 4.0 [] ignore).Steps "number"
            }

            test "number, text and toggle steps pass their values" {
                let seen = ResizeArray<string>()
                let steps =
                    (number "Price" 4.0 [ 6.5 ] (fun v -> seen.Add (string v))).Steps
                    @ (text "Name" "Ada" [ "Grace" ] seen.Add).Steps
                    @ (toggle "Gift" false [ true ] (fun b -> seen.Add (string b))).Steps

                steps |> List.iter (fun s -> s.Run ())
                Expect.equal (List.ofSeq seen) [ "6.5"; "Grace"; "True" ] "each value"
                Expect.equal (steps |> List.map _.Log) [ Some "set Price = 6.5"; Some "set Name = Grace"; Some "set Gift = true" ] "each line"
            }

            test "a number field reads only numbers" {
                Expect.equal (Controls.parseNumber "6.5") (Some 6.5) "a number"
                Expect.equal (Controls.parseNumber " 7 ") (Some 7.0) "padded"
                Expect.equal (Controls.parseNumber "") None "empty"
                Expect.equal (Controls.parseNumber "abc") None "text"
            }

            test "a latest-wins desk cancels the older request" {
                let desk = Desk<int>()
                let first = desk.Quote ()
                let second = desk.Quote ()
                Expect.equal (outcome first) "cancelled" "the older request"
                Expect.equal desk.Pending 1 "one waiting"
                desk.SettleNewest 2
                Expect.equal (outcome second) "settled: 2" "newest and pending are the same request"
                Expect.equal desk.Pending 0 "none waiting"
            }

            test "a queued desk answers the oldest, or the newest on request" {
                let desk = Desk<int>(queued = true)
                let a = desk.Quote ()
                let b = desk.Quote ()
                let c = desk.Quote ()
                Expect.equal desk.Pending 3 "three waiting"
                desk.SettleNewest 3
                Expect.equal (outcome c) "settled: 3" "the newest"
                desk.Settle 1
                Expect.equal (outcome a) "settled: 1" "the oldest"
                desk.Fail "down"
                Expect.equal (outcome b) "failed: down" "the one left"
                Expect.equal desk.Pending 0 "none waiting"
            }

            test "answering an empty desk leaves it empty" {
                for desk in [ Desk<int>(); Desk<int>(queued = true) ] do
                    desk.Settle 1
                    desk.Fail "x"
                    desk.SettleNewest 1
                    desk.FailNewest "x"
                    Expect.equal desk.Pending 0 "still empty"
                    let t = desk.Quote ()
                    Expect.equal (outcome t) "pending" "a later request is unaffected"
            }
        ]
#endif
```

Add `<Compile Include="ControlTests.fs" />` after `AuthoringTests.fs` in the test fsproj.

- [ ] **Step 2: Run the tests.** Expected: build FAIL — `button`, `slider`, `SettleNewest`, `Controls.parseNumber` not defined.

- [ ] **Step 3: Implement.** Replace `Control`, `Desk` and `Helpers` in `docs/maps/model/Helpers.fs` (keep `MapSource` unchanged):

```fsharp
/// <summary>The widget a live map renders for a control, and the action its value is written through.</summary>
type Widget =
    | Button of press: (unit -> unit)
    /// <summary>A range over the integers from <c>min</c> to <c>max</c>, inclusive; <c>set</c> runs on every movement.</summary>
    | Slider of min: int * max: int * start: int * set: (int -> unit)
    /// <summary>A number field; <c>set</c> runs when a number is committed.</summary>
    | Number of start: float * set: (float -> unit)
    /// <summary>A text field; <c>set</c> runs when the text is committed.</summary>
    | Text of start: string * set: (string -> unit)
    | Toggle of start: bool * set: (bool -> unit)

/// <summary>One action of a replay, with the log line shown before it.</summary>
type Step = { Log: string option; Run: unit -> unit }

/// <summary>A control beneath a map: its widget on a live map, its steps on a replay.</summary>
type Control =
    {
        Label: string
        Widget: Widget
        Steps: Step list
    }
```

`Desk` (replaces the existing type):

```fsharp
/// <summary>A pretend remote service whose requests stay pending until a control answers them.</summary>
/// <remarks>
/// By default a new request cancels the one before it, so a superseded flight drops. A queued desk keeps every
/// request, in the order made: <c>Settle</c> and <c>Fail</c> answer the oldest, <c>SettleNewest</c> and
/// <c>FailNewest</c> the newest.
/// </remarks>
type Desk<'T>(?queued: bool) =
    let queued = defaultArg queued false
    let pending = ResizeArray<TaskCompletionSource<'T>>()

    let answer (index: int) (complete: TaskCompletionSource<'T> -> unit) =
        if pending.Count > 0 then
            let i = if index < 0 then pending.Count - 1 else index
            let request = pending[i]
            pending.RemoveAt i
            complete request

    /// <summary>A request that completes when a control answers it.</summary>
    /// <remarks>The argument is read for its dependency only.</remarks>
    member _.Quote(_: 'R) : Task<'T> =
        let request = TaskCompletionSource<'T>()

        if not queued then
            for older in pending do
#if FABLE_COMPILER
                // fable-library's TaskCompletionSource has no TrySetCanceled.
                older.SetException (OperationCanceledException ())
#else
                older.TrySetCanceled () |> ignore
#endif
            pending.Clear ()

        pending.Add request
        request.Task

    member _.Settle(value: 'T) = answer 0 (fun request -> request.SetResult value)
    member _.Fail(message: string) = answer 0 (fun request -> request.SetException (Exception message))
    member _.SettleNewest(value: 'T) = answer -1 (fun request -> request.SetResult value)
    member _.FailNewest(message: string) = answer -1 (fun request -> request.SetException (Exception message))

    /// <summary>The requests awaiting an answer.</summary>
    member _.Pending = pending.Count
```

`Helpers` (replaces the existing module):

```fsharp
[<RequireQualifiedAccess>]
module Controls =

    /// <summary>The number in a field's text, or <c>None</c> for empty or non-numeric text.</summary>
    let parseNumber (text: string) : float option =
        // The empty check guards Fable, where Number("") is 0.
        match text.Trim () with
        | "" -> None
        | trimmed ->
            match Double.TryParse trimmed with
            | true, v -> Some v
            | _ -> None

    let internal steps (label: string) (show: 'V -> string) (values: 'V list) (set: 'V -> unit) : Step list =
        values
        |> List.map (fun v -> { Log = Some $"set %s{label} = %s{show v}"; Run = fun () -> set v })

[<AutoOpen>]
module Helpers =

    /// <summary>The controls of a map, in order: the row order on a live map and the step order on a replay.</summary>
    let controls (items: Control list) : Control list = items

    let button (label: string) (press: unit -> unit) : Control =
        { Label = label; Widget = Button press; Steps = [ { Log = None; Run = press } ] }

    /// <summary>A slider over <c>min</c> to <c>max</c>, starting at <c>start</c>; a replay writes each of <c>replay</c>.</summary>
    let slider (label: string) (min: int, max: int) (start: int) (replay: int list) (set: int -> unit) : Control =
        { Label = label; Widget = Slider (min, max, start, set); Steps = Controls.steps label string replay set }

    /// <summary>A number field starting at <c>start</c>; a replay writes each of <c>replay</c>.</summary>
    let number (label: string) (start: float) (replay: float list) (set: float -> unit) : Control =
        { Label = label; Widget = Number (start, set); Steps = Controls.steps label string replay set }

    /// <summary>A text field starting at <c>start</c>; a replay writes each of <c>replay</c>.</summary>
    let text (label: string) (start: string) (replay: string list) (set: string -> unit) : Control =
        { Label = label; Widget = Text (start, set); Steps = Controls.steps label id replay set }

    /// <summary>A checkbox starting at <c>start</c>; a replay writes each of <c>replay</c>.</summary>
    let toggle (label: string) (start: bool) (replay: bool list) (set: bool -> unit) : Control =
        let show (b: bool) = if b then "true" else "false"
        { Label = label; Widget = Toggle (start, set); Steps = Controls.steps label show replay set }
```

`string 6.5` is `"6.5"` on .NET and in Fable; `string true` is `"True"` on .NET, hence `show` for toggles.

- [ ] **Step 4: Update the model tests' uses.** `ModelTests.fs`'s `cart` uses `Desk<decimal>()` unchanged; confirm it
  compiles. Fix `AuthoringTests.fs` only if it references `Control.Run` (it does not; its fence text is data).

- [ ] **Step 5: Run the tests.** Expected: every `Controls` test passes, and every existing test still passes.

- [ ] **Step 6: Commit.**

```bash
rtk git add docs/maps/model/Helpers.fs docs/maps/tests/ControlTests.fs docs/maps/tests/Ranvier.Docs.Maps.Tests.fsproj
rtk git commit -m "docs(maps): input controls and a queued desk"
```

The docs build is broken from here until Task 3 (`SignalMap` still reads `Control.Run`); do not run it in between.

---

### Task 2: The `policy=` flag and the `Said` frame

**Files:**
- Modify: `docs/maps/authoring/MapFence.fs`, `docs/Maps.fs`, `docs/maps/model/MapModel.fs`
- Test: `docs/maps/tests/AuthoringTests.fs`, `docs/maps/tests/ModelTests.fs`

**Interfaces:**
- Produces:
  - `MapFlags = { Timeline: bool; Replay: bool; Policy: string }`
  - `MapFlags.parse: string list -> Result<MapFlags, string>`
  - `MapFence.compile: cellId: string -> flags: string list -> code: string -> Result<MapFenceOutput, (int * string) list>`; a flag problem is `Error [ 0, message ]`
  - Render: `Ranvier.Docs.Maps.SignalMapComponent.SignalMap (<source>) Ranvier.FlightPolicy.<Policy> [| <bindings> |] <timeline>`
  - `Cue.Said`; `MapModel.said: Scene -> string -> Frame`

- [ ] **Step 1: Write the failing tests.** In `AuthoringTests.fs`, replace the existing "replay renders the scenario, replayed" test with the first test below, change the live test's call to `MapFence.compile "cart-live" [] cart`, and add the rest:

```fsharp
            test "replay renders the scenario, replayed" {
                match MapFence.compile "cart" [ "replay" ] cart with
                | Ok output ->
                    Expect.stringContains output.Code "let scenario (graph': Graph) : Control list =" "the scenario module"
                    Expect.stringContains output.Render "Ranvier.Docs.Maps.Replayed Map_cart.scenario" "a replayed source"
                    Expect.stringEnds output.Render "|] true" "with the timeline"
                | Error problems -> failtestf "rejected: %A" problems
            }

            test "the policy defaults to cancel-previous" {
                match MapFence.compile "cart" [] cart with
                | Ok output -> Expect.stringContains output.Render ") Ranvier.FlightPolicy.CancelPrevious [|" "the default"
                | Error problems -> failtestf "rejected: %A" problems
            }

            test "policy=queue and policy=keep-latest render their policies" {
                for flag, policy in [ "policy=queue", "Queue"; "policy=keep-latest", "KeepLatest" ] do
                    match MapFence.compile "cart" [ flag ] cart with
                    | Ok output -> Expect.stringContains output.Render $"Ranvier.FlightPolicy.%s{policy} [|" flag
                    | Error problems -> failtestf "rejected: %A" problems
            }

            test "an unknown or empty policy is rejected at the opening line" {
                for flag in [ "policy=fifo"; "policy=" ] do
                    match MapFence.compile "cart" [ flag ] cart with
                    | Error [ 0, message ] -> Expect.stringContains message "cancel-previous, keep-latest or queue" flag
                    | other -> failtestf "%s: %A" flag other
            }

            test "a fence of buttons and inputs generates" {
                let code =
                    String.concat
                        "\n"
                        [
                            "let qty = createSignal 1"
                            "let total = createMemo (fun _ -> 4 * qty.Value)"
                            ""
                            "controls ["
                            "    slider \"Qty\" (1, 10) 1 [ 3 ] (fun v -> qty.Value <- v)"
                            "    button \"Reset qty\" (fun () -> qty.Value <- 1)"
                            "]"
                        ]

                match MapFence.compile "inputs" [ "timeline" ] code with
                | Ok output ->
                    Expect.stringContains output.Code "        slider \"Qty\" (1, 10) 1 [ 3 ] (fun v -> qty.Value <- v)" "the controls as written"
                    Expect.stringContains output.Render "[| (\"qty\", 1, 1); (\"total\", 2, 2) |] true" "bindings and timeline"
                | Error problems -> failtestf "rejected: %A" problems
            }
```

  Also update the `cart` fence text's `controls` block to `button "Add tea" (fun () -> …)` / `button "Settle quote" (fun () -> …)`,
  and the live test's expected prefix to
  `"SignalMap (Ranvier.Docs.Maps.Live Map_cart_live.scenario) Ranvier.FlightPolicy.CancelPrevious [| (\"lines\", 2, 2); (\"subtotal\", 4, 6);"`.

  In `ModelTests.fs`, add:

```fsharp
            test "a said frame logs its text and keeps the scene" {
                let c = cart ()
                let scene = sceneOf c.Graph
                let frame = MapModel.said scene "set Qty = 3"
                Expect.equal frame.Cue Said "its cue"
                Expect.equal frame.Log "set Qty = 3" "its line"
                Expect.isTrue (obj.ReferenceEquals (frame.After, scene)) "the scene it leaves"
            }
```

- [ ] **Step 2: Run the tests.** Expected: build FAIL — `MapFence.compile`, `Said`, `MapModel.said` not defined.

- [ ] **Step 3: Implement `MapFlags`.** In `MapFence.fs`:

```fsharp
/// <summary>The words of a <c>map</c> fence's info string.</summary>
type MapFlags =
    {
        Timeline: bool
        /// <summary>Presses every control once, in order, for the timeline to play back; implies <c>Timeline</c>.</summary>
        Replay: bool
        /// <summary>The case name of the map graph's <c>FlightPolicy</c>.</summary>
        Policy: string
    }

    /// <summary>The flags of a fence, or the problem with its <c>policy=</c>.</summary>
    static member parse(flags: string list) : Result<MapFlags, string> =
        let replay = List.contains "replay" flags

        let policy =
            match flags |> List.tryFind (fun f -> f.StartsWith "policy=") with
            | None
            | Some "policy=cancel-previous" -> Ok "CancelPrevious"
            | Some "policy=keep-latest" -> Ok "KeepLatest"
            | Some "policy=queue" -> Ok "Queue"
            | Some flag -> Error $"%s{flag}: the policy is cancel-previous, keep-latest or queue."

        policy
        |> Result.map (fun policy ->
            {
                Timeline = replay || List.contains "timeline" flags
                Replay = replay
                Policy = policy
            })
```

- [ ] **Step 4: Implement the render and `compile`.** Change `render` to take the policy and emit it after the source:

```fsharp
    let private render (source: string) (policy: string) (bindings: (string * int * int) list) (timeline: bool) =
        let bindings =
            bindings
            |> List.map (fun (name, first, last) -> $"(\"%s{name}\", %d{first}, %d{last})")
            |> String.concat "; "

        let timeline = if timeline then "true" else "false"
        $"Ranvier.Docs.Maps.SignalMapComponent.SignalMap (%s{source}) Ranvier.FlightPolicy.%s{policy} [| %s{bindings} |] %s{timeline}"
```

  In `generate`, pass `flags.Policy`. Add after `generate`:

```fsharp
    /// <summary>The F# for a <c>map</c> fence from its raw flags; a flag problem is reported at the opening line, 0.</summary>
    let compile (cellId: string) (flags: string list) (code: string) : Result<MapFenceOutput, (int * string) list> =
        match MapFlags.parse flags with
        | Ok flags -> generate cellId flags code
        | Error problem -> Error [ 0, problem ]
```

  In `docs/Maps.fs`, `transform` matches on `MapFence.compile input.CellId input.Flags input.Code`.

- [ ] **Step 5: Implement `Said`.** In `MapModel.fs`, add the case at the end of `Cue`:

```fsharp
    /// <summary>A replayed input's write, logged before the events it causes.</summary>
    | Said
```

  and at the end of `MapModel`, before `#endif`:

```fsharp
    /// <summary>A frame that logs <c>text</c> and leaves <c>scene</c> as it is.</summary>
    let said (scene: Scene) (text: string) : Frame =
        {
            Event = Unchecked.defaultof<TraceEvent>
            Cue = Said
            Log = text
            After = scene
        }
```

- [ ] **Step 6: Run the tests.** Expected: all pass, including the five new authoring tests and the `said` test.

- [ ] **Step 7: Commit.**

```bash
rtk git add docs/maps/authoring/MapFence.fs docs/Maps.fs docs/maps/model/MapModel.fs docs/maps/tests/AuthoringTests.fs docs/maps/tests/ModelTests.fs
rtk git commit -m "docs(maps): a policy= flag, and a frame for a replayed input"
```

---

### Task 3: `SignalMap` renders inputs, replays steps, takes the policy

**Files:**
- Modify: `docs/maps/SignalMap.fs`, `docs/theme/maps.css`
- Modify: every map fence in `docs/content` (they must compile for the docs build that verifies this task)

**Interfaces:**
- Consumes: `Widget`, `Step`, `Control`, `Controls.parseNumber` (Task 1); `Cue.Said`, `MapModel.said`, the render shape (Task 2).
- Produces: `SignalMap (source: MapSource) (policy: FlightPolicy) (bindings: (string * int * int)[]) (timeline: bool) : HtmlElement`

- [ ] **Step 1: Move the fences to `button`.** In each `controls [` block of `docs/content/concepts/suspension.md`
  (2), `guide/async-and-pending.md` (2), `guide/getting-started.md` (4), `guide/signal-maps.md` (4, including the
  bespoke `Thermo` example), `guide/tracing.md` (1) and `index.md` (1), rewrite `"Label", fun () -> body` as
  `button "Label" (fun () -> body)`. A multi-line body keeps its indentation and takes the `)` on its last line:

```fsharp
controls [
    button "Two writes" (fun () ->
        a.Value <- a.Value + 1
        b.Value <- b.Value + 1)
]
```

  In the `Thermo` example, the render becomes `SignalMap (Live scenario) FlightPolicy.CancelPrevious [||] false`.
  Check none remain: `rtk grep -rn '^\s*"[^"]*", fun () ->' docs/content` → no matches.

- [ ] **Step 2: Add the policy argument.** `SignalMap`'s signature gains `(policy: FlightPolicy)` after `source`,
  the doc comment's summary gains "Its graph runs under <c>policy</c>.", and `start` creates
  `new Graph ({ GraphOptions.Default with FlightPolicy = policy })`.

- [ ] **Step 3: Add the widget builders** to the private `Dom` module:

```fsharp
    /// <summary>A labelled input: the caption, the widget, and the widget element.</summary>
    let field (label: string) (kind: string) : HTMLElement * HTMLInputElement =
        let wrap = el "label" $"rv-map__input rv-map__input--%s{kind}"
        let caption = el "span" "rv-map__input-label"
        caption.textContent <- label
        let input = el "input" "rv-map__input-field" :?> HTMLInputElement
        wrap.appendChild caption |> ignore
        wrap.appendChild input |> ignore
        wrap, input
```

- [ ] **Step 4: Render controls.** In `start`, `press` becomes a runner that catches and logs by label:

```fsharp
            let attempt (label: string) (run: unit -> unit) =
                try
                    use _ = g.Activate ()
                    run ()
                with ex ->
                    say $"{label} threw: {ex.Message}" "is-error"
```

  Add, inside `SignalMap` before `start`, a function that builds one control's element (live maps):

```fsharp
        let widget (attempt: string -> (unit -> unit) -> unit) (control: Control) : HTMLElement =
            let write run =
                playing <- true
                attempt control.Label run

            match control.Widget with
            | Button press -> Dom.button control.Label "rv-map__button" (fun () -> write press)
            | Slider (min, max, start, set) ->
                let wrap, input = Dom.field control.Label "slider"
                Dom.attrs input [ "type", "range"; "min", string min; "max", string max; "step", "1" ]
                input.value <- string start
                let shown = Dom.el "output" "rv-map__input-value"
                shown.textContent <- string start
                wrap.appendChild shown |> ignore

                input.addEventListener (
                    "input",
                    fun _ ->
                        shown.textContent <- input.value
                        write (fun () -> set (int input.value))
                )

                wrap
            | Number (start, set) ->
                let wrap, input = Dom.field control.Label "number"
                Dom.attrs input [ "type", "number"; "step", "any" ]
                input.value <- string start

                input.addEventListener (
                    "change",
                    fun _ -> Controls.parseNumber input.value |> Option.iter (fun v -> write (fun () -> set v))
                )

                wrap
            | Text (start, set) ->
                let wrap, input = Dom.field control.Label "text"
                Dom.attrs input [ "type", "text" ]
                input.value <- start
                input.addEventListener ("change", fun _ -> write (fun () -> set input.value))
                wrap
            | Toggle (start, set) ->
                let wrap, input = Dom.field control.Label "toggle"
                Dom.attrs input [ "type", "checkbox" ]
                input.``checked`` <- start
                input.addEventListener ("change", fun _ -> write (fun () -> set input.``checked``))
                wrap
```

  The `Live` branch appends `widget attempt control` for each control, then Reset as before. Reset calls `start`,
  which clears `controlRow`, so inputs return to their starting values.

- [ ] **Step 5: Replay steps with `Said` frames.** Extract the body of `poll`'s `Some g` branch into
  `let drain (g: Graph) = …` (reads new events and `append`s their frames) and call it from `poll`. The `Replayed`
  branch becomes:

```fsharp
                | Replayed scenario ->
                    let steps =
                        scenario g |> List.collect (fun c -> c.Steps |> List.map (fun s -> c.Label, s))

                    baseline g
                    playing <- false

                    // One step per task, in order.
                    let rec stepFrom (rest: (string * Step) list) =
                        match rest with
                        | (label, step) :: rest when
                            not disposed
                            && graph
                               |> Option.exists (fun current -> obj.ReferenceEquals (current, g))
                            ->
                            drain g

                            step.Log
                            |> Option.iter (fun line -> append [| MapModel.said tail line |])

                            attempt label step.Run

                            window.setTimeout ((fun () -> stepFrom rest), 0)
                            |> ignore
                        | _ -> ()

                    stepFrom steps
```

  `note` logs `Said` frames through its `| _ -> say frame.Log ""` case; `play` ignores `Said` through its `| _ -> ()`.
  `drain` must run before the `said` frame so the frame follows every event already recorded.

- [ ] **Step 6: Style inputs.** In `docs/theme/maps.css`, after the `.rv-map__button--reset` rule:

```css
.rv-map__input {
  display: inline-flex; align-items: center; gap: .4rem; padding: .2rem .6rem;
  border: 1px solid var(--rv-border); border-radius: 999px; background: var(--rv-raised); color: var(--rv-text);
  font-weight: 500; }
.rv-map__input:focus-within { border-color: var(--rv-contour); }
.rv-map__input-field { font: inherit; color: inherit; accent-color: var(--rv-contour); }
.rv-map__input-field:focus-visible { outline: 2px solid var(--rv-contour); outline-offset: 2px; }
.rv-map__input--number .rv-map__input-field,
.rv-map__input--text .rv-map__input-field {
  width: 6rem; padding: .1rem .35rem; border: 1px solid var(--rv-border); border-radius: .35rem;
  background: var(--rv-surface, transparent); }
.rv-map__input--slider .rv-map__input-field { width: 7rem; }
.rv-map__input-value { min-width: 1.5rem; font-family: var(--rv-mono, inherit); color: var(--nacara-text-muted); }
```

  Before writing, confirm `--rv-surface` and `--rv-mono` exist: `rtk grep -n "\-\-rv-surface\|\-\-rv-mono" docs/theme`.
  Use the existing names if they differ; drop the fallback if the token exists.

- [ ] **Step 7: Build the docs.** Run `rtk dotnet fsi build.fsx docs`. Expected: success, no Fable errors.

- [ ] **Step 8: Check in the browser.** Serve a copy of `docs/output` under `/Ranvier/` with `python -m http.server 8765`
  and drive headless Chrome over CDP (the Node script from the previous branch, `$TEMP/claude/cdp.mjs`, or a new one
  in the scratchpad). On `guide/signal-maps.html`:
  - Every existing map renders and behaves as before (the cart replay starts at its setup state and Play reaches
    subtotal 8, shipping 5, total 13).
  - Pressing a button still runs its action.
  Commit only after this passes.

- [ ] **Step 9: Run the tests** (unchanged; they must still pass). Expected: all pass.

- [ ] **Step 10: Commit.**

```bash
rtk git add docs/maps/SignalMap.fs docs/theme/maps.css docs/content
rtk git commit -m "docs(maps): render inputs, replay their values, and run under a fence's flight policy"
```

---

### Task 4: Guide, examples and the amended spec

**Files:**
- Modify: `docs/content/guide/signal-maps.md`, `docs/.ai/superpowers/specs/2026-09-28-signal-maps-design.md`

- [ ] **Step 1: Add the inputs example** after the timeline example in `signal-maps.md`, with one sentence before it
  ("Inputs write as you change them: the slider on every movement, the toggle on each flip."):

````markdown
```fsharp map timeline
let qty = createSignal 1
let gift = createSignal false
let subtotal = createMemo (fun _ -> 4m * decimal qty.Value)
let total = createMemo (fun _ -> subtotal.Value + (if gift.Value then 2m else 0m))
createEffect (fun () -> printfn $"total {total.Value}")

controls [
    slider "Qty" (1, 10) 1 [ 3 ] (fun v -> qty.Value <- v)
    toggle "Gift wrap" false [ true ] (fun on -> gift.Value <- on)
]
```
````

- [ ] **Step 2: Add the queued replay example** after the replay example, introduced by: "Under `policy=queue`,
  flights apply in the order they started. Setup and the two writes start three quotes. The newest is answered
  first, and waits; answering the older two applies all three, in order."

````markdown
```fsharp map replay policy=queue
let desk = Desk<decimal>(queued = true)
let qty = createSignal 1
let subtotal = createMemo (fun _ -> 4m * decimal qty.Value)
let shipping = createAsync (fun _ _ -> desk.Quote subtotal.Value)
createEffect (fun () -> printfn $"shipping {shipping.Value}")

controls [
    slider "Qty" (1, 10) 1 [ 2; 3 ] (fun v -> qty.Value <- v)
    button "Answer the newest" (fun () -> desk.SettleNewest 12m)
    button "Answer the older two" (fun () ->
        desk.Settle 4m
        desk.Settle 8m)
]
```
````

- [ ] **Step 3: Update "Writing a map".**
  - Flag table gains: `| \`policy=\` | The graph's flight policy: \`cancel-previous\` (default), \`keep-latest\` or \`queue\`. |`
  - The helper list becomes:

```markdown
- `controls [ … ]` lists the map's controls, in order. Each action runs with the graph active, so `batch` and the
  other `Api` functions work inside it. A replay runs every control in this order.
- `button label action` runs `action` when pressed, and once in a replay.
- `slider label (min, max) start replay set` writes each integer it moves to. `number label start replay set` and
  `text label start replay set` write when the value is committed, with Enter or by leaving the field.
  `toggle label start replay set` writes on each flip. `start` sets the widget only: keep it equal to the signal's
  initial value. A replay writes each value in `replay`, in order, and logs `set <label> = <value>` before it.
- `Desk<'T>()` stands in for a remote service. `desk.Quote x` returns a request that stays pending; a newer request
  cancels it. `desk.Settle value` and `desk.Fail message` answer the pending request.
- `Desk<'T>(queued = true)` keeps every request, in order. `Settle` and `Fail` answer the oldest; `SettleNewest`
  and `FailNewest` the newest. `desk.Pending` counts the requests waiting.
```

  - The bespoke example's lead-in notes the extra argument: "`SignalMap` takes the scenario, the graph's flight
    policy, the code bindings and whether to show the timeline."
- [ ] **Step 4: Update "Limits".** Remove the "Buttons are the only input" item.
- [ ] **Step 5: Amend the first spec.** In `2026-09-28-signal-maps-design.md` §5, after the flag table, add:
  `**Amended:** controls are built with \`button\` and the inputs of [the map inputs design](2026-09-28-map-inputs-design.md), which also adds \`policy=\` and queued desks.`
- [ ] **Step 6: Build the docs** (`rtk dotnet fsi build.fsx docs`). Expected: success.
- [ ] **Step 7: Check in the browser** (as Task 3, Step 8), on `guide/signal-maps.html`:
  - Inputs map: set the slider to 5 via CDP (set `value`, dispatch `input`); total reads 20; tick the toggle
    (dispatch `change`); total reads 22. Press Reset: the slider reads 1, the toggle is unchecked, total reads 4.
  - Queued replay: the map opens with one flight in progress (setup's quote). After Play to the end, the log holds
    `set Qty = 2` and `set Qty = 3`, each before its `flight shipping`; no `drop shipping` line appears; shipping
    moves to 4, 8, then 12, in that order, although 12 was answered first.
  - Reset on the queued replay still shows no `drop shipping` line (the graph kept `Queue`).
  - Add a temporary `button "Throw" (fun () -> failwith "boom")` and a `text "Bad" "" [ "x" ] (fun _ -> failwith "boom")`
    to a local copy of a fence, rebuild, and check each logs `… threw: boom` while the map keeps responding; revert.
- [ ] **Step 8: Run the tests.** Expected: all pass.
- [ ] **Step 9: Commit.**

```bash
rtk git add docs/content/guide/signal-maps.md docs/.ai/superpowers/specs/2026-09-28-signal-maps-design.md
rtk git commit -m "docs: inputs, queued desks and policy= in the signal maps guide"
```

---

### Task 5: Final gates

- [ ] `rtk dotnet build Ranvier.slnx` — success.
- [ ] `rtk dotnet run --project docs/maps/tests/Ranvier.Docs.Maps.Tests.fsproj` — all pass.
- [ ] `rtk dotnet fsi build.fsx docs` — success.
- [ ] Comment hygiene: `review-comments.ps1` finds no `FOR-REVIEW` left.
- [ ] Fantomas check on the changed `.fs` files (not `MapFence.fs` or `AuthoringTests.fs`, unformatted on master).
