// Runs the compiled suite (dist/tests) under both delivery modes and writes docs/.ai/fable-compat.md.
// Exits non-zero when a test fails under inline delivery and has no entry in `differences`.
//
//   node fable/Ranvier.Tests.Fable/Report.mjs
import { spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const root = join(import.meta.dirname, "..", "..");
const suite = join(root, "tests", "Ranvier.Tests");
const output = join(root, "docs", ".ai", "fable-compat.md");

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
};

const explained = new Map(Object.entries(differences).flatMap(([why, titles]) => titles.map(t => [t, why])));

function run(mode) {
    const file = join(mkdtempSync(join(tmpdir(), "ranvier-fable-")), `${mode}.json`);
    const mocha = spawnSync(
        process.execPath,
        [join(root, "node_modules", "mocha", "bin", "mocha.js"), "dist/tests/Main.fs.js", "--timeout", "10000",
         "--reporter", "fable/Ranvier.Tests.Fable/Reporter.cjs", "--reporter-option", `output=${file}`],
        { cwd: root, env: { ...process.env, RANVIER_FABLE_DELIVERY: mode }, stdio: "inherit" });
    if (mocha.error) throw mocha.error;
    return new Map(JSON.parse(readFileSync(file, "utf8")).map(r => [r.path.join(" / "), r]));
}

// The files of the suite in compile order.
const files = [...readFileSync(join(suite, "TestFiles.props"), "utf8").matchAll(/Link="([^"]+)"/g)].map(m => m[1]);

// Tests compiled out under Fable: each `#if !FABLE_COMPILER` block that opens with a `// .NET only: <reason>` line.
// A block holding no test and sitting inside one excludes part of that test; a block of helpers is skipped.
const declaration = /^\s*(?:<\| )?(?:test|testCase|testCaseAsync)\s+\$?"([^"]+)"/;

function exclusions(file) {
    const lines = readFileSync(join(suite, file), "utf8").split(/\r?\n/);
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

const modes = { inline: run("inline"), promise: run("promise") };
const tests = [...new Set([...modes.inline.keys(), ...modes.promise.keys()])];
const byFile = new Map(files.map(f => [f, []]));
for (const key of tests) {
    const file = key.split(" / ")[0];
    if (!byFile.has(file)) byFile.set(file, []);
    byFile.get(file).push(key);
}

const escape = s => s.replaceAll("|", "\\|");
const stateOf = (mode, key) => modes[mode].get(key)?.state ?? "not run";
const nameOf = key => key.split(" / ").slice(2).join(" / ");
const untriaged = [];
const summary = [];
const sections = [];
const totals = { run: 0, inline: 0, promise: 0, excluded: 0, partial: 0 };

for (const [file, keys] of byFile) {
    const excluded = files.includes(file) ? exclusions(file) : [];
    const whole = excluded.filter(e => e.kind === "test");
    const parts = excluded.filter(e => e.kind === "part");
    if (keys.length === 0 && excluded.length === 0) continue;
    const passes = mode => keys.filter(k => stateOf(mode, k) === "passed").length;
    const failed = keys.filter(k => stateOf("inline", k) !== "passed" || stateOf("promise", k) !== "passed");
    totals.run += keys.length;
    totals.inline += passes("inline");
    totals.promise += passes("promise");
    totals.excluded += whole.length;
    totals.partial += parts.length;
    summary.push(`| ${file} | ${keys.length} | ${passes("inline")} | ${passes("promise")} | ${whole.length} | ${parts.length} |`);

    const out = [`## ${file}`, ""];
    if (failed.length > 0) {
        out.push("| Failed | Inline | Promise | Difference |", "| --- | --- | --- | --- |");
        for (const key of failed) {
            const inline = stateOf("inline", key);
            const title = key.split(" / ").at(-1);
            let why = inline === "passed" ? "Async results arrive on a later microtask" : explained.get(title);
            if (!why) {
                why = "**untriaged**";
                untriaged.push(`${key}: ${modes.inline.get(key)?.message ?? inline}`);
            }
            out.push(`| ${escape(nameOf(key))} | ${inline} | ${stateOf("promise", key)} | ${why} |`);
        }
        out.push("");
    }
    if (excluded.length > 0) {
        out.push("| Excluded | Reason |", "| --- | --- |");
        for (const e of excluded)
            out.push(`| ${escape(e.kind === "part" ? `part of: ${e.name}` : e.name)} | ${escape(e.reason)} |`);
        out.push("");
    }
    const passed = keys.filter(k => !failed.includes(k));
    if (passed.length > 0) {
        out.push(`<details><summary>${passed.length} passed under both deliveries</summary>`, "");
        for (const key of passed) out.push(`- ${nameOf(key)}`);
        out.push("", "</details>", "");
    }
    sections.push(out.join("\n"));
}

writeFileSync(output, [
    "# Fable compatibility",
    "",
    "Generated by `dotnet fsi build.fsx test-fable` (`fable/Ranvier.Tests.Fable/Report.mjs`); do not edit by hand.",
    "",
    "The .NET suite in `tests/Ranvier.Tests`, compiled with Fable and run under Node.js with Mocha, in two deliveries:",
    "",
    "- **inline**: a test's `TaskCompletionSource` settles its awaiters before `SetResult` returns, as on .NET.",
    "  A failure here is a difference in behaviour.",
    "- **promise**: a test's `TaskCompletionSource` is a promise, as in an application. A test that fails only here",
    "  observes microtask delivery.",
    "",
    "Each difference named below is described in [the Fable guide](../content/fable/index.md).",
    "Excluded tests exercise a .NET-only facility and are compiled out with `#if !FABLE_COMPILER`.",
    "",
    "| File | Run | Inline passed | Promise passed | Excluded | Partly excluded |",
    "| --- | --- | --- | --- | --- | --- |",
    ...summary,
    `| **Total** | ${totals.run} | ${totals.inline} | ${totals.promise} | ${totals.excluded} | ${totals.partial} |`,
    "",
    ...sections,
].join("\n"));

console.log(`inline ${totals.inline}/${totals.run}, promise ${totals.promise}/${totals.run}, excluded ${totals.excluded}; wrote ${output}`);
if (untriaged.length > 0) {
    console.error(`${untriaged.length} untriaged inline failure(s):\n  ${untriaged.join("\n  ")}`);
    process.exit(1);
}
