import { writeFile } from 'node:fs/promises';
import { gzipSync } from 'node:zlib';
import { build } from '../../../examples/Fable.Ranvier.Playground/node_modules/vite/dist/node/index.js';
const results = [];
for (const library of ['ranvier', 'ripple']) {
  for (const symbol of ['all', 'createGraph', 'createDashboard', 'createAsyncView']) {
    const module = library === 'ranvier' ? 'RanvierCase' : 'RippleCase';
    const entry = `size-${library}-${symbol}.js`;
    await writeFile(entry, symbol === 'all' ? `export * from './generated/${module}.js';\n` : `export { ${symbol} } from './generated/${module}.js';\n`);
    let modules;
    const output = await build({ configFile: false, logLevel: 'silent', plugins: [{ name: 'inspect-modules', generateBundle(_, bundle) { modules = Object.values(bundle).filter(x => x.type === 'chunk').flatMap(x => Object.entries(x.modules).map(([id, details]) => ({ id, renderedLength: details.renderedLength, originalLength: details.originalLength }))); } }], build: { write: false, lib: { entry, formats: ['es'], fileName: 'fixture' }, minify: true } });
    const code = (Array.isArray(output) ? output : [output]).flatMap(x => x.output).filter(x => x.type === 'chunk').map(x => x.code).join('\n');
    results.push({ library, symbol, minifiedBytes: Buffer.byteLength(code), gzipLevel9Bytes: gzipSync(code, { level: 9 }).length, modules });
  }
}
await writeFile('size-inspection.json', JSON.stringify(results, null, 2));
console.log(JSON.stringify(results.map(({ modules, ...rest }) => rest), null, 2));
