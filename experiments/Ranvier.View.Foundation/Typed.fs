module Ranvier.View.Foundation.Typed

open Fable.Core
open Fable.Core.JsInterop
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View.Foundation.Representation

/// <summary>An element modifier constrained to its native DOM type.</summary>
[<Erase>]
type Modifier<'native> = private Modifier of ModifierAction

let attribute name value : Modifier<'native> =
    validateAttribute name
    Modifier (Attribute (name, value))

let optionalAttribute name value : Modifier<'native> =
    validateAttribute name
    Modifier (OptionalAttribute (name, value))

let reactiveAttribute name (read: unit -> string option) : Modifier<'native> =
    validateAttribute name

    if isNull (box read) then
        invalidArg "read" "Attribute reader cannot be null"

    Modifier (ReactiveAttribute (name, read))

let booleanAttribute name enabled : Modifier<'native> =
    optionalAttribute name (if enabled then Some "" else None)

let reactiveBooleanAttribute name (read: unit -> bool) : Modifier<'native> =
    if isNull (box read) then
        invalidArg "read" "Attribute reader cannot be null"

    reactiveAttribute name (fun () -> if read () then Some "" else None)

let ariaBooleanAttribute name enabled : Modifier<'native> =
    attribute name (if enabled then "true" else "false")

let reactiveAriaBooleanAttribute name (read: unit -> bool option) : Modifier<'native> =
    if isNull (box read) then
        invalidArg "read" "Attribute reader cannot be null"

    reactiveAttribute name (fun () ->
        read ()
        |> Option.map (fun enabled -> if enabled then "true" else "false"))

let prefixedAttributeName prefix suffix =
    if System.String.IsNullOrEmpty suffix then
        raise (System.ArgumentException ("Attribute suffix cannot be empty", "suffix"))

    let name = prefix + suffix
    validateAttribute name
    name

let id value : Modifier<'native> =
    Modifier (Attribute ("id", value))

let title value : Modifier<'native> =
    Modifier (Attribute ("title", value))

let value text : Modifier<HTMLInputElement> =
    Modifier (InputValue text)

let browser (hook: 'native -> (unit -> unit)) : Modifier<'native> =
    Modifier (browserModifier (fun node -> hook (unbox node)))

/// <summary>Applies a fixed property value during element construction.</summary>
let staticProperty (setter: 'native -> 'value -> unit) value : Modifier<'native> =
    Modifier (fun backend -> backend.Property (fun node -> setter (unbox node) value))

#if FABLE_COMPILER
// Each presents a function as an untyped function of the same arity; the JavaScript representations are identical.
[<Emit("$0")>]
let private asUntypedReader (read: unit -> 'value) : unit -> obj = jsNative

[<Emit("$0")>]
let private asUntypedWriter (setter: 'native -> 'value -> unit) : System.Func<obj, obj, unit> = jsNative

[<Emit("$0")>]
let private asUntypedHandler (handler: Event -> unit) : obj -> unit = jsNative

#endif
/// <summary>Tracks a property reader and writes changed values to each mounted element.</summary>
let reactiveProperty (setter: 'native -> 'value -> unit) (read: unit -> 'value) : Modifier<'native> =
    let readObject () =
        box (read ())

    let write =
        System.Func<obj, obj, unit>(fun node value -> setter (unbox node) (unbox value))

#if FABLE_COMPILER
    Modifier (fun backend -> backend.ReactiveProperty (asUntypedReader read, asUntypedWriter setter))
#else
    Modifier (fun backend -> backend.ReactiveProperty (readObject, write))
#endif

/// <summary>Wraps an event handler to run in the current graph and owner, untracked, until that owner is disposed.</summary>
let ownedHandler (handler: obj -> unit) : obj -> unit =
    let graph = Graph.Current
    let owner = Api.getOwner ()

    fun event ->
        if not owner.IsDisposed then
            graph.Run (fun () -> Api.runWithOwner owner (fun () -> Api.untrack (fun () -> handler event)))

/// <summary>Adds a direct event listener and returns its removal.</summary>
[<Emit("(() => { $0.addEventListener($1, $2); return () => $0.removeEventListener($1, $2); })()")>]
let listenDirect (node: obj) (eventName: string) (handler: obj -> unit) : unit -> unit = jsNative

/// <summary>Handles a named DOM event through the backend's listener.</summary>
let listener (eventName: string) (handler: Event -> unit) : Modifier<'native> =
#if FABLE_COMPILER
    Modifier (listenModifier eventName (asUntypedHandler handler))
#else
    Modifier (listenModifier eventName (fun event -> handler (unbox event)))
#endif

/// <summary>Unwraps typed modifiers for a backend; Fable reuses the erased modifier array.</summary>
let modifierActions (modifiers: Modifier<'native> array) : ModifierAction array =
#if FABLE_COMPILER
    unbox modifiers
#else
    modifiers
    |> Array.map (fun (Modifier apply) -> apply)
#endif

/// <summary>Creates an element from typed modifier and child arrays.</summary>
let element (ops: Operations<'view>) tag (modifiers: Modifier<'native> array) children =
    ops.Element tag (modifierActions modifiers) children

/// <summary>Creates a div with typed modifiers and ordered children.</summary>
let div (ops: Operations<'view>) (modifiers: Modifier<HTMLDivElement> array) children =
    ops.Element Div (modifierActions modifiers) children

/// <summary>Creates a button with typed modifiers and ordered children.</summary>
let button (ops: Operations<'view>) (modifiers: Modifier<HTMLButtonElement> array) children =
    ops.Element Button (modifierActions modifiers) children

/// <summary>Creates a void input element with typed modifiers.</summary>
let input (ops: Operations<'view>) (modifiers: Modifier<HTMLInputElement> array) =
    ops.Element Input (modifierActions modifiers) [||]

/// <summary>Builds a Program element directly from typed modifier and child arrays.</summary>
let inline programElement tag (modifiers: Modifier<'native> array) (children: Program.View array) : Program.View =
    Program.element tag (modifierActions modifiers) children

/// <summary>Creates text updated from a tracked string reader, using string equality to skip unchanged writes.</summary>
let reactiveText (ops: Operations<'view>) read =
    ops.Bind (=) read
