import { mount as ranvierMount, mountPrepared, mountTemplate } from "../../generated-tests/FrameworkPerf.js";
import { parts, scopes, core, coreOwners, mountVariant, mountDirect, mountReactiveNative } from "../../generated-tests/FrameworkDiagnostics.js";
import { mount as rippleMount } from "../../generated-ripple/RipplePerf.js";
import { mount as solidMount } from "./solid.jsx";
import { mount as reactMount } from "./react.mjs";
import { verify, expectState } from "./contract.mjs";
import { allCases as rangeCases } from "../../generated-tests/RangeChecks.js";
import { mountNative } from "./native.mjs";
import * as ranvierSemantics from "../../generated-tests/FrameworkSemantics.js";
import * as solidSemantics from "./semantics-solid.jsx";
import * as rippleSemantics from "../../generated-ripple/RippleSemantics.js";

const adapt = (mount, id = false) => (host, width) => {
  const [update, dispose] = mount(id ? host.id : host, width);
  return { update, dispose, flush() {} };
};
const adapters = {
  ranvier: adapt(ranvierMount), ripple: adapt(rippleMount, true), solid: solidMount, react: reactMount,
  "ranvier-prepared": adapt(mountPrepared),
  "ranvier-template": adapt(mountTemplate),
  "ranvier-batched": adapt((host, width) => mountVariant(host, width, true)),
  "ranvier-direct": adapt(mountDirect),
  "reactive-native": adapt(mountReactiveNative),
  "ranvier-unbatched": adapt((host, width) => mountVariant(host, width, false)),
  "native-create": (host, width) => mountNative(host, width),
  "native-clone": (host, width) => mountNative(host, width, true),
  "native-descendants": (host, width) => mountNative(host, width, false, true)
};
let hostIndex = 0;
function host() {
  const node = document.createElement("main");
  node.id = `framework-host-${++hostIndex}`;
  document.body.append(node);
  return node;
}

// Probes take the host element, except Ripple, which mounts by host ID.
window.frameworkSemantics = {
  ranvier: { caret: host => ranvierSemantics.caret(host), failure: (host, kind) => ranvierSemantics.failure(host, kind) },
  solid: { caret: host => solidSemantics.caret(host), failure: (host, kind) => solidSemantics.failure(host, kind) },
  ripple: { caret: host => rippleSemantics.caret(host.id), failure: (host, kind) => rippleSemantics.failure(host.id, kind) },
  host
};

window.frameworkPerf = {
  parts, scopes, core, coreOwners,
  rangeProofs() {
    const passed = [];
    for (const [name, run] of rangeCases) { run(); passed.push(name); }
    return passed;
  },
  prepare(mode, width, phase, count) {
    const hosts = Array.from({ length: count }, host);
    const instances = phase === "dispose" ? hosts.map(node => adapters[mode](node, width)) : [];
    return {
      run() {
        if (phase === "mount") for (const node of hosts) instances.push(adapters[mode](node, width));
        else for (const instance of instances) instance.dispose();
      },
      cleanup() {
        for (const instance of instances) instance.dispose();
        for (const node of hosts) node.remove();
        instances.length = 0; hosts.length = 0;
      }
    };
  },
  operations(mode, width, phase) {
    const prepared = this.prepare(mode, width, phase, 1);
    const counts = {}, undo = [];
    const wrap = (proto, name, key = name) => {
      const original = proto[name];
      proto[name] = function (...args) {
        counts[key] = (counts[key] || 0) + 1;
        return original.apply(this, args);
      };
      undo.push(() => proto[name] = original);
    };
    wrap(Document.prototype, "createElement");
    wrap(Document.prototype, "createTextNode");
    wrap(Document.prototype, "createComment");
    wrap(Document.prototype, "createDocumentFragment");
    wrap(Node.prototype, "appendChild");
    wrap(Node.prototype, "insertBefore");
    wrap(Node.prototype, "removeChild");
    wrap(Node.prototype, "cloneNode");
    wrap(Element.prototype, "remove");
    wrap(CharacterData.prototype, "remove");
    wrap(WeakSet.prototype, "add", "claim");
    wrap(WeakSet.prototype, "delete", "unclaim");
    wrap(EventTarget.prototype, "addEventListener");
    wrap(EventTarget.prototype, "removeEventListener");
    try { prepared.run(); return counts; }
    finally {
      for (const restore of undo.reverse()) restore();
      prepared.cleanup();
    }
  },
  probe(probe, width, phase, count) {
    const prepared = this.prepare("ranvier", width, phase, count);
    const undo = [];
    if (probe === "root-removal-only" || probe === "detach-first" || probe === "remove-disabled") {
      for (const proto of [Element.prototype, CharacterData.prototype]) {
        const original = proto.remove;
        proto.remove = function () {
          if (probe === "remove-disabled") return;
          if (probe === "detach-first") {
            let top = this;
            while (top.parentNode && top.parentNode.nodeName !== "MAIN") top = top.parentNode;
            if (top !== this && top.parentNode?.nodeName === "MAIN") Element.prototype.__diagnosticOriginalRemove.call(top);
            return original.call(this);
          }
          if (this.parentNode?.nodeName === "MAIN") return original.call(this);
        };
        undo.push(() => proto.remove = original);
        if (proto === Element.prototype) {
          proto.__diagnosticOriginalRemove = original;
          undo.push(() => delete proto.__diagnosticOriginalRemove);
        }
      }
    } else if (probe === "claims-disabled") {
      const add = WeakSet.prototype.add, remove = WeakSet.prototype.delete, has = WeakSet.prototype.has;
      WeakSet.prototype.add = function () { return this; };
      WeakSet.prototype.delete = () => false;
      WeakSet.prototype.has = () => false;
      undo.push(() => { WeakSet.prototype.add = add; WeakSet.prototype.delete = remove; WeakSet.prototype.has = has; });
    } else if (probe === "unclaim-disabled") {
      const remove = WeakSet.prototype.delete;
      WeakSet.prototype.delete = () => false;
      undo.push(() => WeakSet.prototype.delete = remove);
    } else if (probe !== "baseline") throw Error(probe);
    try {
      const start = performance.now();
      prepared.run();
      return (performance.now() - start) / count;
    } finally {
      for (const restore of undo.reverse()) restore();
      prepared.cleanup();
    }
  },
  verify(mode, width) {
    try { return verify(adapters[mode], width); }
    catch (error) { throw Error(`${mode}, width ${width}: ${error.message}`); }
  },
  sample(mode, width, phase, iterations) {
    const mount = adapters[mode];
    const hosts = [], instances = [];
    try {
      const count = phase === "update" ? 1 : iterations;
      for (let i = 0; i < count; i++) hosts.push(host());
      if (phase !== "mount") for (const node of hosts) instances.push(mount(node, width));
      const start = performance.now();
      if (phase === "mount") {
        for (const node of hosts) instances.push(mount(node, width));
      } else if (phase === "update") {
        for (let i = 1; i <= iterations; i++) instances[0].update(i % 2 ? "blocked" : "a");
      } else if (phase === "dispose") {
        for (const instance of instances) instance.dispose();
      } else throw Error(`Unknown phase ${phase}`);
      const ms = (performance.now() - start) / iterations;
      if (phase === "update") expectState(hosts[0], width, iterations % 2 ? "blocked" : "a");
      else if (phase === "mount") for (const node of hosts) expectState(node, width, "a");
      else if (hosts.some(node => node.childNodes.length)) throw Error("Disposal retained DOM");
      return ms;
    } finally {
      for (const instance of instances) instance.dispose();
      for (const node of hosts) node.remove();
    }
  },
  retain(mode, width, count) {
    const hosts = Array.from({ length: count }, host);
    const instances = hosts.map(node => adapters[mode](node, width));
    return () => {
      for (const instance of instances) instance.dispose();
      for (const node of hosts) node.remove();
      hosts.length = 0; instances.length = 0;
    };
  }
};
