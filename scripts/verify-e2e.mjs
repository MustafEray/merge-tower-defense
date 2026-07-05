// End-to-end smoke test for the Phase 2 render/input layer.
//
// Serves the production bundle with `vite preview`, drives it in headless
// Chromium (playwright-core) and asserts through the window.__MTD_DEBUG hook
// that buying and drag-merge work through the real Pixi pointer pipeline.
// Usage: npm run verify:e2e   (screenshots land in out/e2e/)
import { spawn } from "node:child_process";
import { mkdirSync } from "node:fs";
import { chromium } from "playwright-core";

const PORT = 4173;
const SHOTS_DIR = process.env.E2E_SHOTS_DIR ?? "out/e2e";

// Must match Ui.layoutFor: margin 24, lane 64, lane gap 16, cell 72.
const GRID_LEFT = 24;
const GRID_TOP = 24 + 64 + 16;
const CELL = 72;
const cellCenter = (row, col) => ({
  x: GRID_LEFT + (col + 0.5) * CELL,
  y: GRID_TOP + (row + 0.5) * CELL,
});

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
  const page = await browser.newPage({ viewport: { width: 900, height: 660 } });

  const consoleErrors = [];
  page.on("console", (msg) => {
    if (msg.type() === "error") consoleErrors.push(msg.text());
  });
  page.on("pageerror", (err) => consoleErrors.push(String(err)));

  await page.goto(`http://localhost:${PORT}/`);
  await page.waitForSelector("#game-root canvas");
  await page.waitForSelector("#buy-tower");
  await page.waitForTimeout(500);

  const debug = () => page.evaluate(() => window.__MTD_DEBUG());

  assert("canvas is mounted", (await page.$("#game-root canvas")) !== null);
  assert("HUD shows starting gold", (await page.textContent("#hud-gold")) === "100");

  // Buy four towers: purchase cycle is Archer, Cannon, Frost, Archer.
  for (let i = 0; i < 4; i++) {
    await page.click("#buy-tower");
    await page.waitForTimeout(80);
  }

  let state = await debug();
  assert("four towers bought", state.towers.length === 4);
  assert("gold deducted to 20", state.gold === 20);
  assert(
    "purchase cycle types",
    JSON.stringify(state.towers.map((t) => t.type)) ===
      JSON.stringify(["Archer", "Cannon", "Frost", "Archer"])
  );
  assert("buy button still affordable at 20 gold", !(await page.isDisabled("#buy-tower")));

  await page.screenshot({ path: `${SHOTS_DIR}/01-towers-bought.png` });

  // Drag the Archer at (0,0) onto the Archer at (0,3) through real pointer
  // events on the Pixi canvas.
  const canvas = await page.$("#game-root canvas");
  const box = await canvas.boundingBox();
  const from = cellCenter(0, 0);
  const to = cellCenter(0, 3);

  await page.mouse.move(box.x + from.x, box.y + from.y);
  await page.mouse.down();
  for (let i = 1; i <= 8; i++) {
    await page.mouse.move(
      box.x + from.x + ((to.x - from.x) * i) / 8,
      box.y + from.y + ((to.y - from.y) * i) / 8
    );
    await page.waitForTimeout(30);
  }
  await page.screenshot({ path: `${SHOTS_DIR}/02-drag-preview.png` });
  await page.mouse.up();
  await page.waitForTimeout(150);

  state = await debug();
  assert("merge left three towers", state.towers.length === 3);
  const merged = state.towers.find((t) => t.row === 0 && t.col === 3);
  assert("merged tower is a level 2 Archer", merged?.type === "Archer" && merged?.level === 2);
  assert("origin cell is empty after merge", !state.towers.some((t) => t.row === 0 && t.col === 0));
  assert("drag resolved back to idle", state.dragging === false);
  assert(
    "HUD notice reports the merge",
    (await page.textContent("#hud-notice")).includes("Merged")
  );

  // Demo enemy spawner + ticker-driven movement (injected DeltaTime).
  await page.waitForTimeout(3500);
  state = await debug();
  assert("demo enemy spawned by the loop", state.enemies >= 1);

  await page.screenshot({ path: `${SHOTS_DIR}/03-merged-and-enemies.png` });

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
