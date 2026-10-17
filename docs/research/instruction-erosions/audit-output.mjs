import { build } from '../../../examples/Fable.Ranvier.Playground/node_modules/vite/dist/node/index.js';
import { mkdtempSync, readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve, join } from 'node:path';
import { gzipSync } from 'node:zlib';
import { pathToFileURL } from 'node:url';

// Usage: node audit-output.mjs before-output after-output result.json
const [before, after, output] = process.argv.slice(2);
const work = mkdtempSync(join(tmpdir(), 'ranvier-output-audit-'));
const entries = {
  signals: ['Api_createSignal', 'Api_createMemo', 'Api_createEffect', 'Api_batch', 'Api_createRoot'],
  collections: ['Api_createKeyedCollection', 'Api_createProjection', 'Api_createSelector'],
  async: ['Api_createAsync', 'Api_createAsyncSource', 'Api_createBoundary'],
};
const results = [];
for (const [label, directory] of [['before', before], ['after', after]]) {
  const base = resolve(directory, 'src/Ranvier');
  const modules = Object.fromEntries(readdirSync(base).filter(f => f.endsWith('.js')).map(f => [f, readFileSync(join(base, f)).length]));
  for (const [scenario, names] of Object.entries(entries)) {
    const entry = join(work, `${label}-${scenario}.mjs`);
    writeFileSync(entry, `export { ${names.join(', ')} } from ${JSON.stringify(join(base, 'Api.fs.js').replaceAll('\\', '/'))};\nexport { Graph } from ${JSON.stringify(join(base, 'Core.fs.js').replaceAll('\\', '/'))};\n`);
    const built = await build({
      configFile: false, logLevel: 'error',
      build: { write: false, minify: true, target: 'es2022', lib: { entry, formats: ['es'] } },
    });
    const code = built[0].output.filter(f => f.type === 'chunk').map(f => f.code).join('\n');
    results.push({ label, scenario, bytes: Buffer.byteLength(code), gzipBytes: gzipSync(code).length, modules });
  }
  const core = await import(pathToFileURL(join(base, 'Core.fs.js')));
  const types = await import(pathToFileURL(join(base, 'Types.fs.js')));
  for (const affinity of [types.ThreadAffinity.Guarded, types.ThreadAffinity.Unchecked, types.ThreadAffinity.Serialised]) {
    const options = types.GraphOptions__WithThreadAffinity_75EF8D52(types.GraphOptions_get_Default(), affinity);
    const graph = new core.Graph(options);
    let dispatched = 0;
    // Export names include overload hashes; resolve the public member by its prefix.
    core[Object.keys(core).find(k => k.startsWith('Graph__Dispatch_'))](graph, () => dispatched++);
    if (dispatched !== 1 || core.Graph__get_PendingWork(graph) !== 0 || core.Graph__Pump(graph) !== 0 || !core.Graph__get_IsOnGraphThread(graph)) throw new Error(`${label}: dispatch contract`);
    core.Graph__Dispose(graph);
  }
}
writeFileSync(output, JSON.stringify({ node: process.version, target: 'es2022', work, results }, null, 2));
for (const scenario of Object.keys(entries)) {
  const b = results.find(r => r.label === 'before' && r.scenario === scenario);
  const a = results.find(r => r.label === 'after' && r.scenario === scenario);
  console.log(JSON.stringify({ scenario, before: b.bytes, after: a.bytes, saved: b.bytes-a.bytes, gzipSaved: b.gzipBytes-a.gzipBytes }));
}
