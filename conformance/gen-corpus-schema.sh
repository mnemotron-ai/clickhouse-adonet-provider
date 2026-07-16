#!/bin/sh
# Wave 2 (WS-2): axis-2 (GetSchema) schema-case generation. A case is a JSON
# descriptor {"collection": ..., "restrictions": [...]}; name <class>-NNN-<hash8>.json,
# hash8 = sha256 of the normalized (single-line) json — same as gen-corpus-wave1.sh.
# The script is idempotent. IMPORTANT (determinism): all Tables/Views/Columns cases
# MUST be restricted to the conformance_fixture database — system table lists
# depend on the server version.
set -eu
cd "$(dirname "$0")/corpus"

n_schema=0
emit() { # emit <json>
  json="$1"
  n_schema=$((n_schema+1)); num=$(printf '%03d' "$n_schema")
  hash=$(printf '%s' "$json" | shasum -a 256 | cut -c1-8)
  printf '%s\n' "$json" > "schema-${num}-${hash}.json"
}

emit '{"collection": "MetaDataCollections"}'
emit '{"collection": "DataSourceInformation"}'
emit '{"collection": "DataTypes"}'
emit '{"collection": "Restrictions"}'
emit '{"collection": "Tables", "restrictions": ["conformance_fixture", null]}'
emit '{"collection": "Views", "restrictions": ["conformance_fixture", null]}'
emit '{"collection": "Columns", "restrictions": ["conformance_fixture", null, null]}'
emit '{"collection": "Columns", "restrictions": ["conformance_fixture", "orders", null]}'

echo "schema corpus: $(ls schema-*.json | wc -l | tr -d ' ') cases"
