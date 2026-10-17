import { test } from "node:test";
import assert from "node:assert/strict";
import { JSDOM } from "jsdom";
import { verify } from "./contract.mjs";

test("benchmark rejects a mount that never creates the workload", () => {
  const dom = new JSDOM("<!doctype html>");
  globalThis.document = dom.window.document;
  try {
    assert.throws(() => verify(() => ({ dispose() {} }), 1), /DOM contract failed/);
  } finally { delete globalThis.document; dom.window.close(); }
});
