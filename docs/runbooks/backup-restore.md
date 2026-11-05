# Runbook: backup and restore

**Script:** `scripts/ops/backup-restore.sh [container] [user] [database]`
**Last rehearsal:** 2026-11-05 — PASSED (see results below).

## What it does

1. `pg_dump -Fc` of the live database into `artifacts/backup/<db>-<timestamp>.dump`.
2. Creates `tuesday_restore_<timestamp>`.
3. Restores the dump into that scratch database with `--no-owner --no-privileges`.
4. Compares row counts for `boards`, `items`, `item_column_values`, `item_groups`,
   `board_columns`, `automations`, `"AspNetUsers"`, `"AspNetUserRoles"` and `api_tokens`.
5. Drops the scratch database and fails if any table differs.

The source database is only read. The dump format is custom (`-Fc`), so restore needs
`pg_restore`, not `psql`.

## Rehearsal result

| table | source | restored |
|---|---|---|
| boards | 3 | 3 |
| items | 507 | 507 |
| item_column_values | 3000 | 3000 |
| item_groups | 3 | 3 |
| board_columns | 20 | 20 |
| automations | 2 | 2 |
| AspNetUsers | 4 | 4 |
| api_tokens | 1 | 1 |

168 KB dump, restored and verified in under ten seconds.

## Production procedure

```bash
# 1. Take the backup (hourly is the documented cadence; keep 7 daily + 4 weekly).
pg_dump -Fc -h 127.0.0.1 -p 5432 -U tuesday tuesday > /backups/tuesday-$(date +%F).dump

# 2. Verify it before you need it. Never keep a dump you have not restored.
createdb -h 127.0.0.1 -p 5432 -U tuesday restore_check
pg_restore -h 127.0.0.1 -p 5432 -U tuesday -d restore_check --no-owner --no-privileges tuesday-$(date +%F).dump
psql -d restore_check -c 'select count(*) from items;'
dropdb -h 127.0.0.1 -p 5432 -U tuesday restore_check

# 3. Store off-host, encrypted, with 30-day retention.
```

## Restore into a live environment

1. Stop the web host (it applies migrations at start-up, so it must not race the restore).
2. `pg_restore --clean --if-exists --no-owner --no-privileges -d tuesday <dump>`.
3. Start the host and check `/health/ready`; it must report database, Redis and storage.
4. Sign in and load the largest board.

## What this does not cover

- **Object storage attachments** are not in the dump. Back up the storage volume separately
  and keep the two in step: a database row pointing at a missing object is a broken attachment.
- **Redis** holds counters and cache only; it is rebuilt empty on purpose.
- **Point-in-time recovery** needs WAL archiving, which is not configured here. The dump gives
  you the moment it was taken, nothing between dumps.
