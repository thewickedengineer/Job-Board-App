# CLAUDE.md — TalentBridge Job Board

Project constitution for Claude Code. Read this fully before writing any code. Everything here is a decision already made — do not re-litigate it, do not offer alternatives mid-build, do not substitute a technology because it is more familiar.

---

## 1. What we are building and why

A monorepo containing two Angular applications and two .NET Web APIs that together form a job board.

- **App 1 — Post.** A hiring manager signs up, logs in, and creates job postings. Low volume: a handful of writes per day. Optimize for correctness, validation quality, and auditability.
- **App 2 — Search.** Candidates browse and read job postings anonymously. High volume: many concurrent readers, read-only. Optimize for latency, cacheability, and cheap queries.

**The load asymmetry is the design brief, not a footnote.** Every architectural choice below exists to answer it. A reviewer will be looking for evidence that the two sides were designed differently on purpose.

Build this the way it would be built in production, at a scale appropriate to a focused feature set. No toy shortcuts, no over-engineering into a distributed-systems showcase.

---

## 2. Non-negotiable stack

| Concern | Choice |
|---|---|
| Frontend | **Angular 22**, one app per project, standalone components, signals, zoneless change detection |
| Backend | **.NET 10** Web API, one API per app, minimal APIs |
| Database | **Supabase (PostgreSQL)** — a single instance, two schemas |
| Write-side data access | **EF Core** (App 1 only) |
| Read-side data access | **Dapper** (App 2 only) |
| Inter-app communication | **Transactional outbox + HTTP push with retry** |
| Container orchestration | **docker-compose** at repo root |
| Tests | **xUnit** (.NET), **Vitest** (Angular) |

### 2.1 On EF Core *and* Dapper

The brief says "persist via EF Core" for App 1 and "use Dapper" under database requirements. These are not in conflict — they are the correct split:

- **App 1 writes through EF Core.** Rich domain model, change tracking, transactional outbox in the same `SaveChangesAsync`, migrations owned here.
- **App 2 reads through Dapper.** Flat projections, hand-tuned SQL, no tracking overhead, no materialization cost on the hot path.

This is CQRS at the smallest sensible scale, and it is the direct answer to the load-imbalance hint. Write it up in the README as a deliberate decision.

---

## 3. Repository layout

```
talentbridge/
├─ apps/
│  ├─ post-web/                     # Angular 22 — hiring manager portal
│  └─ search-web/                   # Angular 22 — public job board
├─ services/
│  ├─ TalentBridge.Post.Api/        # .NET 10 — write side
│  ├─ TalentBridge.Post.Domain/
│  ├─ TalentBridge.Post.Infrastructure/
│  ├─ TalentBridge.Post.Tests/
│  ├─ TalentBridge.Search.Api/      # .NET 10 — read side
│  ├─ TalentBridge.Search.Infrastructure/
│  └─ TalentBridge.Search.Tests/
├─ db/
│  ├─ migrations/                   # EF Core migrations (post schema)
│  └─ search-schema.sql             # hand-written DDL for the read model
├─ docs/
│  ├─ wireframes.html               # produced by the wireframe step
│  └─ ARCHITECTURE.md
├─ docker-compose.yml
├─ .env.example
├─ TalentBridge.sln
└─ README.md
```

Angular apps run via `ng serve`. Only the two APIs and the database are containerized.

---

## 4. Data model

### 4.1 `post` schema — write model (EF Core owns this)

**`managers`**
`id` uuid pk · `email` citext unique not null · `password_hash` text not null · `full_name` text not null · `organization` text not null · `email_verified` bool default false · `created_at` timestamptz · `last_login_at` timestamptz

**`refresh_tokens`**
`id` uuid pk · `manager_id` fk · `token_hash` text not null · `expires_at` timestamptz · `revoked_at` timestamptz null · `created_at` timestamptz

**`job_postings`**
`id` uuid pk · `manager_id` fk not null · `reference_code` text · `title` text not null · `department` text not null · `employment_type` text not null · `seniority` text not null · `openings` int not null default 1 · `work_arrangement` text not null · `location` text not null · `country` text not null · `salary_min` numeric(12,2) not null · `salary_max` numeric(12,2) not null · `salary_currency` char(3) not null default 'CAD' · `pay_period` text not null default 'Annual' · `salary_visible` bool not null default true · `description` text not null · `responsibilities` text · `requirements` text · `skills` text[] · `application_url` text · `application_email` text · `closing_date` date not null · `status` text not null · `slug` text not null unique · `created_at` timestamptz not null · `updated_at` timestamptz not null · `published_at` timestamptz null · `version` int not null default 1

Check constraints in the database, not only in code: `salary_min < salary_max`, `openings > 0`, `status in (...)`.

**`outbox_messages`**
`id` bigserial pk · `aggregate_id` uuid · `type` text · `payload` jsonb · `occurred_at` timestamptz · `processed_at` timestamptz null · `attempts` int default 0 · `last_error` text null

### 4.2 `search` schema — read model (Dapper owns this, plain SQL DDL)

**`job_listings`** — a denormalized projection built for the two queries App 2 actually runs.

`id` uuid pk · `slug` text unique · `title` · `department` · `location` · `country` · `work_arrangement` · `employment_type` · `seniority` · `salary_min` · `salary_max` · `salary_currency` · `pay_period` · `salary_visible` · `description` · `responsibilities` · `requirements` · `skills` text[] · `organization` · `application_url` · `application_email` · `closing_date` · `published_at` · `is_open` bool · `search_vector` tsvector generated · `projected_at` timestamptz

Indexes that must exist and must be justified in the README:
- GIN on `search_vector` for keyword search
- GIN on `skills`
- B-tree composite on `(is_open, published_at desc)` for the default listing
- B-tree on `department`, `work_arrangement`, `employment_type`
- B-tree on `closing_date`

The read model carries `organization` copied from the manager. That denormalization is intentional — the read side must never join across schemas, and must never touch `post` tables.

---

## 5. API contracts

### 5.1 Post API — `http://localhost:5001`

```
POST   /api/auth/signup            → 201 { accessToken, refreshToken, manager }
POST   /api/auth/login             → 200 { accessToken, refreshToken, manager }
POST   /api/auth/refresh           → 200 { accessToken, refreshToken }
POST   /api/auth/logout            → 204
GET    /api/me                     → 200 manager                       [auth]
POST   /api/job-postings           → 201 JobPostingResponse + Location  [auth]
GET    /api/job-postings           → 200 paged, manager's own only      [auth]
GET    /api/job-postings/{id}      → 200 JobPostingResponse             [auth]
PUT    /api/job-postings/{id}      → 200 JobPostingResponse             [auth]
POST   /api/job-postings/{id}/close→ 200 JobPostingResponse             [auth]
GET    /health                     → 200
```

`POST /api/job-postings` returns the **complete persisted record** — server-generated `id`, `slug`, `status`, `createdAt`, `version` — because App 1's confirmation screen renders exactly what came back. Never let the client render its own optimistic copy on the confirmation screen.

### 5.2 Search API — `http://localhost:5002`

```
GET  /api/jobs?q=&department=&location=&workArrangement=&employmentType=
              &seniority=&salaryMin=&postedWithinDays=&sort=&page=&pageSize=
     → 200 { items[], page, pageSize, total, totalPages }
GET  /api/jobs/{slug}   → 200 JobDetailResponse | 404
GET  /api/facets        → 200 { departments[], locations[], ... } with counts
POST /internal/projections/job   → 202   [internal, shared-secret header]
GET  /health            → 200
```

`pageSize` caps at 50. `page` is 1-based. Reject unknown `sort` values with 400 rather than silently defaulting.

---

## 6. Validation and the error contract

Validation rules, enforced **identically on both sides**:

| Field | Rule |
|---|---|
| Title | required, 3–120 chars |
| Department | required, 2–80 |
| Location | required, 2–120 — unless Work Arrangement is `Remote`, where it may be the country |
| Description | required, 50–10,000 chars |
| Salary Min / Max | both required, > 0, ≤ 10,000,000, **Min strictly less than Max** |
| Salary Currency | required, ISO 4217, from an allowed list |
| Closing Date | required, strictly in the future (compare in UTC, date-only) |
| Employment Type / Seniority / Work Arrangement | required, must be a known enum value |
| Openings | 1–999 |
| Application Email | valid email when present |
| Application URL | absolute http/https when present |
| Skills | max 20 entries, each 1–40 chars |

Server-side validation is **not** a mirror of client-side validation for show — it is the authority. The client version exists purely for latency.

**Error contract:** every 400 returns RFC 9457 `ValidationProblemDetails`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "salaryMax": ["Salary maximum must be greater than salary minimum."],
    "closingDate": ["Closing date must be in the future."]
  },
  "traceId": "..."
}
```

Keys are **camelCase and match Angular form control names exactly**, so the client can map server errors onto controls mechanically with no translation table. This is a hard requirement — a mismatch here is a build failure, not a cosmetic issue. Cross-field failures (min ≥ max) are keyed to `salaryMax`.

On the Angular side, server errors are applied via `control.setErrors({ server: message })`, an error summary banner is rendered at the top of the form listing every failure, focus moves to that banner, and each entry links to and focuses its control. Server errors clear on the next change to that control.

---

## 7. Authentication

Self-issued JWT, owned by the Post API. Do **not** use Supabase Auth — the API must be self-contained and testable without network access to Supabase's auth service.

- Password hashing: ASP.NET Core `PasswordHasher<T>` (PBKDF2), or BCrypt via `BCrypt.Net-Next`. Never store anything reversible.
- Access token: JWT, 15-minute lifetime, HMAC-SHA256, claims `sub`, `email`, `name`, `org`.
- Refresh token: opaque random 256-bit value, **hashed at rest**, 14-day lifetime, rotated on every use, old token revoked.
- Storage in the browser: access token in memory (a signal in an auth service), refresh token in an `httpOnly` `SameSite=Strict` cookie. Do not put either in `localStorage`.
- Functional `CanActivateFn` guard on protected routes; a functional HTTP interceptor attaches the bearer token and transparently retries once on 401 after a refresh.
- Rate limit `/api/auth/login` and `/api/auth/signup` with the built-in ASP.NET Core rate limiter (fixed window, per IP). Return 429 with `Retry-After`.
- Generic failure message on login — never disclose whether the email exists.

The Search API has **no** authentication. It is public by design.

---

## 8. Inter-app communication

**Transactional outbox in the Post API, HTTP push to the Search API, idempotent apply.**

1. On publish, the job posting row and an `outbox_messages` row are written in a **single EF Core transaction**. Either both land or neither does.
2. A `BackgroundService` in the Post API polls unprocessed outbox rows (every 2s, batch of 20, `FOR UPDATE SKIP LOCKED`), and POSTs each to `POST /internal/projections/job` on the Search API with a shared-secret header.
3. Retries use exponential backoff via a Polly resilience pipeline. After N attempts the row is left with `last_error` set and surfaced on `/health`. Never lose the message; never block the user's request on delivery.
4. The Search API's projection endpoint **upserts** into `search.job_listings` keyed on `id`, and ignores any message whose `version` is not greater than the stored one. Delivery is at-least-once; application is idempotent.

Write the eventual-consistency window into the UI: after a successful post, the confirmation screen tells the manager the listing will appear on the public board shortly. Do not pretend it is synchronous.

**Do not** let the Search API read from the `post` schema. That shortcut defeats the entire point of the exercise and a reviewer will spot it immediately.

---

## 9. Read-side performance (App 2)

Because this is the high-traffic side, it must show deliberate work:

- **Dapper only.** Parameterized SQL in dedicated query classes, no string concatenation, no ORM.
- **Output caching** on `GET /api/jobs` and `GET /api/jobs/{slug}` (`AddOutputCache`, vary by query, 60s for lists, 300s for details), invalidated by tag on projection apply.
- **ETag / `If-None-Match`** on detail responses so repeat views cost a 304.
- Keyset-friendly pagination; hard cap on `pageSize`; never `SELECT *`.
- Full-text search through the generated `tsvector` column, not `ILIKE '%...%'`.
- `AsNoTracking` is irrelevant here — there is no DbContext on this side at all. Keep it that way.
- Response compression, and a `Cache-Control: public` header on anonymous reads.
- Connection pooling via a single registered `NpgsqlDataSource`.

State these choices in `docs/ARCHITECTURE.md` with the reasoning. The decisions matter more than the volume of code.

---

## 10. Angular 22 conventions

Both apps, no exceptions:

- Standalone components. **No NgModules.**
- Zoneless: `provideZonelessChangeDetection()`. No `zone.js` in the build.
- State in **signals**; derived state in `computed`; `effect` used sparingly and never to mutate other signals.
- Server data via `httpResource` / `resource` where it fits the read pattern, otherwise a typed service returning observables consumed with `toSignal`.
- New control flow only — `@if` / `@for` / `@switch` / `@defer`. No `*ngIf`, no `*ngFor`.
- `ChangeDetectionStrategy.OnPush` everywhere.
- **Typed reactive forms** for the job posting form. Cross-field salary validation is a form-group-level validator, and the error is surfaced against `salaryMax`.
- `input()` / `output()` / `model()` signal APIs, not decorators.
- Functional route guards, functional interceptors, `provideHttpClient(withFetch(), withInterceptors([...]))`.
- Lazy-loaded routes. `@defer` for below-the-fold blocks on the search page.
- Strict TypeScript: `strict: true`, `strictTemplates: true`, no `any`.
- Styling: SCSS with CSS custom properties for the design tokens, shared token file duplicated into each app (no shared build library — keep the apps independently deployable).
- Accessibility is part of "done": labelled inputs, correct heading order, focus management after submit, `aria-live` for async results, visible focus rings, keyboard-operable filters and date picker.
- `environment.ts` holds only the API base URL. No secrets in the frontend, ever.

**search-web** additionally: filters are reflected in the URL query string so results are shareable and back/forward works; debounce keyword input by 300ms; skeleton loaders, not spinners, for list loading.

---

## 11. .NET 10 conventions

- Minimal APIs grouped with `MapGroup`, endpoint handlers in separate files — no 600-line `Program.cs`.
- `TypedResults` everywhere for typed, testable returns.
- Validation with **FluentValidation**, wired into a filter that produces the `ValidationProblemDetails` shape in §6.
- `AddProblemDetails()` plus a global exception handler (`IExceptionHandler`). No raw exception text ever reaches a client.
- OpenAPI via `Microsoft.AspNetCore.OpenApi`, served in Development only.
- `Microsoft.Extensions.Http.Resilience` / Polly for the outbox publisher's HTTP client.
- Serilog with structured logging and a correlation/trace id on every request.
- Health checks: `/health` (liveness) and `/health/ready` (database reachable, outbox not backed up).
- Nullable reference types on, warnings as errors.
- No repository-over-DbContext abstraction on the write side; EF Core's `DbContext` *is* the unit of work. On the read side, one query class per screen.
- Configuration via `IOptions<T>` with validation on startup — the app must **fail fast** if the connection string or JWT secret is missing.
- CORS: explicit origins for the two Angular dev servers, not `AllowAnyOrigin`.

---

## 12. Docker

Single `docker-compose.yml` at the repo root that starts **both APIs and a Postgres database**. Multi-stage Dockerfiles per API (SDK build stage → `aspnet` runtime stage, non-root user, no SDK in the final image).

Two database modes, both supported and both documented in the README:

1. **Local Postgres in compose** (default for `docker compose up` to work on a clean machine) — a `postgres:17` service with a named volume and an init script that creates both schemas.
2. **Supabase** — set `USE_SUPABASE=true` and the connection string in `.env`; the local `postgres` service is then skipped via a compose profile.

`.env.example` is committed with every key present and every value empty or a placeholder. The real `.env` is gitignored. **Never commit a real connection string, anon key, service-role key, or JWT secret.**

Migrations: the Post API applies EF Core migrations on startup in Development only, guarded by a flag. The `search` schema DDL runs from `db/search-schema.sql` via the init path.

---

## 13. Testing

The brief asks for one test. Ship meaningfully more than one, but keep them fast and meaningful rather than padding coverage:

**Required**
- `CreateJobPostingValidatorTests` — the full validation matrix, especially `salaryMin >= salaryMax` and past closing dates, including boundary cases (equal values, closing date = today).
- `OutboxPublisherTests` — a message is written in the same transaction as the posting; a failed push leaves the message unprocessed and increments `attempts`.
- `JobProjectionHandlerTests` — applying the same message twice is a no-op; an older `version` is ignored.
- An integration test over the Post API using `WebApplicationFactory` + **Testcontainers** for Postgres: POST a valid job → 201 with a populated record; POST an invalid one → 400 with the exact `errors` keys from §6.
- One Angular test (Vitest): the job posting form maps a server `ValidationProblemDetails` payload onto the right controls and renders the error summary.

Tests run offline. No test may require Supabase or the network.

---

## 14. Definition of done

- [ ] `docker compose up` on a clean machine brings up both APIs and the database, both health endpoints green.
- [ ] `ng serve` in each app runs against the running APIs with no CORS errors.
- [ ] A manager can sign up, log in, post a job, and see the server's saved record on the confirmation screen.
- [ ] Submitting an invalid job shows server errors anchored to the correct fields.
- [ ] Within seconds, that job appears on the public board and its detail page loads by slug.
- [ ] Filters and search on App 2 work and are reflected in the URL.
- [ ] Closed and expired postings are excluded from the board.
- [ ] All tests pass via `dotnet test` and `npm test`.
- [ ] No secrets in the repo; `.env.example` is complete.
- [ ] README explains setup, the CQRS split, the outbox choice, and the read-side performance work.

---

## 15. Build order

Work in these phases. **Stop and report at the end of each phase** with what was built and what to verify. Do not run ahead.

1. **Scaffold** — monorepo layout, solution, both Angular apps, both APIs, `.editorconfig`, `.gitignore`, `.env.example`, empty compose file. Verify: everything builds.
2. **Database** — EF Core model + initial migration for the `post` schema; `search-schema.sql` for the read model with all indexes. Verify: migration applies to a local Postgres container.
3. **Post API — auth** — signup, login, refresh, logout, rate limiting, `/api/me`. Verify: full auth cycle via HTTP file / curl.
4. **Post API — job postings** — create, list, get, update, close; FluentValidation; the error contract from §6. Verify: integration tests green.
5. **Outbox + projection** — outbox table, background publisher with Polly, Search API projection endpoint, idempotent upsert. Verify: post a job, watch it land in `search.job_listings`.
6. **Search API** — Dapper queries, list with filters and paging, detail by slug, facets, output caching, ETags. Verify: query performance sane against ~1,000 seeded rows.
7. **post-web** — auth screens, dashboard, the job posting form, confirmation, edit. Build to the wireframes in `docs/wireframes.html`. Verify: all states from the wireframes exist.
8. **search-web** — results with filters and URL sync, detail page, skeletons, empty and error states, mobile layout. Verify: against wireframes.
9. **Docker** — Dockerfiles, compose with both modes, health checks, seed data. Verify: clean-machine `docker compose up`.
10. **Tests, README, ARCHITECTURE.md** — finish the test suite and write the docs.

---

## 16. Ask me, do not assume

Stop and ask before proceeding when you hit any of these:

- **Supabase configuration.** I will supply the project URL, connection string (pooled, port 6543, and direct, port 5432), and the database password when you reach phase 2. Do not invent placeholders that look real, do not scaffold around a guessed host, and do not use the Supabase MCP tools to create a project without asking me first.
- The JWT signing secret and the internal shared secret for the projection endpoint — I will supply, or you may generate and tell me, but they go in `.env` only.
- Any decision that would deviate from this document.
- Any point where the wireframes and this document disagree — this document wins on behaviour, the wireframes win on layout, but tell me either way.

## 17. Rules for you

- **Do not** substitute technologies. No MediatR, no AutoMapper, no Entity Framework on the read side, no Dapper on the write side, no NgRx, no Angular Material unless I ask — hand-built components against the wireframes.
- **Do not** generate placeholder/TODO implementations and call a phase done. If something cannot be finished, say so explicitly.
- **Do not** write a README that describes features that do not exist.
- Prefer fewer, better files over many thin ones. Do not create abstraction layers with one implementation.
- Commit at the end of each phase with a conventional-commit message. Do not commit `.env`.
- Keep the two Angular apps independent. No shared library. Duplicating a small DTO interface between them is correct here — coupling them is not.
- When you finish a phase, give me: what changed, how to verify it by hand, and anything you deferred.

---

## 18. Kickoff

Read `docs/wireframes.html` if present, then begin **Phase 1** and stop when it is complete.
