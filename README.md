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

## Building

```sh
docker compose up -d   # local ClickHouse used by tests and the conformance suite
make ci                # lint + build + tests + conformance
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow and
`conformance/README.md` for the conformance test suite.

## License

Apache-2.0 — see [LICENSE](LICENSE), [NOTICE](NOTICE) and
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

*ClickHouse is a trademark of ClickHouse, Inc. https://clickhouse.com —
this project is an independent, unofficial integration and is not affiliated
with or endorsed by ClickHouse, Inc.*
