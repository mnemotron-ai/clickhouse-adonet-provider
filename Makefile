# Build and conformance targets. Target names and semantics are a stable
# contract relied on by CI — see conformance/README.md.
# Reference ClickHouse: docker compose up -d (pinned version, port 18123).
# CI overrides the endpoints via CH_URL / CH_CONNECTION / CLICKHOUSE_CONNECTION.

RUNNER = dotnet run --project tools/Conformance.Runner -c Release --
export CLICKHOUSE_CONNECTION ?= Host=localhost;Port=18123;Protocol=http;Username=default

.PHONY: golden golden-schema fixture replay conformance lint test build ci hooks oracle-up self-parity

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
