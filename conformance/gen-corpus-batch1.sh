#!/bin/sh
# Corpus batch 1: each supported type in isolation + smoke queries.
# Case name: <class>-NNN-<hash8>.sql, hash8 = sha256 of the query text.
# Idempotent; editing a case changes its hash and therefore creates a new case.
set -eu
cd "$(dirname "$0")/corpus"

n_smoke=0; n_types=0
emit() { # emit <class> <sql>
  class="$1"; sql="$2"
  case "$class" in
    smoke) n_smoke=$((n_smoke+1)); num=$(printf '%03d' "$n_smoke");;
    types) n_types=$((n_types+1)); num=$(printf '%03d' "$n_types");;
  esac
  hash=$(printf '%s' "$sql" | shasum -a 256 | cut -c1-8)
  printf '%s\n' "$sql" > "${class}-${num}-${hash}.sql"
}

# --- smoke ---
emit smoke "SELECT 1 AS x"
emit smoke "SELECT sum(number) AS s, count() AS c FROM numbers(1000)"
emit smoke "SELECT number, toString(number) AS s, number * 2 AS d FROM numbers(10) ORDER BY number"
emit smoke "SELECT number FROM numbers(10) WHERE number < 0 ORDER BY number"
emit smoke "SELECT 'tab\ttab' AS t, 'nl\nnl' AS n, 'q''q' AS q, 'bs\\\\bs' AS b"

# --- types: integers ---
emit types "SELECT toUInt8(0) AS lo, toUInt8(255) AS hi"
emit types "SELECT toUInt16(0) AS lo, toUInt16(65535) AS hi"
emit types "SELECT toUInt32(0) AS lo, toUInt32(4294967295) AS hi"
emit types "SELECT toUInt64(0) AS lo, toUInt64(18446744073709551615) AS hi"
emit types "SELECT toInt8(-128) AS lo, toInt8(127) AS hi"
emit types "SELECT toInt16(-32768) AS lo, toInt16(32767) AS hi"
emit types "SELECT toInt32(-2147483648) AS lo, toInt32(2147483647) AS hi"
emit types "SELECT toInt64(-9223372036854775808) AS lo, toInt64(9223372036854775807) AS hi"
emit types "SELECT toInt128('-170141183460469231731687303715884105728') AS lo, toInt128('170141183460469231731687303715884105727') AS hi"
emit types "SELECT toUInt128('340282366920938463463374607431768211455') AS hi"
emit types "SELECT toInt256('-57896044618658097711785492504343953926634992332820282019728792003956564819968') AS lo"
emit types "SELECT toUInt256('115792089237316195423570985008687907853269984665640564039457584007913129639935') AS hi"

# --- types: floating point and decimals ---
emit types "SELECT toFloat32(1.5) AS a, toFloat32(-0.125) AS b, toFloat32(3.4e38) AS big"
emit types "SELECT toFloat64(1.5) AS a, toFloat64(-1e-300) AS tiny, toFloat64(1.7976931348623157e308) AS big"
emit types "SELECT toDecimal32('123.45', 2) AS d32, toDecimal64('1234567890.123456', 6) AS d64"
emit types "SELECT toDecimal128('12345678901234567890.1234567890', 10) AS d128"
emit types "SELECT toDecimal256('1234567890123456789012345678901234567890.123456', 6) AS d256"

# --- types: strings ---
emit types "SELECT 'plain' AS s, '' AS empty"
emit types "SELECT toFixedString('abcde', 5) AS fs"
emit types "SELECT toLowCardinality('repeated-value') AS lc"

# --- types: date and time ---
emit types "SELECT toDate('2024-06-15') AS d"
emit types "SELECT toDate32('1899-12-31') AS d32"
emit types "SELECT toDateTime('2024-06-15 12:34:56') AS dt"
emit types "SELECT toDateTime('2024-06-15 12:34:56', 'UTC') AS dt_utc"
emit types "SELECT toDateTime64('2024-06-15 12:34:56.789', 3) AS dt64_3"
emit types "SELECT toDateTime64('2024-06-15 12:34:56.123456', 6) AS dt64_6"

# --- types: Nullable / special ---
emit types "SELECT CAST(NULL AS Nullable(Int32)) AS v"
emit types "SELECT toNullable(42) AS v"
emit types "SELECT CAST(NULL AS Nullable(String)) AS s, CAST('x' AS Nullable(String)) AS x"
emit types "SELECT true AS t, false AS f"
emit types "SELECT toUUID('61f0c404-5cb3-11e7-907b-a6006ad3dba0') AS u"
emit types "SELECT CAST('a' AS Enum8('a' = 1, 'b' = 2)) AS e8"
emit types "SELECT CAST('big' AS Enum16('big' = 300, 'small' = -300)) AS e16"

# --- types: composites (documented behavior, no silent data corruption) ---
emit types "SELECT [1, 2, 3] AS arr"
emit types "SELECT ['a', 'b''c', ''] AS arr_s"
emit types "SELECT map('k1', 1, 'k2', 2) AS m"
emit types "SELECT tuple(1, 'x') AS t"
emit types "SELECT arrayJoin([toNullable(1), NULL, toNullable(3)]) AS v"

echo "corpus: $(ls *.sql | wc -l | tr -d ' ') cases"
