import { pathToFileURL } from 'node:url';
import { writeFileSync } from 'node:fs';
import inspector from 'node:inspector';

const [bundle, scenario, output, scaleText = '1', profileText = '0'] = process.argv.slice(2);
const { all } = await import(pathToFileURL(`${bundle}/Scenarios.fs.js`));
const cases = Array.from(all(Number(scaleText))).filter(c => c.Scenario === scenario);
const samples = [];
for (const c of cases) {
  for (let i = 0; i < 40; i++) {
    const p = c.Prepare(c.Ops * 2);
    p.Run();
    p.Teardown();
  }
}
for (let round = 0; round < 21; round++) {
  for (const c of (round % 2 ? [...cases].reverse() : cases)) {
    const p = c.Prepare(c.Ops * 4);
    global.gc();
    global.gc();
    const start = process.hrtime.bigint();
    p.Run();
    const elapsed = Number(process.hrtime.bigint() - start);
    p.Teardown();
    samples.push({ round, engine: c.Engine, operations: c.Ops * 4, nsPerOp: elapsed / (c.Ops * 4) });
  }
}
let profile;
if (profileText === '1') {
  const c = cases.find(c => c.Engine === 'Ranvier');
  const session = new inspector.Session();
  session.connect();
  const post = (method, params = {}) => new Promise((resolve, reject) => session.post(method, params, (error, value) => error ? reject(error) : resolve(value)));
  await post('Profiler.enable');
  await post('Profiler.setSamplingInterval', { interval: 100 });
  await post('Profiler.start');
  for (let i = 0; i < 50; i++) {
    const p = c.Prepare(c.Ops * 2);
    p.Run();
    p.Teardown();
  }
  profile = (await post('Profiler.stop')).profile;
  session.disconnect();
}
const median = a => [...a].sort((a, b) => a - b)[Math.floor(a.length / 2)];
const summary = Object.fromEntries(cases.map(c => [c.Engine, median(samples.filter(s => s.engine === c.Engine).map(s => s.nsPerOp))]));
writeFileSync(output, JSON.stringify({ bundle, scenario, node: process.version, flags: process.execArgv, summary, samples, profile }, null, 2));
console.log(JSON.stringify({ scenario, summary, ratio: summary.Ranvier / summary['Fable.Ripple'] }));
