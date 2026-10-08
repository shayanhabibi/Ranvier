import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { writeFile } from 'node:fs/promises';
import { setTimeout } from 'node:timers/promises';
const require = createRequire(new URL('../../../examples/Fable.Ranvier.Playground/package.json', import.meta.url));
const { chromium } = require('@playwright/test');
const vite = fileURLToPath(new URL('../../../examples/Fable.Ranvier.Playground/node_modules/vite/bin/vite.js', import.meta.url));
try {
  await fetch('http://127.0.0.1:5181');
  throw new Error('Port 5181 occupied');
} catch (error) { if (!error.cause) throw error; }
const server = spawn(process.execPath, [vite, 'preview', '--host', '127.0.0.1', '--port', '5181', '--strictPort'], { cwd: fileURLToPath(new URL('.', import.meta.url)), windowsHide: true, stdio: 'ignore' });
let browser;
try {
  let ready = false;
  for (let i = 0; i < 100; i++) {
    if (server.exitCode !== null) throw new Error('Preview exited');
    try { ready = (await fetch('http://127.0.0.1:5181')).ok; } catch {}
    if (ready) break;
    await setTimeout(100);
  }
  if (!ready) throw new Error('Preview not ready');
  browser = await chromium.launch();
  const page = await browser.newPage();
  await page.goto('http://127.0.0.1:5181');
  await page.waitForFunction(() => window.comparison);
  const result = { browser: browser.version(), date: new Date().toISOString(), cycles: 10000, warmups: 5, rounds: 25, samples: { ranvier: [], ripple: [] } };
  for (let round = -5; round < 25; round++) {
    for (const library of round % 2 === 0 ? ['ranvier','ripple'] : ['ripple','ranvier']) {
      const measured = await page.evaluate(({ library, cycles }) => {
        let values = 0, runs = 0;
        const start = performance.now();
        for (let i = 0; i < cycles; i++) {
          const model = window.comparison[library].createGraph();
          model.Set(1);
          values += model.Value();
          runs += model.Runs();
          model.Dispose();
        }
        return { elapsed: performance.now() - start, values, runs };
      }, { library, cycles: result.cycles });
      if (measured.values !== result.cycles * 4 || measured.runs !== result.cycles * 2) throw new Error(JSON.stringify({ library, measured }));
      if (round >= 0) result.samples[library].push(measured.elapsed);
    }
  }
  await writeFile(new URL('core-lifetimes.json', import.meta.url), JSON.stringify(result, null, 2));
  console.log(JSON.stringify(Object.fromEntries(Object.entries(result.samples).map(([library, samples]) => {
    const sorted = [...samples].sort((a,b) => a-b);
    return [library, { medianMs: sorted[12], p95Ms: sorted[23] }];
  })), null, 2));
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null) await new Promise(resolve => { server.once('exit', resolve); server.kill(); });
}
