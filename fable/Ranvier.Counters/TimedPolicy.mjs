import assert from "node:assert/strict";
import { performance } from "node:perf_hooks";

function burst(inputs, rearm, step) {
  let now = 0, deadline = 0, due = Infinity, candidate = 0, published = 0;
  let arms = 0, callbacks = 0, admissions = 0;
  const arm = delay => { arms++; due = now + delay; };
  const advance = amount => {
    const target = now + amount;
    while (due <= target) {
      now = due;
      due = Infinity;
      callbacks++;
      if (now >= deadline) { published = candidate; admissions++; }
      else arm(deadline - now);
    }
    now = target;
  };
  for (let i = 1; i <= inputs; i++) {
    candidate = i;
    deadline = now + 100;
    if (rearm || due === Infinity) arm(100);
    advance(step);
  }
  advance(100);
  assert.equal(published, candidate);
  return { arms, callbacks, posts: callbacks, admissions, published };
}

const rows = [];
for (const step of [1, 150]) {
  for (const inputs of [1, 64, 4096]) {
    const reference = burst(inputs, true, step);
    const lazy = burst(inputs, false, step);
    assert.equal(lazy.published, reference.published);
    assert.equal(lazy.admissions, reference.admissions);
    for (const rearm of [true, false]) {
      for (let i = 0; i < 1000; i++) burst(inputs, rearm, step);
      const repeats = Math.max(100, Math.floor(100000 / inputs));
      const samples = [];
      for (let sample = 0; sample < 5; sample++) {
        const start = performance.now();
        for (let i = 0; i < repeats; i++) burst(inputs, rearm, step);
        samples.push((performance.now() - start) * 1e6 / repeats);
      }
      samples.sort((a, b) => a - b);
      rows.push({ inputs, step, policy: rearm ? "rearm" : "lazy", ...burst(inputs, rearm, step), medianNanoseconds: samples[2] });
    }
  }
}
console.log(JSON.stringify({ runtime: process.version, clock: "simulated monotonic milliseconds", backend: "counting one-shot slot", rows }, null, 2));
