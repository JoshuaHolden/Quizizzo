# Quizizzo

Quizizzo is a real-time browser party-game platform built around a shared display, an authenticated host controller, anonymous player phones, and a server-authoritative game engine. This repository is a .NET 10 modular monolith. Milestone progress and mandatory boundaries live in [AGENTS.md](AGENTS.md).

## Prerequisites

- .NET SDK 10.0.400 or a compatible later patch
- Node.js 22 (only required when rebuilding pinned browser assets outside Docker)
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
npm install
npm run build:client
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

## Party and display foundation

Authenticated hosts use `/host` to create or resume their active party and `/host/party/{partyId}` for the lobby foundation. Active room codes are four unambiguous characters and are protected by a PostgreSQL partial unique index. Host ownership is checked in the application layer and backed by the Identity user foreign key.

Opening `/display` creates or restores a durable display session using an HttpOnly browser cookie. Only a SHA-256 token hash is stored. The screen supplies a short-lived pairing code and host link; `/host/pair-display/{pairingCode}` allows only the owning host to attach it to a party.

## Anonymous player sessions

The paired display renders a QR code for `/join/{roomCode}`. A player enters a validated name and receives a persistent generated character. Join submissions are antiforgery-protected and rate-limited per IP. The server writes a 256-bit credential to an HttpOnly, SameSite cookie and persists only its SHA-256 hash.

Opening or refreshing `/play` validates that credential and reconstructs the player's ID, party, name, character, status, and score. Rejoining the same party from the same browser restores that identity instead of creating a duplicate. Host, display, and player views use a thin SignalR hub for change hints and always reload authoritative state from application services. Transport connection IDs are never application identities, and short player disconnects are absorbed by a configurable grace period.

The automated [Milestone 5 recovery gate](docs/testing/recovery-gate.md) exercises the real HTTP page and SignalR hub pipelines for host, display, and player refreshes and transport replacement before game-engine work begins.

Adding games, the drawing subsystem, Phaser integration, CI/CD, and Hetzner deployment are deliberately scheduled in later milestones in `AGENTS.md`; they are not part of Foundation.
