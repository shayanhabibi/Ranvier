module Ranvier.Tests.ValueReaders

open Expecto

[<Tests>]
let tests =
    ValueReaderCases.cases
    |> Array.map (fun (name, run) -> testCase name (fun _ -> run ()))
    |> Array.toList
    |> testList "Value readers"
