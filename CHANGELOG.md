# Changelog

All notable changes to this project are documented in this file.

## [2.0.0] - Unreleased

### Breaking
- `PaginatedIEnumerable<T>` and `PaginatedList<T>` are replaced by the immutable `PagedResult<T>` class (`Items`, `Page`, `PageSize`, `TotalCount`, `TotalPages`, `HasPreviousPage`, `HasNextPage`).
- `AddPagination` → `ToPagedListAsync(int page, int pageSize, CancellationToken)`. Invalid `page`/`pageSize` now throws `ArgumentOutOfRangeException` instead of returning an empty result.
- `AddOptionalPagination` → `ToOptionalPagedListAsync(int? page, int? pageSize, CancellationToken)`. The fallback (return everything when paging values are missing or invalid) is unchanged.
- Paging an unordered query throws `InvalidOperationException`.

### Fixed
- The total count was lost when a paged result was serialized to JSON (the old type serialized as a bare array).
- `PaginatedList<T>` threw `NotImplementedException` when enumerated.
- A large page number could overflow the skip offset; it now throws `ArgumentOutOfRangeException`.
- The repository failed to build because `global.json` pinned a .NET 8 preview SDK.

### Added
- `PagedResult<T>.Map(selector)` to project items while keeping the paging metadata.
- `CancellationToken` support.
- The `COUNT` query is skipped when the fetched page already shows where the data ends.
- Package metadata: description, MIT license, README, XML docs and SourceLink with an embedded PDB.
- CI workflow that builds, checks formatting and runs the tests on every push and pull request to `dev`/`main`.

### Changed
- Publishing triggers on `published` releases (not drafts), runs on GitHub-hosted runners, runs the tests first and requests minimal permissions.
- Tests run against SQLite in-memory instead of mocked queryables.

## [1.0.2] - 2024-11-08

- Last 1.x release.
