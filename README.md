# context-pin

A versioned rules manifest for coding agents: publish a set of standards once, pin
each consuming repository to a version and a channel, serve it over HTTP with a
content hash, and let CI detect drift between what a repo has locally and what is
currently published — without a network call on the hot path.

> **Status: early build, in public from the first commit.** The pieces land as a
> sequence of small, independently green pull requests. This README is updated in
> the same PR that makes a claim true; if it says something works, it does.

## Why

Coding-agent standards (architecture principles, delivery procedures, review
checklists) tend to live as prose in one repository and get consulted, copied, or
re-derived by every other repository that wants them. That works until the
standard changes: nothing tells a consumer it drifted, and nothing proves which
version of the standard a given commit was built against.

context-pin is the missing piece around that prose, not a replacement for it: a
manifest API that serves a versioned, content-hashed bundle of rules; a lock file
a consuming repo commits, so its CI is deterministic and works offline; and a
sync script plus a GitHub Action that flags drift between the two.

It deliberately reuses rather than re-derives where a working implementation
already exists elsewhere in this author's public repositories — see
[Provenance](#provenance).

## Current scope

| Piece | Status |
|---|---|
| `ContextPin.Service` — health endpoint, minimal API host | Built |
| Domain model (rule sets, rules, repo pins, findings) + migrations | Built |
| Manifest API (`GET /api/repos/{owner}/{repo}/manifest`) with ETag/content-hash versioning | Not yet |
| Import of a first rule set (ported from `architecture-standards`) | Not yet |
| `scripts/aurelius-sync.sh` + a drift-detection GitHub Action | Not yet |
| MCP server, hosted governance portal, per-repo billing, self-hosted mode | Out of scope for now — see [Non-goals, for now](#non-goals-for-now) |

## Running it locally

```bash
docker compose up -d          # Postgres, once
dotnet run --project src/ContextPin.Service
```

Health check: `curl http://localhost:5080/health` — this also confirms the
database connection and applies any pending migration.

## Data

One Postgres database, migrated by numbered `.sql` files under
`src/ContextPin.Service/Migrations/`, applied at startup and tracked in a
`schema_migrations` table. Not EF Core migrations: generating those needs
`dotnet ef migrations add`, which needs the SDK's design-time tooling. Plain
SQL plus a tracking table gives the same guarantee — schema is migrated,
never assumed into existence — without that dependency.

| Table | Holds |
|---|---|
| `rule_sets` | A versioned, content-hashed, immutable bundle of rules |
| `rules` | One rule within a rule set |
| `repo_pins` | Which rule set a given `(owner, repo, channel)` resolves to right now |
| `findings` | Reported violations, append-only |

## Tests

```bash
dotnet test tests/ContextPin.Service.Tests/ContextPin.Service.Tests.csproj
```

CI (`.github/workflows/ci.yml`) runs the same command on every push and pull
request to `main`.

## Repository hygiene

- **Secret scanning**, in two places: `scripts/hooks/pre-commit` (staged changes,
  before a secret becomes history) and `.github/workflows/secret-scan.yml` (full
  history, on every push/PR and weekly). Run `scripts/setup.sh` once to install the
  hook. Reproduce the CI scan locally with `scripts/scan-secrets.sh`.
- **No document in this repository describes a fictional feature.** If a claim in
  a document turns out to be wrong, the fix belongs in the same PR that touches
  the code it describes, not a later cleanup pass.

## Non-goals, for now

An MCP server, a portal with diff review and four-eyes approval, per-repo
billing, and a self-hosted delivery mode are all real parts of the eventual
product and are explicitly not attempted in this repository's current build-out.
Each is a substantial separate surface, and a stub that does not actually work is
worse than an honest gap. They will get their own scoped work when they do.

## Provenance

Several pieces here are ported from, or deliberately deferred to, other public
repositories by the same author rather than rebuilt from scratch:

- **Secret scanning** (`.gitleaks.toml`, `scripts/scan-secrets.sh`,
  `scripts/hooks/pre-commit`) — ported from
  [`konradcinkusz/architecture-standards`](https://github.com/konradcinkusz/architecture-standards).
- **The first rule set**, once imported, is a port of the fifteen architecture
  principles documented in that same repository's
  [reference architecture](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/architecture/00-REFERENCE-ARCHITECTURE.md).
- **Identity**, when this service needs to authenticate a caller, is meant to
  consume [`konradcinkusz/authservice`](https://github.com/konradcinkusz/authservice)
  as a pinned dependency rather than reimplement JWT/OAuth here.
- **Telemetry and evaluation** are meant to build on
  [`konradcinkusz/copilot-scope`](https://github.com/konradcinkusz/copilot-scope) and
  [`konradcinkusz/agent-eval-bench`](https://github.com/konradcinkusz/agent-eval-bench)
  respectively, rather than duplicate them.

## License

MIT — see [`LICENSE`](LICENSE).
