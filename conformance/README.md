# Conformance suite

Differential test suite: every corpus case is executed both against a real
ClickHouse server (the reference) and through this provider; outputs must
match. The comparator's exit code is the merge gate.

## Layout

| Path | Meaning |
|---|---|
| `corpus/` | inputs: `<class>-NNN-<hash8>.sql` (queries) and `.json` (schema cases) |
| `golden/` | expected outputs — **generated, never hand-edited** |
| `actual/` | provider outputs from the last replay (transient, git-ignored) |
| `policy.json` | the single place where any tolerance is defined and justified |
| `allowlist.txt` | temporarily accepted failing cases; must shrink to empty |
| `frontier.txt` | list of cases not yet matching; empty header = batch done |

## Targets

```sh
make golden        # regenerate query goldens from a raw ClickHouse HTTP response
make golden-schema # regenerate schema-collection goldens (reviewed before freeze)
make replay        # run the corpus through the provider -> conformance/actual/
make conformance   # structural compare, policy-aware; exit 0 = parity
make self-parity   # comparator sanity check: golden vs golden must be 100% green
```

## Rules

- Golden files change only through the `make golden*` targets. A hand-edited
  golden is a lie with a green checkmark.
- Every tolerance (float epsilon, trailing-zero normalization, …) lives in
  `policy.json` with a written justification. Nothing is ignored silently.
- Query corpus cases must be deterministic: explicit `ORDER BY`, pinned server
  version (`docker-compose.yml`), fixed UTC timezone.
- A failing case is either fixed in the provider, or triaged: bad corpus case
  (fix the case, regenerate its golden), or reference nondeterminism (extend
  `policy.json` with justification). Never special-case a single input.
