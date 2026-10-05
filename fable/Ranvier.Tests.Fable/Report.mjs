// Runs each compiled build of the suite under both delivery modes and writes docs/.ai/fable-compat.md.
// Exits non-zero when a test fails under inline delivery and has no entry in `differences`.
//
//   node fable/Ranvier.Tests.Fable/Report.mjs
import { spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const root = join(import.meta.dirname, "..", "..");
const suite = join(root, "tests", "Ranvier.Tests");
const output = join(root, "docs", ".ai", "fable-compat.md");

// The compiled builds: Release, and Release with the trace log compiled in.
const builds = [
    { name: "untraced", dir: "dist/tests" },
    { name: "traced", dir: "dist/tests-traced" },
];
const deliveries = ["inline", "promise"];

// Each inline-delivery failure, by test title, with the difference in the Fable guide that explains it.
const differences = {
    "Equality of value types differs": [
        "a struct tuple still compares by value",
        "an option is compared by reference, so Some 1 over Some 1 propagates",
        "the structural policy cuts off a nan write, unlike the identity one",
        "updateBy returns the input when f returns an equal value-typed element",
        "createOptionMemo under structural policy: an unchanged element costs one Equals, in the memo's cutoff",
        "a hand-written memo over Some wakes on every root write on .NET",
    ],
    "Every `await` suspends": [
        "a continuation run inside another computation's body leaves that computation's edges alone",
        "a cleanup registered in a continuation run inside an effect belongs to the root",
        "a pending read in a continuation run inside an effect does not suspend the effect",
        "an effect run inside a continuation keeps the edges it reads after settling another flight",
        "a memo run inside a continuation keeps the edges it reads after settling another flight",
        "a read after an await on a completed task is tracked",
        "an async memo whose task captures a pending read is pending",
    ],
    "A `Queue` flight applies on a later microtask": [
        "Queue applies every result in the order the flights started",
        "Queue shows an applied outcome while a later flight is in progress",
        "the Queue policy applies a failure in the middle without losing what follows",
        "the Queue policy applies a synchronous failure after the flight started before it",
        "the Queue policy keeps a run suspended on a source pending when an older flight lands",
    ],
    "Trace records carry less": [
        "a node reports the test's file:line",
        "a node created through a combinator reports the user's line",
        "why steps, history runs and snapshot nodes carry recorded values",
        "valueText renders a value as one line",
    ],
};

const explained = new Map(Object.entries(differences).flatMap(([why, titles]) => titles.map(t => [t, why])));

function run(build, mode) {
    const file = join(mkdtempSync(join(tmpdir(), "ranvier-fable-")), `${build.name}-${mode}.json`);
    const mocha = spawnSync(
        process.execPath,
        [join(root, "node_modules", "mocha", "bin", "mocha.js"), `${build.dir}/Main.fs.js`, "--timeout", "10000",
         "--reporter", "fable/Ranvier.Tests.Fable/Reporter.cjs", "--reporter-option", `output=${file}`],
        { cwd: root, env: { ...process.env, RANVIER_FABLE_DELIVERY: mode }, stdio: "inherit" });
    if (mocha.error) throw mocha.error;
    return new Map(JSON.parse(readFileSync(file, "utf8")).map(r => [r.path.join(" / "), r]));
}

// The files of the suite in compile order.
const sources = new Map(
    [...readFileSync(join(suite, "TestFiles.props"), "utf8")
        .matchAll(/<Compile Include="\$\(MSBuildThisFileDirectory\)([^"]+)" Link="([^"]+)"/g)]
        .map(m => [m[2], resolve(suite, m[1])]));
const files = [...sources.keys()];

// Tests compiled out under Fable: each `#if !FABLE_COMPILER` block that opens with a `// .NET only: <reason>` line.
// A block holding no test and sitting inside one excludes part of that test; a block of helpers is skipped.
const declaration = /^\s*(?:<\| )?(?:test|testCase|testCaseAsync)\s+\$?"([^"]+)"/;

function exclusions(file) {
    const lines = readFileSync(sources.get(file), "utf8").split(/\r?\n/);
    const found = [];
    let enclosing = null;
    for (let i = 0; i < lines.length; i++) {
        const test = lines[i].match(declaration);
        if (test) enclosing = test[1];
        if (!/^#if !FABLE_COMPILER/.test(lines[i])) continue;
        let depth = 1, end = i + 1;
        for (; end < lines.length && depth > 0; end++) {
            if (/^#if/.test(lines[end])) depth++;
            else if (/^#endif/.test(lines[end])) depth--;
        }
        const block = lines.slice(i + 1, end - 1);
        const marker = block[0]?.match(/\/\/ \.NET only: (.*)$/);
        if (!marker) continue;
        const tests = block.map(l => l.match(declaration)).filter(Boolean).map(m => m[1]);
        const code = block.slice(1).find(l => l.trim() !== "" && !l.trim().startsWith("//")) ?? "";
        if (tests.length > 0) tests.forEach(name => found.push({ name, reason: marker[1], kind: "test" }));
        else if (!/^(let|type|open|\[<)/.test(code)) found.push({ name: enclosing ?? "(file)", reason: marker[1], kind: "part" });
        i = end - 1;
    }
    return found;
}

// results[build][mode]: test key to its record.
const results = Object.fromEntries(builds.map(b => [b.name, Object.fromEntries(deliveries.map(m => [m, run(b, m)]))]));
const runs = builds.flatMap(b => deliveries.map(m => results[b.name][m]));
const byFile = new Map(files.map(f => [f, []]));
for (const key of new Set(runs.flatMap(r => [...r.keys()]))) {
    const file = key.split(" / ")[0];
    if (!byFile.has(file)) byFile.set(file, []);
    byFile.get(file).push(key);
}

const escape = s => s.replaceAll("|", "\\|");
const stateOf = (build, mode, key) => results[build][mode].get(key)?.state ?? "not run";
const ran = (build, key) => deliveries.some(m => results[build][m].has(key));
const nameOf = key => key.split(" / ").slice(2).join(" / ");
const untriaged = [];
const summary = [];
const sections = [];
const totals = Object.fromEntries(builds.map(b => [b.name, { run: 0, inline: 0, promise: 0 }]));
let excludedTotal = 0, partialTotal = 0;

for (const [file, keys] of byFile) {
    const excluded = files.includes(file) ? exclusions(file) : [];
    const whole = excluded.filter(e => e.kind === "test");
    const parts = excluded.filter(e => e.kind === "part");
    if (keys.length === 0 && excluded.length === 0) continue;
    excludedTotal += whole.length;
    partialTotal += parts.length;

    const cells = [];
    const failures = [];
    for (const { name: build } of builds) {
        const inBuild = keys.filter(k => ran(build, k));
        const passes = mode => inBuild.filter(k => stateOf(build, mode, k) === "passed").length;
        totals[build].run += inBuild.length;
        for (const m of deliveries) totals[build][m] += passes(m);
        cells.push(inBuild.length, passes("inline"), passes("promise"));
        for (const key of inBuild) {
            const inline = stateOf(build, "inline", key), promise = stateOf(build, "promise", key);
            if (inline === "passed" && promise === "passed") continue;
            let why = inline === "passed" ? "Async results arrive on a later microtask" : explained.get(key.split(" / ").at(-1));
            if (!why) {
                why = "**untriaged**";
                untriaged.push(`${build}: ${key}: ${results[build].inline.get(key)?.message ?? inline}`);
            }
            failures.push(`| ${build} | ${escape(nameOf(key))} | ${inline} | ${promise} | ${why} |`);
        }
    }
    summary.push(`| ${file} | ${cells.join(" | ")} | ${whole.length} | ${parts.length} |`);

    const out = [`## ${file}`, ""];
    if (failures.length > 0) out.push("| Build | Failed | Inline | Promise | Difference |", "| --- | --- | --- | --- | --- |", ...failures, "");
    if (excluded.length > 0) {
        out.push("| Excluded | Reason |", "| --- | --- |");
        for (const e of excluded)
            out.push(`| ${escape(e.kind === "part" ? `part of: ${e.name}` : e.name)} | ${escape(e.reason)} |`);
        out.push("");
    }
    const passed = keys.filter(k =>
        builds.every(({ name: b }) => !ran(b, k) || deliveries.every(m => stateOf(b, m, k) === "passed")));
    if (passed.length > 0) {
        out.push(`<details><summary>${passed.length} passed in every build and delivery that runs them</summary>`, "");
        for (const key of passed) {
            const only = builds.filter(b => ran(b.name, key));
            out.push(`- ${nameOf(key)}${only.length < builds.length ? ` (${only.map(b => b.name).join(", ")} only)` : ""}`);
        }
        out.push("", "</details>", "");
    }
    sections.push(out.join("\n"));
}

const header = builds.flatMap(b => [`${b.name}: run`, "inline", "promise"]);
const totalCells = builds.flatMap(b => [totals[b.name].run, totals[b.name].inline, totals[b.name].promise]);

writeFileSync(output, [
    "# Fable compatibility",
    "",
    "Generated by `dotnet fsi build.fsx test-fable` (`fable/Ranvier.Tests.Fable/Report.mjs`); do not edit by hand.",
    "",
    "The .NET suite in `tests/Ranvier.Tests`, compiled with Fable and run under Node.js with Mocha. Two builds:",
    "",
    "- **untraced**: the Release build.",
    "- **traced**: the Release build with the trace log (`RanvierTrace=true`) compiled in. Tests of the log run only here.",
    "",
    "Each build runs in two deliveries:",
    "",
    "- **inline**: a test's `TaskCompletionSource` settles its awaiters before `SetResult` returns, as on .NET.",
    "  A failure here is a difference in behaviour.",
    "- **promise**: a test's `TaskCompletionSource` is a promise, as in an application. A test that fails only here",
    "  observes microtask delivery.",
    "",
    "Each difference named below is described in [the Fable guide](../content/fable/index.md).",
    "Excluded tests exercise a .NET-only facility and are compiled out with `#if !FABLE_COMPILER`.",
    "",
    `| File | ${header.join(" | ")} | Excluded | Partly excluded |`,
    `| --- | ${header.map(() => "---").join(" | ")} | --- | --- |`,
    ...summary,
    `| **Total** | ${totalCells.join(" | ")} | ${excludedTotal} | ${partialTotal} |`,
    "",
    ...sections,
].join("\n"));

for (const { name } of builds) {
    const t = totals[name];
    console.log(`${name}: inline ${t.inline}/${t.run}, promise ${t.promise}/${t.run}`);
}
console.log(`excluded ${excludedTotal}; wrote ${output}`);
if (untriaged.length > 0) {
    console.error(`${untriaged.length} untriaged inline failure(s):\n  ${untriaged.join("\n  ")}`);
    process.exit(1);
}
