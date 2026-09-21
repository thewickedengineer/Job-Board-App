# TalentBridge

A job board built as two deliberately different applications over one Postgres database:

- **Post** (`apps/post-web` + `services/TalentBridge.Post.*`) — hiring managers sign up and create job postings. Low write volume; optimised for correctness, validation quality and auditability.
- **Search** (`apps/search-web` + `services/TalentBridge.Search.*`) — candidates browse postings anonymously. High read volume; optimised for latency and cacheability.

The two sides share one database but not one design: the write side is EF Core over a normalised schema with a transactional outbox; the read side is Dapper over a denormalised projection with output caching and ETags. The decisions and the reasoning are in [`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md); the project constitution is [`CLAUDE.md`](./CLAUDE.md). This README tracks what is actually built and how to run it.

## Status

| Phase | Component | State |
|---|---|---|
| 1–2 | Monorepo, `post` schema (EF Core migrations), `search` schema (SQL DDL) | ✅ |
| 3 | Post API — self-issued JWT auth, rotating refresh cookies, rate limiting | ✅ |
| 4 | Post API — job postings CRUD, §6 validation, RFC 9457 errors | ✅ |
| 5 | Transactional outbox publisher (Polly) → Search projection endpoint (idempotent upsert) | ✅ |
| 6 | Search API — Dapper queries, facets, output caching, ETag/304, compression | ✅ |
| 7 | post-web — sign up / log in, dashboard, posting form, confirmation, edit | ✅ |
| 8 | search-web — public board, filters in the URL, detail page | ⏳ scaffold only |
| 9 | Docker — Dockerfiles, compose (local Postgres / Supabase modes), seed | ⏳ compose file is a placeholder |
| 10 | Final test pass, docs | ⏳ |

## Tech stack

| Concern | Choice | Version |
|---|---|---|
| Frontend | Angular — standalone components, signals, zoneless, lazy routes, strict templates | 22.1 |
| Backend | ASP.NET Core minimal APIs, `TypedResults`, FluentValidation, Serilog | .NET 10 |
| Database | PostgreSQL (local container now; Supabase-compatible) — two schemas | 17 |
| Write-side data access | EF Core + Npgsql, snake_case naming, migrations in `db/migrations` | 10 |
| Read-side data access | Dapper + `NpgsqlDataSource`, hand-written SQL, no DbContext | 2.1 |
| Inter-service | Transactional outbox → HTTP push with `Microsoft.Extensions.Http.Resilience` (Polly v8) | — |
| Auth | Self-issued HS256 JWT (15 min) + opaque refresh tokens hashed at rest (14 days) | — |
| Tests | xUnit + Testcontainers (Postgres) on .NET; Vitest on Angular | — |

## Quick start (local development)

Everything below runs on macOS/Linux/WSL. You need three long-running processes for the Post side (database, Post API, post-web) and one more (Search API) for the projection to have somewhere to go.

### 1. Prerequisites

- **.NET SDK 10.0** — `dotnet --version` should print `10.x`.
  On macOS with Homebrew: `brew install dotnet` and add `export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"` to your shell profile (the formula needs it; the `.pkg` installer does not).
- **Node.js 22+** and npm — `node --version`.
- **Docker** (Desktop or Engine) — used for the local Postgres and by the .NET integration tests (Testcontainers pulls `postgres:17` on first run).

### 2. Secrets

```sh
cp .env.example .env
```

Fill in the two secrets — both must be at least 32 characters:

```sh
echo "Jwt__SigningSecret=$(openssl rand -base64 48)"        >> .env
echo "Projection__SharedSecret=$(openssl rand -base64 48)"  >> .env
```

`.env` is gitignored. Both APIs read it from the repo root on `dotnet run` (Development only), so nothing secret ever lives in `appsettings*.json`. Each API refuses to start if its secret is missing or short. Keys use ASP.NET's `Section__Key` form so the same file will feed docker-compose via `env_file`.

### 3. Database

Start a local Postgres with both schemas initialised:

```sh
docker run -d --name talentbridge-pg -p 5432:5432 \
  -e POSTGRES_DB=talentbridge -e POSTGRES_USER=talentbridge -e POSTGRES_PASSWORD=talentbridge \
  -v "$PWD/db/init/01-schemas.sql:/docker-entrypoint-initdb.d/01-schemas.sql:ro" \
  -v "$PWD/db/search-schema.sql:/docker-entrypoint-initdb.d/02-search-schema.sql:ro" \
  postgres:17
```

That matches the connection string in both `appsettings.Development.json` files (`Host=localhost;Port=5432;Database=talentbridge;Username=talentbridge;Password=talentbridge`). The `post` schema's tables are created by the Post API on its first start (EF Core migrations, Development only); the `search` schema comes from the init script and is re-applied idempotently by the Search API on start.

Later: `docker start talentbridge-pg` / `docker stop talentbridge-pg`.

### 4. Restore and build

```sh
dotnet tool restore                 # pins dotnet-ef (dotnet-tools.json)
dotnet build TalentBridge.sln       # warnings are errors; expect 0 of each
(cd apps/post-web   && npm install)
(cd apps/search-web && npm install)
```

### 5. Run the APIs (one terminal each)

```sh
dotnet run --project services/TalentBridge.Search.Api   # http://localhost:5002
dotnet run --project services/TalentBridge.Post.Api     # http://localhost:5001
```

Start the Search API first if you can: the Post API's outbox publisher begins delivering two seconds after it starts, and a Search API that isn't up yet just means the first deliveries are retried (see *Outbox and projection* below — nothing is lost).

Check both are ready:

```sh
curl localhost:5001/health/ready    # Healthy  (database reachable, outbox not backed up)
curl localhost:5002/health/ready    # Healthy  (database reachable)
```

OpenAPI documents (Development only): `http://localhost:5001/openapi/v1.json`, `http://localhost:5002/openapi/v1.json`.

### 6. Seed the public board (optional)

```sh
docker exec -i talentbridge-pg psql -U talentbridge -d talentbridge < db/seed/search-listings.sql
```

Loads ~1,000 deterministic listings (plus a few closed and expired) into the read model only. They have no write-side counterpart and no manager can edit them; they exist so `GET /api/jobs` has something to return and query plans can be judged at a realistic size.

### 7. Run the hiring-manager portal

```sh
cd apps/post-web && npx ng serve    # http://localhost:4200
```

Open http://localhost:4200, click **Sign up**, and create an account. Signup rejects personal mailbox domains (gmail, outlook, yahoo, icloud…) — use any other domain, e.g. `you@yourcompany.example`. Passwords need 12+ characters with upper and lower case and a digit.

Then: **+ Post a job** → fill the form → **Publish** → the confirmation screen shows the record exactly as the API stored it. Within about two seconds the outbox delivers it to the Search API:

```sh
curl "localhost:5002/api/jobs?q=<a word from your title>"
curl  localhost:5002/api/jobs/<the slug from the confirmation screen>
```

`apps/search-web` (the public board UI) is scaffolded but not built yet; `npx ng serve --port 4201` in it shows a placeholder.

### 8. Ports and processes at a glance

| Process | Command | URL |
|---|---|---|
| PostgreSQL 17 | `docker start talentbridge-pg` | `localhost:5432` |
| Search API | `dotnet run --project services/TalentBridge.Search.Api` | http://localhost:5002 |
| Post API | `dotnet run --project services/TalentBridge.Post.Api` | http://localhost:5001 |
| post-web | `cd apps/post-web && npx ng serve` | http://localhost:4200 |
| search-web | `cd apps/search-web && npx ng serve --port 4201` | http://localhost:4201 |

CORS on both APIs allows exactly `http://localhost:4200` and `http://localhost:4201` (`Cors:AllowedOrigins`), with credentials, so the refresh cookie works from the dev server.

## Configuration reference

Every setting is bound through `IOptions<T>` and validated at startup; a bad or missing value fails the process immediately with a message naming the key. Environment variables (including `.env`) override `appsettings*.json`; nested keys use `__`.

| Key | Used by | Default (Development) | Notes |
|---|---|---|---|
| `Database__ConnectionString` | both | local container string | **required** |
| `Database__ApplyMigrationsOnStartup` | Post | `true` | Development only; EF Core migrations |
| `Database__ApplySchemaOnStartup` | Search | `true` | Development only; runs `db/search-schema.sql` |
| `Jwt__SigningSecret` | Post | — | **required**, ≥ 32 chars, `.env` only |
| `Jwt__Issuer` / `Jwt__Audience` | Post | `talentbridge-post-api` / `talentbridge-post-web` | |
| `Jwt__AccessTokenMinutes` / `Jwt__RefreshTokenDays` | Post | `15` / `14` | |
| `Projection__SharedSecret` | both | — | **required**, ≥ 32 chars, `.env` only; sent as `X-Projection-Secret` |
| `Projection__SearchApiBaseUrl` | Post | `http://localhost:5002` | compose will set the in-network address |
| `Outbox__Enabled` | Post | `true` | tests set `false` and drive batches by hand |
| `Outbox__PollIntervalSeconds` / `Outbox__BatchSize` | Post | `2` / `20` | |
| `Outbox__MaxAttempts` / `Outbox__BackedUpAfterMinutes` | Post | `10` / `2` | parked-row and degraded thresholds for `/health/ready` |
| `AuthRateLimit__Login*` / `AuthRateLimit__Signup*` | Post | 5 per 5 min / 5 per 10 min | fixed window, per IP |
| `Cors__AllowedOrigins__0…n` | both | the two dev servers | explicit origins, never `*` |

Frontend configuration is `apps/post-web/src/environments/environment.ts`: `apiBaseUrl` (`http://localhost:5001`) and `jobBoardUrl` (`http://localhost:4201`, used by "View on job board" links). No secrets on the frontend.

## Testing

```sh
dotnet test TalentBridge.sln                 # 96 tests; needs Docker (Testcontainers starts postgres:17)
cd apps/post-web && npx ng test              # 9 Vitest specs, no network
```

What the suites cover:

- **`CreateJobPostingValidatorTests`** (51 cases, in-memory, clock pinned with `FakeTimeProvider`) — the full §6 matrix including `salaryMin ≥ salaryMax`, closing date = today (UTC), every length and enum boundary.
- **`AuthEndpointsTests`** — signup shape and cookie flags, exact 400 keys, case-insensitive duplicate email, identical 401 for wrong password vs. unknown email, `/api/me`, refresh rotation and replay detection, logout, 429 with `Retry-After`.
- **`JobPostingEndpointsTests`** — 201 with the complete record and an outbox row in the same transaction, exact 400 keys (`salaryMax`, `closingDate`, …), ownership (404 for someone else's), paging/filters/sort rejection, version conflicts (409), draft → publish, close is irreversible.
- **`OutboxPublisherTests`** — a posting insert that fails leaves no outbox row (same transaction); a failed push leaves the row unprocessed with `attempts+1` and `last_error`; success marks it processed and sends the payload; parked rows are skipped and reported `Unhealthy`.
- **`JobProjectionHandlerTests` / `ProjectionEndpointTests`** — first apply inserts, the same message twice is a no-op, an older version is ignored, missing/wrong secret is 401, 202 with `applied` true/false.
- **`JobEndpointsTests`** (Search) — closed/expired excluded, hidden salaries withheld and unfilterable, keyword hits on skills, combined filters, 400 keys, detail with ETag → 304, facets counted with their own dimension excluded, cache evicted on projection.
- **post-web** — the posting form maps a `ValidationProblemDetails` payload onto the right controls and renders the error summary (the §13-required test); the date picker's keyboard contract.

Tests never touch Supabase or the network beyond the local Docker daemon. The integration tests share one container per test assembly and use unique data per test, so they can run in any order.

## Manual API walkthroughs

`services/TalentBridge.Post.Api/TalentBridge.Post.Api.http` and `services/TalentBridge.Search.Api/TalentBridge.Search.Api.http` are REST-client files (VS Code REST Client, Rider, Visual Studio) that walk the full auth cycle, job-posting CRUD, the public reads including the ETag/304 round trip, and a manual projection push. Run them top to bottom against the running APIs.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `dotnet: command not found` after `brew install dotnet` | Add `export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"` to your shell profile. |
| API exits with `Jwt:SigningSecret is required` / `Projection:SharedSecret …` | `.env` is missing or the value is shorter than 32 characters. Run the API from the repo (it walks up from the working directory to find `.env`). |
| `/health/ready` on the Post API is `Unhealthy` mentioning "parked" | An outbox row exhausted `Outbox:MaxAttempts` (usually a 4xx from the Search API). Inspect `post.outbox_messages.last_error`; `update post.outbox_messages set attempts = 0 where processed_at is null` requeues. |
| `/health/ready` is `Degraded` mentioning "unprocessed" | The Search API was unreachable for a while; rows deliver once it is back. The circuit breaker deferred them without burning attempts. |
| Login returns 429 / the form shows a countdown | Fixed-window rate limit: 5 login attempts per 5 minutes per IP. Wait for the countdown or raise `AuthRateLimit__LoginPermitLimit`. |
| Sign-up says personal mailbox domains aren't accepted | By design (wireframe 1.1). Use a non-personal domain. |
| Browser shows CORS errors | The dev server must be on port 4200 (post-web) or 4201 (search-web); other origins need adding to `Cors__AllowedOrigins__n`. |
| `dotnet test` fails with `DockerUnavailableException` | Docker Desktop is not running or is paused. Unpause it from the whale menu. |
| Port 5432 already in use | Another Postgres is running. Stop it, or map the container to a different port and change `Database__ConnectionString` in `.env`. |
| `ng serve` says "Port 4200 is already in use" | A previous dev server is still running: `pkill -f "ng serve"`. |

## Layout

```
apps/
  post-web/                       Angular 22 — hiring manager portal (ng serve → :4200)
  search-web/                     Angular 22 — public job board       (ng serve → :4201; scaffold)
services/
  Directory.Build.props           shared .NET settings (net10.0, nullable, warnings-as-errors)
  TalentBridge.Post.Api/          .NET 10 minimal API — write side    (:5001)
    Auth/                         endpoints, validators, TokenService, refresh cookie, rate limiting
    JobPostings/                  endpoints, contracts, validators, slug generator
    Outbox/                       OutboxPublisher (BackgroundService), ProjectionClient, health check
    Validation/ Errors/ Configuration/
  TalentBridge.Post.Domain/       Manager, RefreshToken, JobPosting, OutboxMessage, JobProjectionMessage
  TalentBridge.Post.Infrastructure/  PostDbContext + entity configurations (EF Core, Npgsql)
  TalentBridge.Post.Tests/        xUnit: validator matrix + Testcontainers integration tests
  TalentBridge.Search.Api/        .NET 10 minimal API — read side     (:5002)
    Jobs/                         /api/jobs, /api/jobs/{slug}, /api/facets, ETags, cache policies
    Projections/                  /internal/projections/job behind the shared secret
  TalentBridge.Search.Infrastructure/
    Queries/                      JobFilter, JobListQuery, JobDetailQuery, FacetsQuery (Dapper)
    Projections/                  JobProjectionHandler (upsert with version guard), wire contract
    Persistence/                  SearchSchema (embedded DDL), DataSourceHealthCheck
  TalentBridge.Search.Tests/      xUnit + Testcontainers
db/
  migrations/                     EF Core migrations for the post schema (compiled into Post.Infrastructure)
  search-schema.sql               hand-written DDL for search.job_listings + indexes (idempotent)
  init/01-schemas.sql             container init: citext extension + both schemas
  seed/search-listings.sql        ~1,000-row demo/perf seed for the read model
docs/
  ARCHITECTURE.md                 the decisions and why
  Wireframes.html                 the screens both apps are built to
TalentBridge.sln
dotnet-tools.json                 pins dotnet-ef
docker-compose.yml                placeholder until Phase 9
.env.example                      every key the stack needs; copy to .env
```

## Angular conventions

Both apps: standalone components, `provideZonelessChangeDetection()` with no `zone.js` in the build, signals for state and `computed` for derived state, `httpResource`/`resource` for reads and typed services for commands, new control flow only (`@if`/`@for`/`@switch`/`@defer`), `ChangeDetectionStrategy.OnPush` everywhere, `input()`/`output()`/`model()` signal APIs, functional guards and interceptors with `provideHttpClient(withFetch(), withInterceptors([...]))`, lazy-loaded routes, `strict: true` + `strictTemplates: true`, SCSS with CSS custom-property design tokens (`src/styles/_tokens.scss`, duplicated per app — no shared library), and accessibility as part of done (labelled inputs, heading order, focus management, `aria-live` for async results, visible focus rings, keyboard-operable custom controls).

---

# Technical details

## Database

One Postgres instance, two schemas, two owners:

- **`post`** — written by the Post API through EF Core. Tables: `managers`, `refresh_tokens`, `job_postings`, `outbox_messages`. Invariants live in the database as CHECK constraints (`salary_min < salary_max`, `openings > 0`, closed vocabularies for `status`, `employment_type`, `seniority`, `work_arrangement`, `pay_period`), not only in validation code. `email` is `citext` with a unique index.
- **`search`** — written only by the Search API's projection endpoint, read with Dapper. One denormalised table, `job_listings`, with a stored generated `tsvector` (title weighted A, department/organization/location/skills B, description C, responsibilities/requirements D) and the indexes each query needs: GIN on `search_vector` and `skills`, B-tree on `(is_open, published_at desc)`, `department`, `work_arrangement`, `employment_type`, `closing_date`. `organization` is copied from the manager on purpose — the read side never joins to `post`.

The local container is described in *Quick start → 3. Database*.

Migrations (the `dotnet-ef` tool is pinned in `dotnet-tools.json`; run `dotnet tool restore` once):

```sh
# apply to the database in POST_DB_CONNECTION_STRING (defaults to the local container)
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

## Outbox and projection (write side → read side)

```
Post API                                   Search API
────────                                   ──────────
SaveChangesAsync ─┬─ job_postings row
                  └─ outbox_messages row   (same transaction)
OutboxPublisher   every 2 s: SELECT … FOR UPDATE SKIP LOCKED LIMIT 20
      │           POST /internal/projections/job  ──►  X-Projection-Secret check
      │           (Polly: 3 retries w/ backoff, 5 s attempt,   │
      │            30 s total, circuit breaker)               ▼
      │                                          INSERT … ON CONFLICT (id) DO UPDATE
      │                                          … WHERE job_listings.version < excluded.version
      ◄── 202 { applied: true|false } ──────────  (stale / duplicate ⇒ no-op, still 202)
processed_at = now()
```

- **Never lost, never blocking.** The user's request only writes rows. Delivery happens later; a Search API outage does not fail a publish.
- **At-least-once delivery, idempotent apply.** A message may be sent twice (crash after push, before `processed_at`); the version guard makes the second apply a no-op. Out-of-order delivery is also harmless: an older version never overwrites a newer row.
- **Failure accounting.** A genuine failure (non-2xx after retries, timeout, connection refused) increments `attempts` and records `last_error`. While the circuit breaker is open nothing is sent, so those polls do *not* count — a long outage cannot exhaust a message. After `Outbox:MaxAttempts` (default 10) a row is **parked**: it stays in the table, is skipped by the poller, and `/health/ready` reports `Unhealthy` with a `parked` count. Reset `attempts` to 0 to requeue it. Rows unprocessed for longer than `Outbox:BackedUpAfterMinutes` make `/health/ready` `Degraded`.
- **4xx from the Search API is not retried** (a poison message would never succeed); it parks quickly and shows on health.
- **Scale-out safe.** `SKIP LOCKED` lets several Post API instances run the loop without double-claiming.
- **Shared secret.** `Projection__SharedSecret` (≥ 32 chars, from `.env`) is sent as `X-Projection-Secret` and compared in constant time; the endpoint is excluded from OpenAPI and has no other auth. The Search API also applies `db/search-schema.sql` on startup in Development so a fresh database is ready without compose.
- The Search API keeps its **own copy** of `JobProjectionMessage`; the services share a wire contract, not an assembly.

## Search API (read side)

| Endpoint | Result |
|---|---|
| `GET /api/jobs?q=&department=&location=&workArrangement=&employmentType=&seniority=&salaryMin=&salaryMax=&postedWithinDays=&sort=&page=&pageSize=` | `200 { items[], page, pageSize, total, totalPages }` — open, unexpired postings only |
| `GET /api/jobs/{slug}` | `200 JobDetailResponse` (with `similar[]`, `ETag`), `304` on `If-None-Match`, `404` for an unknown slug. Closed/expired postings still return 200 with `isOpen:false` so the page can say so |
| `GET /api/facets?…same filters…` | `200 { departments[], locations[], workArrangements[], employmentTypes[], seniorities[], total }` with counts |

- `department`, `workArrangement`, `employmentType`, `seniority` accept repeated keys (multi-select). `sort` is `recent` (default) `relevance` `salaryDesc` `salaryAsc` `closingSoon`; anything else is a 400. `pageSize` caps at 50.
- `salaryMin`/`salaryMax` filter by overlap with the posting's range; postings with a hidden salary never match a salary filter and return `null` numbers.
- Caching: in-process output cache tagged `jobs` and evicted on every applied projection; `Cache-Control: public, max-age=60` (lists, facets) / `300` (detail); weak ETags on detail. Responses are Brotli/gzip compressed.
- `docs/ARCHITECTURE.md` §6 explains each index, the one `ILIKE`, and the three cache layers.

Seeding the board with ~1,000 demo rows is *Quick start → 6*.

## post-web (hiring manager portal)

`apps/post-web` — Angular 22, standalone, zoneless, signals, lazy routes, `OnPush` everywhere, strict templates, SCSS with the design tokens in `src/styles/_tokens.scss` (duplicated into search-web by design).

| Route | Screen | Wireframe |
|---|---|---|
| `/signup`, `/login` | account creation, sign-in with 401 banner and 429 countdown | 1.1, 1.2 |
| `/` | dashboard: search (300 ms debounce), status, sort and page in the URL; skeleton after 200 ms; empty vs. filtered-empty; error panel with retry | 1.3, 1.3b |
| `/postings/new` | the posting form; Cancel / Save as draft / Publish in a sticky footer | 1.4, 1.4b |
| `/postings/:id/confirmation` | the saved record exactly as the API returned it, with the propagation note | 1.5 |
| `/postings/:id` | edit; save with the loaded `version` (409 → reload or overwrite); Close posting with a typed `CLOSE` confirmation | 1.6 |

- **Session**: the access token lives in a signal in `AuthService`, never in storage; the refresh token is the `httpOnly` cookie the API sets. On load the app calls `/api/auth/refresh` (`provideAppInitializer`) so a reload on a protected page stays there. A functional interceptor attaches the bearer and, on a 401, refreshes once and retries; a functional guard redirects to `/login?returnUrl=…`.
- **Server errors** (`core/problem-details.ts`): `applyServerErrors(form, problem)` walks the `errors` keys, calls `control.setErrors({ server })` on the control of the same name, clears it on that control's next change, and returns unmatched keys so the banner still lists them. The banner is `role="alert"`, focused after the response, and each line focuses its input.
- **Custom controls** (hand-built, `shared/`): keyboard-operable date picker (arrows, PageUp/PageDown, Home/End, Esc; past days disabled and skipped), chip input (Enter/comma commit, Backspace removes), segmented radio groups, focus-trapped `alertdialog`, toasts (5 s, paused on hover; errors persist).
- **Tests** (`npx ng test`): the form maps a `ValidationProblemDetails` payload onto controls and renders the summary; the date picker's keyboard contract.
