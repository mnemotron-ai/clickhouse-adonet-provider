# SSAS / SSDT smoke checklist (manual, Windows)

Manual smoke test on Windows (VS2022 + the Analysis Services Projects
extension + an SSAS instance, Multidimensional and Tabular). Run before every
release and record the results here via a PR.

Prerequisites: the net4x provider build is installed into the GAC and the
factory is registered in both 32-bit and 64-bit `machine.config` (see
`deploy/`), and a test ClickHouse server is reachable from the machine.

| # | Check | How | Status | Date / notes |
|---|---|---|---|---|
| 1 | Provider appears in the SSDT Connection Manager | VS2022 → MD project → Data Source Wizard → the provider list shows "Mnemotron ADO.NET Data Provider for ClickHouse" (x86 design-time) | ☐ | |
| 2 | DSV wizard reads tables/columns | Data Source View Wizard → the ClickHouse table tree renders (GetSchema: Tables/Columns), selecting 2–3 tables builds a DSV | ☐ | |
| 3 | MD cube completes Process Full | A cube on 2–3 tables → Process Full with no SQL errors (with the draft `clickhouse.xsl` cartridge or passing on the default one — record which worked) | ☐ | |
| 4 | Tabular (1400+) imports data | Tabular project → legacy provider data source pointing at the provider → import the same tables into VertiPaq | ☐ | |

Cartridge outcome (draft `clickhouse.xsl` vs default): _____
