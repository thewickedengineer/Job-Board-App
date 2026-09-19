# TalentBridge

A job board built as two deliberately different applications over one Postgres database:

- **Post** (`apps/post-web` + `services/TalentBridge.Post.*`) — hiring managers sign up and create job postings. Low write volume; optimised for correctness and validation.
- **Search** (`apps/search-web` + `services/TalentBridge.Search.*`) — candidates browse postings anonymously. High read volume; optimised for latency and cacheability.

The full design is in [`CLAUDE.md`](./CLAUDE.md). This README tracks what is actually built.

## Status

**Phase 4 — Post API job postings.** Managers can create, list, read, update and close their own postings. Validation is the §6 matrix in FluentValidation, returned as RFC 9457 `ValidationProblemDetails` with camelCase keys that match the Angular form controls. Every publish/update/close writes an `outbox_messages` row in the same transaction as the posting; the publisher that delivers those rows is Phase 5. Auth from Phase 3 is unchanged.

## Layout

```
apps/
  post-web/                       Angular 22 — hiring manager portal (ng serve → :4200)
  search-web/                     Angular 22 — public job board       (ng serve → :4201)
services/
  Directory.Build.props           shared .NET settings (net10.0, nullable, warnings-as-errors)
  TalentBridge.Post.Api/          .NET 10 minimal API — write side    (:5001)
  TalentBridge.Post.Domain/       Manager, RefreshToken, JobPosting, OutboxMessage
  TalentBridge.Post.Infrastructure/  PostDbContext + entity configurations (EF Core, Npgsql)
  TalentBridge.Post.Tests/        xUnit
  TalentBridge.Search.Api/        .NET 10 minimal API — read side     (:5002)
  TalentBridge.Search.Infrastructure/
  TalentBridge.Search.Tests/      xUnit
db/
  migrations/                     EF Core migrations for the post schema (compiled into Post.Infrastructure)
  search-schema.sql               hand-written DDL for search.job_listings + indexes (idempotent)
  init/01-schemas.sql             compose init: citext extension + both schemas
docs/
  Wireframes.html
TalentBridge.sln
docker-compose.yml                empty until Phase 9
.env.example                      every key the stack will need; copy to .env
```

## Prerequisites

- .NET SDK 10.0
- Node.js 22+ and npm
- Docker (for the local Postgres and for the integration tests, which use Testcontainers)

## Secrets

Copy `.env.example` to `.env` and fill in `Jwt__SigningSecret` and `Projection__SharedSecret` (`openssl rand -base64 48` for each). The Post API loads `.env` from the repo root on `dotnet run` in Development; docker-compose will inject the same file. Nothing secret lives in `appsettings*.json`, and the API refuses to start if the signing secret is missing or shorter than 32 characters.

## Build and test

```sh
# .NET — both APIs, all libraries, all tests
dotnet build TalentBridge.sln
dotnet test  TalentBridge.sln

# Angular — each app is independent; run in apps/post-web and apps/search-web
npm install
npx ng build
npx ng test
```

## Database

One Postgres instance, two schemas, two owners:

- **`post`** — written by the Post API through EF Core. Tables: `managers`, `refresh_tokens`, `job_postings`, `outbox_messages`. Invariants live in the database as CHECK constraints (`salary_min < salary_max`, `openings > 0`, closed vocabularies for `status`, `employment_type`, `seniority`, `work_arrangement`, `pay_period`), not only in validation code. `email` is `citext` with a unique index.
- **`search`** — written only by the Search API's projection endpoint, read with Dapper. One denormalised table, `job_listings`, with a stored generated `tsvector` (title weighted A, department/organization/location/skills B, description C, responsibilities/requirements D) and the indexes each query needs: GIN on `search_vector` and `skills`, B-tree on `(is_open, published_at desc)`, `department`, `work_arrangement`, `employment_type`, `closing_date`. `organization` is copied from the manager on purpose — the read side never joins to `post`.

Until docker-compose lands (Phase 9), run a local Postgres like this:

```sh
docker run -d --name talentbridge-pg -p 5432:5432 \
  -e POSTGRES_DB=talentbridge -e POSTGRES_USER=talentbridge -e POSTGRES_PASSWORD=talentbridge \
  -v "$PWD/db/init/01-schemas.sql:/docker-entrypoint-initdb.d/01-schemas.sql:ro" \
  -v "$PWD/db/search-schema.sql:/docker-entrypoint-initdb.d/02-search-schema.sql:ro" \
  postgres:17
```

Migrations (the `dotnet-ef` tool is pinned in `dotnet-tools.json`; run `dotnet tool restore` once):

```sh
# apply to the database in POST_DB_CONNECTION_STRING (defaults to the container above)
dotnet ef database update --project services/TalentBridge.Post.Infrastructure --startup-project services/TalentBridge.Post.Infrastructure

# add a migration after changing the model
dotnet ef migrations add <Name> --project services/TalentBridge.Post.Infrastructure --startup-project services/TalentBridge.Post.Infrastructure \
  --output-dir ../../db/migrations --namespace TalentBridge.Post.Infrastructure.Migrations
```

The Post API also applies pending migrations itself on startup when `ASPNETCORE_ENVIRONMENT=Development` and `Database:ApplyMigrationsOnStartup=true` (both set in `appsettings.Development.json`).

## Authentication (Post API)

| Endpoint | Auth | Result |
|---|---|---|
| `POST /api/auth/signup` | – | `201 { accessToken, refreshToken, manager }` + `Set-Cookie: tb_refresh` |
| `POST /api/auth/login` | – | `200 { accessToken, refreshToken, manager }` + cookie; `401` with one generic message for any failure |
| `POST /api/auth/refresh` | cookie or `{ refreshToken }` | `200 { accessToken, refreshToken }`; the presented token is revoked and replaced |
| `POST /api/auth/logout` | cookie or body | `204`, cookie cleared |
| `GET /api/me` | Bearer | `200 manager` |

- Passwords: ASP.NET Core `PasswordHasher` (PBKDF2, per-user salt). Unknown emails are verified against a dummy hash so timing does not reveal whether an account exists.
- Access token claims: `sub`, `email`, `name`, `org`, `jti`, `iat`, `exp`, `iss`, `aud`.
- Refresh tokens are 256-bit random values; only the SHA-256 hash is stored. Rotation is a conditional `UPDATE ... WHERE revoked_at IS NULL`, so concurrent refreshes cannot both win. Presenting an already-revoked token is treated as theft and revokes every active token for that manager.
- Browser clients keep the access token in memory and never see the refresh token: it travels only in the cookie (path `/api/auth`). The `refreshToken` body field exists for non-browser clients such as the `.http` file.
- Rate limiting: fixed window per IP — 5 logins / 5 min and 5 signups / 10 min by default (`AuthRateLimit` section). Rejections are `429` with `Retry-After` and `retryAfterSeconds` in the body.
- Work-email rule (from wireframe 1.1): signup rejects common personal mailbox domains (gmail, outlook, yahoo, icloud, …).

`services/TalentBridge.Post.Api/TalentBridge.Post.Api.http` walks the whole cycle; `TalentBridge.Post.Tests/Integration` does the same automatically against a Testcontainers Postgres.

## Job postings (Post API)

| Endpoint | Result |
|---|---|
| `POST /api/job-postings` | `201` + `Location`, body is the complete persisted record (`id`, `slug`, `status`, `version`, timestamps are server-owned) |
| `GET /api/job-postings?q=&status=&sort=&page=&pageSize=` | `200 { items[], page, pageSize, total, totalPages }` — the caller's own postings only |
| `GET /api/job-postings/{id}` | `200` or `404` (also for someone else's posting) |
| `PUT /api/job-postings/{id}` | `200`; body must include the `version` loaded — stale → `409`; closed → `409` |
| `POST /api/job-postings/{id}/close` | `200`; irreversible, idempotent |

- **Validation** (`JobPostings/JobPostingValidators.cs`): title 3–120, department 2–80, location 2–120 unless Remote (then the country stands in), description 50–10,000, salaries > 0 and ≤ 10,000,000 with min strictly below max (keyed to `salaryMax`), ISO currency from the allowed list, closing date strictly after today in UTC, closed vocabularies for employment type / seniority / work arrangement / pay period, openings 1–999, email and absolute http(s) URL when present, ≤ 20 skills of 1–40 chars (keyed to `skills`). `CreateJobPostingValidatorTests` covers every boundary.
- **Status**: `Draft` or `Published` on create/update. Drafts are fully validated (the schema's NOT NULL columns allow nothing looser) but never projected to the board. A published posting cannot revert to draft. Reads report `Expired` for a published posting whose closing date has passed; it is derived, never stored.
- **Slug**: `title-place` (`senior-warehouse-supervisor-leeds`), assigned once and never changed; collisions get `-2`, `-3`, … and a true race falls back to a random tail.
- **Reference code** is optional and unique within a manager's postings (partial unique index; a clash is a `400` keyed to `referenceCode`).
- **Concurrency**: `version` is the EF concurrency token and increments on every mutation. It is also carried on every projection message so the read side can discard stale updates.
- **Outbox**: `Enqueue()` in `JobPostingEndpoints.cs` adds a `job-posting.changed` row (full `JobProjectionMessage` snapshot) to the same change set; `SaveChangesAsync` commits posting and message together or not at all.
- List filters: `q` matches title, department or reference code; `status` is `All|Draft|Published|Closed|Expired`; `sort` is one of `createdDesc|createdAsc|closingAsc|closingDesc|titleAsc|titleDesc` — anything else is a `400`; `pageSize` caps at 50.

## Run

```sh
dotnet run --project services/TalentBridge.Post.Api     # http://localhost:5001/health  and  /health/ready
dotnet run --project services/TalentBridge.Search.Api   # http://localhost:5002/health

cd apps/post-web   && npx ng serve                       # http://localhost:4200
cd apps/search-web && npx ng serve                       # http://localhost:4201
```

## Angular conventions in place

Both apps are generated with: standalone components, zoneless change detection (`provideZonelessChangeDetection()`, no `zone.js` dependency), SCSS, Vitest, `strict: true` and `strictTemplates: true`, and no SSR. `src/environments/environment.ts` holds only the API base URL.
