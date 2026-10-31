# Rendering capability observations implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide passive, truthful, extensible runtime capability observations across native hosts and browser realms.

**Architecture:** A shared registry preserves independent facts and provenance; native and browser providers query their actual runtime. A bounded additive native ABI exposes compiled facts and live host values without enabling facilities.

**Tech Stack:** F#, Fable, Task/CancellationToken, WebCore C++ ABI, Python producer probes, Chromium, NativeAOT.

**Spec:** `docs/superpowers/specs/2026-10-30-rendering-capabilities-design.md` (root approved).

## Core and view state

- **Core primitives used:** CapabilityService owns registry registrations, cancellation and immutable observation snapshots. No reactive graph state is required for passive queries.
- **View-owned state:** Native adapter retains only the borrowed host, owner-thread dispatch and host disposal subscription. These describe the native host lifetime; no reactive owner tree or scheduler is duplicated.

## Global constraints

- Never promote Exported into Implemented, Enabled, Permitted or Available.
- Unknown arbitrary names remain Unknown; negatives require authoritative evidence.
- Native20/ABI28, Core22/Views0.1.12/backendalpha12; forward-integrate Native19 and managed21 before final gates.
- No permission prompts, playback, GPU acquisition, callback installation or setting mutation during discovery.
- C:/wk/src is root-owned; no engine edits without coordination.
- Format-support results describe the query, not successful decoding/rendering of arbitrary input.
- Snapshot arrays are copied; native document queries refresh after replacement.

## Review focus

- Contradictory providers retain both observations and produce Unknown, including conflicting values.
- Cancellation/disposal while a provider ignores cancellation cannot publish late or hang.
- Zero, false, empty text and absent remain distinguishable through the native wire.
- Missing browser query APIs remain Unknown rather than guessed unsupported.
- Host replacement and owner-thread dispatch invalidate old document/context reads.

### Task 1: Shared observations and registry

**Files:** Create `src/Ranvier.Dom/Capabilities.fs`; update both DOM compile/pack lists; create `tests/Capabilities/Registry.fsx` with durable pure fixtures.

**Interfaces:** CapabilityKey(category,name); independent CapabilityFact values; CapabilityObservation with typed optional value and provenance; CapabilityProvider(id,keys,observe); CapabilityService.KnownKeys(), Observe(key,ct), Register(provider), Dispose(); Capabilities.create().

- [ ] Write pure tests for all-unknown, agreeing/contradictory facts, independent dimensions, false/zero/empty/absent, copied names, normalization, registration order/removal, duplicate IDs, malformed arguments and failures.
- [ ] Observe RED in owned SageFs session before implementing the registry.
- [ ] Implement typed model, validation and combination; execute synchronous provider starts before asynchronous aggregation.
- [ ] Add async fixtures for pre/mid cancellation, ignored token, disposed registry and late completion; observe RED then GREEN.
- [ ] Persist GREEN fixtures/source; run FCS check and public API/impact before adapter consumption; commit with message file.

### Task 2: Native producer catalogue and live query ABI

**Files:** Modify `host/RanvierWebCoreAbi.h`, `host/RanvierWebCoreAbi.cpp`, host CMake compile list as needed; create bounded capability catalogue helper and `tests/abi/capabilities` producer probes/catalogue checker. Existing DOM generated files stay unchanged.

**Interfaces:** Six rv_capability/rv_host_capability exports from approved spec; value kinds Absent0/Boolean1/Number2/Text3; boolean numeric wire0/1; wrong-kind getters return unsupported without modifying outputs; UTF8 length and capacity count exact bytes, with no terminator copied.

- [ ] Forward-merge exact frozen Native19 head; record parent and profile provenance.
- [ ] Write producer RED probes for export presence, catalogue coverage, all ABI/model names, current renderer settings and every current compiled/stub profile facility.
- [ ] Implement catalogue and actual settings/MIME query getters with authoritative compile guards; arbitrary unknown names succeed as Unknown.
- [ ] Add RED/GREEN buffer/null/enum/wrong-kind/thread/lifetime tests, before/after existing setting setter checks and no-mutation assertions.
- [ ] Allocate Native20/ABI28 separately; build private runtime under granted lease; preserve full producer results and hash receipts; commit.

### Task 3: Native and browser provider adapters

**Files:** Create `src/Fable.Ranvier.Dom/BrowserCapabilities.fs`, Views `src/Ranvier.View.WebCore/Capabilities.fs`; bounded declarations/compile lists in Views; shared `tests/NativeApi.Conformance/Capabilities.fs` and focused native/browser runners.

**Interfaces:** BrowserCapabilities.forDocument(document); native Capabilities.forHost(host), both returning CapabilityService and using Task observations from Task1. Native adapter uses existing PostToOwner/Cycle and Disposed, never assumed SynchronizationContext.

- [ ] Establish clean FCS owner-dispatch and current-document route evidence before changes; capture public impact/API baseline.
- [ ] Write shared RED fixtures for real provider APIs, unknowns, live settings, image/font query results and realm identity.
- [ ] Implement native mappings to Task2, querying current host document for font-format support; browser maps actual realm ImageDecoder/CSS/policy APIs without resource acquisition.
- [ ] Write RED/GREEN lifecycle fixtures for worker completion, owner dispatch, host disposal without further pumping, cancellation, replacement and late completion.
- [ ] Run focused JIT, genuine native PE AOT and actual browser fixtures with all runtime and managed assets hashed before execution.
- [ ] Commit source and focused evidence.

### Task 4: Integrated source, package and repository delivery

**Files:** Version/pin/CI metadata across three isolated trees; conformance runner lists; durable evidence under `docs/research/native-api-delivery`; H07 handover and issue112.

- [ ] Forward-merge final managed21/Views11, retaining H06 and SVG; allocate Core22/Views12 and Native20 pins separately.
- [ ] Wire full shared runners and CI; build all four normal/traced packages; verify private net8/net10 consumers.
- [ ] Run integrated native JIT/AOT and browser suites with exact frozen runtime pre/post hashes; retain actual PE/CLR-directory proof and executed paths.
- [ ] Batch normal/traced/API/FCS/all repository analyzer groups/formatter/XML/comment checks once after source stabilizes.
- [ ] Save a manifest with every input/output hash, source/integration parents and exact counts; acceptance agent audits read-only.
- [ ] Create/attach stacked PRs and track CI once; update issue112 with actual evidence and explicit official-release/platform limitations. Keep issue open until full acceptance.
