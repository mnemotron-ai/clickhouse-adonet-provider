# Mnemotron ADO.NET Data Provider for ClickHouse®

**Unofficial** managed ADO.NET data provider for [ClickHouse](https://clickhouse.com),
targeting .NET Framework 4.6.2 / 4.8 and .NET 8. Built for classic
`System.Data.Common` consumers — SQL Server Analysis Services (Multidimensional
and Tabular), SSIS, and legacy .NET Framework applications — where the official
client no longer ships .NET Framework targets.

**Not affiliated with or endorsed by ClickHouse, Inc.**

## Features

- Full ADO.NET surface: `DbConnection`, `DbCommand`, `DbDataReader`,
  `DbParameter`, `DbDataAdapter`, `DbConnectionStringBuilder`, and a
  `DbProviderFactory` (invariant name `Mnemotron.Data.ClickHouse`).
- Compressed binary-over-HTTP(S) transport (fork of
  [ClickHouse.Client](https://github.com/DarkWanderer/ClickHouse.Client) 7.14.0).
- Schema discovery via `GetSchema` for design-time tools.
- Strong-named assemblies, GAC-installable; SQL Server Analysis Services
  deployment collateral.

## String column sizing (SSIS / SSDT throughput)

`GetSchemaTable` reports a bounded size for `String` columns so ADO.NET
consumers keep them as inline strings (SSIS `DT_WSTR`) rather than LOBs
(`DT_NTEXT`), which stream per cell and are far slower. Two connection-string
settings control this:

- `DefaultStringSize` (default `4000`): the width reported for every unbounded
  `String` column. Set it to your real maximum to shrink SSIS row buffers;
  set `0` (or `>4000`) to force LOB semantics for genuinely huge text.
- `ProbeStringLengths` (default `true`): a schema read runs one
  `max(lengthUTF8(...))` aggregate over the query and reports each `String`
  column's actual maximum length (with headroom) instead of the flat
  `DefaultStringSize` — automatic tight buffers, no manual tuning. The probe is
  a full scan: cheap for import-sized tables, expensive for very large ones —
  set `ProbeStringLengths=false` (and tune `DefaultStringSize`) for those.

## Project

Source, issue tracker, and full documentation:
<https://github.com/mnemotron-ai/clickhouse-adonet-provider>

## License

Apache-2.0 — see `LICENSE`, `NOTICE`, and `THIRD-PARTY-NOTICES.md` in the
source repository.

---

*ClickHouse is a trademark of ClickHouse, Inc. https://clickhouse.com —
this project is an independent, unofficial integration and is not affiliated
with or endorsed by ClickHouse, Inc.*
