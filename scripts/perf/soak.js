#!/usr/bin/env node
// HTTP-level soak: N virtual users, each signed in, cycling workspace -> board -> item.
// Reports per-endpoint percentiles and fails when a target is missed.
//
//   node scripts/perf/soak.js --base http://127.0.0.1:5201,http://127.0.0.1:5202 \
//       --users 200 --seconds 120 --board <guid> --item <guid>
//
// This measures server render and auth throughput. It does not open Blazor circuits; circuit
// memory is a separate concern (docs/runbooks/multi-instance.md). Treat the p95 here as the
// request-path floor, not as the whole "200 users" story.

const args = Object.fromEntries(
  process.argv.slice(2).reduce((pairs, value, index, all) => {
    if (value.startsWith('--')) pairs.push([value.slice(2), all[index + 1]]);
    return pairs;
  }, []),
);

const BASES = (args.base || 'http://127.0.0.1:5205').split(',').map((b) => b.replace(/\/$/, ''));
const USERS = Number(args.users || 200);
const SECONDS = Number(args.seconds || 120);
const BOARD = args.board;
const ITEM = args.item;
const EMAIL = args.email || 'admin@tuesday.local';
const PASSWORD = args.password || 'Tuesday!Demo1';
const THINK_MS = Number(args.think || 2000);
const BOARD_TARGET_MS = Number(args.target || 1500);

if (!BOARD || !ITEM) {
  console.error('--board and --item are required');
  process.exit(2);
}

const results = new Map();
const statusCounts = new Map();
let startedAt = 0;

function record(label, ms, status) {
  if (!results.has(label)) results.set(label, []);
  results.get(label).push(ms);
  const key = `${label} ${status}`;
  statusCounts.set(key, (statusCounts.get(key) || 0) + 1);
}

const percentile = (values, p) => {
  if (values.length === 0) return 0;
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1)];
};

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

class Session {
  constructor(base) {
    this.base = base;
    this.cookies = new Map();
  }

  store(response) {
    for (const header of response.headers.getSetCookie?.() ?? []) {
      const [pair] = header.split(';');
      const index = pair.indexOf('=');
      if (index > 0) this.cookies.set(pair.slice(0, index).trim(), pair.slice(index + 1));
    }
  }

  header() {
    return [...this.cookies].map(([k, v]) => `${k}=${v}`).join('; ');
  }

  async get(path, label) {
    const started = performance.now();
    const response = await fetch(this.base + path, { headers: { Cookie: this.header() }, redirect: 'manual' });
    this.store(response);
    const body = await response.text();
    record(label, performance.now() - started, response.status);

    return { status: response.status, body };
  }

  async signIn() {
    const page = await this.get('/login', 'login page');
    const token = /name="__RequestVerificationToken"[^>]*value="([^"]+)"/.exec(page.body)?.[1];
    if (!token) throw new Error('login page had no antiforgery token');

    const body = new URLSearchParams({
      'Credentials.Email': EMAIL,
      'Credentials.Password': PASSWORD,
      '__RequestVerificationToken': token,
      _handler: 'login',
    });

    const started = performance.now();
    const response = await fetch(this.base + '/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded', Cookie: this.header() },
      body,
      redirect: 'manual',
    });
    this.store(response);
    await response.text();
    record('login post', performance.now() - started, response.status);

    if (response.status !== 302 && response.status !== 200) {
      throw new Error('sign-in failed with ' + response.status);
    }
  }
}

async function virtualUser(index) {
  const session = new Session(BASES[index % BASES.length]);

  try {
    await session.signIn();
  } catch (error) {
    record('sign-in error', 0, 0);
    return;
  }

  while (performance.now() - startedAt < SECONDS * 1000) {
    try {
      await session.get('/workspace', 'workspace');
      await session.get(`/board/${BOARD}`, 'board 5k');
      await session.get(`/board/${BOARD}/item/${ITEM}`, 'item detail');
    } catch {
      record('request error', 0, 0);
    }

    await sleep(THINK_MS + Math.random() * THINK_MS);
  }
}

(async () => {
  console.log(`soak: ${USERS} users, ${SECONDS}s, bases ${BASES.join(', ')}`);

  startedAt = performance.now();
  const users = Array.from({ length: USERS }, (_, index) => virtualUser(index));
  const heartbeat = setInterval(() => {
    const elapsed = ((performance.now() - startedAt) / 1000).toFixed(0);
    console.log(`  ${elapsed}s … board samples ${results.get('board 5k')?.length ?? 0}`);
  }, 15000);

  await Promise.allSettled(users);
  clearInterval(heartbeat);

  const wall = (performance.now() - startedAt) / 1000;
  let failed = false;

  console.log(`\ncompleted in ${wall.toFixed(1)}s\n`);
  console.log('endpoint            n      p50      p95      p99   err%');

  for (const [label, values] of [...results].sort()) {
    const errors = [...statusCounts].filter(([k, v]) => k.startsWith(label + ' ') && !k.match(/ (200|302)$/)).reduce((sum, [, v]) => sum + v, 0);
    const errorRate = (errors / values.length) * 100;

    console.log(
      `${label.padEnd(18)} ${String(values.length).padStart(5)} ${percentile(values, 50).toFixed(0).padStart(7)}ms ${percentile(values, 95).toFixed(0).padStart(7)}ms ${percentile(values, 99).toFixed(0).padStart(7)}ms ${errorRate.toFixed(2).padStart(7)}`,
    );

    if (label === 'board 5k' && percentile(values, 95) > BOARD_TARGET_MS) failed = true;
    if (errorRate > 1) failed = true;
  }

  const boardP95 = percentile(results.get('board 5k') ?? [], 95);
  console.log(`\ntarget: board 5k p95 <= ${BOARD_TARGET_MS}ms — measured ${boardP95.toFixed(0)}ms`);
  console.log(failed ? 'SOAK FAILED' : 'SOAK PASSED');

  process.exit(failed ? 1 : 0);
})().catch((error) => {
  console.error('SOAK CRASHED', error);
  process.exit(1);
});
