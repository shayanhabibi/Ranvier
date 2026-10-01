# Elmish and Ranvier.Elmish: task dashboard measurements

Measured 2026-09-30T17:26:33.7566864Z on Microsoft Windows 10.0.26200, .NET 10.0.12; 24 logical processors; AMD Ryzen 9 9900X 12-Core Processor.

SDK 10.0.401. Elmish 5.0.2; Avalonia 12.1.0; FuncUI checkout e2757a4216e2b215a862a35af6bddc5efb0f71b4; local Ranvier HEAD 4646639d530b39e5a48c29915db2c47e3671722b with tracing disabled. Both checkouts can contain uncommitted changes; source.json records status and SHA-256 hashes of the measured sources.

Eight rotated trials of 600 messages after two warmups per scenario. The figures below are the median of trial mean microseconds per message; ranges are the minimum and maximum trial means. P95 pools per-message samples. Allocations cover the UI thread only. Counters are from a separate replay.

These are headless Avalonia dispatch-and-drain timings, including real controls/templates; they do not measure GPU presentation, desktop frame rate or human input latency. Construction is excluded. See README.md for reproduction and interpretation.

## 100 tasks (50 visible rows)

### background

- **elmish**: 1337.9 µs/message (trial means 1331.4–1573.9); pooled p95 2642.4 µs; 1213479 bytes/message; GC totals 344/10/0. View builds: root 527, section 2108, row 26350; host patches 527; selector evaluations 0.
- **elmish-cached**: 79.9 µs/message (trial means 72.8–96.4); pooled p95 172.2 µs; 94385 bytes/message; GC totals 24/0/0. View builds: root 527, section 56, row 263; host patches 527; selector evaluations 0.
- **ranvier**: 37.2 µs/message (trial means 30.6–53.6); pooled p95 114.7 µs; 18130 bytes/message; GC totals 0/0/0. View builds: root 0, section 56, row 263; host patches 319; selector evaluations 28985.

Ranvier's median trial mean is 35.98× faster than uncached Elmish and 2.15× faster than cached Elmish (a ratio below 1 means slower).

### selected-edit

- **elmish**: 1644.8 µs/message (trial means 1607.9–1759.3); pooled p95 2484.2 µs; 1400580 bytes/message; GC totals 400/0/0. View builds: root 599, section 2396, row 29950; host patches 599; selector evaluations 0.
- **elmish-cached**: 211.7 µs/message (trial means 205.1–267.3); pooled p95 357.7 µs; 159112 bytes/message; GC totals 40/0/0. View builds: root 599, section 748, row 599; host patches 599; selector evaluations 0.
- **ranvier**: 229.8 µs/message (trial means 218.3–266.2); pooled p95 381.0 µs; 150476 bytes/message; GC totals 40/0/0. View builds: root 0, section 748, row 599; host patches 1347; selector evaluations 32945.

Ranvier's median trial mean is 7.16× faster than uncached Elmish and 0.92× faster than cached Elmish (a ratio below 1 means slower).

### search-page

- **elmish**: 28617.1 µs/message (trial means 26313.3–29167.8); pooled p95 66203.7 µs; 8378733 bytes/message; GC totals 3370/3151/1006. View builds: root 500, section 2000, row 17500; host patches 500; selector evaluations 0.
- **elmish-cached**: 28364.4 µs/message (trial means 27962.6–28926.5); pooled p95 65757.0 µs; 8275738 bytes/message; GC totals 3294/3092/970. View builds: root 500, section 900, row 14999; host patches 500; selector evaluations 0.
- **ranvier**: 29608.9 µs/message (trial means 24526.3–29808.2); pooled p95 66469.2 µs; 8656745 bytes/message; GC totals 3466/3256/1071. View builds: root 500, section 900, row 14999; host patches 18799; selector evaluations 34999.

Ranvier's median trial mean is 0.97× faster than uncached Elmish and 0.96× faster than cached Elmish (a ratio below 1 means slower).

### mixed

- **elmish**: 4138.2 µs/message (trial means 4056.8–4437.6); pooled p95 24821.6 µs; 1629655 bytes/message; GC totals 575/366/114. View builds: root 553, section 2212, row 18575; host patches 553; selector evaluations 0.
- **elmish-cached**: 2978.3 µs/message (trial means 2918.8–3152.1); pooled p95 32474.1 µs; 899323 bytes/message; GC totals 341/309/101. View builds: root 553, section 173, row 1655; host patches 553; selector evaluations 0.
- **ranvier**: 3017.6 µs/message (trial means 2987.2–3169.4); pooled p95 33381.6 µs; 885022 bytes/message; GC totals 340/300/100. View builds: root 50, section 173, row 1655; host patches 2118; selector evaluations 22839.

Ranvier's median trial mean is 1.37× faster than uncached Elmish and 0.99× faster than cached Elmish (a ratio below 1 means slower).

## 1,000 tasks (50 visible rows)

### background

- **elmish**: 1391.4 µs/message (trial means 1336.0–1423.9); pooled p95 2332.4 µs; 1363368 bytes/message; GC totals 384/0/0. View builds: root 556, section 2224, row 27800; host patches 556; selector evaluations 0.
- **elmish-cached**: 91.3 µs/message (trial means 88.2–96.7); pooled p95 200.1 µs; 172000 bytes/message; GC totals 48/0/0. View builds: root 556, section 82, row 23; host patches 556; selector evaluations 0.
- **ranvier**: 17.6 µs/message (trial means 16.8–18.3); pooled p95 84.4 µs; 5801 bytes/message; GC totals 0/0/0. View builds: root 0, section 82, row 23; host patches 105; selector evaluations 30580.

Ranvier's median trial mean is 79.11× faster than uncached Elmish and 5.19× faster than cached Elmish (a ratio below 1 means slower).

### selected-edit

- **elmish**: 1381.5 µs/message (trial means 1320.3–1579.3); pooled p95 2087.3 µs; 1402459 bytes/message; GC totals 400/0/0. View builds: root 599, section 2396, row 29950; host patches 599; selector evaluations 0.
- **elmish-cached**: 193.7 µs/message (trial means 183.4–219.0); pooled p95 321.2 µs; 160962 bytes/message; GC totals 40/0/0. View builds: root 599, section 748, row 599; host patches 599; selector evaluations 0.
- **ranvier**: 200.5 µs/message (trial means 191.3–225.8); pooled p95 332.3 µs; 151286 bytes/message; GC totals 40/0/0. View builds: root 0, section 748, row 599; host patches 1347; selector evaluations 32945.

Ranvier's median trial mean is 6.89× faster than uncached Elmish and 0.97× faster than cached Elmish (a ratio below 1 means slower).

### search-page

- **elmish**: 41670.5 µs/message (trial means 35390.0–43055.1); pooled p95 67880.5 µs; 12339770 bytes/message; GC totals 4877/4793/1600. View builds: root 600, section 2400, row 25000; host patches 600; selector evaluations 0.
- **elmish-cached**: 41357.9 µs/message (trial means 39330.4–42928.5); pooled p95 68880.2 µs; 12273283 bytes/message; GC totals 4816/4728/1564. View builds: root 600, section 1000, row 23799; host patches 600; selector evaluations 0.
- **ranvier**: 43403.3 µs/message (trial means 42838.7–44535.8); pooled p95 71263.6 µs; 12565620 bytes/message; GC totals 5055/4913/1651. View builds: root 600, section 1000, row 23799; host patches 26599; selector evaluations 51799.

Ranvier's median trial mean is 0.96× faster than uncached Elmish and 0.95× faster than cached Elmish (a ratio below 1 means slower).

### mixed

- **elmish**: 4301.3 µs/message (trial means 4222.9–5042.4); pooled p95 28258.3 µs; 2307260 bytes/message; GC totals 835/571/179. View builds: root 574, section 2296, row 23900; host patches 574; selector evaluations 0.
- **elmish-cached**: 3002.1 µs/message (trial means 2933.5–3636.9); pooled p95 31468.5 µs; 1386172 bytes/message; GC totals 543/479/152. View builds: root 574, section 200, row 2398; host patches 574; selector evaluations 0.
- **ranvier**: 3313.1 µs/message (trial means 3237.9–3947.1); pooled p95 35327.7 µs; 1268729 bytes/message; GC totals 518/509/169. View builds: root 60, section 200, row 2398; host patches 2778; selector evaluations 29149.

Ranvier's median trial mean is 1.30× faster than uncached Elmish and 0.91× faster than cached Elmish (a ratio below 1 means slower).

## What the counters explain

For 1,000-task background updates, cached Elmish and Ranvier both construct only 23 row views. Cached Elmish still patches its root 556 times; Ranvier performs 105 targeted host patches with no root rebuilds. Ranvier evaluates 30,580 selectors, so it does not avoid all propagation work. Its median trial mean is 5.19× faster than cached Elmish and its UI-thread allocation is 96.6% lower in this scenario.

Selected edits update both a visible row and the detail section. Cached Elmish already reuses the other views, and Ranvier performs several independent host patches per message. Search/paging changes membership and builds many controls in every runner. In the mixed trace, a small number of topology changes contributes much of the measured UI work. The counters show equivalent row construction between cached Elmish and Ranvier; the differences include patch scope, selector overhead and each integration's model equality guard, rather than only the message-loop engine.

Ranvier has slightly higher observed median times than cached Elmish for selected edits, search/paging and mixed work at both sizes. Several trial ranges overlap, so small median differences are not evidence of a statistically established advantage. The strong background result applies to mostly offscreen edits in this paged dashboard; the uncached baseline spends much more time and allocation rebuilding views.

## Shared update function

The pure pass excludes every runner and all UI work; it is reported separately and is not subtracted from dispatch timings.

- 100 tasks, background: 0.50 µs/message.
- 100 tasks, selected-edit: 0.11 µs/message.
- 100 tasks, search-page: 4.31 µs/message.
- 100 tasks, mixed: 0.65 µs/message.
- 1000 tasks, background: 0.22 µs/message.
- 1000 tasks, selected-edit: 0.22 µs/message.
- 1000 tasks, search-page: 38.89 µs/message.
- 1000 tasks, mixed: 3.32 µs/message.

## Scope

All variants share the same immutable update and control topology. Cached Elmish is a deliberate stronger baseline than rebuilding every view. Ranvier trades root rebuilding for selector and ownership costs; search/paging changes row membership, while surviving row owners and cached views remain. FuncUI can replace native hosts when a row moves position, requiring cached views to be remounted. Both Elmish runners use FuncUI's conventional structural model guard; Ranvier uses its graph equality policy. This single-machine synthetic workload is not evidence that every Elmish application becomes faster. Raw trials and message samples are preserved in raw.json.
