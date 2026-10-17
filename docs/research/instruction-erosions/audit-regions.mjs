import { pathToFileURL } from 'node:url';

const [bundle, scenario = 'create', scaleText = '1', warmupText = '10'] = process.argv.slice(2);
const { all } = await import(pathToFileURL(`${bundle}/Scenarios.fs.js`));
const cases = Array.from(all(Number(scaleText))).filter(c => c.Scenario === scenario);
for (const c of cases) {
  for (let i = 0; i < Number(warmupText); i++) {
    const p = c.Prepare(c.Ops * 2);
    p.Run();
    p.Teardown();
  }
  for (const n of [c.Ops, c.Ops * 2]) {
    const p = c.Prepare(n);
    global.gc();
    global.gc();
    console.log(`MEASURE-BEGIN ${c.Engine} ${scenario} ${n}`);
    const start = process.hrtime.bigint();
    p.Run();
    const elapsed = Number(process.hrtime.bigint() - start);
    console.log(`MEASURE-END ${c.Engine} ${scenario} ${n} ns=${elapsed}`);
    p.Teardown();
  }
}
