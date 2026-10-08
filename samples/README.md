# Pagination sample

A minimal ASP.NET Core API that uses **the packed NuGet package** (not a project reference), so it checks what consumers actually get: package metadata, dependency resolution, real JSON serialization and real SQL.

```bash
./samples/smoke-test.sh   # pack, start, assert every scenario, stop
./samples/run.sh          # pack and run on http://localhost:5080, then use PaginationSample/requests.http
```

Each response carries an `X-Db-Commands` header with the number of SQL commands it ran — a full page costs 2 (page + `COUNT`), a short last page costs 1.

The sample is not part of the solution, so CI does not build it. It uses SQLite; to try your production provider, swap `UseSqlite` in `Program.cs` and check `/debug/sql`.
