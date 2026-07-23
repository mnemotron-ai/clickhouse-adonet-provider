# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning: [SemVer 2.0](https://semver.org/).

## [Unreleased]

### Changed
- **Read-path performance overhaul** (wide-row throughput on the .NET
  Framework leg ~3× in the project bench, 342k → ~1M rows/s; net8 was already
  near the wire ceiling): `ExtendedBinaryReader` rewritten as a standalone
  single-buffer reader (drops the `BufferedStream` + `PeekableStreamWrapper` +
  `BinaryReader` stack that cost two stream calls per primitive and 128-byte
  string chunking); UUID reads are two register-width reads (was 14× slower
  than any other type on net48); Enum lookups use a reverse index instead of a
  per-row LINQ scan; `Decimal(P≤28)` values construct `System.Decimal`
  directly from (mantissa, scale) — no division, no `BigInteger` for the
  16-byte format; DateTime/DateTime64 take an epoch-arithmetic fast path for
  UTC columns (NodaTime remains for real timezones); FixedString decodes
  straight from the reader buffer.
- net4x only: the shared HTTP handler now sets `MaxConnectionsPerServer=16`
  (the legacy default of 2 concurrent connections per host serialized
  parallel SSIS dataflows). net8 is unchanged (no cap there).
- **Breaking (GetSchema):** `Tables`/`Views`/`Columns` restriction arrays now
  follow the SqlClient shapes SSDT's wizards actually pass — Tables
  `(Catalog, Owner, Table, TableType)`, Views `(Catalog, Owner, Table)`,
  Columns `(Catalog, Owner, Table, Column)`. Previously the shapes were
  provider-specific `(Database, Table[, Column])` and any longer array threw
  `ArgumentException`, which SSDT's DSV wizard swallows — rendering an empty
  object tree (found in the first live SSAS smoke, 2026-07-20). `Catalog` and
  `Owner` are both database filters (ClickHouse has no catalog/schema split);
  `TableType` accepts `BASE TABLE`/`TABLE`/`VIEW`; restrictions beyond the
  declared count are now ignored instead of throwing.

### Added
- `ProbeStringLengthsCacheTtl` connection-string setting (seconds, default
  300, `0` = off): ProbeStringLengths results are reused across repeated
  schema reads of the same query, so SSIS package validations stop re-running
  the full `max(lengthUTF8(...))` scan every time.
- `bench` command in the conformance runner (`make bench`): provider-vs-raw
  throughput matrix and per-type isolation benches against materialized
  tables; the measurement harness behind the numbers above. Not part of `ci`.
- Documented `Timeout` semantics for heavy pulls (`docs/deploy-windows.md`
  §9, verified empirically): the default 2 minutes only bounds time to first
  body bytes — an already-streaming response is never cut; a heavy
  aggregation (or a compressible result parked in the server's HTTP buffer)
  that produces nothing for 2 minutes dies with zero rows. Recommendation:
  explicit `Timeout=1800` for processing-class queries. Also noted:
  `Compression=false` can roughly double throughput on fast LANs (gzip is a
  win only over slow links).
- `Mnemotron.Data.ClickHouse.csproj` is now publish-ready as a NuGet package:
  `PackageReadmeFile` (`PACKAGE.md`, packed at the package root), `Copyright`,
  `RepositoryType=git`, and deterministic builds (`Deterministic=true`,
  `ContinuousIntegrationBuild=true` when `CI=true`) alongside the existing
  `PackageId`/`Description`/`Authors`/`PackageTags`/`PackageProjectUrl`,
  `PackageLicenseExpression=Apache-2.0`, SourceLink, and symbol package
  (`.snupkg`) metadata. Publishing (`dotnet nuget push`, an API key in the
  release workflow) is still deliberately deferred.
- Fork of ClickHouse.Client 7.14.0 as `Mnemotron.Data.ClickHouse`
  (net462/net48/net8.0, strong-named).
- Conformance suite: query corpus, golden outputs captured from a pinned
  ClickHouse 25.3 (HTTP `TSVWithNamesAndTypes`), replay through the provider,
  structural comparator with a written tolerance policy.
- `GetDataTypeName` returns the verbatim server type string.
- `Setup.exe` / `Uninstall.exe` (`tools/Installer/`, net48): a from-scratch
  C# port of `deploy/register-provider.ps1` and `deploy/install-cartridge.ps1`
  so installing/uninstalling the provider no longer requires PowerShell.
  Same behavior: GAC install of the provider + dependency closure,
  `DbProviderFactories` registration in both machine.config branches, and
  cartridge deployment to the discovered SSAS server / SSDT folders.
- Tag-driven release pipeline (`.github/workflows/release.yml`): pushing a
  `v*` tag publishes the provider and installer, assembles a Windows
  installer zip (`Setup.exe`, `Uninstall.exe`, provider build, cartridge,
  `deploy/*.ps1`, `INSTALL.md`, `SHA256SUMS`), and creates a GitHub release.
- `Mnemotron.Data.ClickHouse.csproj`: pinned `<AssemblyVersion>1.0.0.0</AssemblyVersion>`
  so the GAC/machine.config identity stays fixed while the package version
  floats per release tag.
- `GetSchemaTable` reports bounded, non-LOB sizes for `String` columns
  (`DefaultStringSize`, default 4000) so SSIS keeps them as `DT_WSTR` instead
  of `DT_NTEXT` — large throughput win for design-time tools.
- `ProbeStringLengths` connection setting (**on by default**): report each
  `String` column's actual maximum length (one aggregate scan per schema read)
  for automatic tight SSIS buffers. Set `ProbeStringLengths=false` for very
  large tables where the per-schema-read scan is too costly.
- Additive single-assembly net48 payload (`provider-net48-merged/`) in the
  release zip: `make merge-net48` (`dotnet-ilrepack` 2.0.45, `/internalize`,
  repo `.snk`) merges the provider + its 20 dependency DLLs into one
  strong-named `Mnemotron.Data.ClickHouse.dll` (same identity,
  `Version=1.0.0.0` / `PublicKeyToken=1a9f1c23413d5b5e`). Structurally
  eliminates the host-bindingRedirect failure class for that payload — a
  host redirecting `System.Text.Json` / `Microsoft.Extensions.*` cannot
  affect the internalized copies inside the merged assembly. The default
  `Setup.exe` payload is unchanged (still the unmerged folder); flipping it
  is a separate decision pending the SSAS smoke (issue #1).

### Fixed
- Decimal columns that fit `System.Decimal` (precision ≤ 28) are now returned
  as `System.Decimal` instead of the custom `ClickHouseDecimal`, so ADO.NET
  consumers (SSIS/SSAS) map them to native numeric (`DT_NUMERIC`) rather than
  `DT_NTEXT`. `ClickHouseDecimal` is still used for Decimal(P>28) where
  `System.Decimal` would overflow; `UseCustomDecimals=false` forces
  `System.Decimal` for those too (accepting the overflow risk).
- Third-party hosts without bindingRedirects (SSAS/SSIS/SSDT): a process-scoped
  `AssemblyResolve` fallback resolves the GAC dependency closure's internal
  version mismatches instead of failing with `TypeInitializationException`.
- `System.Text.Json` usage is isolated from core type initialization so a host
  that pins an ancient `System.Text.Json` cannot poison the type registry.
- Factory `Instance` is a `public static readonly` field, as
  `DbProviderFactories.GetFactory` requires on .NET Framework.
