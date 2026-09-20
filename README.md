# TalentBridge

A job board built as two deliberately different applications over one Postgres database:

- **Post** (`apps/post-web` + `services/TalentBridge.Post.*`) — hiring managers sign up and create job postings. Low write volume; optimised for correctness and validation.
- **Search** (`apps/search-web` + `services/TalentBridge.Search.*`) — candidates browse postings anonymously. High read volume; optimised for latency and cacheability.

The full design is in [`CLAUDE.md`](./CLAUDE.md). This README tracks what is actually built.

## Status

**Phase 7 — post-web.** The hiring-manager portal is complete against wireframes 1.1–1.6: sign up, log in (generic 401, 429 lockout countdown), dashboard (URL-synced search/status/sort/paging, skeleton, empty vs. filtered-empty, error + retry), the job-posting form (typed reactive form, §6 client rules, group-level salary validator surfaced on `salaryMax`, server errors mapped mechanically onto controls with a focused error-summary banner), confirmation (echoes the API record), and edit (version conflicts → reload or overwrite, close with typed confirmation). Both APIs are done; search-web and Docker remain.

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

### Seed the board

```sh
docker exec -i talentbridge-pg psql -U talentbridge -d talentbridge < db/seed/search-listings.sql
```

Loads ~1,000 deterministic listings (plus a few closed and expired) into the read model only — they have no write-side counterpart and exist so the board has something to show and query plans can be judged at a realistic size.

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

## Run

```sh
dotnet run --project services/TalentBridge.Post.Api     # http://localhost:5001/health  and  /health/ready
dotnet run --project services/TalentBridge.Search.Api   # http://localhost:5002/health  and  /health/ready

cd apps/post-web   && npx ng serve                       # http://localhost:4200
cd apps/search-web && npx ng serve                       # http://localhost:4201
```

## Angular conventions in place

Both apps are generated with: standalone components, zoneless change detection (`provideZonelessChangeDetection()`, no `zone.js` dependency), SCSS, Vitest, `strict: true` and `strictTemplates: true`, and no SSR. `src/environments/environment.ts` holds only the API base URL.
