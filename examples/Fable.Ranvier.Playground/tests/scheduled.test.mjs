import test, { after } from "node:test";
import assert from "node:assert/strict";
import { JSDOM } from "jsdom";
import { create, createShared, createBatched } from "../generated-tests/ScheduledChecks.js";

const dom = new JSDOM("<!doctype html><body></body>");
globalThis.window = dom.window;
globalThis.document = dom.window.document;
after(() => { dom.window.close(); delete globalThis.window; delete globalThis.document; });
const value = fixture => fixture.Root.getAttribute("data-value");
const tick = async () => { await Promise.resolve(); await Promise.resolve(); };

test("mount created inside a batch renders when the batch settles", async () => {
  const f = createBatched();
  try {
    assert.equal(value(f), "0");
    assert.equal(f.Root.firstChild.data, "0");
    f.Set(1); assert.equal(value(f), "0");
    await tick(); assert.equal(value(f), "1");
  } finally { f.DisposeGraph(); }
});

test("microtask mount starts current and coalesces a burst", async () => {
  const f = create(false, false);
  try {
    assert.equal(value(f), "0");
    for (let i = 1; i <= 100; i++) f.Set(i);
    assert.equal(value(f), "0");
    assert.equal(f.Writes(), 1);
    await tick();
    assert.equal(value(f), "100");
    assert.equal(f.Writes(), 2);
    assert.equal(f.Root.firstChild.data, "100");
  } finally { f.DisposeGraph(); }
});

test("synchronous mode keeps immediate writes", () => {
  const f = create(true, false);
  try { f.Set(1); assert.equal(value(f), "1"); assert.equal(f.Writes(), 2); }
  finally { f.DisposeGraph(); }
});

test("explicit flush commits once and makes scheduled callback inert", async () => {
  const f = create(false, false);
  try {
    f.Set(1); f.Set(2); f.Flush();
    assert.equal(value(f), "2"); assert.equal(f.Writes(), 2);
    await tick(); assert.equal(f.Writes(), 2);
    f.Set(3); await tick(); assert.equal(value(f), "3");
  } finally { f.DisposeGraph(); }
});

test("disposing a mount cancels queued writes and removes listeners", async () => {
  const f = create(false, false);
  try {
    f.Set(1); f.Dispose(); f.Dispose(); f.Flush(); await tick();
    assert.equal(value(f), "0"); assert.equal(f.Writes(), 1);
    assert.equal(f.Host.childNodes.length, 0);
    f.Root.querySelector("button").click(); await tick(); assert.equal(f.Writes(), 1);
  } finally { f.DisposeGraph(); }
});

test("graph disposal cancels queued writes", async () => {
  const f = create(false, false);
  f.Set(1); f.DisposeGraph(); f.Flush(); await tick();
  assert.equal(value(f), "0"); assert.equal(f.Host.childNodes.length, 0);
});

for (const [mode, label] of [[1, "pending"], [2, "failed"]]) {
  test(`${label} reader invalidates an earlier queued value`, async () => {
    const f = create(false, false);
    try {
      f.Set(1); f.SetMode(mode); await tick();
      assert.equal(value(f), "0"); assert.equal(f.Root.firstChild.data, "0");
      assert.equal(f.Writes(), 1);
      if (mode === 1) { f.Resolve("ready"); await tick(); assert.equal(value(f), "ready"); }
      f.SetMode(0); f.Set(2); await tick(); assert.equal(value(f), "2");
    } finally { f.DisposeGraph(); }
  });
}

test("a deferred setter failure does not strand sibling bindings or later updates", async () => {
  const f = create(false, false);
  try {
    f.Set(13); await tick();
    assert.equal(value(f), "0"); assert.equal(f.Root.firstChild.data, "13");
    assert.deepEqual(f.Errors(), ["setter failed"]);
    f.Set(14); await tick(); assert.equal(value(f), "14");
    assert.deepEqual(f.Errors(), []);
  } finally { f.DisposeGraph(); }
});

test("optional attribute removal coalesces with later values", async () => {
  const f = create(false, false);
  try {
    f.Set(1); f.Set(-1); await tick(); assert.equal(f.Root.hasAttribute("title"), false);
    f.Set(-2); f.Set(2); await tick(); assert.equal(f.Root.title, "2");
  } finally { f.DisposeGraph(); }
});

test("separate mount queues flush and dispose independently", async () => {
  const a = create(false, false), b = create(false, false);
  try {
    a.Set(1); b.Set(2); a.Flush();
    assert.equal(value(a), "1"); assert.equal(value(b), "0");
    b.Dispose(); await tick(); assert.equal(value(b), "0");
  } finally { a.DisposeGraph(); b.DisposeGraph(); }
});

test("event batching reduces graph recomputations before the DOM commit", async () => {
  const f = create(false, true);
  try {
    f.Root.querySelector("button").click();
    assert.equal(f.Computes(), 2); assert.equal(value(f), "0");
    await tick(); assert.equal(value(f), "2"); assert.equal(f.Writes(), 2);
  } finally { f.DisposeGraph(); }
});

test("reentrant writes settle in another wave without recursive flush", async () => {
  const f = create(false, false);
  try {
    f.OnWrite(v => { if (v === "1") { f.Set(2); f.Flush(); } });
    f.Set(1); await tick(); await tick();
    assert.equal(value(f), "2"); assert.equal(f.Root.firstChild.data, "2");
    assert.equal(f.Writes(), 3);
  } finally { f.DisposeGraph(); }
});

test("setter disposing the mount stops the rest of the commit", async () => {
  const f = create(false, false);
  try {
    f.OnWrite(() => f.Dispose()); f.Set(1); await tick();
    assert.equal(f.Host.childNodes.length, 0);
    assert.equal(f.Root.firstChild.data, "0");
  } finally { f.DisposeGraph(); }
});

test("input roundtrip preserves focus and caret with queued updates", async () => {
  const f = create(false, true);
  try {
    document.body.appendChild(f.Host);
    const input = f.Root.querySelector("input");
    input.focus(); input.value = "AdXa"; input.setSelectionRange(3, 3);
    input.dispatchEvent(new window.Event("input", { bubbles: true }));
    await tick();
    assert.equal(document.activeElement, input);
    assert.equal(input.value, "AdXa"); assert.equal(input.selectionStart, 3);
    f.SetName("Grace"); await tick(); assert.equal(input.value, "Grace");
  } finally { f.DisposeGraph(); f.Host.remove(); }
});

test("mounts sharing one graph keep separate queues and owners", async () => {
  const [a, b] = createShared();
  try {
    a.Set(1); b.Set(2); a.Flush();
    assert.equal(value(a), "1"); assert.equal(value(b), "0");
    a.Dispose(); await tick(); assert.equal(value(b), "2");
    b.Set(3); await tick(); assert.equal(value(b), "3");
  } finally { a.DisposeGraph(); }
});

test("deferred setter reads stay untracked and see current state", async () => {
  const f = create(false, false);
  let seen;
  try {
    f.OnWrite(() => { seen = f.Other(); });
    f.Set(1); f.SetOther(3); await tick(); assert.equal(seen, 3);
    f.SetOther(4); await tick(); assert.equal(f.Writes(), 2);
    f.Set(2); await tick(); assert.equal(seen, 4);
  } finally { f.DisposeGraph(); }
});

test("resources registered by a queued setter keep per-run ownership", async () => {
  const f = create(false, false);
  try {
    f.OnWrite(() => f.RegisterCleanup());
    f.Set(1); await tick(); assert.equal(f.Cleanups(), 0);
    f.Set(2); assert.equal(f.Cleanups(), 1);
    await tick(); f.Dispose(); assert.equal(f.Cleanups(), 2);
  } finally { f.DisposeGraph(); }
});

test("explicit DOM flush settles queued graph work inside a batch", () => {
  const f = create(false, false);
  try {
    f.Batch(() => { f.Set(1); f.Set(2); f.Flush(); assert.equal(value(f), "2"); });
    assert.equal(f.Writes(), 2);
  } finally { f.DisposeGraph(); }
});
