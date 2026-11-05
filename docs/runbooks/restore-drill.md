# Restore drill (P6-14)

Performed on the LAN box (`192.168.1.66`, Debian 13 LXC, 0.9.5) with real data, destroying the
volumes rather than restoring into a scratch database. The point is to prove the backup is usable and
that an operator can follow the runbook without inventing steps under pressure.

## 1. Back up

```bash
cd /opt/tuesday
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
docker compose exec -T postgres pg_dump -U tuesday -d tuesday -Fc -f /tmp/tuesday.dump
docker compose cp postgres:/tmp/tuesday.dump /opt/backups/tuesday-$STAMP.dump
docker compose exec -T postgres rm -f /tmp/tuesday.dump
sha256sum /opt/backups/tuesday-$STAMP.dump > /opt/backups/tuesday-$STAMP.sha256

# attachments volume (the app image has tar; the entrypoint is the web app)
docker run --rm --user root --entrypoint tar \
  -v tuesday_app-files:/data -v /opt/backups:/out \
  tuesday-web:0.9.5 czf /out/app-files-$STAMP.tar.gz -C /data .
```

Record the baseline counts before touching anything:

```bash
docker compose exec -T postgres psql -U tuesday -d tuesday -tAc \
  "select count(*) from boards; select count(*) from items; select count(*) from \"AspNetUsers\";"
```

## 2. Destroy

```bash
docker compose down -v          # removes postgres-data, redis-data, app-files and the network
docker compose up -d postgres redis
docker compose exec -T postgres psql -U tuesday -d tuesday -tAc \
  "select count(*) from information_schema.tables where table_schema='public';"   # 0
```

Do **not** start the app here: with an empty database it would bootstrap a fresh admin and tenant,
and the restored dump would then collide on the admin's unique email.

## 3. Restore

```bash
docker cp /opt/backups/tuesday-$STAMP.dump tuesday-postgres-1:/tmp/restore.dump
docker compose exec -T postgres pg_restore -U tuesday -d tuesday \
  --no-owner --no-privileges /tmp/restore.dump
docker compose exec -T postgres rm -f /tmp/restore.dump

docker run --rm --user root --entrypoint tar \
  -v tuesday_app-files:/data -v /opt/backups:/out \
  tuesday-web:0.9.5 xzf /out/app-files-$STAMP.tar.gz -C /data

docker compose up -d app
curl -fsS http://127.0.0.1:8080/health/ready
```

## 4. Verify — what actually proves it

| Check | Before | After restore |
|---|---|---|
| tables in `public` | 38 | 0 → 38 |
| boards | 1 | 1 |
| items | 1 | 1 |
| users | 2 | 2 |
| invites | 1 | 1 |
| migrations | 9 | 9 |
| admin sign-in | ok | ok |
| invitee sign-in with the pre-backup password | ok | **ok** |
| board renders groups + item | ok | ok (screenshot) |

The invitee sign-in is the strongest single signal: it needs the password hashes **and** the data
protection key ring (both live in the database), so a dump that restored only tables would fail it.

## Pitfalls found while doing it

- `docker run tuesday-web:0.9.5 tar …` runs `tar` as an **argument to the web entrypoint**. Use
  `--entrypoint tar`, and `--user root` because the runtime image is uid 1654 and cannot write to a
  root-owned backup directory.
- Nothing on this box can pull from Docker Hub, so any helper image (alpine for tar) must be
  transferred the same way as the app image. Reusing the app image avoids that.
- `docker compose down -v` is the destructive step; the dump lives outside the volumes, which is why
  `/opt/backups` must not be a volume.
