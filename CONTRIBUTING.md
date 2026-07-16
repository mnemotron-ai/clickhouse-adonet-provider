# Contributing

Correctness in this repo is defined by the conformance suite, not by opinion:
every change must keep `make ci` green.

## Rules

- `main` is PR-only; every PR must pass the gate (`make ci`).
- `conformance/golden/**` is generated — never edit golden files by hand.
  They are regenerated only via `make golden` (queries, captured from a real
  ClickHouse server) or `make golden-schema` (schema collections). A PR that
  hand-edits goldens or relaxes `conformance/policy.json` without maintainer
  sign-off will be rejected.
- PR titles start with the workstream number: `WS-N: <what>`.
- Plain commit messages; all documentation and code comments in English.

## Local setup

```sh
make hooks             # installs the pre-push guard for main
docker compose up -d   # ClickHouse (pinned version) for tests and conformance
make ci                # lint + build + tests + conformance
```

See `conformance/README.md` for how the conformance suite works.
