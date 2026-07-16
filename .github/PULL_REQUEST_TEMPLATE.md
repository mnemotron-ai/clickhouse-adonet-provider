<!--
Title must start with the workstream number: `WS-N: <what>` (see CONTRIBUTING.md).
-->

## What

<!-- What does this PR change, and why? -->

## Checklist

- [ ] `make ci` passes locally (lint + build + test + replay + conformance).
- [ ] `conformance/golden/**` is untouched, **or** it was regenerated via
      `make golden` / `make golden-schema` and the reason is stated below.
- [ ] `conformance/policy.json` is untouched. (If it had to change, this PR
      has explicit maintainer sign-off and a written justification.)
- [ ] PR title starts with `WS-N:`.
- [ ] All new/changed documentation and code comments are in English.
- [ ] Commit messages are plain (no AI-generated trailers/attribution).

## If goldens or policy.json changed

<!--
Required only if the checklist above is not a clean "untouched": explain
what changed, which `make golden*` target regenerated it, and why.
-->

## Related issues

<!-- Closes #... / Relates to #... -->
