# Quizizzo

Quizizzo is a real-time browser party-game platform built around a shared display, an authenticated host controller, anonymous player phones, and a server-authoritative game engine. This repository is a .NET 10 modular monolith. Milestone progress and mandatory boundaries live in [AGENTS.md](AGENTS.md).

## Prerequisites

- .NET SDK 10.0.400 or a compatible later patch
- Docker Desktop (recommended for local PostgreSQL)
- PostgreSQL 17 when not using Compose

## Run locally

1. Copy `.env.example` to `.env` and replace its sample password.
2. Start PostgreSQL with `docker compose up -d postgres`.
3. Set the same connection string through user secrets or `ConnectionStrings__DefaultConnection`. Do not commit real credentials.
4. Apply migrations:
   `dotnet ef database update --project src/Quizizzo.Infrastructure --startup-project src/Quizizzo.Web`
5. Run: `dotnet run --project src/Quizizzo.Web`.

The template connection string in `appsettings.json` is local-development-only. Environment variables override it in containers and production.

## Authentication and database

ASP.NET Core Identity supplies registration, login/logout, remember-me, confirmation/reset architecture, and account management. `ApplicationDbContext` and migrations belong to Infrastructure and use PostgreSQL through Npgsql. The development sender deliberately does not deliver mail; replace the registered `IEmailSender<ApplicationUser>` for production.

Create a migration with:

```powershell
dotnet ef migrations add Name --project src/Quizizzo.Infrastructure --startup-project src/Quizizzo.Web --output-dir Identity/Migrations
```

Never use `EnsureCreated()` for deployed environments.

## Build and test

```powershell
dotnet restore Quizizzo.sln
dotnet build Quizizzo.sln --no-restore
dotnet test Quizizzo.sln --no-build
```

Health endpoints are `/health/live` for process liveness and `/health/ready` for PostgreSQL readiness.

## Containers

`docker compose up --build` starts the isolated `quizizzo` Compose project, web application, private network, and persistent PostgreSQL volume. PostgreSQL is not published on the host. The web port defaults to `127.0.0.1:8081` and can be changed with `QUIZIZZO_HTTP_PORT`, avoiding collisions with an existing website. Apply migrations as an explicit deployment step; container startup intentionally does not mutate the schema automatically.

For a VPS already hosting another website, follow [the coexistence deployment guide](docs/deployment/hetzner-coexistence.md). Quizizzo commands must remain scoped to its Compose project and must never prune or stop unrelated containers.

## Architecture

Dependencies point inward from the Web composition root toward Application, Domain, GameContracts, and GameEngine. Infrastructure implements persistence and external-service concerns. Games are isolated modules discovered through registration rather than game-name conditionals. See [architecture decisions](docs/adr/README.md).

Adding games, the drawing subsystem, Phaser integration, CI/CD, and Hetzner deployment are deliberately scheduled in later milestones in `AGENTS.md`; they are not part of Foundation.
