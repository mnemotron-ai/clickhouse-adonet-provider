# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning: [SemVer 2.0](https://semver.org/).

## [Unreleased]

### Added
- Fork of ClickHouse.Client 7.14.0 as `Mnemotron.Data.ClickHouse`
  (net462/net48/net8.0, strong-named).
- Conformance suite: query corpus, golden outputs captured from a pinned
  ClickHouse 25.3 (HTTP `TSVWithNamesAndTypes`), replay through the provider,
  structural comparator with a written tolerance policy.
- `GetDataTypeName` returns the verbatim server type string.
