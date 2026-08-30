# Quizizzo

Quizizzo is a real-time browser party-game platform built around a shared display, an authenticated host controller, anonymous player phones, and a server-authoritative game engine. This repository is a .NET 10 modular monolith. Milestone progress and mandatory boundaries live in [AGENTS.md](AGENTS.md).

## Prerequisites

- .NET SDK 10.0.400 or a compatible later patch
- Node.js 22 (only required when rebuilding pinned browser assets outside Docker)
- Docker Desktop (recommended for local PostgreSQL)
- PostgreSQL 17 when not using Compose

## Run locally

1. Copy `.env.example` to `.env` and replace its sample password.
2. Build the application image: `docker compose --project-name quizizzo build quizizzo`.
3. Start its private PostgreSQL service: `docker compose --project-name quizizzo up -d postgres`.
4. Apply migrations from the application image on the private network:
   `docker compose --project-name quizizzo run --rm migrate`.
5. Start the web application: `docker compose --project-name quizizzo up -d quizizzo`.
6. Open `http://localhost:8081` and verify `http://localhost:8081/health/ready`.

The one-shot `migrate` service is opt-in and exits after applying EF Core migrations. It uses the same application image and private network as the web service, so PostgreSQL does not need a published host port. Container startup never applies schema changes implicitly.

The template connection string in `appsettings.json` is local-development-only. Environment variables override it in containers and production.
The sample `.env` opts into the Development environment for local Compose use; VPS deployments must set `QUIZIZZO_ASPNETCORE_ENVIRONMENT=Production` (the Compose default).

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
npm run test:client
dotnet restore Quizizzo.sln
dotnet build Quizizzo.sln --no-restore -c Release -p:TreatWarningsAsErrors=true
dotnet test Quizizzo.sln --no-build
```

With the app running at `http://localhost:8081`, run the repeatable Edge browser audit across desktop, tablet, and 320 px phone viewports:

```powershell
npm run test:browser
```

The public audit checks browser errors, accessible control names, accidental horizontal overflow, and user-facing action labels while retaining screenshots under `artifacts/playwright/`. The opt-in end-to-end party audit creates local development records and drives separate host, display, and player browser contexts through joining and an Estimate round:

```powershell
$env:QUIZIZZO_FULL_PARTY_AUDIT = '1'
npx playwright test tests/browser/party-flow.spec.mjs --project=desktop-edge
```

Health endpoints are `/health/live` for process liveness and `/health/ready` for PostgreSQL readiness.

## Containers

The local commands above start the isolated `quizizzo` Compose project, web application, private PostgreSQL database, and private Redis SignalR backplane. Neither PostgreSQL nor Redis is published on the host. The web port defaults to `127.0.0.1:8081` and can be changed with `QUIZIZZO_HTTP_PORT`, avoiding collisions with an existing website. PostgreSQL data, temporary drawing assets, and ASP.NET Core Data Protection keys use separate Quizizzo-specific volumes; Redis carries ephemeral SignalR messages and has no persistence volume. Apply migrations through the explicit one-shot `migrate` service before starting a new application version; ordinary container startup intentionally does not mutate the schema.

Set `QUIZIZZO_ALLOWED_HOSTS` to the exact public Quizizzo hostname in production. The container runs as the built-in non-root `app` user, and `.dockerignore` excludes local secrets, tooling state, generated data, tests, and dependency folders from the image context.

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

## Game engine foundation

The [game engine](docs/architecture/game-engine.md) discovers isolated `IGameModule` implementations and runs each game instance through one bounded, single-consumer command channel. It validates durable actors and UTC deadlines, records idempotent accepted/rejected results, applies shared score awards, persists versioned snapshots to PostgreSQL behind `IGameStateStore`, reconstructs role-specific views, and recovers actors after process replacement. SignalR remains a notification transport rather than authoritative state.

Estimate is the first complete game proof: the host starts it from the existing party lobby, phones receive the reusable number controller, and the display reveals three server-scored rounds before returning everyone to the same lobby with persistent scores.

## Drawing framework

The [reusable drawing framework](docs/architecture/drawing-framework.md) provides a fixed-logical-coordinate Pointer Events canvas for touch, stylus, and mouse. It supports configurable one-to-twelve-frame documents; single-image games use the same controller in one-frame mode with navigation and onion skin automatically removed. Vector strokes, pen/eraser tools, per-frame undo/clear, previous-frame onion skin, and identity-scoped local draft recovery remain in browser JavaScript rather than crossing SignalR point by point.

Large rendered assets are stored through `IDrawingAssetStore`. The initial bounded WebP/PNG adapter uses the persistent, stack-specific `quizizzo-drawing-assets` Compose volume and can later be replaced with object storage. Assets expire after one day by default and an hourly worker removes expired files and PostgreSQL metadata. Image bytes never enter PostgreSQL.

## AniMates

[AniMates](docs/architecture/animates.md)—animation with your mates—assigns private action prompts and runs a server-owned drawing, anonymous playback/voting, creator reveal, and scoring state machine. Phones export validated 512×512 PNG frames through an idempotent same-origin upload rather than SignalR; PostgreSQL stores only bounded ownership/expiry metadata while the dedicated asset volume stores bytes. Phaser cycles the three frames on the shared display, and refreshes reconstruct drawing or submitted/voted state from durable player and game identities.

## Majority Rules

[Majority Rules](docs/architecture/majority-rules.md) runs three server-timed writing, anonymous voting, reveal, and scoring rounds. It proves the reusable text and vote controller contracts: the player page selects each controller from server state without checking the game name. Opaque persisted option IDs keep answer ownership out of voting snapshots, while results reveal authors and award 500 points for every vote received.

## Bullshit

[Bullshit](docs/architecture/bullshit.md) runs three server-owned bluffing, shuffled-choice, and reveal rounds. The truth and bluff-author mapping remain private module state until results; clients receive only persisted opaque shuffled choices. Its reusable Choice controller excludes a player's own bluff, and scoring can combine truth picks, successful bluffs, grouped duplicate-bluff payouts, and an exact-truth bonus.

## Display presentation

The shared display uses one [long-lived Phaser presentation](docs/architecture/phaser-presentation.md) across pairing, lobby, game, results, and return-to-lobby states. Blazor sends reconstructable semantic snapshots while Phaser owns generated character art, responsive 1280×720 scene scaling, tweens, camera effects, and particles. The accessible HTML overlay remains usable without canvas rendering, and reduced-motion preferences disable presentation animation. Phaser 3.90.0 and SignalR are pinned npm dependencies copied to local static assets by `npm run build:client`; no runtime CDN is required.

## Responsive UI

The [responsive UI contract](docs/architecture/responsive-ui.md) covers public, account, host, player-controller, drawing, and shared-display layouts from 320 px phones through 4K screens. It defines touch-target, safe-area, keyboard, reduced-motion, overflow, short-height, and single-frame presentation requirements plus the representative viewport verification matrix.

The completed [production-readiness review](docs/architecture/production-readiness.md) records the runtime, security, persistence, cleanup, and scaling decisions that apply before CI/CD. CI/CD and Hetzner deployment remain scheduled in `AGENTS.md`.

## SignalR Redis backplane

Compose configures the Web service with a password-protected Redis 8 backplane on the private Quizizzo network and an application-specific `Quizizzo.SignalR` channel prefix. Redis distributes SignalR refresh hints only; it contains no authoritative party or game state and can be recreated without data recovery. `/health/ready` includes Redis whenever `ConnectionStrings:Redis` is configured. Non-Compose development and isolated tests may omit that connection string and use the in-process SignalR lifetime manager.

The backplane does not authorize multiple Web replicas: game actors and presence ownership remain process-local. A future multi-replica deployment must first add coordinated actor ownership and distributed presence, and must configure sticky sessions as required by SignalR.
