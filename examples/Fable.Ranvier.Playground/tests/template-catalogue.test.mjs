import test, { after } from "node:test";
import { JSDOM } from "jsdom";
import { cases } from "../generated-tests/TemplateCatalogueChecks.js";

const dom = new JSDOM("<!doctype html><body></body>");
globalThis.window = dom.window;
globalThis.document = dom.window.document;
globalThis.FocusEvent = dom.window.FocusEvent;
after(() => { dom.window.close(); delete globalThis.window; delete globalThis.document; delete globalThis.FocusEvent; });
for (const [name, run] of cases) test(name, run);
