// axe sweep: every primary route, WCAG 2.0/2.1 A+AA.
// Usage: start the app, launch Edge with --remote-debugging-port=9222, then
//   BASE=http://127.0.0.1:5205 node scripts/a11y/axe.js
// Requires a signed-in Development fixture: admin@tuesday.local / Tuesday!Demo1.
// Prints one line per route and writes artifacts/axe.json; exit code is non-zero when a serious or
// critical violation is found.
const puppeteer = require('puppeteer-core');
const path = require('path');

const BASE = process.env.BASE ?? 'http://127.0.0.1:5205';
const AXE = path.resolve(__dirname, '../../node_modules/axe-core/axe.min.js');
const BOARD = process.env.BOARD ?? '01a1aa09-de5c-7b03-aa34-cfc6d0095569';
const ITEM = process.env.ITEM ?? '01a1aa09-defa-731f-a1ef-6ed7679d08f3';

const ROUTES = [
  ['landing', '/', false],
  ['login', '/login', false],
  ['workspace', '/workspace', true],
  ['board', `/board/${BOARD}`, true],
  ['dashboard', `/board/${BOARD}/dashboard`, true],
  ['item', `/board/${BOARD}/item/${ITEM}`, true],
  ['subitems', `/board/${BOARD}/subitems`, true],
  ['search', '/search', true],
  ['notifications', '/notifications', true],
  ['security', '/account/security', true],
  ['tokens', '/account/tokens', true],
  ['notfound', '/nope', true]
];

(async () => {
  const browser = await puppeteer.connect({
    browserURL: 'http://127.0.0.1:9222',
    defaultViewport: null,
    protocolTimeout: 240000
  });

  const axeSource = require('fs').readFileSync(AXE, 'utf8');
  const page = await browser.newPage();
  await page.setCacheEnabled(false);
  await page.setViewport({ width: 1440, height: 900 });

  // Anonymous routes first, then sign in once and run the rest.
  const results = [];

  const sweep = async (name, route) => {
    await page.goto(BASE + route, { waitUntil: 'domcontentloaded' });
    await new Promise((r) => setTimeout(r, 2500));
    // CSP blocks injected <script> tags, so evaluate the bundle through CDP instead.
    await page.evaluate(axeSource);
    await page.evaluate(() => { if (!window.axe) throw new Error('axe did not load'); });
    const report = await page.evaluate(async () => {
      const result = await window.axe.run(document, {
        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] }
      });
      return result.violations.map((v) => ({
        id: v.id,
        impact: v.impact,
        help: v.help,
        nodes: v.nodes.length,
        target: v.nodes[0]?.target?.join(' ') ?? '',
        summary: v.nodes[0]?.failureSummary?.split('\n')[1] ?? ''
      }));
    });

    results.push({ name, route, violations: report });
    const serious = report.filter((v) => v.impact === 'serious' || v.impact === 'critical');
    console.log(
      `${name.padEnd(13)} total=${report.length} serious=${serious.length}` +
      (serious.length ? ' :: ' + serious.map((v) => `${v.id}(${v.nodes}) ${v.target}`).join(' | ') : '')
    );
  };

  await sweep('landing', '/');
  await sweep('login', '/login');

  await page.goto(BASE + '/login', { waitUntil: 'domcontentloaded' });
  await page.evaluate(() => {
    document.querySelector('input[name="Credentials.Email"]').value = 'admin@tuesday.local';
    document.querySelector('input[name="Credentials.Password"]').value = 'Tuesday!Demo1';
    document.querySelector('form[action="/login"]').requestSubmit();
  });
  await page.waitForFunction(() => location.pathname !== '/login', { timeout: 30000 });

  for (const [name, route] of ROUTES.slice(2)) {
    await sweep(name, route);
  }

  require('fs').mkdirSync(path.dirname(path.resolve(__dirname, '../../artifacts/axe.json')), { recursive: true });
  require('fs').writeFileSync(
    path.resolve(__dirname, '../../artifacts/axe.json'),
    JSON.stringify(results, null, 2)
  );

  const failed = results.reduce((n, r) => n + r.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').length, 0);
  console.log(failed === 0 ? 'axe: clean' : `axe: ${failed} serious/critical violations`);
  await page.close();
  process.exit(failed === 0 ? 0 : 1);
})().catch((e) => { console.error('AXE FAILED', e.message); process.exit(1); });
