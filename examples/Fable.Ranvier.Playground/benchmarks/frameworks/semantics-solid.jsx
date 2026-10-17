import { createSignal, onCleanup, flush } from "solid-js";
import { render } from "@solidjs/web";

// Mounts an input whose value is bound to a signal written by its own input handler, and returns its disposal.
// Solid 2 batches writes, so the input handler flushes before the probe reads the input.
export function caret(host) {
  const [source, setSource] = createSignal("");
  const dispose = render(() => <input value={source()} onInput={event => { setSource(event.currentTarget.value); flush(); }} />, host);
  flush();
  return dispose;
}

// Mounts four rows whose third row fails during mount, by its property setter or by its title reader.
export function failure(host, kind) {
  const [source, setSource] = createSignal("a");
  let cleaned = 0, reads = 0, threw = false, message = "", dispose;
  const title = index => {
    reads++;
    if (kind === "reader" && index === 3) throw Error("reader");
    return source();
  };
  const Row = props => {
    onCleanup(() => cleaned++);
    return <div title={title(props.index)} prop:explode={kind === "setter" && props.index === 3 ? "boom" : "ok"} />;
  };
  try {
    dispose = render(() => <section>{[1, 2, 3, 4].map(index => <Row index={index} />)}</section>, host);
    flush();
  } catch (error) {
    threw = true;
    message = String(error && error.message);
  }
  return {
    threw, message,
    update() { const before = reads; setSource("b"); flush(); return reads - before; },
    dispose() { dispose?.(); },
    cleaned: () => cleaned
  };
}
