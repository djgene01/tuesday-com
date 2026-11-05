# Runbook: two replicas behind a proxy

**Compose file:** `docker/compose.replicas.yaml` · **Proxy config:** `docker/nginx/tuesday.conf`

```
browser ──► nginx :8081 ──► app-a :8080
                        └─► app-b :8080
                    both ──► postgres (shared key ring, all data)
                        └─► redis (quota counters, cache)
                        └─► app-files volume (attachments)
```

## What makes it work

| Concern | Mechanism |
|---|---|
| Session cookie decrypts on either replica | Data Protection key ring persisted in `data_protection_keys`, `SetApplicationName("tuesday")` |
| Antiforgery token accepted by either replica | Fixed cookie name `tuesday.antiforgery` + the same shared ring |
| A Blazor circuit stays on one replica | `ip_hash` upstream; a WebSocket cannot be load-balanced per message |
| Attachments visible on both | One `app-files` volume mounted into both services |
| API quota shared | Redis counter keyed by token hash |

The key ring is written to the database by whichever replica activates first; the other reads it.
Concurrent activation is tolerated: the unique index on the friendly name makes one replica's
insert lose, and the loser re-reads the ring.

## Verified on this host (2026-11-05)

- Sign in on `:5201`, then `GET /workspace` on `:5202` → **200**, admin identity rendered.
- Same session through nginx `:8081` → **200**.
- Board page through nginx, sub-item panel expanded → **circuit live over WebSocket** (101 upgrade).
- Stopped `app-a`, replayed the same cookie jar against `app-b` → **200 /workspace**, no sign-out.
- `select count(*) from data_protection_keys` → **1** row shared by both.
- 0 console errors.

## Operating it

```bash
docker compose -f docker/compose.replicas.yaml up -d --build
docker compose -f docker/compose.replicas.yaml ps
docker compose -f docker/compose.replicas.yaml logs -f app-a
```

Rolling restart without a visible outage:

```bash
docker compose -f docker/compose.replicas.yaml up -d --no-deps --build app-a
# wait for its healthcheck to go healthy, then the same for app-b
```

Sessions survive because the ring lives in the database, not in either container.

## Known limits

- **Realtime updates do not cross replicas.** `BoardBroadcaster` is an in-process singleton, so an
  update posted on app-a does not reach a circuit held by app-b. A Redis backplane is the fix and
  is not built. Affinity hides this for a single user, not for two users on different replicas.
- **`ip_hash` is coarse.** Users behind one NAT share a replica; if it dies they are rebalanced on
  reconnect, which costs an open circuit but not a session.
- **Production needs the OpenIddict signing certificate** mounted; this rehearsal stack runs
  `Development` so the seeder creates the demo accounts.
- The attachments volume is a single-host volume, not shared object storage.
