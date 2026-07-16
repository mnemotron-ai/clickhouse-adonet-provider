# Maintainer guide

This file collects the maintainer-only mechanics that don't belong in
`CONTRIBUTING.md` (which is written for contributors): cutting a release,
how PRs get merged, enabling branch protection, and the Actions security
checklist to run through before the repository goes public.

**The repository visibility flip itself (private → public) is a maintainer
decision made outside this guide** — a deliberate, one-way settings change
in repository Settings → General → Danger Zone. This document only prepares
the file-side state so that flip is safe to make; it does not tell you when
to make it.

## Release process

Releases are cut by pushing a tag matching `vX.Y.Z` (a stable release) or
`vX.Y.Z-pre` (a pre-release, e.g. `v0.3.0-pre.1`), which is intended to
trigger a release workflow that builds, packs, and publishes the
`Mnemotron.Data.ClickHouse` NuGet package and attaches build artifacts to a
GitHub Release.

> As of this writing there is no `.github/workflows/release.yml` in the
> repository yet — `ci.yml` and `windows.yml` are the only workflows, and
> both are the merge gate, not a publish step. This section documents the
> intended tag-triggered contract so that when the release workflow is
> added, its trigger shape is already agreed. Until then, releases are
> manual (`dotnet pack` + upload).

Before tagging a release:

1. `make ci` is green on `main` at the commit you intend to tag.
2. `CHANGELOG.md` has an entry for the version (move it out of
   `[Unreleased]`).
3. `docs/ssas-smoke-checklist.md` has been run against the release candidate
   on Windows and recorded via a PR (see that file's header).

## How PRs are reviewed and merged

- `main` is PR-only (enforced locally by `.githooks/pre-push`, and should be
  enforced server-side once branch protection is on — see below).
- The CI gate (`.github/workflows/ci.yml`, job `gate`, i.e. `make ci`:
  lint + build + test + replay + conformance) must be green before merge.
  This is the actual correctness gate; nothing else in this repo overrides
  it.
- Conformance rules are binding, not stylistic (see
  `docs/architecture.md` → Invariants and `conformance/README.md`):
  - `conformance/golden/**` is never hand-edited; it changes only via
    `make golden` / `make golden-schema`, with the reason stated in the PR.
  - `conformance/policy.json` changes only with maintainer sign-off and a
    written justification in the PR.
- PR titles start with `WS-N:`; commit messages are plain, no AI-generated
  trailers or attribution — this applies to maintainers merging PRs too
  (watch for squash-merge commit messages picking up trailers from the
  PR body).
- `.github/CODEOWNERS` requires a maintainer review on `.github/`,
  `conformance/golden/`, and `conformance/policy.json` specifically, and
  is the default owner for everything else.
- `.github/workflows/claude-review.yml` posts an automated review comment
  on same-repo PRs (and on fork PRs when a maintainer asks for it via an
  `@claude` mention — see that file's header for the reasoning). Treat it
  as a first pass, not a substitute for a maintainer's own review; it has
  no authority to approve or merge.

## Enabling branch protection on `main` (once public)

Run once, after the repository is switched to public (adjust
`required_approving_review_count` if solo-maintaining):

```sh
gh api \
  --method PUT \
  -H "Accept: application/vnd.github+json" \
  repos/mnemotron-ai/clickhouse-adonet-provider/branches/main/protection \
  --input - <<'JSON'
{
  "required_status_checks": {
    "strict": true,
    "contexts": ["gate", "windows"]
  },
  "enforce_admins": true,
  "required_pull_request_reviews": {
    "required_approving_review_count": 1,
    "require_code_owner_reviews": true
  },
  "restrictions": null,
  "allow_force_pushes": false,
  "allow_deletions": false
}
JSON
```

Notes:

- `"gate"` is the job id in `.github/workflows/ci.yml`; `"windows"` is the
  job id in `.github/workflows/windows.yml`. Confirm the exact check names
  in Settings → Branches → Add rule → "Add checks" (it autocompletes from
  recent check runs) before relying on this call — job ids are what GitHub
  uses as the check context today, but that's worth eyeballing once rather
  than trusting a doc.
- `windows.yml` is path-filtered (`src/**`, `tests/**`, `tools/**`,
  `**/*.csproj`, `*.sln`, `global.json`; see the file's header comment for
  why). A docs-only PR won't produce a `windows` check run at all. If
  requiring it as a status check blocks docs-only PRs from merging, drop
  `"windows"` from `contexts` and re-add it once you've confirmed GitHub's
  handling of required-but-unrun path-filtered checks behaves the way you
  want on this repo.
- `enforce_admins: true` means maintainers are bound by the same gate too —
  intentional, given the Invariants in `docs/architecture.md`.

## Actions security checklist for going public

Work through this before (or as part of) the visibility flip:

- [ ] **Default workflow token permissions are read-only.** Settings →
      Actions → General → Workflow permissions → "Read repository contents
      permission" (not "Read and write"). Individual workflows request
      elevated permissions explicitly via their own `permissions:` block
      (see `claude-review.yml` for an example of scoping this per-job).
- [ ] **Fork PR runs require approval.** Settings → Actions → General →
      Fork pull request workflows from outside collaborators → "Require
      approval for all outside collaborators" (or at least for first-time
      contributors). This is the control that stops an untrusted PR from
      running Actions unattended, independent of anything in the workflow
      files themselves.
- [ ] **Secrets audit.** Confirm `ANTHROPIC_API_KEY` (used by
      `claude-review.yml`) is the only repository secret, it has no more
      access than needed (a scoped Anthropic API key, not an
      organization-wide credential), and nothing else picked up a secret
      during private development that shouldn't ship into a public Actions
      log.
- [ ] **No `pull_request_target` usage anywhere.** Grep
      `.github/workflows/` for it; there should be none. All PR-triggered
      workflows in this repo use `pull_request`, which does not receive
      elevated permissions or secrets from fork contexts the way
      `pull_request_target` does.
- [ ] **`claude-review.yml` prerequisites are in place**: the
      `ANTHROPIC_API_KEY` secret exists, and the Claude GitHub app
      (https://github.com/apps/claude) is installed on the repository. Both
      are maintainer setup steps outside this file — see the workflow's
      header comment.
- [ ] **Branch protection is enabled** (previous section) so `main` cannot
      be pushed to directly or force-pushed once the local
      `.githooks/pre-push` guard isn't the only thing stopping it.
