#!/usr/bin/env node
// Circuit-level soak: opens N real Blazor circuits on the board page and measures the time from
// navigation to the first rendered grid row. The grid is interactive-only, so an HTTP client
// measures a 15 KB shell and nothing else — this is the only honest way to time it.
//
//   node scripts/perf/circuits.js --base http://127.0.0.1:5201 --pages 200 \
//       --board <guid> --users 200
//
// Requires a Chrome/Edge started with --remote-debugging-port=9222 (see docs/runbooks/performance.md).

const puppeteer = require('puppeteer-core');

const args = Object.fromEntries(
  process.argv.slice(2).reduce((pairs, value, index, all) => {
    if (value.startsWith('--')) pairs.push([value.slice(2), all[index + 1]]);
    return pairs;
  }, []),
);

const BASE = (args.base || 'http://127.0.0.1:5201').replace(/\/$/, '');
const PAGES = Number(args.pages || 50);
const BATCH = Number(args.batch || 10);
const BOARD = args.board;
const EMAIL = args.email || 'admin@tuesday.local';
const PASSWORD = args.password || 'Tuesday!Demo1';
const HOLD_SECONDS = Number(args.hold || 30);
const TARGET_MS = Number(args.target || 1500);

if (!BOARD) {
  console.error('--board is required');
  process.exit(2);
}

const timings = [];
const failures = [];

const percentile = (values, p) => {
  if (!values.length) return 0;
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1)];
};

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function openCircuit(browser, index) {
  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 900 });

  const consoleErrors = [];
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text().slice(0, 160));
  });

  try {
    await page.goto(BASE + '/login', { waitUntil: 'domcontentloaded', timeout: 30000 });
    await page.evaluate(([email, password]) => {
      document.querySelector('input[name="Credentials.Email"]').value = email;
      document.querySelector('input[name="Credentials.Password"]').value = password;
      document.querySelector('form[action="/login"]').requestSubmit();
    }, [EMAIL, PASSWORD]);
    await page.waitForFunction(() => location.pathname !== '/login', { timeout: 30000 });

    const started = Date.now();
    await page.goto(`${BASE}/board/${BOARD}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    await page.waitForSelector('input[aria-label="Item name"]', { timeout: 60000 });
    const elapsed = Date.now() - started;
    timings.push(elapsed);

    if (index % 10 === 0) console.log(`  page ${index}: grid in ${elapsed}ms`);
    return page;
  } catch (error) {
    const where = (() => { try { return new URL(page.url()).pathname; } catch { return '?'; } })();
    failures.push(`${index}@${where}: ${error.message} ${consoleErrors.slice(0, 2).join(' / ')}`);
    await page.screenshot({ path: `C:/Dialtone/data/VLMC/hermes/cache/scratch/circuit-fail-${index}.png` }).catch(() => {});
    await page.close().catch(() => {});
    return null;
  }
}

(async () => {
  console.log(`circuit soak: ${PAGES} circuits on ${BASE}, batches of ${BATCH}, target p95 <= ${TARGET_MS}ms`);

  const browser = await puppeteer.connect({ browserURL: 'http://127.0.0.1:9222', defaultViewport: null, protocolTimeout: 180000 });
  const open = [];

  for (let index = 0; index < PAGES; index += BATCH) {
    const batch = [];
    for (let offset = 0; offset < BATCH && index + offset < PAGES; offset++) {
      batch.push(openCircuit(browser, index + offset));
    }
    for (const page of await Promise.all(batch)) {
      if (page) open.push(page);
    }
    console.log(`  ${open.length}/${PAGES} circuits open`);
  }

  console.log(`\nholding ${open.length} circuits for ${HOLD_SECONDS}s…`);
  await sleep(HOLD_SECONDS * 1000);

  // A circuit that died shows up as a missing row on the next probe.
  let alive = 0;
  for (const page of open) {
    try {
      const ok = await page.evaluate(() => !!document.querySelector('input[aria-label="Item name"]'));
      if (ok) alive++;
    } catch {
      /* closed */
    }
  }

  console.log('\ncircles            n      p50      p95      p99    max');
  console.log(
    `time to grid ${String(timings.length).padStart(9)} ${percentile(timings, 50).toString().padStart(7)}ms ${percentile(timings, 95).toString().padStart(7)}ms ${percentile(timings, 99).toString().padStart(7)}ms ${Math.max(0, ...timings).toString().padStart(6)}ms`,
  );

  console.log(`\ncircuits alive after hold: ${alive}/${open.length}`);
  if (failures.length) console.log('failures:', failures.slice(0, 5).join(' | '));

  await Promise.allSettled(open.map((page) => page.close()));
  await browser.disconnect();

  const failed = failures.length > 0 || alive < open.length || percentile(timings, 95) > TARGET_MS;
  console.log(failed ? 'CIRCUIT SOAK FAILED' : 'CIRCUIT SOAK PASSED');
  process.exit(failed ? 1 : 0);
})().catch((error) => {
  console.error('CIRCUIT SOAK CRASHED', error);
  process.exit(1);
});
