const template = document.createElement("template");
template.innerHTML = '<div class="panel"><input><button>0</button></div>';

export function mountNative(host, width, clone = false, descendants = false) {
  const section = document.createElement("section"), rows = [], nodes = [];
  let live = true;
  for (let i = 0; i < width; i++) {
    const row = clone ? template.content.firstElementChild.cloneNode(true) : document.createElement("div");
    const input = clone ? row.firstElementChild : document.createElement("input");
    const button = clone ? row.lastElementChild : document.createElement("button");
    const text = clone ? button.firstChild : document.createTextNode("0");
    row.id = `row-${i + 1}`;
    row.className = "panel";
    row.setAttribute("title", "a");
    input.value = "a";
    button.disabled = false;
    if (!clone) { button.append(text); row.append(input, button); }
    let count = 0;
    const clicked = () => { if (live) text.nodeValue = String(++count); };
    button.addEventListener("click", clicked);
    section.append(row);
    rows.push({ row, input, button, clicked });
    nodes.push(row, input, button, text);
  }
  host.append(section);
  return {
    update(value) {
      if (live) for (const { row, input, button } of rows) {
        row.setAttribute("title", value); input.value = value; button.disabled = value === "blocked";
      }
    },
    flush() {},
    dispose() {
      if (live) {
        live = false;
        for (const { button, clicked } of rows) button.removeEventListener("click", clicked);
        if (descendants) for (let i = nodes.length - 1; i >= 0; i--) nodes[i].remove();
        section.remove();
      }
    }
  };
}
