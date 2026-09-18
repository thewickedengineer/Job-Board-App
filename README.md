# TalentBridge

A job board built as two deliberately different applications over one Postgres database:

- **Post** (`apps/post-web` + `services/TalentBridge.Post.*`) — hiring managers sign up and create job postings. Low write volume; optimised for correctness and validation.
- **Search** (`apps/search-web` + `services/TalentBridge.Search.*`) — candidates browse postings anonymously. High read volume; optimised for latency and cacheability.

The full design is in [`CLAUDE.md`](./CLAUDE.md). This README tracks what is actually built.

## Status

**Phase 1 — Scaffold.** Monorepo layout, solution, both Angular apps, both APIs. Each API exposes `GET /health` only. No database, auth, or business logic yet.

## Layout

```
apps/
  post-web/                       Angular 22 — hiring manager portal (ng serve → :4200)
  search-web/                     Angular 22 — public job board       (ng serve → :4201)
services/
  Directory.Build.props           shared .NET settings (net10.0, nullable, warnings-as-errors)
  TalentBridge.Post.Api/          .NET 10 minimal API — write side    (:5001)
  TalentBridge.Post.Domain/
  TalentBridge.Post.Infrastructure/
  TalentBridge.Post.Tests/        xUnit
  TalentBridge.Search.Api/        .NET 10 minimal API — read side     (:5002)
  TalentBridge.Search.Infrastructure/
  TalentBridge.Search.Tests/      xUnit
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

## Run

```sh
dotnet run --project services/TalentBridge.Post.Api     # http://localhost:5001/health
dotnet run --project services/TalentBridge.Search.Api   # http://localhost:5002/health

cd apps/post-web   && npx ng serve                       # http://localhost:4200
cd apps/search-web && npx ng serve                       # http://localhost:4201
```

## Angular conventions in place

Both apps are generated with: standalone components, zoneless change detection (`provideZonelessChangeDetection()`, no `zone.js` dependency), SCSS, Vitest, `strict: true` and `strictTemplates: true`, and no SSR. `src/environments/environment.ts` holds only the API base URL.
