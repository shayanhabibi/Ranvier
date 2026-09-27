module Ranvier.Tests.Support

open Expecto

/// <summary>
/// Runs <c>test</c> in an untraced build only. A traced build reports each of its cases as skipped, with reason
/// "traced build".
/// </summary>
/// <remarks>For allocation assertions: the trace log allocates by design.</remarks>
let untracedOnly (test: Test) : Test =
#if RANVIER_TRACE
    test
    |> Test.replaceTestCode (fun name _ -> TestLabel (name, TestCase (Sync (fun () -> skiptest "traced build"), Normal), Normal))
#else
    test
#endif
