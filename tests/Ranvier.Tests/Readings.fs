module Ranvier.Tests.Readings

open Ranvier

/// <summary>The message of a <c>Failed</c> reading, or the reading itself.</summary>
let reason (reading: Reading<'T>) =
    match reading with
    | Failed ex -> ex.Message
    | other -> $"%A{other}"
