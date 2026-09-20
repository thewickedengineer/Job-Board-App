# TalentBridge — Architecture

This document records the decisions and the reasoning. The README says how to run things; this says why they are shaped the way they are. Sections marked *(pending)* describe phases not yet built.

## 1. The brief is an asymmetry

Two user populations with opposite profiles share one dataset:

| | Post (hiring managers) | Search (candidates) |
|---|---|---|
| Traffic | a handful of writes a day | many concurrent anonymous reads |
| Correctness | authoritative validation, audit trail, concurrency control | eventual consistency is fine |
| Latency target | "fast enough" | as close to the cache as possible |
| Data shape | normalised, per-manager ownership | flat, denormalised, pre-joined |

Every choice below is an answer to that table. The two sides are built differently **on purpose**, not by accident of history.

## 2. CQRS at the smallest sensible scale

- **One Postgres, two schemas.** `post` is the write model; `search` is the read model. They live in the same instance so there is one thing to run, back up and secure — but the Search API never has a connection string that can see `post`, and never joins across schemas.
- **EF Core on the write side.** A rich domain (`JobPosting`, `Manager`, `RefreshToken`, `OutboxMessage`) with private setters and behaviour methods, change tracking, a concurrency token, and migrations. `DbContext` *is* the unit of work; there is no repository layer over it, because it would have exactly one implementation and would hide the one property that matters here — that the outbox row and the aggregate commit in a single `SaveChangesAsync`.
- **Dapper on the read side.** One query class per screen (`JobListQuery`, `JobDetailQuery`, `FacetsQuery`), hand-written parameterised SQL, flat row types, no tracking, no materialisation cost beyond the columns actually returned. There is no `DbContext` on this side at all.
- **The read model is a projection, not a view.** `search.job_listings` is a physical, denormalised table. It carries `organization` copied from the manager so a listing never needs the `post.managers` table, and a stored generated `tsvector` so keyword search never computes at query time.

## 3. Write model (`post` schema)

- Invariants live in the database as CHECK constraints (`salary_min < salary_max`, `openings > 0`, closed vocabularies for `status`, `employment_type`, `seniority`, `work_arrangement`, `pay_period`) **and** in FluentValidation. Validation gives the user a good message; the constraint guarantees the row can never be wrong regardless of how it was written.
- `email` is `citext` with a unique index: case-insensitive lookup and uniqueness without `lower()` gymnastics in every query.
- `version` on `job_postings` is the EF concurrency token, increments on every mutation, and is carried on every projection message. One number serves optimistic concurrency for editors (`PUT` must send the version it loaded; stale → 409) and idempotency for the read side.
- `slug` is assigned once and never changes, so a URL on the public board survives edits.
- Status is `Draft | Published | Closed`; *Expired* is derived at read time from `closing_date`. Drafts are fully validated — the schema's NOT NULL columns leave no room for half-filled rows, and that is a feature: a draft is simply a valid posting that is not public yet.
- Guid keys are v7 (time-ordered) generated in the domain and declared `ValueGeneratedNever`, so EF never mistakes a navigation-discovered new entity for an existing row.

## 4. Authentication

Self-issued, so the API is testable with no network: HS256 access tokens (15 min, claims `sub/email/name/org`) and opaque 256-bit refresh tokens whose SHA-256 hash is stored. Refresh rotates on every use with a conditional `UPDATE … WHERE revoked_at IS NULL`, so two concurrent refreshes cannot both win; presenting an already-revoked token is treated as theft and revokes every active token for that manager. The browser keeps the access token in memory and never sees the refresh token — it travels only in an `httpOnly; SameSite=Strict; Path=/api/auth` cookie. Login and signup are rate-limited per IP with the framework's fixed-window limiter; the login failure message never says which half was wrong, and unknown emails are verified against a dummy hash so timing does not say it either.

## 5. Inter-app communication: transactional outbox + HTTP push

The user's request only ever writes rows. Delivery happens later, from a `BackgroundService`:

1. `SaveChangesAsync` commits the posting and an `outbox_messages` row (a full `JobProjectionMessage` snapshot) together or not at all.
2. Every 2 s the publisher claims up to 20 rows with `SELECT … FOR UPDATE SKIP LOCKED` — safe to run on several instances — and POSTs each to `POST /internal/projections/job` with a shared-secret header.
3. The HTTP client runs the `Microsoft.Extensions.Http.Resilience` standard pipeline: 3 retries with exponential backoff and jitter, 5 s attempt timeout, 30 s total, and a circuit breaker.
4. The Search API upserts with `ON CONFLICT (id) DO UPDATE … WHERE job_listings.version < excluded.version` and answers 202 whether or not anything changed. Delivery is at-least-once; application is idempotent; ordering does not matter.

**Failure accounting is the subtle part.** A genuine failure (non-2xx after retries, timeout, connection refused) increments `attempts` and records `last_error`. While the breaker is open nothing is sent, so those polls do *not* count — a long outage cannot exhaust a message. After `MaxAttempts` a row is *parked*: never deleted, skipped by the poller, reported `Unhealthy` on `/health/ready`; a human resets `attempts` to requeue it. 4xx responses are not retried, so a poison message parks quickly instead of being hammered.

Why HTTP push rather than a queue: at this scale a broker is a second thing to run, secure and monitor, and it would move the durability question rather than answer it. The outbox table *is* the queue; Postgres already gives it transactions, locking and durability.

## 6. Read side (`search` schema and Search API)

The public board is where the traffic is, so this is where the deliberate performance work is.

### 6.1 Storage and indexes

`search.job_listings` is one wide row per posting. The indexes exist for specific queries:

| Index | Serves |
|---|---|
| GIN on `search_vector` | `?q=` — `search_vector @@ websearch_to_tsquery('english', …)`; never `ILIKE '%…%'` |
| GIN on `skills` | array containment (`skills @> '{…}'`) and, through the tsvector, keyword hits on skills |
| B-tree `(is_open, published_at desc)` | the default board: open listings newest first, `LIMIT/OFFSET` walks the index in order |
| B-tree on `department`, `work_arrangement`, `employment_type` | facet filters and facet counts |
| B-tree on `closing_date` | the `closing_date >= current_date` predicate on every listing query |
| unique on `slug` | the detail page |

`search_vector` is a **stored generated column** with weights — title A; department, organization, location and skills B; description C; responsibilities and requirements D — so ranking favours the headline over the body. `array_to_string` is only STABLE, so a tiny IMMUTABLE wrapper (`search.skills_to_text`) lets skills participate; without it "python" would not find a posting that lists Python only as a skill.

`is_open` mirrors the write side's status. Expiry (`closing_date < current_date`) is evaluated at query time because `current_date` cannot appear in a generated column; the `closing_date` index keeps that predicate cheap.

Against ~1,000 seeded rows every query in `db/seed/search-listings.sql` scenarios is index-backed and sub-millisecond in `EXPLAIN ANALYZE` (default board 0.3 ms via the composite index, keyword 0.4 ms via the GIN bitmap scan, slug 0.02 ms).

### 6.2 Queries

- **Never `SELECT *`.** Each query names its columns; the list query returns `left(description, 240)` as an excerpt rather than the whole body.
- **One round trip per screen.** The list uses `count(*) over ()` so total and page come back together; facets batch five grouped queries and the total in a single `QueryMultiple`.
- **Facets are counted with their own dimension excluded** (standard faceted-search behaviour): the department counts ignore the department filter but honour everything else, so a candidate can see what adding a second department would return and the "no results" screen can suggest a relaxation that is known to work.
- **Deterministic ordering.** Every sort ends in `id desc`, so paging never shows a row twice or skips one. The contract asks for `page/pageSize` (offset paging); the ordering is keyset-friendly should a cursor be wanted later.
- **`pageSize` caps at 50** and unknown `sort` values are a 400, never a silent default.
- **The one `ILIKE`.** `?location=` is free text ("city or region") matched with `ILIKE '%…%'` against the short `location`/`country` columns. It runs after the indexed predicates have narrowed the set and is cheap at this size; the upgrade path is a trigram index. Everything keyword-shaped goes through the tsvector.
- **Hidden salaries stay hidden.** When `salary_visible` is false the numbers are nulled in every response *and* excluded from salary filters, so they cannot be inferred by probing.

### 6.3 Caching, in three layers

1. **In-process output cache** (`AddOutputCache`): lists and facets for 60 s varying by the full query string, details for 300 s varying by slug, all tagged `jobs`. The projection endpoint evicts the tag whenever it actually applies a message, so a change from the write side is visible on the next request, not after the TTL. Between changes, repeated reads never touch Postgres.
2. **Browser / shared caches**: `Cache-Control: public, max-age=60|300` on every anonymous read, mirroring the server windows.
3. **Conditional requests**: detail responses carry a weak ETag derived from the row's `id:version:projected_at` plus the ids and versions of the similar roles — cheap and deterministic, no body hashing. `If-None-Match` yields a 304 from the endpoint or, for a cached entry, from the output cache middleware itself.

Response compression (Brotli/gzip) runs outside the output cache, so the cache stores one uncompressed body and each client gets its own encoding.

### 6.4 Connections

A single `NpgsqlDataSource` registered once and injected everywhere: one pool, one place to configure it, no per-request connection strings.

## 7. Error contract

Every 4xx/5xx from either API is RFC 9457 problem details with a `traceId`. Validation failures are `ValidationProblemDetails` whose `errors` keys are camelCase and equal to the Angular form control names, so the client maps them mechanically. Cross-field salary failures are keyed to `salaryMax`; skill failures to `skills` (one chip control), not to an index. A global `IExceptionHandler` guarantees no raw exception text leaks.

## 8. Frontends

Two Angular 22 applications that share tokens, primitives and conventions but no code — `src/styles/_tokens.scss` is copied, not imported, so each app deploys alone. Both are standalone, zoneless, signal-driven and `OnPush` throughout; server data flows through `httpResource` where the read pattern fits (the dashboard list keyed on the URL's query params) and typed services with `firstValueFrom` for commands.

### 8.1 post-web (built)

- **Session model.** The access token is a signal; the refresh token is the API's `httpOnly; SameSite=Strict` cookie and the app never sees it. `provideAppInitializer` exchanges the cookie for a token before the first route resolves, so a reload on a protected page stays put and the guard can be synchronous. The interceptor refreshes once on 401 and retries; concurrent 401s share one in-flight refresh.
- **The §6 contract, client side.** Control names equal the API's validation keys and every input's `id` equals its control name. `applyServerErrors` is therefore a loop, not a mapping table: `form.get(key).setErrors({ server })`, cleared on that control's next value change. The cross-field salary rule is a *group* validator whose error the template renders under `salaryMax` — the same key the server uses — so both sources surface in the same place.
- **Zoneless discipline.** Anything that must focus an element a signal change is about to render (the error banner, the calendar grid) waits for `afterNextRender`; a microtask runs too early. Submitting is expressed with a native `<fieldset disabled>` rather than `FormGroup.disable()`, because re-enabling a group re-runs validators and would erase the server errors the response just delivered.
- **URL as state.** The dashboard's search, status, sort and page live only in the query string; the component derives its query from `ActivatedRoute.queryParamMap` and the list resource re-fetches when it changes. Shareable, refresh-safe, back-button-safe for free.
- **Concurrency for editors.** `PUT` carries the `version` loaded with the record; a 409 is a banner with two honest choices — reload (drop my edits) or overwrite (re-fetch the version, resubmit my edits) — never a silent retry.

### 8.2 search-web *(pending — phase 8)*

## 9. Deployment *(pending — phase 9)*
