export function snapshot(host) {
  return [...host.querySelectorAll("div.panel")].map(row => ({
    id: row.id, className: row.className, title: row.getAttribute("title"),
    value: row.querySelector("input").value,
    disabled: row.querySelector("button").disabled,
    text: row.querySelector("button").textContent
  }));
}

export function expectState(host, width, value, clicked = false) {
  const actual = snapshot(host);
  const expected = Array.from({ length: width }, (_, i) => ({
    id: `row-${i + 1}`, className: "panel", title: value, value,
    disabled: value === "blocked", text: clicked && i === 0 ? "1" : "0"
  }));
  if (JSON.stringify(actual) !== JSON.stringify(expected))
    throw Error(`DOM contract failed: ${JSON.stringify(actual)}`);
}

export function verify(mount, width) {
  const host = document.createElement("main");
  host.id = "framework-verify";
  document.body.append(host);
  let instance;
  try {
    instance = mount(host, width);
    expectState(host, width, "a");
    const before = [...host.querySelectorAll("input,button")];
    before[1].click();
    instance.flush();
    expectState(host, width, "a", true);
    instance.update("blocked");
    expectState(host, width, "blocked", true);
    instance.update("a");
    expectState(host, width, "a", true);
    if (before.some((node, i) => node !== host.querySelectorAll("input,button")[i]))
      throw Error("DOM identity changed");
    const retained = host.firstElementChild;
    instance.dispose();
    instance.dispose();
    if (host.childNodes.length) throw Error("Disposal retained DOM");
    const detachedState = snapshot(retained);
    instance.update("disposed");
    if (JSON.stringify(snapshot(retained)) !== JSON.stringify(detachedState))
      throw Error("Post-disposal API update changed detached DOM");
    return { width, initialRows: width, identity: true, click: true, disposalDom: true };
  } finally {
    instance?.dispose();
    host.remove();
  }
}
