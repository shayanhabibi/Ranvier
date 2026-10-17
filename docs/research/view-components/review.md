# Final component review

Fresh gpt-6-astra review: no Critical or Important runtime findings. The documented tracking/ownership wording was corrected: browser setup is owned/untracked; Prop.read preserves caller tracking; the HTML interpreter has no automatic component owner or untracking.

Reviewer independently verified a clean scoped FCS check and 42 passing component/range Node tests; inspected all planned focus areas. Full 106 Node/15 Chromium acceptance and Release build are executor evidence.

# SDD ledger — plan: docs/superpowers/plans/2026-10-09-view-components.md

Pre-flight: Task 1 produces Components/Prop helpers; Task 2 consumes exactly that surface for direct/helper evidence and examples. Existing Operations/Backend signatures are preserved.
Ruling: keep the small helpers generic over Operations to exercise Tree and Program equally — avoids choosing the production representation prematurely — cost if wrong: specialization later.

Ruling: close the server cleanup capability after rendering — a new regression exposed late registrations being silently lost — cost if wrong: stricter behavior for code retaining an expired setup capability.
Task 1: complete (commits 34e2913..d76303a, tests: rtk proxy npm --prefix examples/Fable.Ranvier.Playground test →   15 passed (13.2s))

Ruling: inline the thin component helper — the first V8 probe retained 48 extra bytes per definition at the function boundary — cost if wrong: larger caller output; generated expansion and repeated measurements check this tradeoff.
Task 2: complete (commits d76303a..56ca93d, tests: rtk proxy npm --prefix examples/Fable.Ranvier.Playground test →   15 passed (12.0s))

Final review: fresh gpt-6-astra reviewer; no runtime defects, one documentation finding. Fresh reviewer FCS clean and 42 component/range Node cases pass.
Ruling: correct the tracking/ownership wording now, regraded Important — the existing wording could cause a caller subscription or server resource leak — cost if wrong: documentation becomes more restrictive than the experimental API requires. Documentation-only correction; no runtime change or mirrored test added.
Final: fixed documentation contract against inspected runtime and reviewer probe; existing full suite remains green.
Ruling: defer full server request-local graph/owner lifecycle to Task 9 and document it explicitly — this is a portable composition experiment — cost if wrong: additional server lifecycle work before reuse.
Ruling: leave pending/error/status policy open — existing Bind semantics are retained — cost if wrong: further binding work before a usable release.
Ruling: leave keyed identity and structural regions for their planned proofs — component setup is bounded here — cost if wrong: later identity changes may require adapting components.
Ruling: defer production representation selection, integrated budgets and full sugar libraries — the measurements cover only this helper — cost if wrong: later specialization or wrapper redesign.
Ruling: validate typed/null adapters, not arbitrary invalid JavaScript shapes — the current surface is typed F# — cost if wrong: foreign callers need additional boundary validation.
