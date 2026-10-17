module Ranvier.View.Foundation.Authoring

open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Components

/// <summary>Produces an empty Program view.</summary>
let empty = Program.ops.Empty

/// <summary>Creates a text node with a fixed value.</summary>
let inline text value =
    Program.text value

/// <summary>Creates text updated from a tracked string reader.</summary>
let inline reactiveText read =
    Program.bind (=) read

/// <summary>Renders a constant prop as fixed text and a reactive prop as tracked text.</summary>
let inline propText prop =
    Prop.text Program.ops (=) prop

/// <summary>Renders children in order under their current owner.</summary>
let inline fragment children =
    Program.fragment children

/// <summary>Creates a component whose setup runs once per mounted instance.</summary>
/// <remarks>Setup reads are snapshots; reactive readers update bindings under the component's owner.</remarks>
let inline createComponent (setup: Setup -> 'props -> Program.View) (props: 'props) =
    Program.createComponent (fun context -> setup context props)

/// <summary>Retains keyed rows and supplies readers for their current values.</summary>
/// <remarks>The input array is copied before key callbacks run. Equal keys under <c>=</c> retain mounted rows.</remarks>
let inline keyedArray key read render =
    Keyed.array Program.ops key read render

/// <summary>Retains a selected instance while its key remains equal under <c>=</c>.</summary>
/// <remarks>Include the branch discriminator in the key when the component shape changes.</remarks>
let inline branch key read render =
    Branch.select Program.ops key read render
