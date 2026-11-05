// Measures board-page render latency for a named board and reports p50/p95.
//   node measure.js "Perf 5k" 15
const puppeteer = require('puppeteer-core');

const BASE = 'http://127.0.0.1:5205';
const name = process.argv[2] || 'Perf 5k';
const runs = Number(process.argv[3] || 15);

const percentile = (values, p) => {
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1)];
};

(async () => {
  const browser = await puppeteer.connect({ browserURL: 'http://127.0.0.1:9222', defaultViewport: null });
  const page = await browser.newPage();
  await page.setViewport({ width: 1440, height: 950 });

  await page.goto(BASE + '/login', { waitUntil: 'networkidle0' });
  if (await page.$('input[name="Credentials.Email"]')) {
    await page.type('input[name="Credentials.Email"]', 'admin@tuesday.local', { clear: true });
    await page.type('input[name="Credentials.Password"]', 'Tuesday!Demo1', { clear: true });
    await page.click('button[type="submit"]');
    await page.waitForNavigation({ waitUntil: 'networkidle0' }).catch(() => {});
  }

  await page.goto(BASE + '/workspace', { waitUntil: 'networkidle0' });
  const href = await page.evaluate((target) => {
    const links = [...document.querySelectorAll('a[href^="/board/"]')];
    return links.find((a) => a.textContent.includes(target))?.getAttribute('href') ?? null;
  }, name);

  if (!href) {
    console.error('board not found:', name);
    process.exit(2);
  }

  const url = BASE + href;
  const times = [];

  for (let i = 0; i < runs; i++) {
    const ms = await page.evaluate(async (target) => {
      const start = performance.now();
      const response = await fetch(target, { credentials: 'include', cache: 'no-store' });
      await response.text();
      return performance.now() - start;
    }, url);
    times.push(Math.round(ms));
  }

  console.log(JSON.stringify({
    board: name,
    url,
    runs,
    p50: percentile(times, 50),
    p95: percentile(times, 95),
    min: Math.min(...times),
    max: Math.max(...times),
    samples: times
  }, null, 2));

  await page.close();
  process.exit(0);
})().catch((error) => { console.error('MEASURE FAILED', error.message); process.exit(1); });
