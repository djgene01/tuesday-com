#!/usr/bin/env bash
# Backup/restore rehearsal: dump the live database, restore it into a scratch database, and
# compare the row counts that matter. This proves the dump is usable, which a file size never
# does. Run it against a development database; it writes nothing to the source.
#
#   scripts/ops/backup-restore.sh [container] [user] [database]
set -euo pipefail

CONTAINER="${1:-tuesday-postgres}"
USER_NAME="${2:-tuesday}"
DATABASE="${3:-tuesday}"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
RESTORE_DB="tuesday_restore_${STAMP}"
# Native path for docker cp: Git Bash reports /c/..., which Docker does not understand.
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
if command -v cygpath >/dev/null 2>&1; then ROOT="$(cygpath -m "$ROOT")"; fi
WORK="$ROOT/artifacts/backup"

mkdir -p "$WORK"
DUMP="$WORK/${DATABASE}-${STAMP}.dump"

echo "==> dumping $DATABASE to $DUMP"
docker exec "$CONTAINER" pg_dump -U "$USER_NAME" -d "$DATABASE" -Fc -f "/tmp/source.dump"
docker cp "$CONTAINER:/tmp/source.dump" "$DUMP" >/dev/null
docker exec "$CONTAINER" rm -f /tmp/source.dump

echo "==> creating $RESTORE_DB"
docker exec "$CONTAINER" psql -U "$USER_NAME" -d postgres -q -c "drop database if exists \"$RESTORE_DB\";" -c "create database \"$RESTORE_DB\";"

echo "==> restoring"
docker cp "$DUMP" "$CONTAINER:/tmp/restore.dump" >/dev/null
docker exec "$CONTAINER" pg_restore -U "$USER_NAME" -d "$RESTORE_DB" --no-owner --no-privileges /tmp/restore.dump
docker exec "$CONTAINER" rm -f /tmp/restore.dump

TABLES=(boards items item_column_values item_groups board_columns automations '"AspNetUsers"' '"AspNetUserRoles"' api_tokens)

echo
printf '%-22s %10s %10s %s\n' table source restored verdict
FAILED=0
for table in "${TABLES[@]}"; do
  source_count=$(docker exec "$CONTAINER" psql -U "$USER_NAME" -d "$DATABASE" -tAc "select count(*) from $table; ")
  restored_count=$(docker exec "$CONTAINER" psql -U "$USER_NAME" -d "$RESTORE_DB" -tAc "select count(*) from $table; ")
  if [ "$source_count" = "$restored_count" ]; then verdict="ok"; else verdict="MISMATCH"; FAILED=1; fi
  printf '%-22s %10s %10s %s\n' "$table" "$source_count" "$restored_count" "$verdict"
done

echo
echo "==> dropping $RESTORE_DB"
docker exec "$CONTAINER" psql -U "$USER_NAME" -d postgres -q -c "drop database \"$RESTORE_DB\";"

if [ "$FAILED" -ne 0 ]; then
  echo "REHEARSAL FAILED: restored database does not match the source." >&2
  exit 1
fi

echo "REHEARSAL PASSED in $(du -h "$DUMP" | cut -f1) dump; source untouched."
