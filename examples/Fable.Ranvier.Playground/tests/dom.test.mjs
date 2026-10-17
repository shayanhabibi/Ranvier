import test, { after } from "node:test";
import { JSDOM } from "jsdom";

const { cases } = await import("../generated-tests/DomChecks.js");
const dom = new JSDOM("<!doctype html><body></body>");
globalThis.window = dom.window;
globalThis.document = dom.window.document;
after(() => {
  dom.window.close();
  delete globalThis.window;
  delete globalThis.document;
});

for (const [name, check] of cases) {
  test(name, () => check());
}
