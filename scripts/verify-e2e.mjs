// End-to-end smoke test for the render/input layer and the Phase 3 game
// systems (waves, combat, economy).
//
// Serves the production bundle with `vite preview`, drives it in headless
// Chromium (playwright-core) and asserts through the window.__MTD_DEBUG hook
// that buying, drag-merge, wave spawning and tower fire all work through the
// real Pixi pointer/ticker pipeline.
// Usage: npm run verify:e2e   (screenshots land in out/e2e/)
import { spawn } from "node:child_process";
import { mkdirSync } from "node:fs";
import { chromium } from "playwright-core";

const PORT = 4173;
const SHOTS_DIR = process.env.E2E_SHOTS_DIR ?? "out/e2e";

const failures = [];
const assert = (name, condition) => {
  console.log(`${condition ? "ok  " : "FAIL"} ${name}`);
  if (!condition) failures.push(name);
};

mkdirSync(SHOTS_DIR, { recursive: true });

const server = spawn("npx", ["vite", "preview", "--port", String(PORT), "--strictPort"], {
  stdio: "ignore",
});

try {
  // Wait for the preview server to accept connections.
  for (let i = 0; ; i++) {
    try {
      await fetch(`http://localhost:${PORT}/`);
      break;
    } catch {
      if (i > 50) throw new Error("vite preview did not start");
      await new Promise((r) => setTimeout(r, 200));
    }
  }

  const browser = await chromium.launch({
    executablePath: "/opt/pw-browsers/chromium",
  });
  const page = await browser.newPage({ viewport: { width: 1000, height: 760 } });

  const consoleErrors = [];
  page.on("console", (msg) => {
    if (msg.type() === "error") consoleErrors.push(msg.text());
  });
  page.on("pageerror", (err) => consoleErrors.push(String(err)));

  await page.goto(`http://localhost:${PORT}/`);
  await page.waitForSelector("#game-root canvas");
  await page.waitForSelector("#buy-archer");
  await page.waitForTimeout(400);

  const debug = () => page.evaluate(() => window.__MTD_DEBUG());
  const waitForState = async (name, predicate, timeoutMs) => {
    const deadline = Date.now() + timeoutMs;
    for (;;) {
      const state = await debug();
      if (predicate(state)) return state;
      if (Date.now() > deadline) {
        assert(name, false);
        return state;
      }
      await page.waitForTimeout(200);
    }
  };

  let state = await debug();
  assert("canvas is mounted", (await page.$("#game-root canvas")) !== null);
  assert("HUD shows starting gold", (await page.textContent("#hud-gold")) === "110");
  assert("HUD shows starting lives", (await page.textContent("#hud-lives")) === "10");
  assert("game starts before wave 1", state.wave === 0);

  // The layout comes from the app itself, so pointer math can never drift.
  const { gridLeft, gridTop, cell } = state.layout;
  const cellCenter = (row, col) => ({
    x: gridLeft + (col + 0.5) * cell,
    y: gridTop + (row + 0.5) * cell,
  });

  // Buy two archers; costs escalate 20, 24.
  await page.click("#buy-archer");
  await page.waitForTimeout(80);
  await page.click("#buy-archer");
  await page.waitForTimeout(80);

  state = await debug();
  assert("two towers bought", state.towers.length === 2);
  assert("escalating prices charged", state.gold === 110 - 20 - 24);
  assert(
    "both towers are archers",
    state.towers.every((t) => t.type === "Archer" && t.level === 1)
  );

  // Drag the Archer at (0,0) onto the Archer at (0,1) through real pointer
  // events on the Pixi canvas.
  const canvas = await page.$("#game-root canvas");
  const box = await canvas.boundingBox();
  const from = cellCenter(0, 0);
  const to = cellCenter(0, 1);

  await page.mouse.move(box.x + from.x, box.y + from.y);
  await page.mouse.down();
  for (let i = 1; i <= 6; i++) {
    await page.mouse.move(
      box.x + from.x + ((to.x - from.x) * i) / 6,
      box.y + from.y + ((to.y - from.y) * i) / 6
    );
    await page.waitForTimeout(30);
  }
  await page.screenshot({ path: `${SHOTS_DIR}/01-drag-preview.png` });
  await page.mouse.up();
  await page.waitForTimeout(150);

  state = await debug();
  assert("merge left one tower", state.towers.length === 1);
  assert(
    "merged tower is a level 2 Archer",
    state.towers[0]?.type === "Archer" && state.towers[0]?.level === 2
  );
  assert("drag resolved back to idle", state.dragging === false);
  assert(
    "HUD notice reports the merge",
    (await page.textContent("#hud-notice")).includes("Merged")
  );

  // Wave 1 starts on the scheduler's clock and spawns enemies.
  state = await waitForState("wave 1 starts", (s) => s.wave >= 1, 8000);
  state = await waitForState("wave enemies spawn", (s) => s.enemies > 0, 5000);
  await page.screenshot({ path: `${SHOTS_DIR}/02-wave-active.png` });

  // The merged archer covers the path entry: it must earn kill bounties.
  const goldBefore = state.gold;
  state = await waitForState("combat earns bounty gold", (s) => s.gold > goldBefore, 20000);
  assert("still playing", state.status === "playing");

  await page.screenshot({ path: `${SHOTS_DIR}/03-combat.png` });

  // The mute toggle is a plain HUD button/dispatch round trip like buy or
  // restart; click it through real DOM events and check the label flips.
  assert("mute button starts as Sound: On", (await page.textContent("#mute-toggle")) === "Sound: On");
  await page.click("#mute-toggle");
  assert("mute button toggles to Sound: Off", (await page.textContent("#mute-toggle")) === "Sound: Off");
  await page.click("#mute-toggle");
  assert("mute button toggles back to Sound: On", (await page.textContent("#mute-toggle")) === "Sound: On");

  assert("no console errors", consoleErrors.length === 0);
  if (consoleErrors.length > 0) console.error(consoleErrors.join("\n"));

  await browser.close();
} finally {
  server.kill();
}

if (failures.length > 0) {
  console.error(`\n${failures.length} e2e assertion(s) failed`);
  process.exit(1);
}
console.log("\ne2e verification passed");
