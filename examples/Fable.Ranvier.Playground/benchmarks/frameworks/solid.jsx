import { createSignal, createMemo, flush } from "solid-js";
import { render } from "@solidjs/web";

function Row(props) {
  const [local, setLocal] = createSignal(0);
  return <div id={`row-${props.index}`} class="panel" title={props.source()}>
    <input value={props.source()} />
    <button disabled={props.disabled()} onClick={() => setLocal(local() + 1)}>{local()}</button>
  </div>;
}

// Solid 2 batches writes; each operation flushes so its DOM work is inside the timed window.
export function mount(host, width) {
  let setSource;
  const dispose = render(() => {
    const [source, set] = createSignal("a");
    setSource = set;
    const disabled = createMemo(() => source() === "blocked");
    return <section>{Array.from({ length: width }, (_, i) =>
      <Row index={i + 1} source={source} disabled={disabled} />)}</section>;
  }, host);
  flush();
  let live = true;
  return {
    update: value => { setSource(value); flush(); },
    flush() { flush(); },
    dispose() { if (live) { live = false; dispose(); } }
  };
}
