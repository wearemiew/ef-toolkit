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
- `PageRequest` and `PagingOptions`: bind `page`/`pageSize`/`sort` from the query string with defaults (page 1, 20 items) and a page-size cap (100); new `ToPagedListAsync(PageRequest[, PagingOptions])` overloads.
- `SortMap<T>` and `ApplySort`: allow-listed, client-driven sorting (`?sort=-price,name`) with a stable tie-breaker.
- `WhereIf`: apply a filter only when a condition holds.
- `InvalidQueryRequestException`: thrown only for bad client paging/sorting input, so APIs can map it to a 400 without masking server bugs.
- Sample app (`samples/`) with a smoke test that exercises the packed package end to end.
- `PagedResult<T>.Map(selector)` to project items while keeping the paging metadata.
- `CancellationToken` support.
- The `COUNT` query is skipped when the fetched page already shows where the data ends.
- Package metadata: description, MIT license, README, XML docs and SourceLink with an embedded PDB.
- Automated CI/CD (`.github/workflows/ci.yml` + GitVersion): every PR is built and tested; merges to `dev` publish `-dev.N` prereleases; merges to `main` publish, tag `vX.Y.Z` and create a GitHub release. Version bumps follow Conventional Commits.

### Changed
- Releases are no longer created by hand: the manual release-triggered `publish.yml` is replaced by the pipeline above, running on the repo's self-hosted runners (`ef-toolkit-build`, `dotnet-build`) with minimal permissions.
- Tests run against SQLite in-memory instead of mocked queryables.

## [1.0.2] - 2024-11-08

- Last 1.x release.
