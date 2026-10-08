# ef-toolkit

Miew.EntityFramework.Toolkit — small extension helpers for Entity Framework Core, published to GitHub Packages.

## Workflow

- Base branch: `dev`
- Protected branches: `main`, `dev`
- Branch prefixes: `feat/`, `fix/`, `chore/`, `ci/`, `docs/`, `refactor/`, `test/`, `perf/`
- PRDs: none

### Layers

| Layer | Directory | Test command | Notes |
|---|---|---|---|
| library | `EntityFrameworkToolKit/` | `dotnet test EntityFrameworkToolKit/EntityFrameworkToolKit.sln` | |

## Releasing

Publishing is triggered by creating a GitHub release. The tag (e.g. `2.0.0`, optional leading `v`) becomes the package version — see `.github/workflows/publish.yml`.
