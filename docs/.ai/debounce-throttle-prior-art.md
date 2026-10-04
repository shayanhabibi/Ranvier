# Debounce and throttle implementation prior art

Research date: 2026-10-03. Source inspection only; no comparative benchmarks were run. RxJS references are explicitly pinned to 7.8.2; newer branch source could not be fetched, so this note does not claim the newest RxJS retains that design. Other source links follow their main/master branches and can change. This note concerns event admission and emission, not cancellation of already-started computations.

## Verified source behavior

### RxJS 7.8.2: deadline update instead of restarting for every input

`debounceTime` keeps `lastValue`, `lastTime`, and one active scheduler action. Every input updates value/time; only an idle-to-active transition schedules. When the action fires it compares `scheduler.now()` with `lastTime + dueTime`, rescheduling itself for the remainder if silence has not lasted long enough. Otherwise it clears the active action/value before emitting. Completion flushes the pending value; error does not. This is direct prior art for moving timer mutations out of the input hot path. [Source](https://raw.githubusercontent.com/ReactiveX/rxjs/7.8.2/src/internal/operators/debounceTime.ts)

Its default `AsyncAction` uses `setInterval` internally, recycling a handle only when rescheduled after execution with the same delay; different delays clear/recreate the interval. Thus action reuse is not proof of native timer reuse on every rearm. [Source](https://raw.githubusercontent.com/ReactiveX/rxjs/7.8.2/src/internal/scheduler/AsyncAction.ts)

`throttle` exposes independent leading/trailing flags, defaulting to leading=true/trailing=false. It retains one latest pending value and a duration subscription. Ending the duration can emit the pending value and start another duration. It clears pending state before downstream notification to address reentrancy. These semantics are different from periodic sampling. [Source](https://raw.githubusercontent.com/ReactiveX/rxjs/7.8.2/src/internal/operators/throttle.ts)

### R3: reusable timers and specialized fixed-duration operators

Fixed-time `Debounce` creates one stopped `ITimer` per subscription, with a static callback and subscription object as state. Each input locks a gate, replaces the latest slot, increments a generation, and restarts the same timer. The callback checks the generation observed before obtaining the lock, then emits under the lock and clears the value. Completion flushes the value; disposal disposes the timer. Unlike its fixed-time overload, the async duration-selector implementation contains async execution and replaces its `CancellationTokenSource` when superseded. Fixed durations therefore have a much smaller visible source-level machinery than arbitrary async duration selectors; allocation totals require measurement. [Source](https://raw.githubusercontent.com/Cysharp/R3/main/src/R3/Operators/Debounce.cs)

R3's `InvokeOnce` calls `ITimer.Change(dueTime, InfiniteTimeSpan)`; `CreateStoppedTimer` sets both initial due time and period to infinite. [Source](https://raw.githubusercontent.com/Cysharp/R3/main/src/R3/Internal/TimeProviderExtensions.cs)

`ThrottleFirst` creates one timer, sets a closing flag, arms before delivering the admitted value, and suppresses further inputs while closed. Timer expiry only opens the gate. Input/callback state is locked. [Source](https://raw.githubusercontent.com/Cysharp/R3/main/src/R3/Operators/ThrottleFirst.cs)

`ThrottleLast` creates one timer, starts it only when the pending slot was empty, then overwrites the slot during the fixed interval without extending it. Timer expiry emits the latest value and clears the slot. It does not flush pending state in its completion handler. This is a first-input-anchored trailing window rather than a quiet-period debounce. [Source](https://raw.githubusercontent.com/Cysharp/R3/main/src/R3/Operators/ThrottleLast.cs)

R3 also has `DebounceFrame`: input resets an integer frame counter and registers the observer work item only if inactive. The shared frame runner increments it until the threshold, emits, and removes the work item. It does not create a timer per input. [Source](https://raw.githubusercontent.com/Cysharp/R3/main/src/R3/Operators/DebounceFrame.cs)

R3 describes `TimeProvider` and `FrameProvider` as replacements for general Rx scheduling, with platform-specific providers and fake providers for testing. Its displayed introductory benchmarks concern Range and subscription management, so they should not be presented as debounce/throttle performance evidence. [Project rationale](https://raw.githubusercontent.com/Cysharp/R3/main/README.md)

### System.Reactive: `Throttle` means quiet-period debounce

Fixed-duration `Throttle` replaces its scheduled action on each input using `SerialDisposableValue`. State contains latest value, pending flag, monotonically incremented identifier, and a lock. The scheduled callback captures the identifier and emits only if it remains current. Completion disposes scheduling and flushes the latest value; error disposes scheduling and clears pending state. This makes the stale-callback defense independent of successful cancellation. The file alone does not establish total allocation cost because the supplied scheduler owns scheduled-action implementation. [Source](https://raw.githubusercontent.com/dotnet/reactive/main/Rx.NET/Source/src/System.Reactive/Linq/Observable/Throttle.cs)

### Kotlin Flow: coroutine selection and conflation

`debounce` has a producer using a rendezvous channel, and a consumer selecting between the next input and a timeout while retaining one latest value. Normal closure flushes that value. `sample` instead uses a conflated input channel and a ticker, emitting the latest slot on ticks; completion cancels the ticker and does not guarantee the final value. These implementations fit suspendable stream collection and backpressure. They are not evidence that a coroutine/channel implementation minimizes synchronous signal-graph cost. [Source](https://raw.githubusercontent.com/Kotlin/kotlinx.coroutines/master/kotlinx-coroutines-core/common/src/flow/operators/Delay.kt)

### Runtime scheduling: lazy extension and existing timer multiplexing

Tokio distinguishes a timer's registered expiry from its true expiry. Extending an active deadline can optimistically update atomic state without immediately repositioning it in the wheel; the driver checks the true expiry when servicing entries and reschedules. Its compare-and-swap protocol prevents a racing extension from being marked expired prematurely. Earlier deadlines or already-pending entries use the reregistration path. This is current runtime-level evidence for lazy extension, not a measured comparison for Ranvier. [Timer-entry source](https://raw.githubusercontent.com/tokio-rs/tokio/master/tokio/src/runtime/time/entry.rs)

Tokio's timer wheel has six levels of 64 slots. Such a design is relevant when many active deadlines need shared scheduling; it brings precision/range and intrusive-entry complexity that must be assessed before adopting it. [Wheel source](https://raw.githubusercontent.com/tokio-rs/tokio/master/tokio/src/runtime/time/wheel/mod.rs)

.NET already manages timers through shared timer queues, partitioned to reduce contention, with native wakeups scheduled for queue deadlines. Therefore one managed timer per node does not mean one kernel timer per node; a graph scheduler would primarily target managed overhead, state dispatch, locality, and graph batching. [Runtime source](https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Threading/Timer.cs)

Recent adjacent work includes LazyTick in real-time operating systems (2025) and NG-RES timer research (2026). Their real-time scheduling context differs from managed reactive graphs, so their reported speedups should not be transferred into a recommendation here. [LazyTick publication](https://khchen.eu/publication/lazytick-lazy-and-efficient-management-of-job-release-in-real-time-operating-systems/), [NG-RES 2026 paper](https://drops.dagstuhl.de/entities/document/10.4230/OASIcs.NG-RES.2026.4).

## Design implications (inference, not benchmark results)

A strong candidate for a synchronous reactive runtime combines RxJS's deadline-update state machine with R3's subscription-lifetime timer, static callback, and platform/virtual-time provider. Intermediate debounce inputs then update only latest state and a monotonic deadline, without creating tasks, cancellation sources, or scheduled actions and without changing a native timer. The active callback may wake before the final deadline and rearm; this trades extra callback wakeups against fewer timer-queue updates. Which wins depends on input density, window duration, active-node count, runtime timer implementation, and thread contention.

A strictly leading-only throttle can avoid timers entirely: compare a monotonic timestamp with `nextAllowed`, update it only on admission, and reject earlier inputs. This is suitable only when reopening does not itself have to emit, publish state, or trigger graph work. A trailing throttle needs one pending slot and a wakeup, armed once per window.

Use the graph owner's serialized execution context if available: timer callbacks dispatch a wakeup, then graph-owned code checks the current deadline and disposal before publishing. Otherwise protect timer/input state and serialize publication, taking the pending result before releasing the lock and notifying downstream. Avoid executing user callbacks while holding an internal scheduling lock. Clearing pending state before invoking subscribers is essential to reason about reentrancy. Cancellation or timer restart alone should not be treated as proof that no callback is already queued. A deadline check additionally prevents an old queued callback that begins after a restart from publishing prematurely.

Start with a reusable `TimeProvider` timer per active node and compare Change-per-input against lazy deadline extension. A shared graph heap or wheel is a subsequent scale optimization only if measurements show the existing runtime queues and dispatch cost are insufficient.

Benchmark admission cost, managed allocation, timer mutations, timer wakeups, graph invalidations, and output latency. Include sparse inputs, long continuous bursts, many inactive nodes, many active nodes, disposal races, reentrancy, equal deadlines, and scheduler stalls. No source inspected establishes a universal fastest implementation.
