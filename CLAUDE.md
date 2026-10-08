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

Releases are automatic — never create a release or tag by hand. `.github/workflows/ci.yml` + `GitVersion.yml`:

- PR into `dev`/`main`: build, format check, tests. Nothing is published.
- Merge to `dev`: publishes a prerelease (e.g. `2.1.0-dev.3`) to GitHub Packages.
- Merge `dev` → `main`: publishes the stable version, tags it `vX.Y.Z` and creates a GitHub release with generated notes.

The bump comes from Conventional Commits since the last tag: `feat` → minor, `fix`/`perf` → patch, `!` or a `BREAKING CHANGE:` footer → major (`+semver: …` overrides). Every merge to `main` is at least a patch release.

- Merge `dev` → `main` with a **merge commit**, never squash — a squash title like "Release" hides the `feat!`/`BREAKING CHANGE` commits and under-bumps the version.
- Squash-merging into `dev` is fine only if the PR title is itself a Conventional Commit.
- Update `CHANGELOG.md` (date the release section) in the `dev` → `main` PR.
