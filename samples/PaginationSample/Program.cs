using System.Data.Common;
using System.Globalization;
using EntityFrameworkToolKit.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<DbCommandCounter>();
builder.Services.AddDbContext<ShopDbContext>((sp, options) => options
    .UseSqlite("Data Source=sample.db")
    .AddInterceptors(sp.GetRequiredService<DbCommandCounter>()));

var app = builder.Build();

// Reports how many SQL commands each request ran, so you can see the COUNT query being skipped.
app.Use(async (context, next) =>
{
    var counter = context.RequestServices.GetRequiredService<DbCommandCounter>();
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Db-Commands"] = counter.Count.ToString();
        return Task.CompletedTask;
    });
    await next();
});

// The library's exceptions, mapped the way a real API would.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ArgumentOutOfRangeException ex)
    {
        await Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
    }
    catch (InvalidOperationException ex)
    {
        await Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError).ExecuteAsync(context);
    }
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
    db.Database.EnsureDeleted();
    db.Database.EnsureCreated();
    db.Products.AddRange(Enumerable.Range(1, 95).Select(i => new Product
    {
        Id = i,
        Name = $"Product {i:D3}",
        Price = 10m + i,
    }));
    db.SaveChanges();
}

// Required paging: GET /products?page=2&pageSize=10
app.MapGet("/products", (ShopDbContext db, int page, int pageSize, CancellationToken ct) =>
    db.Products.OrderBy(p => p.Id).ToPagedListAsync(page, pageSize, ct));

// Optional paging: GET /products/optional (everything) or ?page=1&pageSize=5
app.MapGet("/products/optional", (ShopDbContext db, int? page, int? pageSize, CancellationToken ct) =>
    db.Products.OrderBy(p => p.Id).ToOptionalPagedListAsync(page, pageSize, ct));

// Projection in SQL, then Map in memory: GET /products/summaries?page=1&pageSize=5
app.MapGet("/products/summaries", async (ShopDbContext db, int page, int pageSize, CancellationToken ct) =>
{
    var result = await db.Products
        // SQLite can't ORDER BY decimal columns, so sort on a REAL cast; ThenBy keeps ties stable.
        .OrderByDescending(p => (double)p.Price)
        .ThenBy(p => p.Id)
        .Select(p => new { p.Id, p.Name, p.Price })
        .ToPagedListAsync(page, pageSize, ct);

    return result.Map(p => new ProductSummary(p.Id, string.Create(CultureInfo.InvariantCulture, $"{p.Name} ({p.Price:0.00} EUR)")));
});

// Mistake on purpose: no OrderBy, so the library refuses to page it.
app.MapGet("/products/unordered", (ShopDbContext db, CancellationToken ct) =>
    db.Products.ToPagedListAsync(1, 10, ct));

// The SQL that a page query generates: GET /debug/sql?page=3&pageSize=10
app.MapGet("/debug/sql", (ShopDbContext db, int page, int pageSize) => new
{
    Sql = db.Products.OrderBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize).ToQueryString(),
});

app.Run();

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

public sealed record ProductSummary(int Id, string Label);

public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
}

/// <summary>Counts SQL commands executed within one request scope.</summary>
public sealed class DbCommandCounter : DbCommandInterceptor
{
    private int _count;

    public int Count => _count;

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }
}
