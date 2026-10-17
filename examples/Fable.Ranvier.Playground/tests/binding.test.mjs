import test, { after } from "node:test";
import assert from "node:assert/strict";
import { JSDOM } from "jsdom";
import { cases, microtaskFixture } from "../generated-tests/BindingChecks.js";
import { cases as integrationCases } from "../generated-tests/BindingIntegrationChecks.js";
import { cases as generatedCases } from "../generated-tests/GeneratedChecks.js";
import { cases as attributeCases } from "../generated-tests/AttributeChecks.js";
const dom = new JSDOM("<!doctype html><body></body>");
globalThis.document = dom.window.document;
after(() => { dom.window.close(); delete globalThis.document; });
for (const [name, check] of cases) test(name, () => check());
for (const [name, check] of integrationCases) test(name, () => check());
for (const [name, check] of generatedCases) test(name, () => check());
for (const [name, check] of attributeCases) test(name, () => check());
test("microtasks coalesce writes and cancel disposed work", async () => {
  const fixture = microtaskFixture();
  try {
    assert.deepEqual(fixture.Values(), []);
    fixture.Update(2);
    fixture.Update(3);
    await Promise.resolve();
    assert.deepEqual(fixture.Values(), [3]);
    fixture.Update(4);
    fixture.Dispose();
    await Promise.resolve();
    assert.deepEqual(fixture.Values(), [3]);
  } finally { fixture.Dispose(); }
});
