# Quizizzo Engineering Brief and Delivery Tracker

This file is the repository-local implementation brief. Keep it current: check an item only after its implementation and relevant verification are complete. Work on one bounded milestone at a time; do not implement the whole MVP in an uncontrolled pass.

## Product goal

Quizizzo is a polished, real-time, browser-based multiplayer party-game platform: a shared big-screen display, an authenticated host controller, anonymous player phones, and a server-authoritative game engine. It should feel like Jackbox or Mario Party on the web, not a conventional quiz dashboard.

MVP technology is .NET 10, ASP.NET Core, C#, SignalR, EF Core, PostgreSQL, ASP.NET Core Identity, a Blazor Web App, and a long-lived Phaser.js presentation layer embedded in the display. Deploy as a self-hostable modular monolith.

## Non-negotiable boundaries

- The server owns membership, phases, rounds, deadlines, valid actions, submissions, scores, winners, and progression.
- Clients can reconstruct their complete role-specific logical view after refresh or connection replacement. SignalR messages are hints to refresh state, not the only state history.
- A SignalR `ConnectionId` is transport metadata, never player identity.
- Host identity comes from ASP.NET Core Identity. Anonymous players and displays receive durable, securely stored session credentials; store token hashes server-side where practical.
- Temporary disconnect is not leaving. Player, host, and display refreshes must not kill a party.
- SignalR hubs stay thin; Blazor and Phaser never contain authoritative game logic.
- Game modules depend on game contracts, not SignalR, Blazor, Phaser, EF, or physical asset storage.
- Mutations for each party/game are serialized through one command consumer (a `Channel<GameCommand>` actor-like design). Commands are semantic and retryable actions are idempotent.
- The server sends UTC deadlines (`PhaseEndsAtUtc`); clients only render countdowns.
- Blazor owns display state, HTML, accessibility, SignalR, and presentation orchestration. Phaser owns sprites, tweening, particles, effects, camera, and optional audio. Phaser receives semantic events, never scoring decisions or pixel-level server commands.
- Player UI selects reusable controllers (`Choice`, `Text`, `Number`, `Vote`, `Drawing`, `Waiting`) from server-provided view state rather than switching on game names.
- Drawing pointer/stroke work runs in JavaScript on a logical canvas and is not streamed stroke-by-stroke through SignalR. Drafts are recoverable locally. Large assets use an asset-store abstraction, not huge base64 EF fields.
- Use PostgreSQL and EF migrations from the start. Do not use SQLite or production `EnsureCreated()`.
- The Hetzner deployment must coexist with the existing `logiagraph.com` website and its Docker stack: keep a unique Compose project, private database network/volume, no published database port, and a configurable loopback-only web port or an explicitly selected external reverse-proxy network. Never alter its proxy route or prune/stop unrelated containers.
- Treat player input as hostile; validate authorization, ownership, phase, limits, formats, and idempotency server-side. Never log session credentials.
- Use nullable reference types, DI, async APIs, cancellation tokens, validation, structured logging, and tests. Avoid god classes, static mutable state, business logic in UI/hubs, direct SQL in components, and game-name switch forests.
- Do not add Kubernetes, Kafka, RabbitMQ, event sourcing, microservices, service mesh, distributed actors, or multiple databases for the MVP.

## Intended solution boundaries

```text
src/
  Quizizzo.Domain/             Core domain concepts; no UI, transport, or persistence dependencies
  Quizizzo.Application/        Use cases and orchestration
  Quizizzo.Infrastructure/     EF Core, PostgreSQL, Identity persistence, migrations, sessions, assets, email
  Quizizzo.GameContracts/      Contracts that isolated games implement
  Quizizzo.GameEngine/         Lifecycle, commands, state machines, timers, scoring, snapshots, concurrency
  Quizizzo.Web/                Blazor, APIs, auth UI, SignalR hubs, role UIs, QR, JS/Phaser/canvas interop
  Quizizzo.Games.Estimate/
  Quizizzo.Games.MajorityRules/
  Quizizzo.Games.Bullshit/
  Quizizzo.Games.AnimateThis/
tests/
  Quizizzo.Domain.Tests/
  Quizizzo.Application.Tests/
  Quizizzo.GameEngine.Tests/
  Quizizzo.IntegrationTests/
```

References must point inward: Domain is independent; Application may use Domain and GameContracts; Infrastructure implements application abstractions; GameEngine uses Domain/GameContracts; individual games use GameContracts and appropriate domain primitives; Web is the composition root.

## Core functional scope

- Host accounts: register, login, logout, remember-me, confirmation/reset architecture, management, and replaceable email sender.
- Routes: `/`, `/account/*`, `/host`, `/host/party/{partyId}`, `/join`, `/join/{roomCode}`, `/play`, `/display`.
- Parties: authenticated ownership, four-character unambiguous case-insensitive active room codes, lobby/game lifecycle, party-persistent players/characters/scores, and Party Mix-ready game transitions.
- Display pairing: durable display session, scan-to-pair flow, then player join QR/code without TV credential entry.
- Anonymous players: durable player ID and secure session token, reconnect/rejoin without entering a name again, configurable disconnect grace period.
- Role-specific player, host, and display views must reveal only appropriate information.
- MVP games: Estimate, Majority Rules, Bullshit, and mandatory Animate This.
- Animate This: three configurable logical frames; Pointer Events; fixed logical coordinates; touch/stylus/mouse; compact colour/size tools; eraser restoration; stroke undo; confirmed clear; onion skin; safe frame navigation; local draft recovery; validated/idempotent submission; missing-frame fallback; anonymous playback/voting/reveal/scoring; no self-voting.
- Presentation: persistent recognizable player characters, game-specific themes/assets, responsive 16:9 displays at 720p/1080p/4K, and mobile controllers down to 320px without normal horizontal scrolling.
- Operations: structured logs, application and PostgreSQL health checks, isolated Compose resources with persistent database/assets, multi-site-safe reverse-proxy integration, GitHub CI/CD to GHCR and Hetzner, immutable SHA deployments, safe migrations, health verification, secrets outside source control, and documented rollback.

Central MVP defaults: 12 players, 24-character player names, and 200-character text answers; make limits configurable where useful.

## Plan of action and progress

### Milestone 1 — Foundation (completed 2026-08-26)

- [x] Create the .NET 10 solution and all specified source/test projects (game projects may remain empty shells).
- [x] Configure project references and enforce nullable/implicit usings consistently.
- [x] Configure the Blazor Web App as the composition root.
- [x] Configure EF Core with PostgreSQL and ASP.NET Core Identity.
- [x] Implement registration, login, logout, remember-me, account management, and development email abstraction.
- [x] Create the initial Identity/database migration without `EnsureCreated()`.
- [x] Add application and PostgreSQL health checks.
- [x] Add an isolated Docker/Compose stack with persistent PostgreSQL volume, no published database port, configurable loopback web port, and no committed secrets.
- [x] Create the four test projects and meaningful initial architecture/configuration tests.
- [x] Add README covering purpose, prerequisites, local setup, configuration, database/migrations, Docker, and tests.
- [x] Add ADRs for the required decisions: server authority; Blazor/Phaser split; serialized channels; PostgreSQL; anonymous sessions; drawing storage; VPS stack isolation.
- [x] Restore, build, run tests, and fix all failures.
- [x] Stop and report Milestone 1; do not start party/game functionality in the same chunk.

### Remaining milestones

- [ ] Milestone 2 — Party infrastructure: party creation, room codes, ownership, lobby, display sessions.
- [ ] Milestone 3 — Anonymous player sessions: QR join, names, secure credentials, lobby roster, identity recovery.
- [ ] Milestone 4 — SignalR: thin host/player/display connections, groups, status, reconnect handling.
- [ ] Milestone 5 — Recovery gate: prove player/display/host refresh and `ConnectionId` replacement before proceeding.
- [ ] Milestone 6 — Game engine: contracts, discoverable modules, commands, explicit state machines, channel serialization, timers, views, scoring, state-store abstraction.
- [ ] Milestone 7 — Estimate: complete number-controller round loop as the engine proof.
- [ ] Milestone 8 — Phaser presentation: long-lived canvas, characters, join/disconnect/score/result animations, particles.
- [ ] Milestone 9 — Reusable drawing framework: JS canvas interop, strokes/tools/frames/onion skin/drafts/assets.
- [ ] Milestone 10 — Animate This: prompt, three frames, secure submission, playback, voting, reveal, scoring.
- [ ] Milestone 11 — Majority Rules: prove reusable text and vote flows.
- [ ] Milestone 12 — Bullshit: prove hidden-state, shuffled-choice, and advanced scoring support.
- [ ] Milestone 13 — CI/CD: GitHub Actions, image publishing, Hetzner deployment, migrations, rollback, health verification.

## Verification requirements

Every milestone ends with restore/build/tests. Tests ultimately cover room codes, transitions, scoring, invalid/late/duplicate actions, submissions, connection states, recovery, completion, and the full host/display/player integration path. Animate This additionally covers frame count, ownership, phase/deadline, fallback frames, self-vote rejection, scoring, payload limits, and reconnect both before and after submission. Canvas interaction should gain browser/E2E coverage.

The MVP acceptance scenario is the 34-step end-to-end flow from host registration through party creation/pairing, four-player join, Estimate with player reconnect, score persistence, Animate This drawing/draft recovery/playback/voting/reveal, starting another game without rejoin, display recovery, and persisted party completion.

## Major decisions to preserve

1. Modular monolith, with explicit contracts and a single Web composition root.
2. Server-authoritative logical state with reconstructable role-specific snapshots.
3. Per-party/game serialized command processing and idempotent client actions.
4. PostgreSQL persistence behind abstractions that permit future Redis/object storage without coupling games.
5. Durable application sessions independent of transient SignalR connections.
6. Blazor for application/presentation orchestration; long-lived Phaser and canvas JavaScript for high-frequency visuals/input.

## Deviation log

Before deviating, record the change, reason, benefit, and trade-off here. Preserve all non-negotiable boundaries.

| Date | Change | Reason | Benefit | Trade-off |
|---|---|---|---|---|
| 2026-08-26 | Replace SQL Server with containerized PostgreSQL | The target Hetzner VPS already hosts another website and PostgreSQL is the selected datastore | Lighter isolated database container and a consistent local/production provider | Existing SQL Server migrations and provider-specific configuration are replaced before production data exists |
