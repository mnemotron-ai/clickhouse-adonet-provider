# SSAS / SSDT smoke checklist (manual, Windows)

Manual smoke test on Windows (VS2022 + the Analysis Services Projects
extension + an SSAS instance, Multidimensional and Tabular). Run before every
release and record the results here via a PR.

Prerequisites: the net4x provider build is installed into the GAC and the
factory is registered in both 32-bit and 64-bit `machine.config` (see
`deploy/`), and a test ClickHouse server is reachable from the machine.

| # | Check | How | Status | Date / notes |
|---|---|---|---|---|
| 1 | Provider appears in the SSDT Connection Manager | VS2022 → MD project → Data Source Wizard → the provider list shows "Mnemotron ADO.NET Data Provider for ClickHouse" (x86 design-time) | ☑ | 2026-07-20, VS2022 on MSAS16 host: provider listed, Test Connection succeeded against CH 26.3 |
| 2 | DSV wizard reads tables/columns | Data Source View Wizard → the ClickHouse table tree renders (GetSchema: Tables/Columns), selecting 2–3 tables builds a DSV | ☑ | 2026-07-20, after four fixes found by this smoke (see below): tree renders 729 objects, a 4-object DSV (3 dimension views + a 46.8M-row fact view) builds cleanly |
| 3 | MD cube completes Process Full | A cube on 2–3 tables → Process Full with no SQL errors (with the draft `clickhouse.xsl` cartridge or passing on the default one — record which worked) | ☑ | 2026-07-20, MSAS16 + CH 26.3: 3 dimensions + cube over the full 46.8M-row fact — Process Full commits (`state=Processed`), fact partition read 46,886,788 rows in ~10 min. MDX grand totals over all 11 measures match direct ClickHouse SUMs exactly (residual deltas = 7 rows of live CDC stream that arrived after the read) |
| 4 | Tabular (1400+) imports data | Tabular project → legacy provider data source pointing at the provider → import the same tables into VertiPaq | ☐ | not attempted (KiloOlap target is MD-only); optional |

Cartridge outcome (draft `clickhouse.xsl` vs default): **draft cartridge works live** — the generated SQL (double-quoted identifiers, plain SELECTs) processed dimensions and a 46.8M-row measure-group partition with zero SQL errors. One cartridge fix was required: the design-time `schema-classes` assembly name is `Microsoft.DataWarehouse.AS` in VS2022 (the old `Microsoft.DataWarehouse` name throws FileNotFound, which the DSV wizard swallows into an empty tree). Q1 (fix XSL vs in-provider translation) is answered: keep the XSL path.

## Findings from the 2026-07-20 live smoke (all fixed on this branch)

1. **GetSchema restriction shapes** — SSDT passes SqlClient-shaped restriction
   arrays (Tables/Columns = 4, Views = 3); the provider declared 2/2/3 and
   threw, which SSDT swallows into an empty DSV tree. Now SqlClient shapes,
   extras ignored.
2. **Cartridge schema-class assembly** — must be `Microsoft.DataWarehouse.AS`
   (VS2022 extension name), not `Microsoft.DataWarehouse`.
3. **Combined CommandBehavior flags** — the wizard's schema read passes
   `SchemaOnly | KeyInfo`; an exact-value switch streamed the full table.
   Now flag-tested, wrapped as a `LIMIT 0/1` subquery.
4. **`Nullable(Nothing)` columns** (literal `NULL AS x` in views) surfaced CLR
   type `DBNull`, rejected by DataTable ("Invalid storage type: DBNull").
   Now reported as `object`.
5. **Operational requirement, not a code fix**: connection strings for
   SSAS/SSDT must set `UseCustomDecimals=False` — `Decimal(P>28)` otherwise
   surfaces the custom `ClickHouseDecimal` CLR type, which the server's
   System.Data allow-list rejects ("Type ... is not allowed here",
   fwlink 2132227). With `False`, such columns read as `System.Decimal`
   (values wider than its range would overflow — acceptable for business
   data). If a DSV was built before setting it, the type is baked into the
   DSV XML: refresh does not rewrite existing columns — re-add the tables or
   patch `msdata:DataType` out of the `.dsv`.
