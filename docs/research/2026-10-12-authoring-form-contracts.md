# Authored form contracts

The reusable component example now includes a signal-backed checkbox and the existing signal-backed text input, using the generated authoring methods over the existing renderer.

Real Chromium tests cover:

- Synthetic compositionstart/composing-input/compositionend/input events preserve the focused input, node identity and caret while the signal echoes its value.
- A subsequent programmatic label change updates the same input and preserves focus. Caret preservation across that different value is not claimed.
- A native checkbox change updates its signal and displayed state; parent callbacks can toggle the signal and update the live checked property.
- Both event-driven and programmatic checkbox updates retain the native node and leave the checked attribute absent. The live property and default-value attribute remain separate contracts.

The checkbox test first failed because the example control was missing. Adding the example and selecting the existing input type attribute in the authoring policy required no renderer change. The pilot now contains ten attributes and four live properties; full generated HTML coverage remains 112 tags and 34 explicit named attributes.

Synthetic DOM events exercise browser composition-event handling, not real operating-system IME behavior. Manual/platform IME checks and broader browser coverage remain open. These checks advance the typed-DOM form work in framework Task 7; owned async helpers, lifecycle integrations and portable property metadata remain unfinished.

Verification passed: 193 Node cases, 21 Chromium tests, all 21 compiler-contract cases, and the generator's deterministic-output and policy-rejection checks. The Release playground build completed with zero warnings and zero errors. The full Chromium suite was rerun after adding assertions that programmatic checkbox changes also leave the checked attribute absent.

The [performance correction](authoring-performance/2026-10-12-gc-correction.md) records the preceding checkpoint: exact resource/allocation parity, comparable update costs, noisy browser construction, and the GC artefact corrected in the .NET timing harness.
