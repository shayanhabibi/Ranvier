# Execution ledger: 2026-10-04-query-mutation.md

Branch: `feat/query-mutation`. Worktree: `C:/Users/shaya/RiderProjects/Ranvier-query`. Base: `7c672a5`.

Tasks 1–7 are complete: public-API composition, typed query identity and leases,
fetch transitions and generation retirement, staged reconciliation, heterogeneous
FIFO mutations, dictionary workflow, and packaging/documentation/portable gates.
Core source and existing public signatures are unchanged. The original checkout's
user changes remain untouched. No merge, push, deployment, or publication occurred.

## Implementation evidence

- Baseline Release .NET suite: 975 passing cases on each of net8.0 and net10.0.
- Added 32 .NET cases and 29 portable cases covering initial suspension, sharing,
  disposal, stale responses, staging failures, mutations, and the partial dictionary.
- Public sources, dispatch, batching, and ownership were proven in SageFs and
  compiled tests; no core extension was required.
- RED identity tests exposed duplicate requests and missing client cancellation.
  Cancellation reentry exposed an extra request (expected two, observed three).
- Independent review found retained lease payloads and cancellation cleanup gaps;
  both were fixed and covered. Final review reported no remaining blockers.
- Debug exposed malformed generated IL in a nested generic task observer
  (`BadImageFormatException` in a dispatched callback). Extracting a module-private
  observer fixed Debug; Release, Fable, and AOT were rerun on the final code.

## Final verification

- Release untraced: 1007 passed, zero skipped, on each .NET target.
- Release traced and Debug: 1086 passed, six skipped, on each .NET target.
- C# Release: 84 passed on each .NET target.
- Both package variants passed fresh-consumer checks with FSharp.Core 8.0.100
  across net10.0, net8.0, and netstandard2.1.
- All 29 Query cases passed in all four Fable combinations (traced/untraced,
  inline/promise dispatch). Full report gate passed; existing core differences
  remain documented in `docs/.ai/fable-compat.md`. Full counts: untraced inline
  826/844, promise 782/844; traced inline 905/927, promise 856/927; 87 excluded.
- Windows NativeAOT published and its executable reported all checks passed.
  This terminal omits the OS environment variable, so publish used
  `-p:OS=Windows_NT`; no product workaround was added.
- Documentation built, including the recipe and Query API pages. Inherited
  NU1608, esbuild, and external-link warnings remain unchanged.
- XML documentation audit returned zero findings. Comment hygiene, Fantomas,
  and whitespace checks passed. Logs are local in ignored
  `artifacts/query-verification`.

## Execution decisions and limits

- Used a sibling worktree rather than an unignored `.worktrees` directory.
- Combined planned commits into one implementation slice because ownership and
  staged publication share foundations. The tradeoff is a larger review commit.
- Portable tests use controlled completion with an immediate unchecked graph;
  a dedicated guarded manual-dispatch test verifies worker completion visibility.
  Await helpers wait for applied state and bound .NET outcome waits; Fable promises
  lack Task.Result/IsCompleted. Retired-delivery tests retain a short drain delay.
- Trace history deliberately retains published values. Payload collection is
  asserted only in untraced Release; its skip adds one to five existing traced
  allocation skips.
- Stopped the implementation's SageFs session, left the user's daemon running,
  and restored the generated warmup-cache change.
- Application membership, projection, and ordering rules remain explicit. Record
  and list reconstruction remains; normalization, retries, expiry, and automatic
  refetch are outside this release.
