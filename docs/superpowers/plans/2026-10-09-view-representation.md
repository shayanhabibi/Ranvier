# Portable view representation experiment

**Execution:** native, continuing the user's approved typed-function DSL experiment.
**Spec:** ../specs/2026-10-08-ranvier-view-dsl-design.md
**Goal:** compare an opaque instruction tree with an opaque backend-neutral build function using the same interpreters. Keep experimental APIs outside src.

## Constraints

Keep definitions reusable and inert. Setup receives cleanup registration, never a browser node. Use explicit synchronous bindings with equality and browser-operation server policy. This is a representation probe, not the production status/scheduler/keyed-list implementation. Keep the existing lifetime adapter unchanged. Compile Release, measure construction separately from execution, and report costs without claiming Solid parity or a frozen API.

## Review Focus

Check escaping, browser capability rejection, independent mount ownership, setup failure unwind, equality/update locality, and whether measurements compare equivalent operations. Check generated code and distinguish measured allocation bytes from inferred allocation sites. Keyed branch and production status semantics remain explicit outstanding gates in framework Task 2.

## Task 1 — Portable definitions and HTML

- [x] Add failing .NET tests for equivalent escaped HTML, fragments, snapshot bindings, independent component setup, failure cleanup and explicit browser policy.
- [x] Implement the two opaque representations against one backend contract and an HTML interpreter.
- [x] Prove typed modifiers accept input properties only on inputs and generic attributes on the selected native types using SageFs compiler probes.
- [x] Run all portable checks in Release; commit.

## Task 2 — Browser interpretation

- [x] Add failing jsdom/Chromium checks for reuse, component setup once, scalar update locality, static content without effects, event cleanup and failed setup unwind.
- [x] Implement one browser interpreter for both definitions using existing graph APIs and the proven lifetime adapter.
- [x] Run complete playground Node/browser suites and portable checks; commit.

## Task 3 — Sugar and measurements

- [x] Compare direct, function sugar and builder sugar definitions with identical output and resource counts.
- [x] Measure Release definition allocations on .NET and browser construction, mount, update and disposal across static, wide, fragment and reused workloads; repeat independent browser processes.
- [x] Inspect generated JS, write evidence and a provisional recommendation, update framework progress honestly.
- [x] Run repository acceptance, get one independent whole-branch review, fix important findings with regression tests, commit.

**Acceptance:** portable checks, complete playground suites and Release builds pass; baseline whole-solution errors are reported separately. Representation choice remains provisional until keyed branches, status and retained heap gates are proven.

Execution complete: see the evidence report for measurements, review findings/fixes and remaining framework gates. Commits begin at 504b1a2; Task 1 2cb30c6, Task 2 e7760c4, measurements e1cd2c3, followed by the portable edge fixes.
