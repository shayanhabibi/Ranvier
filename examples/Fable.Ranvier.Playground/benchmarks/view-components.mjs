import { spawnSync } from 'node:child_process';
import { definitions } from '../generated-tests/experiments/Ranvier.View.Foundation/ComponentBench.js';

if (process.argv[2] === 'sample') {
  const [, , , representation, helper, reactive] = process.argv;
  const make = count => definitions(representation, helper === 'true', reactive === 'true', count);
  make(10000);
  const samples = [];
  for (let sample = 0; sample < 7; sample++) {
    global.gc();
    const before = process.memoryUsage().heapUsed;
    let values = make(100000);
    global.gc();
    samples.push((process.memoryUsage().heapUsed - before) / values.length);
    if (values.length !== 100000) throw new Error('Incomplete workload');
    values = null;
  }
  console.log(JSON.stringify(samples));
} else {
  const results = [];
  for (const representation of ['tree', 'program'])
    for (const reactive of [false, true])
      for (const helper of [false, true])
        for (let run = 0; run < 3; run++) {
          const result = spawnSync(process.execPath, ['--expose-gc', import.meta.filename, 'sample', representation, String(helper), String(reactive)], { encoding: 'utf8' });
          if (result.status !== 0) throw new Error(result.stderr || result.stdout);
          results.push({ representation, reactive, helper, run, bytesPerDefinition: JSON.parse(result.stdout) });
        }
  console.log(JSON.stringify({ node: process.version, v8: process.versions.v8, count: 100000, results }, null, 2));
}
