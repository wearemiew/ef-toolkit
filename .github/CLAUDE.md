# .github/CLAUDE.md

Load-bearing, hand-maintained guidance for anyone (human or agent) changing
anything under `.github/` in this repo — not incidental docs.

## Read this first — refresh from the references before you change anything here

Runner labels and other authoritative infra facts live in the org-wide
`wearemiew/miew-gitops-bible` wiki, not in this repo. Refresh before editing
anything here — direct fetches, one per page, not a loop:

```bash
T=$(gh auth token)
curl -sfL -H "Authorization: bearer $T" \
  "https://raw.githubusercontent.com/wiki/wearemiew/miew-gitops-bible/GitHub-Actions-Runner-Labels.md"
curl -sfL -H "Authorization: bearer $T" \
  "https://raw.githubusercontent.com/wiki/wearemiew/miew-gitops-bible/GitHub-Actions-Runner-Inventory.md"
```

Use this exact method — the browser wiki URL 404s for unauthenticated clients
(private repo — GitHub 404s a private resource instead of 401-ing, so it
reads as a dead link, not an auth failure), and there's no `gh api` route for
wiki pages. For action version bumps, use `gh api repos/<owner>/<repo>/releases`.

**Neither a reference wiki page nor this file beats a green run.** Don't
quietly rewrite the repo to match a reference; report the divergence and
let the user decide.

## Numbered rules

1. **Self-hosted runners only.** GitHub-hosted labels (`ubuntu-latest` and
   friends) are not an option anywhere in this org's CI. Target the
   capability labels from the wiki: `[self-hosted, test-dotnet]` for jobs
   that run the `dotnet` CLI, `[self-hosted, ci-lite]` for bash/git/curl-only
   jobs.
2. **Every dotnet job on a shared pool runs the wiki's "Set .NET install
   directory" step immediately before `actions/setup-dotnet`** (see ".NET
   Constraints on Shared Runner Pools" in `GitHub-Actions-Runner-Labels`).
3. **Never interpolate a `${{ }}` expression carrying PR-, commit-, or
   branch-authored text directly into a `run:` block.** A workflow
   expression is substituted textually into a `run:` block before bash
   parses it — including inside bash comments. Git permits `"`, `$`,
   backticks, etc. in a PR title or branch name, so a raw value can break
   the script or execute arbitrary shell. Pass it through `env:` instead.
4. **No fixed paths outside `$GITHUB_WORKSPACE` in `run:` steps**, and no
   `sudo rm`/`chmod`/`chown` to force through a permission error — the
   runners are shared and persistent. Write scratch files into the checkout.

## Runner labels

`runs-on:` is an array, and GitHub requires **all** listed labels to match.
A label is its exact string — a near-miss spelling is a different label, and
GitHub will not tell you which one you typed. Check the
`GitHub-Actions-Runner-Inventory` wiki page for which runners actually carry
a label before pointing a `runs-on:` at it. Capability labels (generic,
shared across tenants):

| Label | Meaning |
|---|---|
| `build` | General build capability |
| `docker-build` | Can build/push Docker images |
| `test` | General test capability |
| `test-playwright` | Has Playwright + browsers installed |
| `test-dotnet` | Has .NET SDK installed |
| `test-typescript` | Has Node/TypeScript toolchain installed |
| `healthcheck` | Can run post-deploy healthchecks |
| `dependency-submission` | Can run dependency-submission jobs |
| `build-infra` | Can run infra/tooling pipelines (Terraform, Python, etc.) |
| `ci-lite` | Lightweight utility jobs — bash/git/python3/curl only, no Docker, no heavy toolchain |

Labels for this repo's jobs (all three jobs in `ci.yml`):

| Job | `runs-on` |
|---|---|
| `build` | `[self-hosted, test-dotnet]` |
| `publish` | `[self-hosted, test-dotnet]` |
| `release` | `[self-hosted, ci-lite]` |

Do not use `docker-build` for these: nothing here builds an image, and the
wiki asks lightweight jobs not to compete for that pool.

## What's actually in this repo

`workflows/ci.yml` is the only workflow. Triggers: PRs into and pushes to
`dev`/`main`, plus `workflow_dispatch`. Versions come from `GitVersion.yml`
at the repo root (Conventional Commits drive the bump); the release flow is
described in the root `CLAUDE.md`.

| Job | Runs on PRs | Runs on push to `dev`/`main` | Needs |
|---|---|---|---|
| `build` | yes — restore, build, format check, test, pack, upload artifact | yes | `dotnet`, `git` (full history) |
| `publish` | no | yes — `dotnet nuget push` to GitHub Packages | `dotnet` |
| `release` | no | `main` only — GitHub release + Google Chat notification | `jq`, `curl` |

Secrets used: `GITHUB_TOKEN` (package push, release) and
`GOOGLE_CHAT_WEBHOOK` (release notification; failure must not fail the
release, hence `continue-on-error`).
