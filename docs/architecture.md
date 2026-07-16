# Mnemotron.Data.ClickHouse — architecture map

Managed ADO.NET data provider for ClickHouse targeting SSAS (Multidimensional
and Tabular) and .NET Framework; fork of ClickHouse.Client 7.14.0. Goal:
parity with a live ClickHouse reference server (pinned version, Docker) —
whatever the server returns over HTTP, the provider must surface through
`DbDataReader` without distortion. The conformance suite is the arbiter of
correctness — see `conformance/README.md`.

This file is a map so contributors skip re-exploration. One line per file;
read the file's own header comment for detail. **Keep it current when you add
a file.**

## Flow

```
corpus/*.sql ──make golden──▶ ClickHouse HTTP (raw, FORMAT TSVWithNamesAndTypes) ──▶ golden/
      │                                                                                │
      └────make replay──▶ Mnemotron.Data.ClickHouse (DbProviderFactory→Reader) ──▶ actual/
                                                                                       │
                                        make conformance (policy.json) ◀──────────────┘
                                        exit 0 = parity = mergeable

corpus/*.json ──make golden-schema──▶ GetSchema through the provider ──▶ golden/schema-*
               (snapshot reviewed by a maintainer in the PR, then frozen)
```

## Modules

| Path | Role |
|---|---|
| `src/Mnemotron.Data.ClickHouse/` | the provider (fork of ClickHouse.Client 7.14.0, MIT notices preserved) |
| `src/Mnemotron.Data.ClickHouse/Utility/SchemaDescriber.cs` | `GetSchema` collections (standard ADO.NET shapes) |
| `tools/Conformance.Runner/` | golden/golden-schema/replay/compare/fixture CLI (net8) |
| `conformance/corpus/` | inputs: `<class>-NNN-<hash8>.sql` (queries) and `.json` (schema cases) |
| `conformance/golden/` | expected outputs — generated only via `make golden*` |
| `conformance/policy.json` | comparator tolerance policy (the only place deviations are defined) |
| `conformance/frontier.txt` | cases not yet matching; empty header = batch done |
| `conformance/allowlist.txt` | temporarily accepted red cases; stale entries fail the gate |
| `conformance/fixture.sql` | fixture database for schema cases (idempotent; applied via `make fixture`) |
| `conformance/gen-corpus-batch1.sh` | corpus batch 1 generator (queries; idempotent) |
| `conformance/gen-corpus-schema.sh` | schema-case generator (GetSchema JSON descriptors; idempotent) |
| `docs/ssas-smoke-checklist.md` | manual SSAS/SSDT smoke checklist (Windows) |
| `docs/deploy-windows.md` | Windows deployment: register-provider → install-cartridge → SSAS smoke validation; known limitations |
| `deploy/register-provider.ps1` | GAC + machine.config (x86/x64) provider registration, idempotent, `-Uninstall` |
| `deploy/install-cartridge.ps1` | deploys clickhouse.xsl to SSAS server + design-time cartridge folders, `-Uninstall` |
| `cartridge/clickhouse.xsl` | draft XSL cartridge for SSAS MD; pending empirical verification (see file header) |
| `docker-compose.yml` | pinned reference ClickHouse (port 18123) |
| `Makefile` | gate contract: golden/golden-schema/fixture/replay/conformance/ci/hooks |
| `.github/workflows/ci.yml` | CI gate: live replay vs a ClickHouse service container |
| `.github/workflows/windows.yml` | Windows compile check (path-filtered) |
| `.github/workflows/claude-review.yml` | automated PR review comment (`anthropics/claude-code-action`); same-repo PRs auto, fork PRs via maintainer `@claude` mention |
| `.githooks/pre-push` | local guard: main is PR-only |
| `global.json` | SDK 9 pin (dotnet format rules are SDK-dependent) |
| `CODE_OF_CONDUCT.md` | Contributor Covenant v2.1; enforcement via GitHub private reporting |
| `.github/ISSUE_TEMPLATE/` | bug report / feature request forms, blank-issue + security contact config |
| `.github/PULL_REQUEST_TEMPLATE.md` | PR checklist mirroring CONTRIBUTING (gate, goldens, policy, title, comments) |
| `.github/CODEOWNERS` | default owner + explicit review requirement for `conformance/golden/`, `conformance/policy.json`, `.github/` |
| `.github/dependabot.yml` | weekly `nuget` + `github-actions` dependency update PRs |
| `docs/maintainer-guide.md` | release process, PR merge rules, branch protection setup, public-readiness security checklist |

## Conformance

- Goldens: `conformance/golden/` — expected outputs for the corpus.
- Comparator + replay: `make ci` is the gate; exit 0 = parity.
- Frontier: `conformance/frontier.txt` — cases not yet matching.
- Details and rules: `conformance/README.md`.

## Invariants

- **Bug-for-bug**: match the reference server even where it looks "wrong".
  No result-changing PR merges without a green live gate.
- **main is PR-only** (`.githooks/pre-push`); a maintainer merges.
- **Goldens are never hand-edited** — regenerated only via `make golden` /
  `make golden-schema`, and schema snapshots are reviewed before freezing.
- **The comparator policy (`conformance/policy.json`) changes only with
  maintainer sign-off**, via a PR with a written justification.
- **Plain commit messages and PR bodies** — no generated-by trailers.
- **Trademark**: the product name follows the "… for ClickHouse" form; no
  ClickHouse logo.
