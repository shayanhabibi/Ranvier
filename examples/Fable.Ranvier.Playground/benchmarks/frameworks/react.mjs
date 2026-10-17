import { createElement as h, useState, useMemo } from "react";
import { createRoot } from "react-dom/client";
import { flushSync } from "react-dom";

function Row({ index, source, disabled }) {
  const [local, setLocal] = useState(0);
  return h("div", { id: `row-${index}`, className: "panel", title: source },
    h("input", { value: source, onChange() {} }),
    h("button", { disabled, onClick: () => setLocal(value => value + 1) }, String(local)));
}

export function mount(host, width) {
  let setSource;
  function App() {
    const [source, set] = useState("a");
    setSource = set;
    const disabled = useMemo(() => source === "blocked", [source]);
    return h("section", null, Array.from({ length: width }, (_, i) =>
      h(Row, { key: i + 1, index: i + 1, source, disabled })));
  }
  const root = createRoot(host);
  flushSync(() => root.render(h(App)));
  let live = true;
  return {
    update: value => flushSync(() => setSource(value)),
    flush: () => flushSync(() => {}),
    dispose() { if (live) { live = false; root.unmount(); } }
  };
}
