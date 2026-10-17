import test, { after } from "node:test";
import { JSDOM } from "jsdom";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { cases } from "../generated-tests/ComponentChecks.js";
import { cases as authoringCases } from "../generated-tests/AuthoringChecks.js";
const dom = new JSDOM("<!doctype html><body></body>");
globalThis.window = dom.window;
globalThis.document = dom.window.document;
after(() => {
  dom.window.close();
  delete globalThis.window;
  delete globalThis.document;
});
for (const [name, check] of cases) test(name, () => check());
for (const [name, check] of authoringCases) test(name, () => check());
test("authoring helpers inline into the existing operations", () => {
  const authoring = readFileSync(new URL("../generated-tests/experiments/Ranvier.View.Foundation/Authoring.js", import.meta.url), "utf8");
  const html = readFileSync(new URL("../generated-tests/experiments/Ranvier.View.Foundation/Generated/Html.js", import.meta.url), "utf8");
  assert.doesNotMatch(authoring, /export function /);
  assert.doesNotMatch(html, /export function Program_/);
  const checks = readFileSync(new URL("../generated-tests/AuthoringChecks.js", import.meta.url), "utf8");
  assert.doesNotMatch(checks, /\bAuthoring_(?:Attrs|Props)/);
});
