# Runbook: roll back a release

Two things roll back independently: the application image and the database schema. Decide which
one broke before touching either.

## Application rollback (no schema change)

1. Identify the last good image tag — every release is tagged with its version, e.g.
   `tuesday-web:0.9.0`.
2. Deploy that tag (compose: set `TUESDAY_IMAGE`; Kubernetes: `kubectl set image`).
3. Watch `/health/ready` until ready, then load `/workspace` and one board.
4. Keep the failed image for forensics; do not delete it until the incident is closed.

This is safe when the release only changed code. It is **not** safe if the release shipped a
migration that the old code cannot read.

## Database rollback

Migrations are applied automatically at start-up under an advisory lock, so a rollback must
account for the schema already being forward.

- **Additive migration** (new nullable column, new table): old code ignores it. Roll the image
  back and leave the schema.
- **Destructive migration** (dropped or renamed column): roll forward only. Restore the last
  verified dump instead (`docs/runbooks/backup-restore.md`) and accept the data loss back to the
  dump timestamp.
- Never run `dotnet ef database update <older>` against production. The down-migrations are for
  development and are not rehearsed against real data.

## Post-rollback checks

- `/health/ready` returns 200 with all three checks green.
- Sign-in works for a normal user and an administrator.
- One board opens, a cell edit saves, and an update posts.
- The webhook test endpoint reaches a known receiver.
- Check the log for failed requests during the window and note the first failure time.

## Known gaps

- No blue/green deployment: rollback is a redeploy, with a short window of 502s.
- No automated canary analysis; the decision is a human reading `/health/ready` and a board.
