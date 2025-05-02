# EntityFrameworkToolKit

A lightweight toolkit that extends Entity Framework Core with useful utilities and helpers.

[![Build Status](https://github.com/wearemiew/ef-toolkit/workflows/Build/badge.svg)](https://github.com/wearemiew/ef-toolkit/actions)
[![GitHub Package](https://img.shields.io/badge/GitHub%20Package-1.0.2-blue)](https://github.com/wearemiew/ef-toolkit/pkgs/nuget/Miew.EntityFramework.Toolkit)

## Features

### Pagination
The toolkit provides extension methods for IQueryable to easily add pagination:

- **AddPagination**: Adds pagination to an IQueryable with required page and size parameters
- **AddOptionalPagination**: Adds pagination only if valid page and size parameters are provided, otherwise returns all results

Both methods return a `PaginatedIEnumerable<T>` which includes:
- `Items`: The collection of paginated items
- `Total`: The total count of items before pagination
## Usage Examples

### Basic Pagination

```csharp
using EntityFrameworkToolKit.Pagination;

// In your data access or service layer:
public async Task<PaginatedIEnumerable<Product>> GetProductsAsync(int page, int pageSize)
{
    return await _dbContext.Products
        .OrderBy(p => p.Name)
        .AddPagination(page, pageSize);
}
```

### Optional Pagination

```csharp
using EntityFrameworkToolKit.Pagination;

// This will apply pagination only if page and size are valid
// Otherwise returns all products
public async Task<PaginatedIEnumerable<Product>> GetProductsAsync(int? page, int? pageSize)
{
    return await _dbContext.Products
        .OrderBy(p => p.Name)
        .AddOptionalPagination(page, pageSize);
}
```

### Handling Pagination Results

```csharp
// In your controller or application layer:
var result = await _productService.GetProductsAsync(page, pageSize);

// Access the paginated items
var products = result.Items;

// Access the total count (useful for UI pagination controls)
var totalCount = result.Total;
```

## Requirements

- .NET 8.0 or higher
- Entity Framework Core 8.0.10 or higher

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
