import { spawnSync } from "node:child_process";
import { createRequire } from "node:module";
import { cpus, availableParallelism } from "node:os";
import { dirname, join, resolve } from "node:path";
import { mkdir, readFile, writeFile } from "node:fs/promises";

const require = createRequire(import.meta.url);
const cli = join(dirname(require.resolve("@playwright/test/package.json")), "cli.js");
const directory = resolve("test-results/compiler-diagnostic");
await mkdir(directory, { recursive: true });
const host = {
  source: process.env.GITHUB_SHA ?? "local",
  run: process.env.GITHUB_RUN_ID ?? "local",
  logicalCpus: cpus().length,
  availableParallelism: availableParallelism(),
  cpuModels: [...new Set(cpus().map(cpu => cpu.model))],
  cpuQuota: await readFile("/sys/fs/cgroup/cpu.max", "utf8").catch(() => "unavailable"),
  node: process.version,
};
await writeFile(join(directory, "host.json"), JSON.stringify(host, null, 2));
console.log("DOCS_COMPILER_DIAGNOSTIC_HOST", JSON.stringify(host));
let failed = false;
const summaries = [];
for (const workers of [2, 1]) {
  const args = [cli, "test", "--workers", String(workers), "--grep", "the live editor compiles|the editable example compiles", "--reporter=json", "--output", join(directory, `workers-${workers}`)];
  const result = spawnSync(process.execPath, args, {
    env: { ...process.env, DOCS_COMPILER_DIAGNOSTIC: "1" },
    encoding: "utf8",
    maxBuffer: 20 * 1024 * 1024,
  });
  await writeFile(join(directory, `workers-${workers}-stdout.log`), result.stdout ?? "");
  await writeFile(join(directory, `workers-${workers}-stderr.log`), result.stderr ?? "");
  let report;
  try { report = JSON.parse(result.stdout); } catch {}
  const stats = report?.stats;
  const counts = ["expected", "unexpected", "flaky", "skipped"].map(name => stats?.[name]);
  const validStats = counts.every(value => Number.isInteger(value) && value >= 0);
  const total = validStats ? counts.reduce((sum, value) => sum + value, 0) : 0;
  const actualWorkers = report?.config?.workers;
  const summary = { workers, actualWorkers, exitCode: result.status, signal: result.signal, error: result.error?.message, stats, total };
  summaries.push(summary);
  console.log("DOCS_COMPILER_DIAGNOSTIC_RESULT", JSON.stringify(summary));
  if (result.status !== 0 || total !== 2 || actualWorkers !== workers || stats?.expected !== 2 || stats?.skipped !== 0 || stats?.flaky !== 0) failed = true;
}
await writeFile(join(directory, "summary.json"), JSON.stringify(summaries, null, 2));
if (failed) process.exitCode = 1;
