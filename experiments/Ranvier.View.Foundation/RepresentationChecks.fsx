#if INTERACTIVE
#r "../../src/Ranvier/bin/Release/net10.0/Ranvier.dll"
#load "Representation.fs"
#load "Fixtures.fs"
#endif

open Ranvier.View.Foundation.Representation

let equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let exercise name (ops: Operations<'view>) render =
    let mutable setup = 0
    let mutable clean = 0

    let view =
        ops.Component (fun context ->
            setup <- setup + 1
            context.OnCleanup (fun () -> clean <- clean + 1)

            ops.Element
                Div
                [| Attribute ("id", "\"&<>") |]
                [|
                    ops.Text "<>&\"'"
                    ops.Fragment [| ops.Empty; ops.Bind (=) (fun () -> "&live") |]
                |])

    equal 0 setup
    let expected = "<div id=\"&quot;&amp;&lt;&gt;\">&lt;&gt;&amp;\"'&amp;live</div>"
    equal expected (render Reject view)
    equal expected (render Reject view)
    equal 2 setup
    equal 2 clean

    let browser =
        ops.Element Button [| browserModifier (fun _ -> failwith "browser hook executed on server") |] [||]

    equal "<button></button>" (render Omit browser)
    let mutable rejected = false

    try
        render Reject browser |> ignore
    with _ ->
        rejected <- true

    equal true rejected

    let failing =
        ops.Component (fun context ->
            context.OnCleanup (fun () -> clean <- clean + 1)
            failwith "setup")

    let mutable failed = false

    try
        render Reject failing |> ignore
    with error ->
        equal "setup" error.Message
        failed <- true

    equal true failed
    equal 3 clean
    equal "<input value=\"&quot;&amp;\">" (render Reject (ops.Element Input [| InputValue "\"&" |] [||]))
    printfn "PASS %s portable equivalence, reuse, browser policy and failure unwind" name

exercise "tree" Tree.ops (fun policy view -> Html.render policy (Tree.run view))
exercise "program" Program.ops (fun policy view -> Html.render policy (Program.run view))

let sugarChecks ops run =
    for workload in [ "static"; "wide"; "fragments"; "reactive" ] do
        let output sugar =
            Html.render Reject (run (Ranvier.View.Foundation.Fixtures.create ops workload sugar (fun () -> "live")))

        let direct = output "direct"
        equal true (direct.StartsWith ("<div"))
        equal direct (output "functions")
        equal direct (output "builder")

    printfn "PASS direct/function/builder output equivalence"

sugarChecks Tree.ops Tree.run
sugarChecks Program.ops Program.run

let edgeChecks name (ops: Operations<'view>) run =
    let failures = ResizeArray<string>()

    let check label action =
        try
            action ()
        with error ->
            failures.Add (label + ": " + error.Message)

    check "last modifier wins" (fun () ->
        let view =
            ops.Element Div [| Attribute ("id", "first"); Attribute ("id", "last") |] [||]

        equal "<div id=\"last\"></div>" (Html.render Reject (run view))
        let input = ops.Element Input [| InputValue "first"; InputValue "last" |] [||]
        equal "<input value=\"last\">" (Html.render Reject (run input)))

    check "setup and cleanup failures are preserved" (fun () ->
        let original = System.InvalidOperationException "setup"
        let mutable completed = false

        let view =
            ops.Component (fun context ->
                context.OnCleanup (fun () -> failwith "cleanup")
                context.OnCleanup (fun () -> completed <- true)
                raise original)

        let mutable caught = false

        try
            Html.render Reject (run view) |> ignore
        with :? System.AggregateException as error ->
            equal
                [ "setup"; "cleanup" ]
                (error.InnerExceptions
                 |> Seq.map _.Message
                 |> List.ofSeq)

            equal true (obj.ReferenceEquals (original, error.InnerExceptions[0]))
            caught <- true

        equal true caught
        equal true completed)

    for failure in failures do
        printfn "FAIL %s %s" name failure

    equal 0 failures.Count
    printfn "PASS %s portable modifier/failure edges" name

edgeChecks "tree" Tree.ops Tree.run
edgeChecks "program" Program.ops Program.run

#if !INTERACTIVE
if
    System.Environment.GetCommandLineArgs ()
    |> Array.contains "--allocations"
then
    let measure representation ops =
        for workload in [ "static"; "wide"; "fragments"; "reactive" ] do
            for sugar in [ "direct"; "functions"; "builder" ] do
                let mutable sink = ops.Empty
                let samples = ResizeArray<float>()

                for round in -3 .. 6 do
                    let before = System.GC.GetAllocatedBytesForCurrentThread ()

                    for _ in 1..100 do
                        sink <- Ranvier.View.Foundation.Fixtures.create ops workload sugar (fun () -> "live")

                    let bytes =
                        float (
                            System.GC.GetAllocatedBytesForCurrentThread ()
                            - before
                        )
                        / 100.

                    System.GC.KeepAlive sink

                    if round >= 0 then
                        samples.Add bytes

                let median = (samples |> Seq.sort |> Seq.toArray)[3]

                printfn
                    "{\"representation\":\"%s\",\"workload\":\"%s\",\"sugar\":\"%s\",\"bytesPerDefinition\":%f}"
                    representation
                    workload
                    sugar
                    median

    measure "tree" Tree.ops
    measure "program" Program.ops
#endif

open Ranvier.View.Foundation
open Ranvier.View.Foundation.Template

type GreetingProps = { Title: string; Name: string }

let greeting =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t ->
        t.div
            [| t.Attrs.className "panel"; t.Attrs.title (fun r -> r.Title) |]
            [| t.text "Hello "; t.text (fun r -> r.Name); t.text (fun _ -> "!") |])

let greetingProps = { Title = "T &"; Name = "<Ada>" }

let render policy view =
    Html.render policy (Program.run view)

// Separators keep adjacent texts distinct; values are escaped as the HTML backend escapes them.
equal "<div class=\"panel\" title=\"T &amp;\">Hello <!--!-->&lt;Ada&gt;<!--!-->!</div>" (render Omit (Component.render greeting greetingProps))

let named =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t ->
        t.div [| t.Attrs.className "panel"; t.Attrs.title (fun r -> r.Title) |] [| t.span [||] [| t.text (fun r -> r.Name) |] |])

let namedTwin (props: GreetingProps) =
    Program.createComponent (fun _ ->
        Program.element
            Div
            [|
                Attribute ("class", "panel")
                ReactiveAttribute ("title", fun () -> Some props.Title)
            |]
            [|
                Program.element (HtmlTag "span") [||] [| Program.bind (=) (fun () -> props.Name) |]
            |])

// A template and its createComponent twin produce identical server HTML.
equal (render Omit (namedTwin greetingProps)) (render Omit (Component.render named greetingProps))

let clicking =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t -> t.button [| t.Events.on ("click", fun _ _ -> ()) |] [||])

equal "<button></button>" (render Omit (Component.render clicking greetingProps))

let mutable rejected = false

try
    render Reject (Component.render clicking greetingProps)
    |> ignore
with :? System.InvalidOperationException ->
    rejected <- true

equal true rejected

// The tree representation renders a template through the default expansion.
equal
    "<div class=\"panel\" title=\"T &amp;\"><span>&lt;Ada&gt;</span></div>"
    (Html.render Omit (Tree.run (Component.renderWith Tree.ops named greetingProps)))

let nullTitle =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t -> t.div [| t.Attrs.title (fun _ -> (null: string)) |] [||])

// A null attribute value omits the attribute.
equal "<div></div>" (render Omit (Component.render nullTitle greetingProps))
equal "<div></div>" (Html.render Omit (Tree.run (Component.renderWith Tree.ops nullTitle greetingProps)))

printfn "PASS template portable rendering"

let stripMarkers (html: string) =
    System.Text.RegularExpressions.Regex.Replace (html, "<!--.*?-->", "")

type HoleProps = { Caption: string; Body: Program.View }

let holed =
    Component.define (fun _ (props: HoleProps) -> props) (fun t ->
        t.div [| t.Attrs.className "card" |] [| t.text (fun r -> r.Caption); t.view (fun r -> r.Body) |])

let holeProps =
    {
        Caption = "A"
        Body = Program.element (HtmlTag "em") [||] [| Program.text "<b>" |]
    }

// A view hole renders between region markers; text beside a marker has no separator.
equal "<div class=\"card\">A<!--[--><em>&lt;b&gt;</em><!--]--></div>" (render Omit (Component.render holed holeProps))

// The default expansion renders the same HTML without markers.
equal (stripMarkers (render Omit (Component.render holed holeProps))) (Html.render Omit (Tree.run (Component.renderWith Tree.ops holed holeProps)))

// An empty view hole keeps its markers.
equal
    "<div class=\"card\">B<!--[--><!--]--></div>"
    (render
        Omit
        (Component.render
            holed
            {
                Caption = "B"
                Body = Program.fragment [||]
            }))

let mutable holeRejected = false

try
    render
        Reject
        (Component.render
            holed
            {
                Caption = "C"
                Body = Program.element Button [| listenModifier "click" ignore |] [||]
            })
    |> ignore
with :? System.InvalidOperationException ->
    holeRejected <- true

// A browser-only part inside a view hole follows the Reject policy.
equal true holeRejected

printfn "PASS template view holes"

type Stage =
    | Waiting
    | Working of int
    | Complete of string

let waitingArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [||] [| t.text "waiting" |])

let workingArm =
    Component.define (fun _ (progress: unit -> int) -> progress) (fun t ->
        t.span [||] [| t.text (fun progress -> "working " + string (progress ())) |])

let completeArm =
    Component.define (fun _ (result: unit -> string) -> result) (fun t -> t.span [||] [| t.text (fun result -> "complete " + result ()) |])

let staged =
    Component.define (fun _ (stage: Stage) -> stage) (fun t ->
        t.div
            [||]
            [|
                t.matchWith id (function
                    | Waiting -> t.arm waitingArm ()
                    | Working progress -> t.arm workingArm progress
                    | Complete result -> t.arm completeArm result)
            |])

equal "<div><!--[--><span>working 3</span><!--]--></div>" (render Omit (Component.render staged (Working 3)))
equal "<div><!--[--><span>complete &lt;ok&gt;</span><!--]--></div>" (render Omit (Component.render staged (Complete "<ok>")))

equal (stripMarkers (render Omit (Component.render staged Waiting))) (Html.render Omit (Tree.run (Component.renderWith Tree.ops staged Waiting)))

printfn "PASS template matchWith"

let decadeArm =
    Component.define (fun _ (decade: unit -> int) -> decade) (fun t -> t.span [||] [| t.text (fun decade -> string (decade ())) |])

let decades =
    Component.define (fun _ (value: int) -> value) (fun t ->
        t.div [||] [| t.branch (fun value -> value / 10) (fun decade -> t.arm decadeArm decade) |])

equal "<div><!--[--><span>4</span><!--]--></div>" (render Omit (Component.render decades 42))
equal "<div><span>4</span></div>" (Html.render Omit (Tree.run (Component.renderWith Tree.ops decades 42)))

let mutable roleAdmin = false

let adminArm =
    Component.define (fun _ (tab: unit -> string) -> tab) (fun t -> t.span [||] [| t.text (fun tab -> "admin " + tab ()) |])

let userArm =
    Component.define (fun _ (length: unit -> int) -> length) (fun t -> t.span [||] [| t.text (fun length -> "user " + string (length ())) |])

let roled =
    Component.define (fun _ (tab: string) -> tab) (fun t ->
        t.div
            [||]
            [|
                t.branch id (fun tab ->
                    if roleAdmin then
                        t.arm adminArm tab
                    else
                        t.arm userArm tab.Length)
            |])

let mutable regionReads: (unit -> (obj * obj) array) list = []

let capturing =
    { Tree.ops with
        Switch =
            fun read render ->
                regionReads <- regionReads @ [ read ]
                Tree.ops.Switch read render
    }

let renderRoled tab =
    Html.render Omit (Tree.run (Component.renderWith capturing roled tab))

equal "<div><span>user 4</span></div>" (renderRoled "home")
let readHome = regionReads[0]

let firstKey (read: unit -> (obj * obj) array) =
    fst (read ()).[0]

let homeKey () =
    firstKey readHome

let userHome = homeKey ()

// An equal key with the same definition keeps the arm.
equal true (userHome = homeKey ())

roleAdmin <- true
let adminHome = homeKey ()

// An equal key selecting another definition remounts the arm.
equal false (userHome = adminHome)
equal "<div><span>admin home</span></div>" (renderRoled "home")
equal "<div><!--[--><span>admin home</span><!--]--></div>" (render Omit (Component.render roled "home"))

equal "<div><span>admin away</span></div>" (renderRoled "away")
let readAway = regionReads[2]

// A key change under the same definition remounts the arm.
equal false (adminHome = firstKey readAway)

printfn "PASS template branch"

type Listed = { Id: int; Text: string }

let listedRow =
    Component.define (fun _ (item: unit -> Listed) -> item) (fun t -> t.element (HtmlTag "li") [||] [| t.text (fun item -> (item ()).Text) |])

let listed =
    Component.define (fun _ (items: Listed array) -> items) (fun t ->
        t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) id listedRow |])

let listedItems = [| { Id = 1; Text = "a" }; { Id = 2; Text = "<b>" } |]

equal "<ul><!--[--><li>a</li><li>&lt;b&gt;</li><!--]--></ul>" (render Omit (Component.render listed listedItems))

equal
    (stripMarkers (render Omit (Component.render listed listedItems)))
    (Html.render Omit (Tree.run (Component.renderWith Tree.ops listed listedItems)))

// An empty list keeps its markers.
equal "<ul><!--[--><!--]--></ul>" (render Omit (Component.render listed [||]))

let mutable duplicateRejected = false

try
    render Omit (Component.render listed [| { Id = 1; Text = "a" }; { Id = 1; Text = "b" } |])
    |> ignore
with :? System.InvalidOperationException ->
    duplicateRejected <- true

// Duplicate keys fail server rendering.
equal true duplicateRejected

printfn "PASS template each"

let mutable regionKinds: string list = []

let recording =
    { Tree.ops with
        Keyed =
            fun read render ->
                regionKinds <- regionKinds @ [ "keyed" ]
                Tree.ops.Keyed read render
        Switch =
            fun read render ->
                regionKinds <- regionKinds @ [ "switch" ]
                Tree.ops.Switch read render
    }

let switchedThenListed =
    Component.define (fun _ (items: Listed array) -> items) (fun t ->
        t.div
            [||]
            [|
                t.matchWith (fun (items: Listed array) -> items.Length) (fun length -> t.arm userArm length)
                t.each (fun (item: Listed) -> item.Id) id listedRow
            |])

// An expanded arm switch goes through Switch; an expanded each goes through Keyed.
equal
    "<div><span>user 2</span><li>a</li><li>&lt;b&gt;</li></div>"
    (Html.render Omit (Tree.run (Component.renderWith recording switchedThenListed listedItems)))

equal [ "switch"; "keyed" ] regionKinds

printfn "PASS template switch expansion"

type BadgeProps = { Count: int }

let badge =
    Component.define (fun _ (props: BadgeProps) -> props) (fun t ->
        t.span [| t.Attrs.title (fun badge -> string badge.Count) |] [| t.text (fun badge -> string badge.Count) |])

let carded =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t ->
        t.div
            [||]
            [|
                t.text (fun r -> r.Name)
                t.render badge (fun r -> { Count = r.Title.Length })
            |])

equal "<div>&lt;Ada&gt;<span title=\"3\">3</span></div>" (render Omit (Component.render carded greetingProps))
equal (render Omit (Component.render carded greetingProps)) (Html.render Omit (Tree.run (Component.renderWith Tree.ops carded greetingProps)))

let textual =
    Component.define (fun _ (value: string) -> value) (fun t -> t.text (fun value -> value))

let besideText =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t -> t.span [||] [| t.text "a"; t.render textual (fun r -> r.Name) |])

// Text beside an inlined text root is separated as sibling text is.
equal "<span>a<!--!-->&lt;Ada&gt;</span>" (render Omit (Component.render besideText greetingProps))

let mutable childCleanups = 0

let cleaned =
    Component.define (fun setup () -> setup.OnCleanup (fun () -> childCleanups <- childCleanups + 1)) (fun t -> t.span [||] [||])

let cleaning =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.render cleaned id |])

// An inlined child's cleanup runs with the enclosing server component.
equal "<div><span></span></div>" (render Omit (Component.render cleaning ()))
equal 1 childCleanups

printfn "PASS template inlined children"

type Flags =
    {
        Name: string
        Nickname: string option
        Busy: bool
        Expanded: bool option
        State: string
    }

let flagsTemplate =
    Component.define (fun _ (props: Flags) -> props) (fun t ->
        t.button
            [|
                t.Attrs.title (fun r -> r.Name)
                t.Attrs.id (fun r -> r.Nickname)
                t.Attrs.Button.disabled (fun r -> r.Busy)
                t.Attrs.ariaExpanded (fun r -> r.Expanded)
                t.Attrs.ariaBusy (fun r -> r.Busy)
                t.Attrs.data ("state", fun r -> Some r.State)
            |]
            [| t.text "go" |])

let flagCases =
    [
        {
            Name = "a"
            Nickname = None
            Busy = false
            Expanded = None
            State = "x"
        }
        {
            Name = "b"
            Nickname = Some "n"
            Busy = true
            Expanded = Some false
            State = "y"
        }
        {
            Name = "c"
            Nickname = Some "m"
            Busy = false
            Expanded = Some true
            State = "z"
        }
        {
            Name = null
            Nickname = None
            Busy = false
            Expanded = None
            State = "n"
        }
    ]

// Mirrors flagMarkup in TemplateCatalogueChecks.fs.
let flagMarkup =
    [
        "<button title=\"a\" aria-busy=\"false\" data-state=\"x\">go</button>"
        "<button title=\"b\" id=\"n\" disabled=\"\" aria-expanded=\"false\" aria-busy=\"true\" data-state=\"y\">go</button>"
        "<button title=\"c\" id=\"m\" aria-expanded=\"true\" aria-busy=\"false\" data-state=\"z\">go</button>"
        "<button aria-busy=\"false\" data-state=\"n\">go</button>"
    ]

equal flagMarkup [ for flags in flagCases -> render Omit (Component.render flagsTemplate flags) ]

let staticTemplate =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.button
                    [|
                        t.Attrs.Button.disabled false
                        t.Attrs.ariaExpanded false
                        t.Attrs.id (None: string option)
                        t.Attrs.title "t"
                    |]
                    [||]
                t.button
                    [|
                        t.Attrs.Button.disabled true
                        t.Attrs.ariaExpanded true
                        t.Attrs.id (Some "i")
                        t.Attrs.data ("k", "v")
                    |]
                    [||]
            |])

// Mirrors staticMarkup in TemplateCatalogueChecks.fs.
equal
    "<div><button aria-expanded=\"false\" title=\"t\"></button><button disabled=\"\" aria-expanded=\"true\" id=\"i\" data-k=\"v\"></button></div>"
    (render Omit (Component.render staticTemplate ()))

printfn "PASS template catalogue server markup"
