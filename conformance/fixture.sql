-- Axis-2 (GetSchema) fixture: idempotent statements, applied by the runner
-- `fixture` subcommand. Statements are separated by a top-level semicolon.
-- IMPORTANT: a semicolon is FORBIDDEN anywhere else in this file (inside
-- string literals and comments) — the split is primitive.
-- FR-7 type matrix coverage: signed/unsigned integers, Float, Decimal, String,
-- FixedString, LowCardinality, Date/DateTime/DateTime64, UUID, Nullable,
-- Enum, Bool + one view.

CREATE DATABASE IF NOT EXISTS conformance_fixture;

CREATE TABLE IF NOT EXISTS conformance_fixture.types_matrix
(
    id UInt64,
    i8 Int8,
    i16 Int16,
    i32 Int32,
    i64 Int64,
    u8 UInt8,
    u16 UInt16,
    u32 UInt32,
    f32 Float32,
    f64 Float64,
    dec Decimal(18, 4),
    dec64 Decimal64(6),
    s String,
    fs FixedString(16),
    lc_s LowCardinality(String),
    d Date,
    d32 Date32,
    dt DateTime,
    dt64 DateTime64(3),
    uuid UUID,
    n_i32 Nullable(Int32),
    n_s Nullable(String),
    n_dt64 Nullable(DateTime64(6)),
    e8 Enum8('alpha' = 1, 'beta' = 2),
    b Bool
)
ENGINE = MergeTree ORDER BY id;

CREATE TABLE IF NOT EXISTS conformance_fixture.orders
(
    order_id UInt64,
    customer LowCardinality(String),
    amount Decimal(18, 2),
    created_at DateTime64(3),
    note Nullable(String)
)
ENGINE = MergeTree ORDER BY order_id;

CREATE VIEW IF NOT EXISTS conformance_fixture.orders_view AS
SELECT order_id, customer, amount FROM conformance_fixture.orders
