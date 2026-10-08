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
| `SortMap<T>` + `ApplySort` | Sorts by a spec like `-price,name` (`-` = descending), **only** by registered fields. Unknown or repeated fields throw `ArgumentException` listing the allowed fields. The default key is always appended as a tie-breaker so pages never overlap. |
| `WhereIf(condition, predicate)` | Applies a filter only when the condition is true — optional filters without `if` blocks. |

Bad client input (`?page=0`, `?pageSize=-1`, `?sort=unknown`, a page too large to reach) throws **`InvalidQueryRequestException`**, whose `ParamName` is the query parameter. Map exactly that type to a 400, as the [sample app](samples/README.md) does. Don't map `ArgumentException` in general: that would report server bugs as bad requests.

`PagingOptions` checks its limits when constructed, so a misconfiguration (e.g. `new PagingOptions(defaultPageSize: 50, maxPageSize: 20)`) fails at startup rather than on the first request.

Use `ToPagedListAsync(PageRequest)` for HTTP input. The `ToPagedListAsync(int page, int pageSize)` overload is for values your own code computes; it throws `ArgumentOutOfRangeException`, a programming error.

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
