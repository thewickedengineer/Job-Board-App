# TalentBridge

A job board built as two deliberately different applications over one Postgres database:

- **Post** (`apps/post-web` + `services/TalentBridge.Post.*`) — hiring managers sign up and create job postings. Low write volume; optimised for correctness and validation.
- **Search** (`apps/search-web` + `services/TalentBridge.Search.*`) — candidates browse postings anonymously. High read volume; optimised for latency and cacheability.

The full design is in [`CLAUDE.md`](./CLAUDE.md). This README tracks what is actually built.

## Status

**Phase 2 — Database.** The `post` schema (write model) is defined as an EF Core model with an initial migration; the `search` schema (read model) is hand-written DDL. The Post API registers its `DbContext`, fails fast without a connection string, applies migrations on startup in Development, and exposes `/health` (liveness) and `/health/ready` (database reachable). No auth or business endpoints yet.

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

## Run

```sh
dotnet run --project services/TalentBridge.Post.Api     # http://localhost:5001/health  and  /health/ready
dotnet run --project services/TalentBridge.Search.Api   # http://localhost:5002/health

cd apps/post-web   && npx ng serve                       # http://localhost:4200
cd apps/search-web && npx ng serve                       # http://localhost:4201
```

## Angular conventions in place

Both apps are generated with: standalone components, zoneless change detection (`provideZonelessChangeDetection()`, no `zone.js` dependency), SCSS, Vitest, `strict: true` and `strictTemplates: true`, and no SSR. `src/environments/environment.ts` holds only the API base URL.
