# Support matrix

What this provider is actually proven against, and what is a documented gap.

## ClickHouse server

**Primary gate: ClickHouse `25.3.14`.** Pinned in `docker-compose.yml` and
`.github/workflows/ci.yml`. Every merge to `main` is proven against exactly
this version via `make ci` — live replay of the full conformance corpus
against a real server, structurally compared to golden output
(`conformance/README.md`). This is the only version-check that blocks a PR.

**Compatibility signal: `.github/workflows/compat.yml`.** Runs weekly
(Monday 06:00 UTC) and on manual dispatch, across:

| ClickHouse version | Role |
|---|---|
| `24.8` | minimum supported LTS floor (decided 2026-07-18) |
| `25.3.14` | same pin as the merge gate, for a stable cross-check point |
| `latest` | newest published server image |

This workflow is **non-blocking** — it does not gate PRs and does not run on
pull requests at all (schedule + `workflow_dispatch` only). A red leg means a
maintainer triages: fix a real incompatibility, document a known limitation,
or move the supported floor. It is signal, not a gate.

### Why the split

The conformance goldens in `conformance/golden/` are captured against the
pinned `25.3.14` server and are **version-bound** — in particular the 8
`schema-*` corpus cases (`conformance/corpus/*.json` →
`conformance/golden/schema-*.golden`) read live `system.*` tables directly,
and the shape of those tables is not stable across ClickHouse versions.
Running `make replay` / `make conformance` (golden-compare) against any
version other than `25.3.14` is expected to produce false-positive diffs that
say nothing about real provider compatibility.

So `compat.yml` never runs golden-compare. Instead it runs the **unit test
suite** (`make test`) with `CLICKHOUSE_VERSION` set to the matrix version.
The test suite self-gates on that variable:

- `tests/Mnemotron.Data.ClickHouse.Tests/TestUtilities.cs` reads
  `CLICKHOUSE_VERSION` and computes the feature set available on that server
  (`src/Mnemotron.Data.ClickHouse/ADO/Feature.cs`) — the literal string
  `latest` is treated as "every feature available", mirroring `ci.yml`'s own
  behavior against the pinned server.
- Tests that depend on a version-gated feature (or need to skip on/around a
  specific version) declare it explicitly with
  `tests/Mnemotron.Data.ClickHouse.Tests/Attributes/FromVersionAttribute.cs`
  and
  `tests/Mnemotron.Data.ClickHouse.Tests/Attributes/IgnoreInVersionAttribute.cs`,
  which skip themselves (with an inconclusive result, not a failure) when the
  running server doesn't meet the declared version constraint.

At the floor (`24.8`), every feature in `Feature.cs` is available except
`Dynamic`, which needs `25.1` — those tests self-skip on `24.8` rather than
fail.

## SQL Server Analysis Services / Visual Studio / SSIS

| Component | Status |
|---|---|
| SSAS 2019 (Multidimensional + Tabular) | Exercised by the manual smoke checklist, `docs/ssas-smoke-checklist.md`. |
| SSAS 2022 (Multidimensional + Tabular) | Exercised by the manual smoke checklist, `docs/ssas-smoke-checklist.md`. |
| VS2022 + Analysis Services Projects extension | Exercised by the manual smoke checklist, `docs/ssas-smoke-checklist.md`. |
| VS2019 + Analysis Services Projects extension | Documented in the cartridge path table (`docs/deploy-windows.md`, §5) — the design-time cartridge location is known and deployed to, but VS2019 is **not yet independently smoke-tested**. Known gap. |
| SSIS / SSDT | Supported via the same VS/SSDT tooling path as SSAS Tabular; there is **no dedicated SSIS smoke checklist yet**. Known gap. |

The manual checklist (`docs/ssas-smoke-checklist.md`) is run before every
release and its results are recorded there via a PR. It is the only version
combination independently verified end-to-end; the two gaps above are called
out honestly rather than implied by omission.
