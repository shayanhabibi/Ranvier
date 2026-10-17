# Owned ranges proof

Continue Task 3 of the approved framework plan, using the instruction tree provisionally.
Spec: `docs/superpowers/specs/2026-10-06-fable-ranvier-framework-brief.md` (C01, C03, C07).

Global constraints: experimental only; preserve the baseline browser interpreter and public signatures; caller owns graph; no implicit native removal disposal; full browser and DOM suites are the gate. Work only in the sibling DOM worktree.

### Task 1: Prove scope attachment phases

Produces: `ScopePhase` (SettingUp, Attached, Disposed), `Scope.Phase`, `Scope.MarkAttached`.
Consumes: existing child-first cleanup and shared disposal drain.
Write a lifecycle test first: attachment propagates to existing children, disposed scopes reject attachment, newly created children require their own attachment. Observe RED, use SageFs for the pure state probe, implement, run the full lifetime suite and commit.
Expected: all existing lifetime cases pass; the new phase case passes.
Verification: `rtk proxy npm run test:dom` in the playground.

### Task 2: Prove owned ranges and transactional mounting

Produces: a separate experimental mounting interpreter with detached staging, stable comment boundaries, explicit owned/borrowed native nodes, parent scopes, lifecycle checked movement, deterministic release, independent graph ownership.
Consumes: Scope phases and existing portable Backend/Tree operations.
Write browser cases before implementation: empty/one/fragment mounts; unrelated siblings; node identity on movement; invalid destination and mutated boundaries rejected before movement; reparented owned nodes removed; borrowed nodes restored to original placement; setup and attachment failure unwind bindings/listeners; child-first cleanup while attached; reporter failures cannot interrupt release; disposed operations fail; parent and graph disposal; multiple independent mounts.
Observe RED, implement, run Node and Chromium suites, record limitations and acceptance evidence, commit.
Expected: all DOM and browser cases pass, including existing representation cases.
Verification: `rtk proxy npm test` in the playground.

Review Focus: reentrant disposal during setup/attachment; cross-document and descendant movement; duplicate native ownership; borrowed restoration after original anchor disappears; attachment failures after partial native mutation; retained setup capabilities; release failures and reporter reentry. Do not assume stable markers alone prove ownership.

Performance gate remains open: this proves correctness, not a production representation choice or measured allocation/timing budget. Native ownership transfer between scopes and dynamic structural updates remain later work.
