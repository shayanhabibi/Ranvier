import { readdirSync, readFileSync } from "node:fs";
import { test, expect, openMaps } from "./fixtures.mjs";

const guide = "guide/signal-maps/";
const coldCompilerBudgetMs = 30_000;
const warmCompilerBudgetMs = 5_000;

test("the live editor compiles a map and its controls update the graph", async ({ page, problems }) => {
  test.setTimeout(45_000);
  await page.goto(guide);
  const run = page.getByRole("button", { name: "Run", exact: true });
  await run.click();
  await expect(run).toBeEnabled({ timeout: coldCompilerBudgetMs });
  await page.getByRole("button", { name: "Result", exact: true }).click();
  const result = page.frameLocator(".nacara-live__pane--result iframe");
  const map = result.locator(".rv-map");
  await expect(map.locator(".rv-map-node").first()).toBeVisible({ timeout: warmCompilerBudgetMs });
  await result.getByRole("button", { name: "Settle 4", exact: true }).click();
  await expect(map.locator(".rv-map__log")).toContainText("Total 12");
  await result.getByRole("button", { name: "Fail", exact: true }).click();
  await expect(map.locator(".rv-map__log")).toContainText("Unavailable: feed offline");
  await result.getByRole("button", { name: "Settle 5", exact: true }).click();
  await expect(map.locator(".rv-map__log")).toContainText("Total 15");

  const editor = page.locator(".cm-content");
  const source = await editor.innerText();
  expect(source).toContain("price.Value * 3");
  await editor.fill(source.replace("price.Value * 3", "price.Value * 5"));
  await run.click();
  await expect(run).toBeEnabled({ timeout: warmCompilerBudgetMs });
  await result.getByRole("button", { name: "Settle 4", exact: true }).click();
  await expect(map.locator(".rv-map__log")).toContainText("Total 20");
  await result.getByRole("button", { name: "Settle 5", exact: true }).click();
  await expect(map.locator(".rv-map__log")).toContainText("Total 25");
});

/** The map whose controls include the text `name`. */
const mapWith = (page, maps, name) => maps.filter({ has: page.getByText(name, { exact: true }) }).first();

test("every map on a page hydrates its own host, ids and graph", async ({ page, problems }) => {
  const maps = await openMaps(page, guide);
  await expect(maps).toHaveCount(12);
  const hosts = await page.locator(".rv-map-host").evaluateAll(hosts => hosts.map(host => host.id));
  expect(new Set(hosts).size).toBe(hosts.length);
  const ids = await page.locator("[id]").evaluateAll(elements => elements.map(element => element.id));
  expect(ids.length - new Set(ids).size).toBe(0);

  for (let index = 0; index < 12; index++)
    await expect.poll(() => maps.nth(index).locator(".rv-map-node").count(), `map ${index}`).toBeGreaterThan(0);

  expect(await page.locator(".rv-map__error").evaluateAll(errors => errors.map(e => e.textContent).filter(Boolean))).toEqual([]);
});

test("a live map's controls write, fail and reset its own graph only", async ({ page, problems }) => {
  const maps = await openMaps(page, guide);
  const cart = maps.nth(0);
  const other = maps.nth(2);
  const otherLog = await other.locator(".rv-map__log").textContent();

  await cart.getByRole("button", { name: "Add tea" }).click();
  await expect(cart.locator(".rv-map__log")).toContainText("write lines");
  await expect.poll(() => cart.locator(".rv-map-dot").count(), { message: "a travelling mark" }).toBeGreaterThan(0);
  await expect(cart.locator(".rv-map-dot")).toHaveCount(0, { timeout: 5_000 });
  await cart.getByRole("button", { name: "Fail quote" }).click();
  await expect(cart.locator(".rv-map-node.is-failed")).not.toHaveCount(0);
  await expect(other.locator(".rv-map__log")).toHaveText(otherLog);

  await cart.getByRole("button", { name: "Reset" }).click();
  await expect(cart.locator(".rv-map-node.is-failed")).toHaveCount(0);
  await expect(cart.locator(".rv-map__log")).not.toContainText("write lines");
  await expect(cart.locator(".rv-map-node")).not.toHaveCount(0);
});

test("bound inputs follow external writes and keep an unfinished edit", async ({ page, problems }) => {
  const maps = await openMaps(page, guide);
  const order = mapWith(page, maps, "Load another order");
  const name = order.locator(".rv-map__input--text input");
  const price = order.locator(".rv-map__input--number input");
  const qty = order.locator(".rv-map__input--slider input");

  await expect(name).toHaveValue("Tea");
  await expect(price).toHaveValue("4.5");
  await name.fill("Cof");
  await qty.fill("5");
  await expect(order.locator(".rv-map__input--slider output")).toHaveText("5");
  await expect(order.locator(".rv-map__log")).toContainText("write qty");
  await expect(name).toHaveValue("Cof");

  await order.getByRole("button", { name: "Load another order" }).click();
  await expect(name).toHaveValue("Jam");
  await expect(price).toHaveValue("8");
  await expect(qty).toHaveValue("2");
  await expect(order.locator(".rv-map__input--toggle input")).not.toBeChecked();

  await price.fill("6.5");
  await price.press("Enter");
  await expect(order.locator(".rv-map__log")).toContainText("write price");
});

test("a replay plays, steps, scrubs and keeps its speed across Reset", async ({ page, problems }) => {
  const now = new Date("2026-01-01T00:00:00Z");
  await page.clock.install({ time: now });
  await page.clock.pauseAt(now);
  await page.goto(guide);
  const maps = page.locator(".rv-map");
  const replay = maps.nth(3);
  const scrub = replay.locator(".rv-map__scrub");
  await expect(replay.locator(".rv-map-node").first()).toBeVisible();
  await expect(replay).toHaveAttribute("aria-busy", "true");
  await expect(replay.getByRole("button", { name: "Play" })).toBeDisabled();
  await expect(replay.getByRole("button", { name: "Step" })).toBeDisabled();
  await expect(scrub).toBeDisabled();
  const recordingEnd = Number(await scrub.getAttribute("max"));
  await page.clock.runFor(1000);
  await expect(replay).toHaveAttribute("aria-busy", "false");
  await expect(replay.getByRole("button", { name: "Play" })).toBeEnabled();
  await expect(scrub).toBeEnabled();
  await page.clock.resume();
  const end = Number(await scrub.getAttribute("max"));
  expect(end).toBeGreaterThan(recordingEnd);
  const start = Number(await scrub.getAttribute("min"));
  expect(end).toBeGreaterThan(start);
  await expect(scrub).toHaveValue(String(start));
  await expect(replay.locator(".rv-map__tick")).not.toHaveCount(0);

  await replay.getByRole("button", { name: "Step" }).click();
  expect(Number(await scrub.inputValue())).toBeGreaterThan(start);

  await replay.locator(".rv-map__speed-select").selectOption("4");
  await expect(replay).toHaveAttribute("style", /--rv-map-speed: 4/);
  await replay.getByRole("button", { name: "Play" }).click();
  await expect(replay.getByRole("button", { name: "Pause" })).toBeVisible();
  await expect(scrub).toHaveValue(String(end), { timeout: 20_000 });
  await expect(replay.getByRole("button", { name: "Play" })).toBeVisible();

  await scrub.fill(String(start));
  await expect(scrub).toHaveValue(String(start));
  await replay.getByRole("button", { name: "Reset" }).click();
  await expect(scrub).toHaveValue(String(start));
  await expect(replay.locator(".rv-map__speed-select")).toHaveValue("4");
  await expect(replay).toHaveAttribute("style", /--rv-map-speed: 4/);
});

test("a running node highlights its binding's source lines", async ({ page, problems }) => {
  const maps = await openMaps(page, guide);
  const replay = maps.nth(3);
  const card = replay.locator("xpath=ancestor::div[contains(@class, 'rv-map-card')]");
  const lit = card.locator(".nacara-code__line.rv-map-line--run");
  const scrub = replay.locator(".rv-map__scrub");
  const end = await scrub.getAttribute("max");
  while ((await lit.count()) === 0 && (await scrub.inputValue()) !== end)
    await replay.getByRole("button", { name: "Step" }).click();
  await expect(lit.first()).toBeAttached();
  await scrub.fill(await scrub.getAttribute("min"));
  await expect(lit).toHaveCount(0);
});

test("hover, focus and Enter inspect a node", async ({ page, problems }) => {
  const maps = await openMaps(page, guide);
  const cart = maps.nth(0);
  const node = cart.locator(".rv-map-node").first();
  await node.hover();
  await expect(cart.locator(".rv-map__inspect")).toContainText("runs");
  await node.focus();
  await page.keyboard.press("Enter");
  await expect(cart.locator(".rv-map__why")).not.toHaveText("");
  await expect(node).toHaveAttribute("aria-label", /runs/);
});

test("the code disclosure starts as authored and works from the keyboard", async ({ page, problems }) => {
  await openMaps(page, guide);
  const collapsed = page.locator(".rv-map-card").filter({ hasText: "Load another order" }).locator("details.rv-map-code");
  await expect(collapsed).not.toHaveAttribute("open", "");
  await collapsed.locator("summary").focus();
  await page.keyboard.press("Enter");
  await expect(collapsed).toHaveAttribute("open", "");
  await expect(page.locator(".rv-map-card details.rv-map-code[open]").first()).toBeVisible();
});

test("reduced motion plays a replay at once and draws no travelling dots", async ({ browser }) => {
  const context = await browser.newContext({ reducedMotion: "reduce" });
  const tab = await context.newPage();
  const errors = [];
  tab.on("pageerror", error => errors.push(error.message));
  const maps = await openMaps(tab, guide);
  const replay = maps.nth(3);
  const scrub = replay.locator(".rv-map__scrub");
  await replay.getByRole("button", { name: "Play" }).click();
  await expect(scrub).toHaveValue(await scrub.getAttribute("max"), { timeout: 3_000 });
  await expect(tab.locator(".rv-map-dot")).toHaveCount(0);
  expect(errors).toEqual([]);
  await context.close();
});

test("Reset during pending work leaves no stale timers or animations", async ({ page, problems }) => {
  const maps = await openMaps(page, guide);
  const quick = maps.nth(1);
  for (let round = 0; round < 3; round++) {
    await quick.getByRole("button", { name: "Two quick writes" }).click();
    await quick.getByRole("button", { name: "Reset" }).click();
  }
  await page.waitForTimeout(1500);
  await expect(quick.locator(".rv-map__log")).not.toContainText("write qty");
  await expect(quick.locator(".rv-map__error")).toHaveText("");
});

test("collections draw boxes and rows that follow their keys", async ({ page, problems }) => {
  const maps = await openMaps(page, guide);
  const collection = mapWith(page, maps, "Egg");
  await expect(collection.locator(".rv-map-group")).toHaveCount(1);
  const rows = collection.locator(".rv-map-node--row");
  const before = await rows.count();
  await collection.locator(".rv-map__input--toggle input").check();
  await expect(rows).toHaveCount(before + 1);
});

test("an async and timed map follows its example clock", async ({ page, problems }) => {
  const maps = await openMaps(page, "");
  const cart = mapWith(page, maps, "Rapid cart edits");
  await cart.getByRole("button", { name: "Rapid cart edits" }).click();
  await expect(cart.locator(".rv-map-node.is-timing")).not.toHaveCount(0);
  for (const button of await cart.locator(".rv-map__controls button").all()) {
    if (/ms$/.test(await button.innerText())) await button.click();
  }
  await expect(cart.locator(".rv-map__log")).toContainText("admit");
});

test("pages render their maps' server HTML without JavaScript", async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const tab = await context.newPage();
  await tab.goto(guide);
  await expect(tab.locator(".rv-map")).toHaveCount(12);
  await expect(tab.locator(".rv-map__timeline").first()).toBeVisible();
  await expect(tab.locator("details.rv-map-code").first()).toBeAttached();
  await context.close();
});

test("the map bundles start no Solid renderer", async () => {
  const directory = new URL("../../../../output/_ranvier/maps/", import.meta.url);
  const sources = readdirSync(directory, { recursive: true })
    .filter(name => String(name).endsWith(".js"))
    .map(name => readFileSync(new URL(String(name).replaceAll("\\", "/"), directory), "utf8"));
  expect(sources.length).toBeGreaterThan(0);
  for (const source of sources) {
    expect(source).not.toContain("solid-js");
    expect(source).not.toContain("@solidjs/web");
  }
});

test("the landing state dial holds all six states on hover and focus", async ({ page, problems }) => {
  await page.goto("");
  const dial = page.locator(".rv-dial");
  const rows = dial.locator(".rv-statelist__row");
  await expect(rows.locator("strong")).toHaveText(["Pending", "Fallback", "Ready", "Retained value", "Failed", "Recovered"]);
  await rows.nth(4).hover();
  await expect(rows.nth(4)).toHaveClass(/is-active/);
  await expect(dial.locator(".rv-dial__source")).toHaveClass(/is-failed/);
  await expect(dial.locator(".rv-dial__view .rv-map-node__value")).toHaveText("Total 12");
  await rows.nth(5).focus();
  await expect(dial.locator(".rv-dial__view .rv-map-node__value")).toHaveText("Unavailable");
  await rows.nth(2).hover();
  await rows.nth(5).hover();
  await expect(dial.locator(".rv-dial__dot")).not.toHaveClass(/is-moving/);
  await expect(dial.locator(".rv-statelist")).not.toHaveClass(/is-cycling/);
  await page.mouse.move(0, 0);
  await page.getByRole("link", { name: "Get started", exact: true }).focus();
  await expect(dial.locator(".rv-statelist")).toHaveClass(/is-cycling/);
});

test("the landing state dial honours reduced motion and remains keyboard accessible", async ({ page, problems }) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.goto("");
  const dial = page.locator(".rv-dial");
  const rows = dial.locator(".rv-statelist__row");
  await expect(rows.nth(2)).toHaveClass(/is-active/);
  await expect(dial.locator(".rv-statelist")).not.toHaveClass(/is-cycling/);
  await rows.nth(3).focus();
  await expect(rows.nth(3)).toHaveClass(/is-active/);
  await expect(dial.locator(".rv-dial__source .rv-map-node__value")).toHaveText("4");
  await expect(dial.locator(".rv-dial__view .rv-map-node__value")).toHaveText("Total 12");
  await expect(dial.locator(".rv-dial__dot")).not.toHaveClass(/is-moving/);
});

test("the editable example compiles and mounts its map on Run", async ({ page, problems }) => {
  test.setTimeout(40_000);
  await page.goto(guide);
  await page.getByRole("button", { name: "Run" }).click();
  const output = async () => {
    for (const frame of page.frames())
      if (frame.url() === "about:srcdoc" && (await frame.locator(".rv-map-node").count().catch(() => 0)) > 0) return frame;
    return undefined;
  };
  await expect.poll(output, { timeout: coldCompilerBudgetMs, intervals: [100] }).toBeTruthy();
  const frame = await output();
  // The output pane may sit behind another tab; the press reaches the map either way.
  await frame.locator(".rv-map__button", { hasText: "Settle 4" }).dispatchEvent("click");
  await expect(frame.locator(".rv-map__log")).toContainText("price");
});
