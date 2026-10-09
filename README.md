# EntityFrameworkToolKit

A lightweight toolkit that extends Entity Framework Core with useful utilities and helpers.

[![CI](https://github.com/wearemiew/ef-toolkit/actions/workflows/ci.yml/badge.svg)](https://github.com/wearemiew/ef-toolkit/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/wearemiew/ef-toolkit?label=package)](https://github.com/wearemiew/ef-toolkit/pkgs/nuget/Miew.EntityFramework.Toolkit)

## Installation

The package is published to GitHub Packages. Add the feed once (GitHub requires a token with `read:packages`, even for public packages):

```bash
dotnet nuget add source https://nuget.pkg.github.com/wearemiew/index.json --name wearemiew --username YOUR_GITHUB_USERNAME --password YOUR_GITHUB_TOKEN --store-password-in-clear-text
```

Then install:

```bash
dotnet add package Miew.EntityFramework.Toolkit
```

## Features

### Pagination

Extension methods on `IQueryable<T>` that run the query one page at a time and return a `PagedResult<T>`:

| Member | Description |
|---|---|
| `Items` | The items on this page (`IReadOnlyList<T>`) |
| `Page` | The 1-based page number |
| `PageSize` | The maximum number of items per page |
| `TotalCount` | The total number of items across all pages |
| `TotalPages` | `ceil(TotalCount / PageSize)` |
| `HasPreviousPage` / `HasNextPage` | Convenience flags for pagination controls |
| `Map(selector)` | Projects the items (e.g. entities → DTOs), keeping the metadata |

`PagedResult<T>` is an immutable, non-enumerable class, so returning it from an ASP.NET Core endpoint serializes the items **and** the metadata.

> **The query must be ordered.** Paging without `OrderBy` lets the database return rows in any order, so pages can overlap or skip rows. Both methods throw `InvalidOperationException` when paging an unordered query. Ordering before `Where`/`Select` is fine. Operators that discard ordering in SQL (`Distinct`, `GroupBy`, `Union`/`Concat`) should come *before* `OrderBy`, and raw SQL (`FromSql`) needs an explicit `.OrderBy(...)` rather than an `ORDER BY` inside the SQL.

## Usage

### Required pagination

```csharp
using EntityFrameworkToolKit.Pagination;

public Task<PagedResult<ProductDto>> GetProductsAsync(int page, int pageSize, CancellationToken ct)
{
    return _dbContext.Products
        .OrderBy(p => p.Name)
        .Select(p => new ProductDto(p.Id, p.Name))
        .ToPagedListAsync(page, pageSize, ct);
}
```

`ToPagedListAsync` throws `ArgumentOutOfRangeException` when `page` or `pageSize` is less than 1. In an API, validate those query parameters first, or map the exception to a 400 response.

### Optional pagination

```csharp
// Pages when both values are 1 or greater; otherwise returns every row as a single page.
public Task<PagedResult<Product>> GetProductsAsync(int? page, int? pageSize, CancellationToken ct)
{
    return _dbContext.Products
        .OrderBy(p => p.Name)
        .ToOptionalPagedListAsync(page, pageSize, ct);
}
```

### Response shape

```csharp
app.MapGet("/products", (int page, int pageSize, ProductService service, CancellationToken ct)
    => service.GetProductsAsync(page, pageSize, ct));
```

```json
{
  "items": [{ "id": 11, "name": "Keyboard" }],
  "page": 2,
  "pageSize": 10,
  "totalCount": 42,
  "totalPages": 5,
  "hasPreviousPage": true,
  "hasNextPage": true
}
```

## API helpers

Building blocks for list endpoints, so a typical `GET /products?search=…&sort=-price,name&page=2&pageSize=50` is one expression:

```csharp
using EntityFrameworkToolKit.Filtering;
using EntityFrameworkToolKit.Pagination;
using EntityFrameworkToolKit.Sorting;

static class ProductSorts
{
    // The only fields clients may sort by. Default = initial order and final tie-breaker (use a unique key).
    public static readonly SortMap<Product> Map = new SortMap<Product>()
        .Add("name", p => p.Name)
        .Add("price", p => p.Price)
        .Default(p => p.Id);
}

app.MapGet("/products", ([AsParameters] PageRequest request, string? search, ShopDb db, CancellationToken ct) =>
    db.Products
        .WhereIf(!string.IsNullOrWhiteSpace(search), p => p.Name.Contains(search!))
        .ApplySort(request.Sort, ProductSorts.Map)
        .ToPagedListAsync(request, ct));
```

| Helper | What it does |
|---|---|
| `PageRequest` | Binds `page`, `pageSize` and `sort` from the query string (all optional). Missing values default to page 1 and 20 items; `pageSize` is clamped to 100. Pass `new PagingOptions(defaultPageSize, maxPageSize)` to `ToPagedListAsync` to change those limits. |
| `SortMap<T>` + `ApplySort` | Sorts by a spec like `-price,name` (`-` = descending), **only** by registered fields. Unknown or repeated fields throw `InvalidQueryRequestException` listing the allowed fields. The default key is always appended as a tie-breaker so pages never overlap. |
| `WhereIf(condition, predicate)` | Applies a filter only when the condition is true — optional filters without `if` blocks. |

Bad client input (`?page=0`, `?pageSize=-1`, `?sort=unknown`, a page too large to reach) throws **`InvalidQueryRequestException`**, whose `ParamName` is the query parameter. Map exactly that type to a 400, as the [sample app](samples/README.md) does. Don't map `ArgumentException` in general: that would report server bugs as bad requests.

`PagingOptions` checks its limits when constructed, so a misconfiguration (e.g. `new PagingOptions(defaultPageSize: 50, maxPageSize: 20)`) fails at startup rather than on the first request.

Use `ToPagedListAsync(PageRequest)` for HTTP input. The `ToPagedListAsync(int page, int pageSize)` overload is for values your own code computes; it throws `ArgumentOutOfRangeException`, a programming error.

## Cursor pagination

For feeds, infinite scroll, exports and sync jobs, page by **cursor** instead of page number. Each page is "the next N rows after this row", so it uses the index at any depth (no `OFFSET` scan), and rows inserted or deleted meanwhile don't make pages skip or repeat items. Each page is one SQL query, with no `COUNT`.

```csharp
// GET /products/feed?pageSize=20            → first page
// GET /products/feed?pageSize=20&after=…    → next page (pass nextCursor)
// GET /products/feed?pageSize=20&before=…   → previous page (pass previousCursor)
app.MapGet("/products/feed", ([AsParameters] CursorRequest request, ShopDb db, CancellationToken ct) =>
    db.Products
        .Where(p => p.IsActive)
        .ApplySort(request.Sort, ProductSorts.Map)   // or .OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
        .ToCursorPagedListAsync(request, ct));
```

```json
{
  "items": [{ "id": 21, "name": "Keyboard" }],
  "pageSize": 20,
  "nextCursor": "eyJzIjoi…",
  "previousCursor": "eyJzIjoi…",
  "startCursor": "eyJzIjoi…",
  "endCursor": "eyJzIjoi…",
  "hasNextPage": true,
  "hasPreviousPage": true
}
```

`nextCursor`/`previousCursor` are `null` at the ends of the data. `startCursor`/`endCursor` (the cursors of the first and last item, as in GraphQL Relay's `PageInfo`) are always set on a non-empty page, so a client that reaches the end keeps its position. Sync jobs, "load newer" and feed tailing poll with it:

```csharp
after = page.EndCursor ?? after; // an empty page has no cursors: keep the one you sent
```

Rules:
- **The ordering must be the query's last step**, because the cursor is built from the sort keys of the returned items. Put `Where`/`Select` before `OrderBy`. EF Core operators such as `AsNoTracking` or `Include` may follow it.
- **The last sort key must be unique** (e.g. the Id), or rows with equal keys can be skipped. `SortMap`'s default key does this for you.
- Sort keys must be non-nullable numbers, strings, dates/times (`DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`), `Guid`s or enums. Enums must be stored as numbers (the default); one mapped with `HasConversion<string>()` sorts alphabetically in the database but would be compared numerically. Nullable keys (`int?`, `string?`) throw `InvalidOperationException`, because rows with a NULL key would be skipped. A `string` column that the C# model doesn't annotate as nullable but that holds NULLs can't be detected up front, so keep such columns `NOT NULL`. SQLite can't compare `decimal` or `DateTimeOffset`; cast to `double` there, as the sample does.
- Keys are computed twice: in SQL to filter and order, and in .NET on the returned items to build the cursor. Use plain columns or simple casts. Avoid keys that .NET and the database evaluate differently, such as culture-sensitive `ToLower()`.
- The filter is `k1 >= v1 AND (k1 > v1 OR (k1 = v1 AND k2 > v2) …)`, with plain `>`/`<` on the columns for strings and `Guid`s too (checked on SQLite, SQL Server and PostgreSQL), so it follows the database's collation and type ordering exactly as `ORDER BY` does, and the leading range lets the database seek an index on `(k1, k2, …)`. Add that index for large tables.
- There's no total count or page number: that's the trade-off for constant-time pages. Use `ToPagedListAsync` when the UI needs "page 3 of 10".

Cursors are opaque base64url strings. They aren't signed, so they're validated strictly instead. A malformed or edited cursor, a cursor from a different sort, or `after` and `before` together all throw `InvalidQueryRequestException` (`ParamName` `after` or `before`), which maps to a 400. Values only ever reach the database as SQL parameters. A cursor stops working when the sort it was made for changes (for example after a deploy that changes the sort keys); the client then gets a 400 and starts over from the first page. An empty page (nothing new yet, or the rows around the cursor were deleted) has no cursors.

## Auditing and soft delete

Stamp who changed what and when, and turn deletes into a flag, without a `SaveChanges` override or a base `DbContext`:

```csharp
using EntityFrameworkToolKit.Auditing;

public class Order : IUserAuditable, IUserSoftDeletable
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }    // IAuditable (UTC)
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }     // IUserAuditable
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }        // ISoftDeletable
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }     // IUserSoftDeletable
}

services.AddHttpContextAccessor();
services.AddDbContext<ShopDb>((sp, options) => options
    .UseSqlServer(connectionString)
    .UseAuditing(currentUser: () => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User.Identity?.Name));

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // ... your configuration and query filters ...
    modelBuilder.ApplySoftDeleteQueryFilters();   // last
}
```

| Interface | On insert | On update | On `Remove` |
|---|---|---|---|
| `IAuditable` | `CreatedAt` = `UpdatedAt` = now | `UpdatedAt` = now; `CreatedAt` can't be changed | (as an update, when also soft-deletable) |
| `IUserAuditable` | also `CreatedBy` = `UpdatedBy` = user | also `UpdatedBy` = user; `CreatedBy` can't be changed | |
| `ISoftDeletable` | | | an `UPDATE` setting `IsDeleted` = true, `DeletedAt` = now |
| `IUserSoftDeletable` | | | also `DeletedBy` = user |

- **Opt in per entity.** Implement only the interfaces you need, so timestamps can be used without user columns, and auditing without soft delete. `UpdatedAt` is set on insert too, so it's never null and works as a cursor sort key.
- **The current user** comes from the delegate, which is called once per save. Resolve it inside the delegate, as above, so pooled or long-lived contexts get the right user. Pass a `TimeProvider` to control the clock (e.g. in tests).
- **`ApplySoftDeleteQueryFilters()`** adds `!IsDeleted` to every soft-deletable entity, **combined** with filters already configured (e.g. a tenant filter). Call it at the end of `OnModelCreating`: EF Core 8 keeps one filter per entity, so a later `HasQueryFilter` replaces it.
- **Soft-deleting an entity keeps everything attached to it.** Owned types survive. EF's cascade on tracked dependents is undone, at every level: required children aren't deleted, and optional children keep their foreign key. Dependents that are themselves soft-deletable are soft-deleted with it. Deleting an already-deleted row changes nothing.
- **To see or restore deleted rows,** query with `IgnoreQueryFilters()`, set `IsDeleted = false`, then save. `DeletedAt`/`DeletedBy` are cleared for you. On EF Core 8, `IgnoreQueryFilters()` also drops your other filters.
- **Stamps always come from the interceptor.** On insert, values you set are replaced, so a data import can't keep historical times through `SaveChanges`.

Caveats:
- Only `SaveChanges` is intercepted. `ExecuteUpdate`/`ExecuteDelete` and raw SQL are not audited, and `ExecuteDelete` deletes for real.
- Unique indexes on soft-deletable tables need a filter (`.HasFilter("[IsDeleted] = 0")`), or a deleted row blocks re-creating it.
- Only **tracked** dependents are handled. Untracked soft-deletable children of a deleted parent stay visible until you delete them too, and children whose *required* navigation points at a deleted parent disappear from `Include` (the parent's filter applies). Make such children soft-deletable as well.
- **Avoid `Update()`/`Attach` with an entity built from a request:** it writes every column, so `IsDeleted = false` from the request would un-delete the row. The stored `CreatedAt` is kept, but the returned entity shows the request's value. Load the entity and copy the changes onto it.
- In the save that soft-deletes a parent, changes to its tracked children look like EF's cascade, so they're undone. If you remove a non-soft-deletable child, or set an optional child's foreign key to null, save that change separately. Children *added* in the same save are discarded by EF as soon as you call `Remove(parent)`.
- Implement the interface properties implicitly and keep them mapped (no `[NotMapped]`). `ApplySoftDeleteQueryFilters()` can safely be called more than once.

## Migrating from 1.x

| 1.x | 2.0 |
|---|---|
| `AddPagination(page, size)` | `ToPagedListAsync(page, pageSize, ct)` |
| `AddOptionalPagination(page, size)` | `ToOptionalPagedListAsync(page, pageSize, ct)` |
| `PaginatedIEnumerable<T>.Total` | `PagedResult<T>.TotalCount` |
| `PaginatedIEnumerable<T>` / `PaginatedList<T>` | `PagedResult<T>` (not enumerable: use `.Items`; `.Map(...)` to project) |

Behaviour changes:
- An invalid `page`/`pageSize` passed to `ToPagedListAsync` now throws instead of returning an empty result with `Total = 0`.
- Paging an unordered query now throws `InvalidOperationException`.

See the [CHANGELOG](https://github.com/wearemiew/ef-toolkit/blob/main/CHANGELOG.md) for details.

## Requirements

- .NET 8.0 or higher
- Entity Framework Core 8.0.10 or higher

## Contributing

Contributions are welcome! Open a pull request against `dev`. Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/) (checked by a commit hook after `npm install`).

Releases are automatic: every merge to `dev` publishes a prerelease (`X.Y.Z-dev.N`), and every merge to `main` publishes a stable version and creates a `vX.Y.Z` release. The version bump is derived from the commit messages (`feat` → minor, `fix` → patch, `!`/`BREAKING CHANGE` → major).

```bash
dotnet test EntityFrameworkToolKit/EntityFrameworkToolKit.sln
```
