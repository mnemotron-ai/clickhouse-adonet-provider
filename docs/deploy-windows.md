# Windows deployment — SSAS server + SSDT machine

Install path for the manual SSAS smoke validation
(`docs/ssas-smoke-checklist.md`) on a machine hosting SSAS and/or SSDT.
Two ways to install, doing exactly the same work:

* **`Setup.exe` / `Uninstall.exe`** (`tools/Installer/`) — the primary path.
  Download `Mnemotron.Data.ClickHouse-<tag>-win-installer.zip` from the
  project's GitHub Releases page, unzip it, and run `Setup.exe` as
  Administrator (right-click → "Run as administrator", or from an elevated
  command prompt). No PowerShell required. `Setup.exe` is a from-scratch C#
  port of the two scripts below; they remain the authoritative behavioral
  spec it is tested against.
* **`deploy/register-provider.ps1` + `deploy/install-cartridge.ps1`** — the
  scripted/CI alternative, for automation or when you need a flag the exe
  doesn't expose. Runs from an elevated **Windows PowerShell 5.1** prompt
  (`powershell.exe`, not `pwsh` — GAC installation needs
  `System.EnterpriseServices`, which only exists on the .NET Framework CLR).

Both binaries are **unsigned** for now: Windows SmartScreen will warn on
first run of `Setup.exe` ("Windows protected your PC") — click "More info" →
"Run anyway". Code signing is a tracked work item.

Everything in this document (GAC strategy, machine.config edits, cartridge
locations, known limitations) applies identically to both paths; where
commands differ, both are shown.

## 0. Quick install — Setup.exe / Uninstall.exe

```
Setup.exe                                    # install: GAC + machine.config + cartridge
Setup.exe --payload C:\path\to\publish-net48  # override the provider publish folder (default: provider-net48 next to the exe)
Setup.exe --no-cartridge                     # skip cartridge deployment
Uninstall.exe                                # uninstall (same binary as Setup.exe, copied under this name)
Uninstall.exe --remove-dependencies          # also GAC-remove the shared dependency assemblies
Uninstall.exe --keep-cartridge               # leave deployed cartridge copies in place
Setup.exe --help                             # full option list
```

The release zip also ships `provider-net48-merged\` — a single ILRepack-merged
`Mnemotron.Data.ClickHouse.dll` (same identity: `Version=1.0.0.0`, PublicKeyToken
`1a9f1c23413d5b5e`) instead of the provider + 20 dependency DLLs. It is
**not** the default payload yet (pending the SSAS smoke, issue #1) but can
be field-tested with:

```
Setup.exe --payload C:\path\to\unzipped\provider-net48-merged
```

Verify from an elevated **Windows PowerShell 5.1** prompt:
`[System.Data.Common.DbProviderFactories]::GetFactory('Mnemotron.Data.ClickHouse')`
should return without error. Then restart the SSAS service (and Visual
Studio) and work through `docs/ssas-smoke-checklist.md`.

## 1. Prerequisites

* Build artifacts: `dotnet publish src/Mnemotron.Data.ClickHouse -f net48 -c Release`
  — copy the whole publish folder to the target machine. It contains
  `Mnemotron.Data.ClickHouse.dll` plus its dependency closure (20 DLLs:
  `Microsoft.Extensions.*`, `Microsoft.IO.RecyclableMemoryStream`, `NodaTime`,
  `System.Text.Json` + BCL facades). All are strong-named (verified
  2026-07-18 against the net48 publish output; `System.ValueTuple` left the
  closure with Microsoft.Extensions.Http 10).
* Target machines: SSAS server (MD and/or Tabular instance) and/or the SSDT
  machine (VS2022 + the «Analysis Services Projects» extension).
* Local admin rights.

## 2. Install order

Scripted path (`Setup.exe` does the equivalent of both steps in one run;
see §0):

1. **`deploy/register-provider.ps1`** — on *both* the SSAS server and the
   SSDT machine:
   * GAC-installs the provider **and its dependency closure** (see §4).
   * Registers the factory in `<system.data><DbProviderFactories>` of
     machine.config for **both** Framework branches:
     `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\Config\machine.config`
     and `%WINDIR%\Microsoft.NET\Framework\v4.0.30319\Config\machine.config`.
     Edits go through `System.Xml`; a timestamped backup
     (`machine.config.mnemotron-backup-*`) is written next to each modified
     file. Re-running is a no-op when the entry is already correct.
   * Registered entry (Version is read from the assembly at install time):

     ```xml
     <add name="Mnemotron ADO.NET Data Provider for ClickHouse"
          invariant="Mnemotron.Data.ClickHouse"
          description="ADO.NET data provider for ClickHouse over HTTP(S). Unofficial; not affiliated with or endorsed by ClickHouse, Inc."
          type="Mnemotron.Data.ClickHouse.ADO.ClickHouseConnectionFactory, Mnemotron.Data.ClickHouse, Version=1.0.0.0, Culture=neutral, PublicKeyToken=1a9f1c23413d5b5e" />
     ```

2. **`deploy/install-cartridge.ps1`** — MD only: copies
   `cartridge/clickhouse.xsl` into
   * every discovered `[SSAS instance]\OLAP\bin\Cartridges\` (server side,
     auto-discovered via `HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\OLAP`
     with a filesystem fallback), and
   * the design-time cartridge folders of the Analysis Services Projects
     extension / legacy SSDT layouts (see §5).

   **Restart the SSAS service and Visual Studio afterwards** — the cartridge
   list is read at engine startup (confirmed for the server side by Vertica's
   and Devart's cartridge deployment guidance).

3. **Verify in SSDT** — run the manual SSAS smoke validation
   (`docs/ssas-smoke-checklist.md`) and record results there.

## 3. Uninstall

`Uninstall.exe` (or `Setup.exe --uninstall`) does the equivalent of both
commands below in one step; `--remove-dependencies` maps to
`-RemoveDependencies`.

```powershell
powershell -File deploy\install-cartridge.ps1 -Uninstall
powershell -File deploy\register-provider.ps1 -Uninstall                      # keeps shared deps in GAC
powershell -File deploy\register-provider.ps1 -Uninstall -RemoveDependencies  # removes them too
```

`-RemoveDependencies` GAC-removes `Microsoft.Extensions.*` / `System.*` /
`NodaTime` assemblies that other products may also have GAC'ed — use it only
when nothing else on the machine consumes them (the GAC is version-side-by-side,
so *leaving* them is always safe).

## 4. GAC strategy: provider + full dependency closure

Decision: **GAC everything from the net48 publish folder** (provider + 22
dependency assemblies, all strong-named).

Why not "GAC only the provider":

* SSAS (`msmdsrv.exe`) and VS load the provider from the GAC. Fusion then
  resolves the provider's references from (a) the GAC, (b) the *host
  process* appbase — `...\OLAP\bin\` or `...\Common7\IDE\`. There is no
  per-assembly probing path for GAC-loaded assemblies, and
  `bindingRedirect` can change a *version*, not a load *location*.
* Dropping our dependencies into `msmdsrv.exe`'s or `devenv.exe`'s own
  directory would work but is invasive and fragile across SQL/VS updates —
  rejected.

Trade-off accepted: the closure includes very common assemblies
(`System.Buffers`, `System.Memory`, `Microsoft.Extensions.*`). The GAC is
side-by-side per version+token, so installing them cannot break other
applications; the only cost is registry/disk clutter and the uninstall care
described in §3. Collapsing the closure is no longer deferred: `make
merge-net48` (`dotnet-ilrepack`, `/internalize`, repo `.snk`) produces a
single merged `Mnemotron.Data.ClickHouse.dll` with the same identity, and
the release zip ships it additively as `provider-net48-merged\` (see §0).
The *default* `Setup.exe` payload stays the unmerged folder until the
default-flip decision, gated on the SSAS smoke (issue #1).

## 5. Cartridge locations (design time)

Every Analysis Services engine binary reads a `Cartridges` folder next to
itself (confirmed from decompiled `Microsoft.AnalysisServices.BackEnd`:
`GetCartridgePath` = executing assembly directory + `\Cartridges`), which is
why the file must be copied to several places. Known locations, confirmed by
third-party cartridge deployments (community PostgreSQL cartridge; Microsoft
HIS troubleshooting doc for `db2v0801.xsl`; Vertica forum guidance):

| Consumer | Path |
|---|---|
| SSAS MD server | `%ProgramFiles%\Microsoft SQL Server\MSAS<nn>.<INSTANCE>\OLAP\bin\Cartridges\` |
| VS2019 AS Projects VSIX | `[VS]\Common7\IDE\CommonExtensions\Microsoft\SSAS\Cartridges\` (+ `LocalServer\cartridges`, `LocalServer\MSOLAP\Cartridges`, `MSOLAP\Cartridges` siblings) |
| VS2022 AS Projects VSIX | same layout under `C:\Program Files\Microsoft Visual Studio\2022\<Edition>\` — extrapolated from VS2019, `TODO(ssas-smoke)`: confirm on the operator machine |
| Legacy BIDS/SSDT | `[VS]\Common7\IDE\PrivateAssemblies\DataWarehouseDesigner\UIRdmsCartridge\` |
| SQL tools VS shell | `[SQL tools]\<ver>\Tools\Binn\VSShell\Common7\IDE\DataWarehouseDesigner\UIRdmsCartridge\` |
| MSOLAP local engine | `%ProgramFiles%[ (x86)]\Microsoft Analysis Services\AS OLEDB\<ver>\Cartridges\` |

`install-cartridge.ps1` discovers these by searching the install roots for
`UIRdmsCartridge` folders and for `Cartridges` folders that already contain
stock cartridges (`sql2000.xsl`). To find them manually:

```powershell
Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio","${env:ProgramFiles(x86)}\Microsoft Visual Studio" -Recurse -Directory -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -in 'Cartridges','UIRdmsCartridge' } | Select-Object FullName
```

## 6. How SSAS picks a cartridge

Confirmed from decompiled `Microsoft.AnalysisServices.BackEnd`
(`RDMSCartridge.FindCorrectXSL`, `DataSourceUtilities.GetDbProviderNameAndVersion`):

1. The host calls the provider's `GetSchema("DataSourceInformation")` and
   builds the key `"{DataSourceProductName}.{DataSourceProductVersion}"`.
2. Every `*.xsl` in the Cartridges folder is scanned for
   `mssqlcrt:provider` elements (namespace `urn:sql-microsoft-com:sqlcrt`).
   Elements with `managed="no"` are ignored for ADO.NET connections.
3. An exact match wins immediately; otherwise the longest matching
   `type="prefix"` value across all files wins. `sql2000.xsl` carries an
   empty prefix — the universal fallback (its `[bracket]` identifier quoting
   is what breaks ClickHouse without our cartridge).

`cartridge/clickhouse.xsl` declares
`<mssqlcrt:provider type="prefix" managed="yes" native="no">ClickHouse</mssqlcrt:provider>`
and therefore **requires the provider to report a `DataSourceProductName`
starting with `ClickHouse`** in `GetSchema("DataSourceInformation")` — see
limitation 5 below.

## 7. Known limitations and open issues

| # | Limitation | Evidence |
|---|---|---|
| 1 | **One data source per DSV.** Multi-source DSVs require `OpenRowset`, unavailable to managed (ADO.NET) providers. Pattern: one model = one ClickHouse source. | CData KB on ADO.NET providers in SSAS |
| 2 | **Import/processing only.** DirectQuery (Tabular) and ROLAP (MD) have closed provider lists in SSAS; periodic import/processing is the only mode, regardless of provider quality. No writeback. | SSAS documentation on supported DirectQuery/ROLAP sources |
| 3 | **Bitness.** Both machine.config branches are registered: the SSAS service is 64-bit; VS2022 is 64-bit but older tooling (32-bit VS shells, 32-bit test hosts) reads the 32-bit branch. | — |
| 4 | **`ClickHouseConnectionFactory.Instance` is a property, not a field.** `DbProviderFactories.GetFactory` (both .NET Framework and .NET) reflects for a public static **field** named `Instance`; a property fails with "does not have an Instance field". The conformance runner masks this by calling `RegisterFactory(invariant, instance)` directly. **Provider visibility in SSDT will fail until `src` changes `Instance` to a `public static readonly` field** — out of scope for the deploy collateral (no code changes), flagged for the provider codebase. | .NET referencesource `DbProviderFactories.GetFactory`; `tools/Conformance.Runner/Program.cs:76` |
| 5 | **`GetSchema` implements only `Columns`.** SSDT wizards need `MetaDataCollections`, `DataSourceInformation`, `Tables`, `DataTypes`, `Restrictions`, and cartridge selection needs `DataSourceInformation.DataSourceProductName` (see §6). DSV wizard and cartridge matching are expected to fail until those collections land in the provider. | `src/Mnemotron.Data.ClickHouse/Utility/SchemaDescriber.cs` |
| 6 | **Cartridge is a draft.** `cartridge/clickhouse.xsl` is built from public sources on the cartridge mechanism plus a local synthetic-input transform check (`xsltproc` output executed against a live ClickHouse), not against a live SSAS. Every unverified aspect is marked `TODO(ssas-smoke)` inline. | header of `cartridge/clickhouse.xsl` |

## 8. String column sizing and throughput (`ProbeStringLengths`)

ClickHouse `String` is unbounded, but SSIS/SSAS must map every string column to
a fixed width. Reporting a flat 4000 keeps them out of LOB (`DT_NTEXT`) handling
but bloats the row buffer for narrow columns — a wide view can spend most of its
buffer on empty space, collapsing throughput.

**`ProbeStringLengths` is on by default.** On each schema read the provider runs
one `max(lengthUTF8(col))` aggregate over the query and reports each String
column's actual maximum width (rounded up for headroom), so SSIS buffers stay
tight automatically. This is what makes a real import fast — no manual tuning.

**Caveat — this default trades cheap metadata for tight buffers. Turn it off for
very large tables:**

- **Cost:** the probe is a **full aggregate scan** of the query, run at design
  time *and* at every runtime validation. Milliseconds for import-sized tables;
  a heavy, repeated scan for tables of hundreds of millions / billions of rows.
  Repeated validations within `ProbeStringLengthsCacheTtl` seconds (default 300)
  reuse the previous result instead of re-scanning — one scan per editing/run
  burst, not one per validation. `ProbeStringLengthsCacheTtl=0` restores
  probe-every-time. Staleness inside the TTL window is bounded by the round-up
  headroom already applied to probed widths.
- **Truncation:** probed widths reflect the data *at probe time*. If a column
  later gains longer values, the width frozen in the SSIS package can truncate
  (SSIS fails or loses data). Re-refresh the source metadata after significant
  data growth.
- **To disable:** add `ProbeStringLengths=false` to the connection string. The
  provider then reports the flat `DefaultStringSize` (default 4000, the safe
  `DT_WSTR` ceiling — no truncation, no LOB). Set `DefaultStringSize` to your
  known maximum for a no-scan middle ground (e.g. `DefaultStringSize=2000`), or
  `0`/`>4000` to force LOB semantics for genuinely huge text columns.

## 9. Timeout and connection concurrency for heavy pulls

**`Timeout` (connection string, default 2 minutes) is effectively a
time-to-first-body-bytes limit.** Verified empirically against ClickHouse
25.3.14 on both net8 and the .NET Framework build:

- A query whose response body **is already streaming** is never cut by
  `Timeout` — a 140-second dripping stream completed on the default 2 minutes
  on both runtimes. This is why long multi-minute SSAS imports work.
- A query that produces **no body bytes within `Timeout`** dies with
  `TaskCanceledException` at exactly the deadline, zero rows delivered. Two
  ways to get there in practice: a heavy aggregation (`GROUP BY` over billions
  of rows) that computes for minutes before the first block, or a **small /
  highly-compressible result that sits in the server's ~1 MB HTTP output
  buffer** until the query finishes (with `Compression=true`, the default, a
  well-compressing result can hide there far longer than intuition suggests).

**Recommendation:** for cube processing / heavy extraction queries set
`Timeout` explicitly with headroom over the slowest expected
query-start-to-first-block time, e.g. `Timeout=1800` (seconds) for
half-hour-class processing runs. `Timeout` only bounds the wait for the
stream to start flowing, so a generous value does not risk hung imports —
pair it with a server-side `max_execution_time` if runaway queries are a
concern.

**Connection concurrency (net4x):** the provider raises the shared HTTP
handler's `MaxConnectionsPerServer` to 16 on .NET Framework (the legacy stack
caps a client process at `ServicePointManager.DefaultConnectionLimit` —
historically 2 — concurrent connections per host, which would serialize
parallel SSIS dataflows against the same ClickHouse). .NET 8 has no such cap;
nothing changes there.
