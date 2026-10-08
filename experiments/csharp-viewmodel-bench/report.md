# MVVM Toolkit versus Ranvier: order view-model replay

Measured 2026-10-02T05:41:01.8816579+00:00; .NET 10.0.12, Microsoft Windows 10.0.26200; AMD Ryzen 9 9900X, 24 logical processors. CommunityToolkit.Mvvm 8.4.0 versus the current Ranvier source at repository revision ac96be31ee37d6e3e27a8fda366ad113bb2d9e94. Release, tracing off, tiered compilation off.

For these exact small view models, the Toolkit implementation had lower median CPU time and UI-thread allocation in every CPU-focused flow. The actual WPF binding workload narrowed the steady-state timing gap to about 6–15%. Creation/disposal was approximately 59% slower with Ranvier in the WPF screen-lifetime flow. Several steady-state trial ranges overlap, so the small gaps are observations from this run, not statistically established advantages.

## Actual WPF controls and bindings

Medians of 12 trial batch means after two warmup batches, alternating execution order. Parentheses show the range of trial means. Each operation is the complete named interaction, not an individual property write. Toolkit figures come first in allocation and notification pairs.

- **wpf-quantity-input**: Toolkit 27.90 µs/op (26.59–29.68); Ranvier 29.46 µs/op (28.55–31.02). Ranvier/Toolkit median-time ratio 1.06×. UI allocation 3.83 / 5.03 KiB/op; notifications 3.00 / 4.00 per operation.
- **wpf-refresh**: Toolkit 54.96 µs/op (51.20–66.01); Ranvier 60.57 µs/op (54.78–76.49). Ranvier/Toolkit median-time ratio 1.10×. UI allocation 8.04 / 10.69 KiB/op; notifications 15.00 / 8.00 per operation.
- **wpf-error-retry**: Toolkit 75.04 µs/op (66.49–90.74); Ranvier 86.26 µs/op (79.95–113.64). Ranvier/Toolkit median-time ratio 1.15×. UI allocation 12.50 / 19.90 KiB/op; notifications 29.00 / 16.00 per operation.
- **wpf-100-row-input**: Toolkit 28.64 µs/op (27.86–29.65); Ranvier 31.25 µs/op (29.21–32.38). Ranvier/Toolkit median-time ratio 1.09×. UI allocation 3.83 / 5.03 KiB/op; notifications 3.00 / 4.00 per operation.
- **wpf-open-close**: Toolkit 157.29 µs/op (152.22–169.12); Ranvier 250.04 µs/op (245.33–263.60). Ranvier/Toolkit median-time ratio 1.59×. UI allocation 38.48 / 59.99 KiB/op; notifications 9.00 / 6.00 per operation.

The refresh flow includes quantity input while loading and rejection of a second request. Error/retry includes a failing reload followed by a successful request. The 100-row flow edits one independently bound row, with one shared Ranvier graph. The lifetime flow includes view models, controls, bindings and graphs. These are headless WPF controls with real two-way TextBox binding and dispatcher draining; no window layout, paint or GPU rendering is measured.

## View models with a binding-style subscriber

- **open-load-close**: Toolkit 0.379 µs/op; Ranvier 11.407 µs/op. UI bytes/op 832 / 18216.
- **quantity-edit**: Toolkit 0.105 µs/op; Ranvier 0.738 µs/op. UI bytes/op 72 / 1104.
- **quantity-no-op**: Toolkit 0.005 µs/op; Ranvier 0.020 µs/op. UI bytes/op 24 / 144.
- **refresh**: Toolkit 0.339 µs/op; Ranvier 6.988 µs/op. UI bytes/op 400 / 4840.
- **error-retry**: Toolkit 2.713 µs/op; Ranvier 20.683 µs/op. UI bytes/op 1568 / 11255.
- **mixed-journey**: Toolkit 0.797 µs/op; Ranvier 10.207 µs/op. UI bytes/op 606 / 8269.
- **100-cart-edit**: Toolkit 0.099 µs/op; Ranvier 0.799 µs/op. UI bytes/op 72 / 1104.
- **close-in-flight**: Toolkit 1.605 µs/op; Ranvier 10.365 µs/op. UI bytes/op 1088 / 17424.
- **delayed-service**: Toolkit 28698.350 µs/op; Ranvier 28050.400 µs/op. UI bytes/op 778 / 4232.

The delayed-service test requests a 10 ms Task.Delay on a worker thread. Actual end-to-end medians were 28.70 ms for Toolkit and 28.05 ms for Ranvier. Timer resolution and scheduling dominate; the ranges overlap substantially. This is not evidence of a library advantage. The no-op test also operates near harness overhead and should not be advertised as a speedup ratio.

## What the result supports

Ranvier produced fewer notifications during refresh and error/retry, but still performed more work and allocated more in this implementation. A loaded quantity edit already targets the same single cart in the Toolkit baseline. Multiplication and formatting are cheap, so the dependency graph has little computation to avoid. Automatic pending/error propagation and ownership are the useful distinctions here; this benchmark does not support a performance pitch for this small example.

## Verification and scope

36 basic correctness checks and 20 checks against real WPF bindings passed. These cover loading, success, error, recovery, quantity changes during loading/failure, overlapping request rejection, disposal cancellation, late completion after detaching, and background completion. Trial endpoints were validated against expected totals and displayed values.

Allocations cover only the UI thread and include the service stubs and harness; they exclude background allocations. The quote service is controlled rather than real HTTP. The same examples are used unchanged in both runners. These are synthetic replays of realistic flows on one machine; they are neither frame-rate measurements nor a library-wide comparison. The working tree had existing untracked files. Raw JSON records timestamps, every trial and hashes of the measured harness/example source.

See [README.md](README.md) for commands, flow definitions and measurement details; [results.json](results.json) and [wpf-results.json](wpf-results.json) contain raw trials.

