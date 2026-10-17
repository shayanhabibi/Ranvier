# Binding setup allocation reduction

This follows the mount diagnosis at `a2f2c7f`. The production renderer now keeps a committed-value flag and value instead of wrapping every successful write in an option. Synchronous bindings inline the liveness and setup-failure guards; queued bindings keep their asynchronous callbacks. Built-in property and attribute comparers are shared two-argument delegates. Fable emits them as aliases of its equality function and calls them directly, without a curried intermediate function. No public authoring API changes.

Tracking, untracked comparison/writes, disposal checks, initial setter rollback, failed-setter retries, queued version checks, and per-run cleanup ownership remain in place. Component and element construction are unchanged. Prepared templates are still outstanding.

## Validation

The final full playground test command passes 197 Node tests and 22 Chromium tests. The Release foundation build and independent F# workspace check pass with no errors or warnings. All fourteen pure queued-binding cases also pass in the rebuilt SageFs session. The framework harness verifies all adapters at all three widths in each browser process. A separate review found no semantic regressions.

## Measurements

`binding-results.json` compares a saved, minified counter-free Release bundle from `a2f2c7f` against the changed bundle. Three independent Chromium processes alternate variants, with three warmups and eleven measured rounds. At 128 rows:

- Mount: 946.72 → 933.75 µs. Process medians disagree on the direction; this is not a reliable mount speedup.
- Update: 131.30 → 122.85 µs. The pooled reduction is 6.4%; one process regresses.
- Disposal: 359.38 → 337.34 µs. All three process medians improve, but this change does not address the underlying disposal architecture.
- Retained JS heap: 485,587 → 437,915 bytes, down 9.8%. The reduction is consistent across all three processes. Native DOM memory is excluded.

`binding-framework-results.json` independently compares all four frameworks through their public authoring paths. At 128 rows, in µs per operation:

- Mount: Ranvier 1,064.37; Ripple 578.13; Solid 348.44; React 698.75. Ranvier is 1.84× Ripple and 3.05× Solid.
- Update: Ranvier 134.42; Ripple 134.87; Solid 128.27; React 282.73. Ranvier is 1.00× Ripple and 1.05× Solid.
- Disposal: Ranvier 380.47; Ripple 53.91; Solid 48.12; React 101.41. Ranvier is 7.06× Ripple and 7.91× Solid.
- Retained JS heap: Ranvier 426.8 KiB; Ripple 162.7 KiB; Solid 129.7 KiB; React 189.2 KiB. Ranvier is 2.62× Ripple and 3.29× Solid.

Absolute timings vary between runs. Compare the changed and saved bundles within the paired run; compare frameworks within the framework run. This small reduction is worth keeping for memory, but it does not close the mounting gap. The next mount work remains component/view recipe allocation and prepared DOM structure cloning, with ownership and rollback retained.

## Reproduction

Before editing or recompiling the baseline, build and verify `a2f2c7f` and copy `generated-framework-dist` to the ignored `generated-framework-binding-before` directory. Then, from `examples/Fable.Ranvier.Playground`, run sequentially:

```powershell
rtk node benchmarks/frameworks/run.mjs --build --binding-comparison
rtk node benchmarks/frameworks/compare-cleanup.mjs --binding-comparison
```

These flags write separate artifacts and preserve the earlier cleanup comparison. The paired artifact records hashes of both bundles; the framework artifact records source hashes and every raw sample.
