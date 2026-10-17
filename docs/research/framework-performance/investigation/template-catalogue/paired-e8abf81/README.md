# Paired template comparison: `0e82e46` and `e8abf81`

Six runs of `node benchmarks/frameworks/run.mjs --template-comparison` from `examples/Fable.Ranvier.Playground`, measured on 2026-10-14 between 06:23 and 06:28 UTC.

- Machine: AMD Ryzen 9 9900X, Windows 10.0.26200, Node 26.7.0.
- Browser: Chromium 153.0.8010.12 (Playwright), three processes per run.
- Frameworks: Solid 2.0.0-rc.14 with `@solidjs/compiler`, Fable.Ripple.Dom 1.0.0-beta.9, React 19.3.0; Fable 5.18.0, Release, counters off.
- `base-N.json`: commit `0e82e46`, the commit before plan 2c's code, with the handwritten template members.
- `head-N.json`: commit `e8abf81`, the end of plan 2c's Tasks 1 and 2, with the generated catalogue.

Each commit was built once in its own git worktree (`run.mjs --build` on its first run) and the runs alternated: base 1, head 1, base 2, head 2, base 3, head 3. Every run passed its 45 correctness checks. Each file records its commit (`baseCommit`), time, Chromium version, CPU and source hashes. `dirtyExperiment: true` is a constant written by `run.mjs`; both worktrees were clean checkouts of their commits.
