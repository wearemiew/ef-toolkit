# library layer

## Commands (from the repo root)

- Build: `dotnet build EntityFrameworkToolKit/EntityFrameworkToolKit.sln -c Release -warnaserror`
- Test: `dotnet test EntityFrameworkToolKit/EntityFrameworkToolKit.sln`
- Format check: `dotnet format EntityFrameworkToolKit/EntityFrameworkToolKit.sln --verify-no-changes`
- Pack: `dotnet pack EntityFrameworkToolKit/EntityFrameworkToolKit/EntityFrameworkToolKit.csproj -c Release`

## Conventions

- File-scoped namespaces; one public type per file, grouped by feature folder (`Pagination/`, `Sorting/`, `Filtering/`), each with its own namespace.
- Every public member has XML docs (`GenerateDocumentationFile` is on, so missing docs are warnings).
- Async methods end in `Async` and take `CancellationToken cancellationToken = default` as the last parameter, passed to every EF call.
- Validate public arguments up front with `ArgumentOutOfRangeException` / `ArgumentNullException`.
- Tests use xUnit against SQLite in-memory (`TestHelpers/TestDbContext.cs`), not mocked queryables, so SQL translation is exercised.

## Design principles

- Public API is a breaking-change surface: prefer immutable result types (sealed classes that validate in the constructor, `IReadOnlyList<T>`) and don't expose setters. Avoid `record` for types holding collections — its value equality compares them by reference.
- Result types are plain data. They must not implement `IEnumerable<T>` — serializers would drop their metadata.
- Keep helpers as extension methods over `IQueryable<T>`; no base classes, no DI registration.
- Anything that takes client input (sort names, page sizes) must be allow-listed or clamped by a server-side type (`SortMap<T>`, `PagingOptions`) that is never bound from the request.
- Internal helpers (e.g. `PaginationGuard`) stay `internal`; tests reach them via `InternalsVisibleTo`.
