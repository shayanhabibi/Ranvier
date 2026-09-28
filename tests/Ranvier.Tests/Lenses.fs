module Ranvier.Tests.Lenses

open System
open Expecto
open Ranvier

type Todo = { Id: int; Title: string }

type Addr = { City: string; Zip: string }

type Person = { Name: string; Addr: Addr }

type Model =
    {
        User: Person
        Theme: string
        Todos: Todo list
    }

/// <summary>An element whose <c>Equals</c> calls are tallied in <c>calls</c>, which each test owns.</summary>
type Counted(id: int, name: string, calls: int ref) =
    member _.Id = id
    member _.Name = name

    override _.Equals(other: obj) =
        calls.Value <- calls.Value + 1

        match other with
        | :? Counted as c -> c.Id = id && c.Name = name
        | _ -> false

    override _.GetHashCode() =
        hash (id, name)

type Store = { Items: Counted list; Tick: int }

let private model =
    {
        User =
            {
                Name = "Ada"
                Addr = { City = "Bergen"; Zip = "5003" }
            }
        Theme = "dark"
        Todos =
            [
                { Id = 1; Title = "one" }
                { Id = 2; Title = "two" }
                { Id = 3; Title = "three" }
            ]
    }

/// <summary>Counts the runs of an effect that reads <c>read</c>.</summary>
let private countRuns (read: unit -> 'a) =
    let runs = ref 0

    createEffect (fun () ->
        runs.Value <- runs.Value + 1
        read () |> ignore)

    runs

let private same (a: 'T) (b: 'T) =
    obj.ReferenceEquals (a, b)

[<Tests>]
let tests =
    testList
        "Lenses"
        [
            test "Signal.update writes the function's result" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let seen = countRuns (fun () -> s.Value)

                Signal.update s (fun x -> x + 1)

                Expect.equal s.Peek 2 "the result is written"
                Expect.equal seen.Value 2 "a changed value wakes the reader"
            }

            test "Signal.update inside an effect leaves the effect unsubscribed from the signal" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let t = createSignal 0
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1
                    t.Value |> ignore
                    Signal.update s ((+) 1))

                s.Value <- 100

                Expect.equal runs.Value 1 "a write to the updated signal leaves the effect asleep"
                Expect.equal s.ObserverCount 0 "the effect holds no edge to the updated signal"
            }

            test "Signal.update returning the same reference wakes no root reader and runs no projection pass" {
                use g = new Graph ()
                use _ = g.Activate ()
                let state = createSignal model
                let rootRuns = countRuns (fun () -> state.Value)
                let rows = createProjection _.Id _.Title (fun () -> state.Value.Todos)
                let rowRuns = countRuns (fun () -> rows.Get 1)
                let passes = rows.Runs

                Signal.update state id

                Signal.update state (fun m ->
                    let todos =
                        m.Todos
                        |> List.updateBy _.Id 99 (fun t -> { t with Title = "absent" })

                    if same todos m.Todos then m else { m with Todos = todos })

                Expect.equal rootRuns.Value 1 "the root reader stays asleep"
                Expect.equal rows.Runs passes "the projection runs no pass"
                Expect.equal rowRuns.Value 1 "the row reader stays asleep"
            }

            test "List.updateBy with no match returns the input list" {
                let xs = model.Todos

                let ys =
                    xs
                    |> List.updateBy _.Id 99 (fun t -> { t with Title = "x" })

                Expect.isTrue (same xs ys) "the input reference comes back"
            }

            test "List.updateBy returns the input list when f returns the element itself" {
                let xs = model.Todos
                let ys = xs |> List.updateBy _.Id 2 id
                Expect.isTrue (same xs ys) "the input reference comes back"
            }

            test "updateBy returns the input when f returns a NaN element unchanged" {
                let xs = [ 1.0; nan; 3.0 ]
                let arr = Array.ofList xs

                let keyOf (x: float) =
                    if Double.IsNaN x then 0 else int x

                Expect.isTrue (same xs (xs |> List.updateBy keyOf 0 id)) "the input list comes back"
                Expect.isTrue (same arr (arr |> Array.updateBy keyOf 0 id)) "the input array comes back"
            }

            test "updateBy returns the input when f returns an equal value-typed element" {
                let xs = [ struct (1, "a"); struct (2, "b") ]

                let ys =
                    xs
                    |> List.updateBy (fun struct (k, _) -> k) 2 (fun struct (k, v) -> struct (k, v))

                Expect.isTrue (same xs ys) "an equal struct counts as unchanged"
            }

            test "List.updateBy at index 0 shares the tail" {
                let xs = model.Todos

                let ys =
                    xs
                    |> List.updateBy _.Id 1 (fun t -> { t with Title = "uno" })

                Expect.equal (ys |> List.map _.Title) [ "uno"; "two"; "three" ] "the first element is replaced"
                Expect.isTrue (same (List.tail xs) (List.tail ys)) "the tail is the input's tail"
            }

            test "List.updateBy copies the prefix and shares the tail after the match" {
                let xs = model.Todos

                let ys =
                    xs
                    |> List.updateBy _.Id 2 (fun t -> { t with Title = "dos" })

                Expect.equal (ys |> List.map _.Title) [ "one"; "dos"; "three" ] "the match is replaced"
                Expect.isTrue (same (List.skip 2 xs) (List.skip 2 ys)) "the suffix after the match is shared"
                Expect.isTrue (same xs.Head ys.Head) "the copied prefix holds the same elements"
                Expect.equal (xs |> List.map _.Title) [ "one"; "two"; "three" ] "the input is unchanged"
            }

            test "List.updateBy with duplicate keys rewrites the first match only" {
                let xs =
                    [ { Id = 1; Title = "a" }; { Id = 2; Title = "b" }; { Id = 2; Title = "c" } ]

                let ys =
                    xs
                    |> List.updateBy _.Id 2 (fun t -> { t with Title = "z" })

                Expect.equal (ys |> List.map _.Title) [ "a"; "z"; "c" ] "only the first match changes"
            }

            test "List.updateBy matches keys structurally" {
                let xs = [ [| 1 |], "a"; [| 2 |], "b" ]

                let ys =
                    xs
                    |> List.updateBy fst [| 2 |] (fun (k, _) -> k, "z")

                Expect.equal (ys |> List.map snd) [ "a"; "z" ] "an equal array key matches"
            }

            test "Array.updateBy with no match or an unchanged element returns the input array" {
                let xs = Array.ofList model.Todos

                let missing =
                    xs
                    |> Array.updateBy _.Id 99 (fun t -> { t with Title = "x" })

                let unchanged = xs |> Array.updateBy _.Id 2 id
                Expect.isTrue (same xs missing) "no match returns the input"
                Expect.isTrue (same xs unchanged) "an unchanged element returns the input"
            }

            test "Array.updateBy copies the array and rewrites the first match only" {
                let xs =
                    [| { Id = 1; Title = "a" }; { Id = 2; Title = "b" }; { Id = 2; Title = "c" } |]

                let ys =
                    xs
                    |> Array.updateBy _.Id 2 (fun t -> { t with Title = "z" })

                Expect.isFalse (same xs ys) "a change allocates a new array"
                Expect.equal (ys |> Array.map _.Title) [| "a"; "z"; "c" |] "only the first match changes"
                Expect.equal (xs |> Array.map _.Title) [| "a"; "b"; "c" |] "the input is unchanged"
            }

            test "createOptionMemo: an unrelated root write wakes no dependent and calls no Equals" {
                use g = new Graph ()
                use _ = g.Activate ()
                let calls = ref 0

                let state =
                    createSignal
                        {
                            Items = [ Counted (1, "a", calls); Counted (2, "b", calls) ]
                            Tick = 0
                        }

                let bodyRuns = ref 0

                let selected =
                    createOptionMemo (fun () ->
                        bodyRuns.Value <- bodyRuns.Value + 1

                        state.Value.Items
                        |> List.tryFind (fun c -> c.Id = 2))

                let dependent = countRuns (fun () -> selected.Value)
                let first = selected.Peek

                Signal.update state (fun s -> { s with Tick = s.Tick + 1 })
                selected.Value |> ignore

                Expect.equal bodyRuns.Value 2 "the selection body re-runs"
                Expect.equal dependent.Value 1 "the dependent stays asleep"
                Expect.equal calls.Value 0 "the element's Equals is never called"
                Expect.isTrue (same first selected.Peek) "the Some wrapper keeps its reference"
            }

            test "createOptionMemo: a structurally equal rebuild wakes dependents once under reference policy" {
                use g = new Graph ()
                use _ = g.Activate ()
                let calls = ref 0

                let state =
                    createSignal
                        {
                            Items = [ Counted (2, "b", calls) ]
                            Tick = 0
                        }

                let selected =
                    createOptionMemo (fun () ->
                        state.Value.Items
                        |> List.tryFind (fun c -> c.Id = 2))

                let dependent = countRuns (fun () -> selected.Value)

                Signal.update state (fun s ->
                    { s with
                        Items = [ Counted (2, "b", calls) ]
                    })

                Expect.equal dependent.Value 2 "the dependent wakes once"
                Expect.equal calls.Value 0 "reference policy calls no Equals"
            }

            test "createOptionMemo: a policy-equal re-run returns the previous Some instance" {
                use g =
                    new Graph (
                        { GraphOptions.Default with
                            Equality = StructuralPolicy ()
                        }
                    )

                use _ = g.Activate ()
                let calls = ref 0

                let state =
                    createSignal
                        {
                            Items = [ Counted (2, "b", calls) ]
                            Tick = 0
                        }

                let selected =
                    createOptionMemo (fun () ->
                        state.Value.Items
                        |> List.tryFind (fun c -> c.Id = 2))

                let dependent = countRuns (fun () -> selected.Value)
                let first = selected.Peek

                Signal.update state (fun s ->
                    { s with
                        Items = [ Counted (2, "b", calls) ]
                    })

                selected.Value |> ignore

                Expect.isTrue (same first selected.Peek) "the previous Some comes back"
                Expect.equal dependent.Value 1 "the dependent stays asleep"
                Expect.isGreaterThan calls.Value 0 "the policy comparer is the element's Equals"
            }

            test "createOptionMemo under structural policy: an unchanged element costs one Equals, in the memo's cutoff" {
                use g =
                    new Graph (
                        { GraphOptions.Default with
                            Equality = StructuralPolicy ()
                        }
                    )

                use _ = g.Activate ()
                let calls = ref 0
                let element = Counted (2, "b", calls)
                let trigger = createSignal 0

                let selected =
                    createOptionMemo (fun () ->
                        trigger.Value |> ignore
                        Some element)

                let dependent = countRuns (fun () -> selected.Value)

                trigger.Value <- 1
                selected.Value |> ignore

                Expect.equal dependent.Value 1 "the dependent stays asleep"
                Expect.equal calls.Value 1 "the selection skips Equals on the same instance; the cutoff calls it once"
            }

            test "createOptionMemo follows None and Some transitions" {
                use g = new Graph ()
                use _ = g.Activate ()
                let state = createSignal model

                let selected =
                    createOptionMemo (fun () ->
                        state.Value.Todos
                        |> List.tryFind (fun t -> t.Id = 4))

                Expect.equal selected.Value None "absent"

                Signal.update state (fun m ->
                    { m with
                        Todos = m.Todos @ [ { Id = 4; Title = "four" } ]
                    })

                Expect.equal selected.Value (Some { Id = 4; Title = "four" }) "present"

                Signal.update state (fun m ->
                    { m with
                        Todos = m.Todos |> List.filter (fun t -> t.Id <> 4)
                    })

                Expect.equal selected.Value None "absent again"
            }

            test "a hand-written memo over Some wakes on every root write on .NET" {
                use g = new Graph ()
                use _ = g.Activate ()
                let state = createSignal model

                let wrapped =
                    createMemo (fun _ ->
                        state.Value.Todos
                        |> List.tryFind (fun t -> t.Id = 2))

                let dependent = countRuns (fun () -> wrapped.Value)

                Signal.update state (fun m -> { m with Theme = "light" })

                Expect.equal dependent.Value 2 "a fresh Some is a new reference"
            }

            test "createOptionMemo inside a createMemo body raises InvalidOperationException" {
                use g = new Graph ()
                use _ = g.Activate ()
                let state = createSignal model

                let outer =
                    createMemo (fun _ -> (createOptionMemo (fun () -> state.Value.Todos |> List.tryHead)).Value)

                Expect.throwsT<InvalidOperationException> (fun () -> outer.Value |> ignore) "the owned-node rule applies"
            }

            test "a batch of two updates gives one effect run" {
                use g = new Graph ()
                use _ = g.Activate ()
                let state = createSignal model
                let seen = ResizeArray ()
                createEffect (fun () -> seen.Add (state.Value.Theme, state.Value.User.Name))

                batch (fun () ->
                    Signal.update state (fun m -> { m with Theme = "light" })
                    Signal.update state (fun m -> { m with User.Name = "Grace" }))

                Expect.equal (List.ofSeq seen) [ "dark", "Ada"; "light", "Grace" ] "one run for both writes"
            }

            test "a record-path write re-runs only the readers on the path" {
                use g = new Graph ()
                use _ = g.Activate ()
                let state = createSignal model
                let user = createMemo (fun _ -> state.Value.User)
                let addr = createMemo (fun _ -> user.Value.Addr)
                let city = createMemo (fun _ -> addr.Value.City)
                let zip = createMemo (fun _ -> addr.Value.Zip)
                let name = createMemo (fun _ -> user.Value.Name)
                let theme = createMemo (fun _ -> state.Value.Theme)
                let cityRuns = countRuns (fun () -> city.Value)
                let zipRuns = countRuns (fun () -> zip.Value)
                let nameRuns = countRuns (fun () -> name.Value)
                let themeRuns = countRuns (fun () -> theme.Value)

                Signal.update state (fun m -> { m with User.Addr.City = "Oslo" })

                Expect.equal city.Value "Oslo" "the leaf moved"
                Expect.equal (user.Runs, addr.Runs, city.Runs) (2, 2, 2) "every spine memo re-runs"

                Expect.equal (zip.Runs, name.Runs, theme.Runs) (2, 2, 2) "each direct child of a spine node re-runs"

                Expect.equal (cityRuns.Value, zipRuns.Value, nameRuns.Value, themeRuns.Value) (2, 1, 1, 1) "only the reader of the changed leaf wakes"
            }

            test "a keyed write wakes only the written projection row" {
                use g = new Graph ()
                use _ = g.Activate ()
                let state = createSignal model
                let rows = createProjection _.Id _.Title (fun () -> state.Value.Todos)

                let rowRuns =
                    dict [ for t in model.Todos -> t.Id, countRuns (fun () -> rows.Get t.Id) ]

                let keyRuns = countRuns (fun () -> rows.Keys)

                Signal.update state (fun m ->
                    let todos =
                        m.Todos
                        |> List.updateBy _.Id 2 (fun t -> { t with Title = "dos" })

                    if same todos m.Todos then m else { m with Todos = todos })

                Expect.equal (rows.Get 2) "dos" "the row moved"

                Expect.equal (rowRuns[1].Value, rowRuns[2].Value, rowRuns[3].Value, keyRuns.Value) (1, 2, 1, 1) "only the written row's reader wakes"
            }
        ]
