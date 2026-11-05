#!/usr/bin/env bash
# Clean-room guard: the shipped app must not mention the product whose behaviour it mirrors,
# nor carry its asset paths. Documentation may name it (the clean-room protocol does), so the
# scan covers only what users and packages see.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT" || exit 1

# Terms that must never appear in code, markup, styles or shipped assets.
PATTERN='monday\.com|monday board|mondaydotcom|monday-logo|Monday\.com'

fail=0

while IFS= read -r hit; do
  echo "forbidden reference: $hit"
  fail=1
done < <(grep -rInE "$PATTERN" src tests Dockerfile package.json 2>/dev/null)

# Trademark-ish asset names would be a copying problem even without the string in code.
while IFS= read -r asset; do
  echo "forbidden asset name: $asset"
  fail=1
done < <(find src -iname '*monday*' 2>/dev/null)

if [ "$fail" -ne 0 ]; then
  echo "CLEAN-ROOM SCAN FAILED" >&2
  exit 1
fi

echo "Clean-room scan passed: no product references in src, tests, Dockerfile or package.json."
