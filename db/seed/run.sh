#!/usr/bin/env bash
# Applies db/seed/search-listings.sql using an ADO.NET-style connection string
# (the same value the APIs use), translated to psql's key=value form.
set -euo pipefail

conn="${SEED_CONNECTION:?SEED_CONNECTION is required}"
declare -A kv
IFS=';' read -ra parts <<< "$conn"
for part in "${parts[@]}"; do
  key="${part%%=*}"; value="${part#*=}"
  kv["$(echo "$key" | tr '[:upper:]' '[:lower:]' | tr -d ' ')"]="$value"
done

export PGPASSWORD="${kv[password]:-}"
psql \
  --host "${kv[host]:-postgres}" \
  --port "${kv[port]:-5432}" \
  --username "${kv[username]:-${kv[user id]:-talentbridge}}" \
  --dbname "${kv[database]:-talentbridge}" \
  --set ON_ERROR_STOP=1 \
  --quiet \
  --file /seed.sql

echo "Seeded search.job_listings."
