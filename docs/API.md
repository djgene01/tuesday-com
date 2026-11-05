# Public API (v1)

Bearer-token REST API. Tokens are issued on the **API tokens** page and shown once; the server
stores only their SHA-256 hash.

```
Authorization: Bearer tue_...
```

| Method | Path | Notes |
|---|---|---|
| GET | `/api/v1/boards` | Boards the caller can see |
| GET | `/api/v1/boards/{boardId}/items` | Cursor-paginated items with values |
| POST | `/api/v1/boards/{boardId}/items/{itemId}/updates` | Posts an update; honours `Idempotency-Key` |
| POST | `/api/v1/boards/{boardId}/webhooks` | Registers a webhook after a challenge echo |
| POST | `/api/v1/boards/{boardId}/webhooks/test` | Dispatches a test event to every enabled webhook |
| POST | `/api/v1/webhooks/{webhookId}/enable` | Re-enables a webhook that auto-disabled |

The served contract is `/openapi/v1.json`, and `docs/openapi/v1.json` is a committed snapshot.
`OpenApiSnapshotTests` fails the build when the two disagree, so a v1 change has to be deliberate.

## Pagination

`GET /items?take=50&cursor=...` returns `items`, `hasMore` and `nextCursor`.

The cursor is an opaque base64url token over a monotonic database sequence, not an offset.
Positions are per group and change on a drag, so an offset would skip or repeat rows; the
sequence does not move. Pass `nextCursor` back unchanged, and stop when it is `null`.

```
{ "items": [...], "nextCursor": "NTA3", "hasMore": false }
```

## Idempotency

Send `Idempotency-Key: <your key>` on a POST. The first call runs and stores its response for
24 hours; a retry with the same key and body replays that exact status and body with
`Idempotent-Replayed: true`. Reusing a key with a **different** body returns `409` rather than
silently running twice.

Only successful responses are stored, so a caller can fix a rejected request and retry with the
same key.

## Quotas

Every token gets `Api:DailyQuota` calls per UTC day (default **1000**), counted in Redis so all
replicas share one counter. Responses carry `X-RateLimit-Limit` and `X-RateLimit-Remaining`.
An exhausted token receives `429` with `Retry-After` and a `retry_in_seconds` field.

## Webhooks

- Registration performs a challenge echo to prove the URL is a live endpoint somebody controls.
- Every delivery is signed: `X-Tuesday-Signature: sha256=<hmac-sha256 of the raw body>` with the
  webhook's secret, plus `X-Tuesday-Event` and `X-Tuesday-Attempt`.
- A delivery is attempted up to **5 times** with bounded backoff on `5xx`, `408`, `429` and
  transport errors; `4xx` other than `429` fails immediately.
- **20 consecutive failed deliveries** disable the webhook. Re-enable it through
  `POST /api/v1/webhooks/{id}/enable`, which also resets the failure streak.
- **SSRF guard:** URLs resolving to loopback, private, link-local, CGNAT or multicast addresses
  are refused at registration and re-checked before every delivery. Redirects are not followed.
  The check happens before the socket opens, so a DNS answer that changes between the check and
  the connect is the residual risk.

## Rate limiting

A fixed-window limiter also runs in front of the API, independent of the daily quota. It is a
coarse abuse control; the quota is the one clients should budget against.
