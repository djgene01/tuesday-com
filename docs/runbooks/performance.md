# Performance: the 200-user checkpoint

Two harnesses, because the app renders the grid in two stages and only one of them is visible
over plain HTTP.

| Harness | What it measures |
|---|---|
| `scripts/perf/soak.js` | HTTP request path: sign-in, workspace, board shell (15 KB), item page. Uses N concurrent cookie sessions. |
| `scripts/perf/circuits.js` | Real Blazor circuits: navigation → first rendered grid row, and whether the circuits stay alive. |

The board's grid lives inside an `InteractiveServer` component, so an HTTP client receives the
shell and never the rows. **Use the circuit numbers for NFR-1**; the HTTP numbers describe the
request path only.

## Results — 5,000-item board, one replica, 200 concurrent circuits

Measured 2026-11-05 against `tuesday-web:0.9.2` (container, one app instance, PostgreSQL 17,
Redis 7.4) on the development host:

| Metric | Result | Target |
|---|---|---|
| Time to first grid row, p50 | **331 ms** | — |
| Time to first grid row, p95 | **416 ms** | ≤ 1,500 ms (NFR-1) |
| p99 | 449 ms | — |
| max | 555 ms | — |
| Circuits alive after a 30 s hold | **200 / 200** | 200 |
| App memory with 200 circuits | **3.0 GiB** (≈550 MiB idle) | — |
| App memory per circuit | **≈12 MB** | sizing input |

The circuits were opened one at a time and held: the browser is the bottleneck at this scale, not
the server. All 200 stayed connected through the hold, so the number is a live measure, not an
extrapolation.

## Results — HTTP request path, 200 concurrent sessions

| Endpoint | n | p50 | p95 | p99 | errors |
|---|---|---|---|---|---|
| workspace | 5,435 | 78 ms | 228 ms | 437 ms | 0% |
| board shell (5k) | 5,435 | 657 ms | 1,323 ms | 2,594 ms | 0% |
| item detail | 5,435 | 570 ms | 1,063 ms | 1,644 ms | 0% |
| login page | 200 | 272 ms | 363 ms | 379 ms | 0% |
| login POST | 200 | 956 ms | 1,410 ms | 1,495 ms | 0% |

Both replicas sat near 9 cores each of CPU while the load was applied.

## Reproduce

```bash
docker compose -f docker/compose.replicas.yaml up -d
docker compose -f docker/compose.replicas.yaml exec -T postgres \
  psql -U tuesday -d tuesday -v name="Perf 5k" -v count=5000 < scripts/perf/seed.sql

BID=$(docker compose -f docker/compose.replicas.yaml exec -T postgres psql -U tuesday -d tuesday -tAc "select \"Id\" from boards where \"Name\"='Perf 5k';")

# A Chrome/Edge with a debugging port is needed for the circuit harness only.
"/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" --headless=new \
  --remote-debugging-port=9222 --user-data-dir="$TMPDIR/edge-perf" about:blank &

node scripts/perf/circuits.js --base http://127.0.0.1:5201 --pages 200 --batch 1 --board "$BID" --hold 30
node scripts/perf/soak.js --base http://127.0.0.1:5201 --users 200 --seconds 120 --board "$BID" --item <guid>
```

Open circuits **one at a time** (`--batch 1`). At `--batch 2` and above the headless browser
starves itself and pages miss their selector; that is a harness limit, not a server one — the
server was idle while it happened.

## What is still not measured

- **Sustained interaction** — typing in cells, dragging, posting updates — the harness measures
  load and idle, not 200 users editing at once.
- **Multi-replica circuits** — all 200 ran on one instance. With a Redis backplane the cost per
  write grows with replica count; that has not been measured.
- **Database under write load** — the fixtures are read-heavy; cell-edit contention is unmeasured.
- **p99 of the request path** on the board shell (2.6 s) is above the NFR-1 p95 budget; it is not
  the metric the requirement names, but it is the tail a slow client would feel.
