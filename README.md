# Mnemotron ADO.NET Data Provider for ClickHouse®

**Unofficial** managed ADO.NET data provider for [ClickHouse](https://clickhouse.com),
targeting .NET Framework 4.6.2 / 4.8 and .NET 8. Built for classic
`System.Data.Common` consumers — SQL Server Analysis Services (Multidimensional
and Tabular), SSIS, and legacy .NET Framework applications — where the official
client no longer ships .NET Framework targets.

**Not affiliated with or endorsed by ClickHouse, Inc.**

> Status: early development, pre-release. The NuGet package
> `Mnemotron.Data.ClickHouse` is not published yet.

## Features

- Full ADO.NET surface: `DbConnection`, `DbCommand`, `DbDataReader`,
  `DbParameter`, `DbDataAdapter`, `DbConnectionStringBuilder`, and a
  `DbProviderFactory` (invariant name `Mnemotron.Data.ClickHouse`).
- Compressed binary-over-HTTP(S) transport (fork of
  [ClickHouse.Client](https://github.com/DarkWanderer/ClickHouse.Client) 7.14.0).
- Schema discovery via `GetSchema` for design-time tools (in progress).
- Strong-named assemblies, GAC-installable; SQL Server Analysis Services
  deployment collateral (in progress).

## String column sizing (SSIS / SSDT throughput)

`GetSchemaTable` reports a bounded size for `String` columns so ADO.NET
consumers keep them as inline strings (SSIS `DT_WSTR`) rather than LOBs
(`DT_NTEXT`), which stream per cell and are far slower. Two connection-string
settings control this:

- `DefaultStringSize` (default `4000`): the width reported for every unbounded
  `String` column. Set it to your real maximum to shrink SSIS row buffers;
  set `0` (or `>4000`) to force LOB semantics for genuinely huge text.
- `ProbeStringLengths` (default `false`): when `true`, a schema read runs one
  `max(lengthUTF8(...))` aggregate over the query and reports each `String`
  column's actual maximum length (with headroom) instead of the flat
  `DefaultStringSize` — automatic tight buffers, no manual tuning. The probe is
  a full scan: cheap for import-sized tables, expensive for very large ones.

## Building

```sh
docker compose up -d   # local ClickHouse used by tests and the conformance suite
make ci                # lint + build + tests + conformance
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow and
`conformance/README.md` for the conformance test suite.

## Contributing

Issues and pull requests are welcome. Start with
[CONTRIBUTING.md](CONTRIBUTING.md) for the workflow and rules (the
conformance suite is the correctness gate — see `conformance/README.md`);
this project follows the [Code of Conduct](CODE_OF_CONDUCT.md).

## License

Apache-2.0 — see [LICENSE](LICENSE), [NOTICE](NOTICE) and
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

*ClickHouse is a trademark of ClickHouse, Inc. https://clickhouse.com —
this project is an independent, unofficial integration and is not affiliated
with or endorsed by ClickHouse, Inc.*
