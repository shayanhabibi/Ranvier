import { defineConfig } from "vite";
import { transformAsync } from "@babel/core";
import { transform as solidTransform } from "@solidjs/compiler";
import { fileURLToPath } from "node:url";
import sharedBackendProbe from "./shared-backend-probe.mjs";

const headers = {
  "Cross-Origin-Opener-Policy": "same-origin",
  "Cross-Origin-Embedder-Policy": "require-corp"
};
// Ladder rungs replace the compiled test adapters with a rung's gated Fable output.
const ladderTests = process.env.RANVIER_LADDER_TESTS;
export default defineConfig({
  resolve: ladderTests ? { alias: [{ find: /^\.\.\/\.\.\/generated-tests\//, replacement: fileURLToPath(new URL(`../../${ladderTests}/`, import.meta.url)) }] } : {},
  plugins: [{
    name: "benchmark-solid-jsx", enforce: "pre",
    async transform(code, id) {
      if (process.env.RANVIER_SHARED_BACKEND === "true" && id.endsWith("/Ranvier.View.Foundation/Mounting.js")) {
        return await transformAsync(code, {
          filename: id, babelrc: false, configFile: false, plugins: [sharedBackendProbe]
        });
      }
      if (!/\/frameworks\/(semantics-)?solid\.jsx$/.test(id)) return;
      // Solid 2's native compiler, released with the runtime it targets.
      const { code: compiled, map } = solidTransform(code, { filename: id, generate: "dom", hydratable: false, sourceMap: true });
      return { code: compiled, map };
    }
  }],
  build: {
    minify: process.env.RANVIER_PERF_PROFILE === "true" ? false : true,
    sourcemap: true,
    outDir: process.env.RANVIER_PERF_OUTPUT || (process.env.RANVIER_SHARED_BACKEND === "true" ? "generated-framework-shared" : "generated-framework-dist"), emptyOutDir: true,
    rollupOptions: { input: fileURLToPath(new URL("index.html", import.meta.url)) }
  },
  server: { headers }, preview: { headers }
});
