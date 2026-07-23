# Build and conformance targets. Target names and semantics are a stable
# contract relied on by CI — see conformance/README.md.
# Reference ClickHouse: docker compose up -d (pinned version, port 18123).
# CI overrides the endpoints via CH_URL / CH_CONNECTION / CLICKHOUSE_CONNECTION.

RUNNER = dotnet run --project tools/Conformance.Runner -c Release -f net8.0 --
export CLICKHOUSE_CONNECTION ?= Host=localhost;Port=18123;Protocol=http;Username=default

.PHONY: golden golden-schema fixture replay conformance lint test build ci hooks oracle-up self-parity replay-net48 conformance-net48 merge-net48 bench

oracle-up:
	docker compose up -d --wait

# Query snapshot ceremony: the ONLY way *.sql goldens change
# (skips *.json schema cases — those are snapped by golden-schema).
golden:
	$(RUNNER) golden conformance/corpus conformance/golden

# Fixture database for schema cases (idempotent): conformance/fixture.sql over raw HTTP.
fixture:
	$(RUNNER) fixture conformance/fixture.sql

# Schema-collection snapshot ceremony: ONLY *.json schema cases, through the
# PROVIDER -> conformance/golden/schema-*.golden. Unlike query goldens these
# have no external reference: the snapped output is human-reviewed in the PR
# and then frozen (see conformance/README.md).
golden-schema: fixture
	$(RUNNER) golden-schema conformance/corpus conformance/golden

# Run the corpus through OUR provider -> conformance/actual/
# (deterministic: the runner wipes the directory and iterates in sorted order).
replay: fixture
	$(RUNNER) replay conformance/corpus conformance/actual

# Structural comparator; tolerances live in conformance/policy.json,
# temporarily accepted reds in conformance/allowlist.txt (must shrink).
# Exit 0 = parity.
conformance:
	$(RUNNER) compare conformance/golden conformance/actual conformance/policy.json conformance/allowlist.txt

# net48-under-mono leg (issue #3): replays the provider's .NET Framework
# build path, not just net8.0. Bypasses $(RUNNER)/dotnet run (no net48 apphost
# on linux/mac) and runs the built exe under mono directly.
replay-net48: fixture
	dotnet build tools/Conformance.Runner -c Release -f net48
	mono tools/Conformance.Runner/bin/Release/net48/conformance-runner.exe replay conformance/corpus conformance/actual-net48

# Same compare as `conformance`, against the net48 replay output.
conformance-net48:
	$(RUNNER) compare conformance/golden conformance/actual-net48 conformance/policy.json conformance/allowlist.txt

# Comparator sanity check: golden vs golden must be 100% green
# (no allowlist here — honest entries would show up as stale).
self-parity:
	rm -rf conformance/actual && cp -R conformance/golden conformance/actual
	$(RUNNER) compare conformance/golden conformance/actual conformance/policy.json

lint:
	dotnet format ClickHouseAdoNetProvider.sln --verify-no-changes --severity error

test:
	dotnet test tests/Mnemotron.Data.ClickHouse.Tests -c Release -f net8.0 -v q

build:
	dotnet build ClickHouseAdoNetProvider.sln -c Release -v q

# The PR gate. CI runs exactly this; conformance = live replay + compare.
ci: lint build test replay conformance

hooks:
	git config core.hooksPath .githooks

# Manual throughput bench (NOT in ci): provider vs raw-HTTP ceiling, compression axis.
# BENCH_ARGS e.g. "provider 5000000 nocompress"; default = full matrix on 2M rows.
bench:
	$(RUNNER) bench $(BENCH_ARGS)

# Release-time only (issue #5): merge the published net48 provider +
# dependency closure into one strong-named assembly via ILRepack, for the
# additive provider-net48-merged/ payload in the release zip. Expects
# publish/provider-net48 to already exist (release.yml publishes it first).
# NOT wired into `ci` — the merge is exercised only when release.yml runs
# on a tag push.
merge-net48:
	dotnet tool restore
	rm -rf publish/provider-net48-merged
	mkdir -p publish/provider-net48-merged
	cd publish/provider-net48 && dotnet ilrepack /internalize \
		/keyfile:$(CURDIR)/Mnemotron.Data.ClickHouse.snk \
		/lib:$(HOME)/.nuget/packages/microsoft.netframework.referenceassemblies.net48/1.0.3/build/.NETFramework/v4.8 \
		/out:$(CURDIR)/publish/provider-net48-merged/Mnemotron.Data.ClickHouse.dll \
		Mnemotron.Data.ClickHouse.dll $$(ls *.dll | grep -v '^Mnemotron.Data.ClickHouse.dll$$')
