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
  Quizizzo.Games.AniMates/
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
- MVP games: Estimate, Majority Rules, Bullshit, and mandatory AniMates.
- AniMates: three configurable logical frames; Pointer Events; fixed logical coordinates; touch/stylus/mouse; compact colour/size tools; eraser restoration; stroke undo; confirmed clear; onion skin; safe frame navigation; local draft recovery; validated/idempotent submission; missing-frame fallback; anonymous playback/voting/reveal/scoring; no self-voting.
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

### Milestone 2 — Party infrastructure (completed 2026-08-26)

- [x] Add `Party`, `PartyId`, `RoomCode`, `PartyStatus`, `DisplaySession`, and `DisplaySessionId` domain types.
- [x] Generate normalized four-character room codes without `0`, `O`, `1`, `I`, or `L`.
- [x] Enforce unique active room codes and one active party per host in PostgreSQL.
- [x] Add authenticated party creation, resume/recent views, and server-side owner authorization.
- [x] Add durable display credentials using a secure browser token with only its SHA-256 hash persisted.
- [x] Add short-lived display pairing codes and owner-authorized party pairing.
- [x] Add `/host`, `/host/party/{partyId}`, `/host/pair-display/{pairingCode}`, and `/display` UI foundations.
- [x] Add the PostgreSQL party/display migration plus domain, application, persistence-model, health, and authorization tests.
- [x] Restore, build, run tests, and fix all failures.
- [x] Stop before anonymous player sessions and SignalR.

### Milestone 3 — Anonymous player sessions (completed 2026-08-26)

- [x] Add durable `Player`, `PlayerId`, `PlayerName`, status, score, and persistent character domain models.
- [x] Centralize the 12-player and 24-character name limits and validate hostile/invalid input.
- [x] Add QR-driven `/join`, `/join/{roomCode}`, and reconstructable `/play` flows.
- [x] Issue a 256-bit anonymous player credential in an HttpOnly cookie and store only its SHA-256 hash.
- [x] Restore the same player ID, name, character, score, and party after refresh without asking for the name again.
- [x] Prevent a repeated same-browser join from creating a duplicate player.
- [x] Add antiforgery validation and per-IP join rate limiting.
- [x] Show the persisted lobby roster to the authorized host and paired display.
- [x] Add the PostgreSQL player migration with party, status, time, and unique token-hash indexes.
- [x] Add domain/application/persistence/QR/authorization tests and pass the full build/test gate.
- [x] Stop before SignalR connection tracking and realtime roster updates.

### Milestone 4 — SignalR (completed 2026-08-26)

- [x] Add a thin party hub whose host, player, and display registration delegates identity and ownership checks to application services.
- [x] Keep durable host user IDs, player IDs, and display session IDs independent from transient SignalR `ConnectionId` values.
- [x] Add party and role-specific groups plus a dedicated unpaired-display session group.
- [x] Track presence by durable subject across multiple tabs/connections and expose host/player/display snapshots.
- [x] Add a configurable player disconnect grace period; cancel pending disconnects when the durable player reconnects.
- [x] Publish state-change hints for player join/reconnect, display pairing, and role presence changes.
- [x] Add a locally served, pinned SignalR browser client with automatic reconnect and Connected/Reconnecting/Disconnected UI status.
- [x] Refresh host, player, and display views from authoritative application state after each hint instead of treating messages as state history.
- [x] Document WebSocket proxy requirements without changing the existing `logiagraph.com` route or unrelated Docker resources.
- [x] Add presence, multi-connection identity, reconnect-grace, hub negotiation, local-client-asset, and DI lifetime tests.
- [x] Restore, build, run all 41 tests, and fix all failures.
- [x] Stop before the explicit refresh/connection-replacement recovery gate.

### Milestone 5 — Recovery gate (completed 2026-08-26)

- [x] Exercise the production Razor page pipeline, cookie middleware, application services, SignalR hub, groups, and presence registry in an isolated integration-test host.
- [x] Prove an authenticated host refresh reconstructs the same owned party and room code.
- [x] Prove a paired display refresh reconstructs the same display session, party, room code, and roster.
- [x] Prove a player refresh reconstructs the same player ID, party, name, character, score, and status.
- [x] Prove distinct host, display, and player SignalR connection IDs collapse to one durable role identity and that replacing one transport does not remove another.
- [x] Prove player replacement within the disconnect grace period cancels disconnection and refresh after grace expiry reconnects the same player.
- [x] Fix prerender disposal so a realtime component that never started JavaScript interop can be safely discarded during refresh.
- [x] Document the recovery proof matrix and the test-only long-polling transport choice.
- [x] Restore, build with zero warnings, and pass all 44 tests.
- [x] Stop before game-engine implementation.

### Milestone 6 — Game engine (completed 2026-08-27)

- [x] Define transport-, UI-, persistence-, and concrete-game-neutral module, action, actor, state, transition, event, score, and view contracts.
- [x] Add a case-insensitive module catalog that discovers registered games and rejects duplicate or invalid descriptors during composition.
- [x] Run each game instance through one bounded `Channel<GameCommand>` with a single mutation consumer.
- [x] Validate party ownership, participant identity, terminal state, current phase deadline, system actions, and module rules on the server.
- [x] Persist accepted and rejected command results in snapshots so retries cannot repeat transitions, events, or score awards.
- [x] Schedule deterministic UTC deadline actions through the same command channel and reject early or late actions authoritatively.
- [x] Add versioned opaque module state, shared score accumulation, semantic events, explicit completion, and role-specific host/display/player views.
- [x] Add `IGameStateStore`, optimistic revision checks, an in-process adapter, and lazy actor reconstruction from stored snapshots without coupling modules to storage.
- [x] Register one process-wide runtime/catalog/store in the Web composition root while keeping SignalR outside the engine.
- [x] Document the module boundary, command flow, timer ordering, idempotency, views, snapshots, and recovery model.
- [x] Add architecture, discovery, 100-command concurrency, authorization, invalid/late/duplicate action, scoring, timer, completion, store-concurrency, view-secrecy, and recovery tests.
- [x] Restore, build with zero warnings, and pass all 61 tests.
- [x] Stop before implementing Estimate or any other concrete game rules.

### Milestone 7 — Estimate (completed 2026-08-27)

- [x] Implement a discoverable three-round Estimate module with explicit answering, results, and completed phases.
- [x] Add server-owned number limits, UTC answer deadlines, hidden submissions, closest-answer rankings, tie handling, and cumulative score awards.
- [x] Add transport-safe action decoding and keep invalid, late, repeated-command, and repeated-submission validation authoritative and idempotent.
- [x] Add generic player controller view contracts and a reusable number controller selected by controller kind rather than game name.
- [x] Add host start/advance/finish controls, player submission/waiting views, and display answering/result/score views.
- [x] Authenticate player mutations from the durable HttpOnly session cookie and authorize host/display/player role views against durable identities.
- [x] Publish game-state hints over SignalR while every role reloads a complete authoritative role snapshot.
- [x] Persist the active game pointer in PostgreSQL and persist final scores before returning the same players to the lobby for another game.
- [x] Add the PostgreSQL active-game migration plus domain, application, engine, composition, persistence-model, secrecy, deadline, scoring, and full-loop tests.
- [x] Restore, build with zero warnings, and pass all 73 tests.
- [x] Stop before Phaser presentation work.

### Milestone 8 — Phaser presentation (completed 2026-08-27)

- [x] Pin Phaser 3.90.0 through npm, serve it locally without a runtime CDN, and include it in the multi-stage container asset build.
- [x] Add one long-lived 1280×720 Phaser scene that survives pairing, lobby, game, results, and return-to-lobby state changes.
- [x] Keep Blazor authoritative for display state, accessible HTML, SignalR, and orchestration while Phaser receives only reconstructable semantic snapshots.
- [x] Generate persistent recognizable characters from each player's body, colour, eyes, mouth, and accessory traits without external image dependencies.
- [x] Animate player joins, departures, disconnect/reconnect status, server score changes, result winners, camera flashes, and confetti particles.
- [x] Use Phaser `FIT` scaling and centred letterboxing in a dedicated full-screen display layout for 720p, 1080p, and 4K output.
- [x] Respect reduced-motion preferences and retain the complete logical display as an accessible HTML fallback when canvas rendering is unavailable.
- [x] Document the presentation lifecycle, authority boundary, semantic snapshot, generated assets, recovery behavior, and local dependency pipeline.
- [x] Add snapshot-mapping, local-asset, script-order, reduced-motion, fallback-render, and display-refresh integration coverage.
- [x] Run npm clean install/audit and client build, validate JavaScript syntax, restore, build with zero warnings, and pass all 77 tests.
- [x] Stop before reusable drawing-framework work.

### Milestone 9 — Reusable drawing framework (completed 2026-08-27)

- [x] Add a bounded vector document model with fixed logical coordinates and configurable 1–12 frame support.
- [x] Treat one-frame image games as a first-class mode with the same APIs, no frame navigation, and no onion dependency.
- [x] Handle touch, stylus, and mouse through Pointer Events in JavaScript without sending per-point traffic to Blazor or SignalR.
- [x] Add compact persistent colour/size controls, pen/eraser restoration, stroke undo, and two-step current-frame clear.
- [x] Add safe frame navigation and a separate previous-frame-only onion layer that is never baked into drawing data.
- [x] Add identity-scoped local drafts, strict restoration validation, later-round cleanup, and a successful-submission clear seam.
- [x] Add the reusable Blazor `Drawing` controller selected by controller kind rather than concrete game name.
- [x] Add `IDrawingAssetStore` plus a bounded, path-safe filesystem adapter, one-day TTL/hourly cleanup, and isolated persistent Compose volume.
- [x] Document framework authority, recovery, single-frame behavior, storage, and the Milestone 10 submission boundary.
- [x] Add JavaScript model tests plus .NET configuration, local-asset, health, and asset-store boundary tests.
- [x] Run npm clean install/audit/build/client tests, validate JavaScript syntax, restore, build with zero warnings, and pass all .NET tests.
- [x] Stop before AniMates rules, submission endpoints, playback, voting, reveal, or scoring.

### Milestone 10 — AniMates (completed 2026-08-27)

- [x] Rename the complete game surface to AniMates, including module/action identifiers, routes, project/namespace names, tests, and documentation.

- [x] Implement private prompts and an explicit server-owned Drawing → Voting → Results → Completed state machine.
- [x] Require three logical 512×512 frames while copying the latest completed frame into missing later slots.
- [x] Export PNG frames in JavaScript and submit multipart assets without moving image bytes through Blazor or SignalR.
- [x] Validate durable player ownership, game instance, round scope, phase/controller, dimensions, type, per-frame size, and total payload.
- [x] Use a stable submission/command ID across refresh and make asset registration plus the semantic game command idempotent.
- [x] Persist opaque PostgreSQL asset ownership/expiry metadata with a migration and delete expired rows alongside one-day asset TTL cleanup.
- [x] Keep live drawings secret; provide anonymous playback/vote views, reject self-votes, reveal creators, and award popularity scores.
- [x] Add Phaser three-frame playback at 150 ms plus accessible HTML and reduced-motion fallback presentation.
- [x] Prove reconnect before/after submission, fallback frames, late/duplicate/invalid actions, payload limits, secrecy, scoring, and asset serving.
- [x] Run npm audit/build/client tests, validate JavaScript, restore, build with zero warnings, and pass all 104 .NET tests.

### Milestone 11 — Majority Rules (completed 2026-08-27)

- [x] Implement a discoverable three-round Answering → Voting → Results → Completed state machine.
- [x] Add bounded, normalized 200-character answers with server-owned phase and UTC deadline validation.
- [x] Keep answer content secret during writing and use persisted opaque option IDs for anonymous voting views.
- [x] Exclude a player's own answer from their vote options and reject forged self-votes authoritatively.
- [x] Rank revealed answers, identify their authors, and award 500 points per vote through shared score accumulation.
- [x] Add reusable Text and Vote phone controllers selected by controller kind rather than game name.
- [x] Preserve full round, submission, vote, deadline, and result state in reconstructable role-specific snapshots.
- [x] Cover normalization, payload validation, secrecy, anonymity, self-voting, scoring, deadlines, idempotency, late actions, recovery, and completion.
- [x] Restore, build with zero warnings, and pass all 112 .NET tests.

### Milestone 12 — Bullshit (completed 2026-08-27)

- [x] Implement a discoverable three-round Bluffing → Choosing → Results → Completed state machine.
- [x] Keep truths, exact-answer flags, and bluff-author mappings private until the server-owned reveal.
- [x] Generate opaque choices, cryptographically shuffle them once, and persist their order for refresh recovery.
- [x] Group equivalent bluffs, exclude every co-author's own choice, and reject forged self-choices server-side.
- [x] Add the reusable Choice phone controller selected by controller kind rather than game name.
- [x] Combine truth-choice, successful-bluff, grouped-author, and silent exact-truth bonuses into idempotent score awards.
- [x] Handle partial deadline progression, reveal missing actions safely, and complete all three rounds under host control.
- [x] Cover hidden views, stable shuffle, opaque IDs, duplicate bluffs, self-choice, advanced scoring, malformed/late/duplicate actions, deadlines, recovery, and completion.
- [x] Run npm audit/build/client tests and JavaScript syntax checks; restore, build with zero warnings, and pass all 121 .NET tests.

### Production-readiness review (completed 2026-08-27)

- [x] Replace the production in-memory game snapshot adapter with optimistic, versioned PostgreSQL JSONB persistence while retaining the in-memory test adapter.
- [x] Recover interrupted completed games, evict/dispose completed actors, bound per-game queues/player command history, and add retained snapshot cleanup.
- [x] Make malformed transport actions idempotent inside the actor and add structured logging for deadline, observer, presence, and cleanup failures.
- [x] Serialize party admission/start operations so concurrent joins cannot exceed capacity or cross a start-game transition.
- [x] Encapsulate concurrent drawing-registration conflicts in Infrastructure, clean losing temporary assets, and fully validate PNG structure/CRC/dimensions.
- [x] Add bounded NAT-compatible request limiting, upload/join body limits, secure production cookies, host filtering, forwarded-header constraints, and security headers.
- [x] Run the container as non-root, persist Data Protection keys, exclude secrets/generated data from the build context, and preserve loopback/private-network VPS isolation.
- [x] Consolidate Choice/Vote rendering behind one reusable option component with explicit round scopes so local selections recover without leaking into later rounds.
- [x] Remove unused template pages, refresh the product home page, and document production persistence, scaling boundaries, and Hetzner coexistence.
- [x] Enforce `latest-recommended` .NET analyzers/code style; format; pass a zero-warning strict Release build, 132 .NET tests, 9 client tests, JavaScript syntax checks, and npm/NuGet vulnerability audits.

### Responsive UI hardening (completed 2026-08-28)

- [x] Make the public, account, host, player, and display shells mobile-first with safe-area padding, virtual-keyboard resizing, bounded content, and no accidental page-level horizontal scrolling.
- [x] Provide visible keyboard focus, a skip link, reduced-motion and forced-colour support, 44×44 CSS-pixel touch targets, and 16 px mobile form controls.
- [x] Reflow host headings, rosters, results, game actions, account navigation, and dense account tables without losing content at narrow widths.
- [x] Keep number, text, choice, vote, waiting, and drawing controllers usable down to 320 px and on short landscape phones.
- [x] Bound the drawing canvas by both available width and dynamic viewport height, reflow its tool/frame controls, and keep a single-frame drawing continuously visible.
- [x] Let the reconstructable display HTML overlay scroll independently of Phaser so pairing, QR, results, drawings, and scores remain reachable on portrait or short displays.
- [x] Document the breakpoint matrix and add responsive contract tests for viewport metadata, overflow, touch sizing, safe areas, controllers, tables, and single-frame playback.
- [x] Verify exact browser-emulated 320×568, 667×375, and 1440×900 layouts with no horizontal document overflow; build with zero warnings; pass all 136 .NET tests and 9 client tests.

### Local Docker smoke test (completed 2026-08-28)

- [x] Build the production Web project graph from the secret- and test-excluded Docker context.
- [x] Start isolated PostgreSQL, apply all EF migrations through the one-shot migration service, and launch the non-root Web container.
- [x] Verify the home page plus live and PostgreSQL-ready health endpoints on loopback-only port 8081.
- [x] Confirm both containers belong to the `quizizzo` Compose project and private network, with no published PostgreSQL port or unrelated container changes.
- [x] Remove container-only GSSAPI and development HTTPS-redirection warnings and pass the focused deployment configuration tests.

### Paired-display reopen hotfix (completed 2026-08-28)

- [x] Reproduce and trace the paired `/display` prerender failure to concurrent roster/game reads sharing a request-scoped EF context.
- [x] Load reconstructable realtime display state through a fresh operation scope so prerender and circuit work never share the page request context.
- [x] Rebuild only the Web container, preserve PostgreSQL/session volumes, verify repeated `/display` requests and readiness return HTTP 200, and confirm clean logs.
- [x] Extend short-lived operation scopes to host and player realtime state plus host game mutations so SignalR hints cannot race a circuit-scoped EF context.
- [x] Verify host/player recovery and scope contracts, redeploy only the Web container, and confirm healthy readiness with no new concurrency exceptions.

### UI beautification and browser journey audit (completed 2026-08-29)

- [x] Add a repeatable Playwright audit that drives the locally installed Edge browser against the live Docker application.
- [x] Inspect the public, account, join, display, host, and player journeys for console errors, failed requests, overflow, accessibility defects, and confusing copy.
- [x] Replace the generic home page and application sidebar on `/` with a distinctive responsive landing experience and restrained parallax depth.
- [x] Respect reduced-motion, keyboard, touch-target, contrast, safe-area, and 320 px layout requirements throughout the new experience.
- [x] Replace technical, terse, or ambiguous player and host action labels with concise outcome-oriented language while preserving generic controller contracts.
- [x] Add automated presentation contracts and browser screenshots at representative phone, tablet, desktop, and shared-display sizes.
- [x] Rebuild the live Docker Web image, run the client and .NET quality gates, repeat the browser audit, and record the verified findings.

### Milestone 13 — CI/CD (completed 2026-08-30)

- [x] Stage 1: add read-only GitHub CI for client audit/build/tests, JavaScript validation, analyzer-style verification, strict Release .NET build/tests, production image build, and non-root verification.
- [x] Stage 2: publish immutable commit-SHA images to GHCR without deployment credentials.
- [x] Stage 3: prepare and manually prove the least-privilege Hetzner deployment account, production environment, backups, and preflight checks.
- [x] Stage 4: deploy through a protected GitHub environment with explicit migrations, isolated service replacement, and health verification.
- [x] Stage 5: prove and document rollback to the previously healthy immutable image without affecting `logiagraph.com`.

### Player avatar designer (completed 2026-08-30)

- [x] Replace individually extracted character PNGs with the original Kenney spritesheets and XML atlas names.
- [x] Give the production Phaser presentation separate portrait and full-body player rendering modes while retaining the presenter as a separate character.
- [x] Replace random avatar assignment with a mobile-first join designer for presentation, skin, hair, shirt, trousers, and shoes.
- [x] Persist and server-validate semantic atlas selections so refresh/reconnect reconstructs the same avatar.
- [x] Keep player portraits along the bottom during play and use full-body avatars for podiums, winner reveals, and stage animations.
- [x] Add authenticated, bounded, rate-limited player reactions such as kiss and angry, rendered only on that player's portrait.
- [x] Add domain, persistence, join-flow, renderer, reaction, recovery, responsive, and browser coverage.

### AniMates six-player showdown gallery (completed 2026-08-31)

- [x] Cap AniMates at six players through its server-owned game descriptor while preserving the wider platform party limit for other games.
- [x] Replace sequential Same Prompt Showdown playback with a simultaneous, adaptive A–F gallery that animates every entry together.
- [x] Scale the creator reveal to the same bounded three-column grid and keep one- to six-entry layouts inside the 16:9 stage.
- [x] Replace loose overlapping portraits with compact bottom player cards and fit long player names within their card.
- [x] Hide empty activity badges and fit the portrait, presence, name, and score inside each square player card.
- [x] Open a server-owned 90-second showdown vote as soon as all animations arrive, reveal early when everyone votes, and discard missing votes at timeout.
- [x] Let phone players open each A–F option in a looping review modal before explicitly locking in or closing it.
- [x] Keep an owner-confirmed Close party escape hatch visible during active games and abandon the active party atomically so a replacement can be created.
- [x] Bind vote action kinds and selection properties immutably so the review modal submits `animates.showdown-vote` with `submissionPlayerId` rather than a stale answer action.

### Main-display composition hotfix (completed 2026-08-31)

- [x] Promote the Phaser scene to the sole visual presentation after successful startup while retaining the complete HTML view as an accessible and no-canvas fallback.
- [x] Keep persistent headings, deadlines, animations, answer cards, avatars, and scores in bounded, non-overlapping stage regions.
- [x] Replace the temporary geometric briefing presenter with an animated full-body character assembled by the production Kenney atlas rig.
- [x] Hide player avatars during presenter briefings and keep the drawing tutorial within its dedicated lower-stage region.
- [x] Validate JavaScript syntax, rebuild client assets, pass all 9 client tests, build with zero warnings, and pass all 174 .NET tests.

### Mobile avatar-picker follow-up (completed 2026-08-31)

- [x] Split the join-page character controls into accessible Head, Body, and Legs tabs with click and arrow-key navigation.
- [x] Compact the mobile preview, room introduction, spacing, and card chrome without reducing touch targets or hiding submitted fields.
- [x] Reinforce the successful-Phaser display handoff directly in the presentation bridge so the fallback overlay cannot remain visually active after startup.

### Host lobby closure (completed 2026-08-31)

- [x] Give the authenticated party owner a two-step destructive confirmation for closing an open lobby.
- [x] Serialize closure against player admission and game start, retire the room code, reject non-owners and active games, and return the host to the dashboard.
- [x] Broadcast a reconstructable lobby-closed refresh hint after persistence succeeds.

### Host lobby visual refresh (completed 2026-08-31)

- [x] Replace the legacy host lobby card with a branded control-room layout and prominent room-code hero.
- [x] Separate display connection, player roster, game selection, and destructive closure into clear responsive regions.
- [x] Give each game a distinctive launch tile while preserving disabled player-count rules, touch targets, reduced motion, and forced-colour support.
- [x] Replace the stock desktop sidebar with a compact branded top navigation and carry the same visual system into active host game controls and score panels.

### AniMates turn-based guessing expansion (completed 2026-08-31)

- [x] Give every player a private absurd prompt and let all players create their three-frame animations simultaneously.
- [x] Preserve submitted animations and run sequential Guessing → Choosing → Results cycles for each animator.
- [x] Mix the real prompt with persisted, shuffled player guesses and present matching A/B/C lettered answer cards on display and phones.
- [x] Hide each player's own guess and reject forged self-choices authoritatively.
- [x] Award 100 points per fake-answer pick, 50 points to a correct chooser, and 100 points to the animator for each correct pick.
- [x] Show unfinished artists thinking, submitted artists idle, and the server-owned countdown in the display's top-right corner.
- [x] Present cumulative score-proportional podiums between reveals with winner cheering, lowest-place crying, and a static reduced-motion fallback.
- [x] Preserve frame fallback, deadlines, idempotent asset submission, reconstructable options, recovery, and host-controlled progression.

### AniMates Same Prompt Showdown (completed 2026-08-31)

- [x] Add presenter-led, host-advanced speech-bubble briefings before both AniMates rounds with a future voice-ready semantic boundary.
- [x] Add Round 2 with one shared prompt, five simultaneous frames, and isolated round-scoped draft/upload validation.
- [x] Play every animation anonymously for three complete loops before the host opens voting.
- [x] Put self-excluding A/B/C animation previews on phones and reject forged self-votes server-side.
- [x] Award 100 points per received vote plus a 200-point winner bonus, including explicit tied-winner handling.
- [x] Reveal every animation creator together, enlarge the winner, run in the winning avatar, and fire confetti.
- [x] Preserve accessible HTML, reduced-motion static results, deadlines, recovery, idempotency, and server authority.
- [x] Give both presenter briefings a distinct animated stage and accessible frame/tool tutorial with a static reduced-motion fallback.
- [x] Apply a cohesive locally served typography, phase palette, animation-card, title-stinger, and celebration system across both rounds.

### Unified host display controls (completed 2026-09-01)

- [x] Keep the authenticated party owner on the main display after presenting instead of requiring routine navigation back to the host lobby.
- [x] Add owner-only display controls for choosing and launching games, advancing active phases, and closing the party with confirmation.
- [x] Preserve an uncluttered TV-only display for unauthenticated paired screens and retain server-authoritative host authorization and game mutations.
- [x] Refresh both host and display snapshots after realtime hints so the combined screen remains reconstructable after refresh or reconnect.

### Lobby display polish and player moderation (completed 2026-09-01)

- [x] Let the authenticated owner remove a player directly from the top-right of that player's lobby card.
- [x] Authorize and serialize removal on the server, restrict it to the lobby, retire the player's durable membership, and broadcast a reconstructable refresh hint.
- [x] Refit atlas portraits inside their cards and retain names, scores, and presence without overlap.
- [x] Recompose the join prompt, room code, QR treatment, and join URL with a playful party-game presentation.
- [x] Fill non-16:9 browser windows without exposed stage bars while retaining responsive high-density rendering.

### Direct host-display session flow (completed 2026-09-01)

- [x] Remove the host dashboard, recent-party history, separate host control page, and manual display-pairing page from the product flow.
- [x] Make the authenticated `/host` entry point resume the host's sole active party or create one fresh party and open it directly on `/display`.
- [x] Keep game selection, progression, player moderation, and party closure on the owner-authorized display controls.
- [x] Return to the public home page after closure, retire the old room code, and create a new display session and room code on the next launch.

### Player-card interaction polish (completed 2026-09-02)

- [x] Anchor the owner-only remove action inside its Phaser player card and keep the callback live after display state updates.
- [x] Keep score text within the card boundary and move reaction bursts clear of the HTML join card at a higher scene depth.
- [x] Add a server-validated poop reaction to the phone controller and shared display.
- [x] Replace persistent raw SignalR errors with concise controller notices that dismiss automatically.

### Single-viewport phone controllers (completed 2026-09-02)

- [x] Put join, avatar design, and player game routes in a dedicated safe-area-aware `100dvh` shell with document scrolling disabled.
- [x] Split the dense avatar designer into six keyboard-accessible sections while preserving every persisted character option.
- [x] Compact number, text, choice, vote, waiting, reaction, and drawing controls to fit portrait and short-landscape phones without reducing touch targets below 44 px.
- [x] Keep transient errors and drawing pickers as bounded overlays so they do not reflow the active controller.
- [x] Add responsive contracts and full-party browser assertions that reject vertical document scrolling on join and play routes.

### Removed-player rejoin hotfix (completed 2026-09-02)

- [x] Preserve normal cookie-based reconnect for active and temporarily disconnected party members.
- [x] Treat a kicked or left membership as retired when the same phone submits the join form again, issuing a fresh player identity and replacing its cookie.
- [x] Keep the retired record excluded from the roster and member count while covering the fresh-membership path with an application regression test.

### Avatar-designer contrast hotfix (completed 2026-09-02)

- [x] Give the phone join and character-designer journey a dedicated dark party-game surface.
- [x] Raise field-label, tab, hint, placeholder, input-border, and navigation-link contrast without changing the single-viewport layout.

### Phone answer-selection hotfix (completed 2026-09-02)

- [x] Accept the compact opaque GUID format emitted by reusable phone choice and vote controllers.
- [x] Apply the transport fix consistently to AniMates choices/showdown votes, Bullshit choices, and Majority Rules votes.
- [x] Retain rejection of missing, malformed, and empty identifiers and cover both dashed and compact transport formats.

### Phone gesture hardening (completed 2026-09-02)

- [x] Disable pinch and double-tap zoom inside the dedicated phone controller shell without affecting public, host, or display pages.
- [x] Prevent long-press callouts and accidental selection/highlighting of controller chrome and labels.
- [x] Preserve caret placement and text editing inside player-name, text-answer, and numeric entry fields.
- [x] Add a Safari gesture-event fallback and responsive contract coverage for the route-scoped behavior.

### Full-body avatar neck polish (completed 2026-09-02)

- [x] Narrow the Kenney neck layer so the head and shirt masks form a clean collar instead of pointed skin flaps.
- [x] Keep the live display, phone avatar preview, and presenter lab rigs visually consistent.
- [x] Ground full-body shoes on their stage shadow and results podium, lower the reveal row toward the bottom of the display, and separate player-name and score baselines inside the card.
- [x] Validate all three JavaScript entry points and cover the production/designer neck proportion with a presentation contract test.

### Round-ranking interstitial and shared character rig (completed 2026-09-02)

- [x] Mark real round-result boundaries in the server-owned display payload and suppress podium motion during ordinary answer and creator reveals.
- [x] Give the round interstitial the full stage: presenter introduction, current standings, exact podium-top placement, and sequential player entrances.
- [x] Count each player's authoritative score up from the prior snapshot one player at a time, revealing first place last for suspense.
- [x] Run winner laugh, last-place cry, and remaining-player idle animations without moving avatar shoe baselines off their podiums.
- [x] Consolidate Kenney atlas resolution, full-body/portrait assembly, and idle/laugh/cry/fart animation behavior into one character rig shared by the designer and display.
- [x] Keep the designer avatar idling and trigger only an occasional one-second fart after a long delay while respecting reduced-motion preferences.
- [x] Validate JavaScript syntax, pass all 9 client tests, pass the focused presentation/game contracts, and pass all 191 .NET tests.

### AniMates briefing and drawing-timer configuration (completed 2026-09-02)

- [x] Make both AniMates briefing screens feature a substantially larger presenter with a looping talking mouth, subtle body motion, and animated speech panel.
- [x] Move the shared talking rig's arms down and inward from the shoulders so its hands rest beside the body rather than forming a wide T-pose.
- [x] Add a host-control setting for drawing seconds per frame, default it to 45 seconds, and show the calculated three- and five-frame round durations before launch.
- [x] Carry bounded game configuration through the generic start pipeline and persist the selected timing inside the reconstructable AniMates state.
- [x] Calculate authoritative drawing deadlines as frame count multiplied by seconds per frame while preserving the fixed-duration test seam and backward-compatible state recovery.
- [x] Reject values outside 10–180 seconds server-side and cover default, custom, round-one, round-two, forwarding, UI, and presentation behavior.
- [x] Pass analyzer formatting, JavaScript syntax, all 9 client tests, a zero-warning strict Release build, and all 195 .NET tests.

### Cloudflare-ready display soundtrack (completed 2026-09-02)

- [x] Import the supplied lobby, gameplay, and countdown MP3 files under a content-hashed `/media/audio` path.
- [x] Loop Quiz Show Groove in the paired lobby and Quiz Show Sparkle throughout active game phases on the shared display only.
- [x] Replace gameplay music with Countdown to Zero for the final 20 seconds of each authoritative AniMates drawing deadline, including mid-countdown refresh recovery.
- [x] Add a persistent top-left sound control to every display state with remembered mute state and an explicit autoplay-permission prompt.
- [x] Serve audio with `audio/mpeg`, `Content-Length`, and one-year immutable browser and Cloudflare cache directives.
- [x] Cover soundtrack selection, countdown timing, mute restoration, static delivery, cache headers, script ordering, and deadline snapshot mapping.
- [x] Pass analyzer formatting, JavaScript syntax, all 12 client tests, a zero-warning strict Release build, and all 199 .NET tests.

### Canvas-only display and character-motion polish (completed 2026-09-02)

- [x] Remove the duplicate audience-facing HTML lobby/game/drawing/score fallback from the shared display.
- [x] Carry room code, join URL/QR data, game copy, deadlines, and revealed entries in the reconstructable Phaser snapshot.
- [x] Render pairing, lobby, QR, game headings, countdowns, answers, drawings, and rankings within the long-lived Phaser scene.
- [x] Await Phaser scene creation and show only a concise unsupported-browser state when startup or resize reconstruction fails.
- [x] Replace the round winner's particle burst with the shared character-rig celebration and retain static reduced-motion standings.
- [x] Make idle motion a slow, subtle breathing loop with the hands relaxed at waist height.
- [x] Clear previous phase chrome before the dedicated standings interstitial so prior reveal copy cannot remain visible.
- [x] Validate JavaScript syntax, all 12 client tests, a zero-warning strict Release build, and all 200 .NET tests.

### Canvas-first phone controller refresh (completed 2026-09-02)

- [x] Replace the pale active-controller surface with the same dark pink, purple, and cyan visual language as the join experience and shared display.
- [x] Consolidate room, phase heading, live connection, total score, and reaction access into one compact glass header.
- [x] Move the five player reactions into a bounded overlay launched from a single 44 px control.
- [x] Let the logical drawing canvas consume the full usable phone width instead of inheriting the old 42rem controller cap.
- [x] Replace the permanent five-tool and frame rows with one compact dock for pen, eraser, undo, and frame navigation.
- [x] Move colour, brush size, onion skin, and confirmed frame clearing into a bounded pen-settings overlay.
- [x] Keep portrait and short-landscape controllers within one non-scrolling visual viewport while preserving 44 px touch targets and local pointer input.
- [x] Update the responsive architecture contract, pass all 12 client tests, a zero-warning Release Web build, and all 200 .NET tests.

### AniMates answer-stage composition (completed 2026-09-02)

- [x] Move the current animation into a larger dedicated left-stage paper card without colliding with the answer choices.
- [x] Add visible translucent tape, tape shadows, paper depth, and restrained entrance motion to make the animation feel mounted to the stage.
- [x] Place every answer inside a separate rounded, high-contrast game board on the right.
- [x] Stack two- and three-answer rounds vertically and adapt larger sets to two columns while keeping labels and copy bounded.
- [x] Move and scale the player rail below the animation and answer regions for choosing and reveal phases.
- [x] Document the stage regions, validate JavaScript syntax, pass all 12 client tests, and pass all 201 .NET tests.

### AniMates showdown collision polish (completed 2026-09-02)

- [x] Move the shared-display sound control into a compact site-styled top-left pill outside the game heading region.
- [x] Give Round 2 playback and voting a dedicated bounded header panel so the prompt, instruction, vote count, and points never share a baseline.
- [x] Integrate creator, vote, points, and rank metadata into each result animation card instead of covering the drawings with separate result panels.
- [x] Reveal result cards from a contracted scale directly to their final size without an overshoot that can collide with neighbouring animations.
- [x] Validate JavaScript syntax, pass all 12 client tests, pass the focused display contract tests, and pass all 201 .NET tests.

### AniMates prompt catalogue (completed 2026-09-02)

- [x] Embed and validate the supplied 1,000-entry drawing-prompt catalogue as an AniMates-owned game asset.
- [x] Select distinct Round 1 prompt/distractor pairs and a separate shared Round 2 prompt when a new game starts.
- [x] Persist prompt assignments in the server-authoritative module snapshot so refresh and process recovery never reroll an active game.
- [x] Add each supplied distractor to its Round 1 choice set alongside the true prompt and human guesses.
- [x] Award no player or animator points when a built-in distractor is selected and identify it only during the result reveal.
- [x] Use catalogue prompts in Round 2 without exposing or using their paired distractors.
- [x] Preserve recovery of older AniMates snapshots through legacy prompt/stat fallbacks and emit version-4 snapshots for new games.
- [x] Cover the embedded catalogue, private assignments, neutral distractor scoring, dynamic showdown prompts, and asset-backed submission recovery; pass all 203 .NET tests with no build warnings.

### Slop Machine (completed 2026-09-02)

- [x] Add a discoverable 3–12 player Slop Machine module using the existing serialized game actor, durable snapshots, generic phone controllers, SignalR hints, host controls, and return-to-lobby flow.
- [x] Implement the complete Game Intro → two-heat Fresh Slop → Algorithm Roulette → Thumbnail Telephone → Comments Section → Beat the Machine → final review → joint-winner celebration state machine.
- [x] Keep server authority over UTC deadlines, assignments, one-reel re-spins, structured blank formats, derangements, decoys, anonymous options, self-vote restrictions, scoring, tied bonuses, machine titles, and machine-identification awards.
- [x] Import and validate all 996 generated WebP thumbnails plus their embedded manifest and two stored machine titles, prevent within-session repetition, and serve immutable media with Cloudflare-ready cache headers.
- [x] Add reconstructable thumbnail/title media contracts to reusable Choice, Vote, player, display, and Phaser snapshots without coupling the shared controller layer to a game name.
- [x] Give the shared display a dedicated unstable content-factory treatment, hero/feed and gallery compositions, view-labelled sequential score counts, movement and biggest-gainer highlights, full-body podium characters, and a ridiculous final channel rank.
- [x] Add FAKE, UNSUBSCRIBE, and REPORT THIS SLOP to the existing authenticated per-player reaction limiter and transient phone reaction surface.
- [x] Keep the phone controller within one visual viewport with bounded thumbnails, large touch targets, locked submissions, authoritative countdowns, and safely rendered normalized title/comment data.
- [x] Document scoring, recovery, media schema, static caching, presentation hooks, and the validated repeatable thumbnail import command.
- [x] Exercise complete 3-, 4-, and 12-player games plus illegal actions, timeouts, scoring ties, Telephone behavior, final human/machine outcomes, recovery, assets, reactions, and responsive contracts; pass JavaScript syntax, all 12 client tests, a zero-warning strict Release build, and all 224 .NET tests.

### Slop Machine display soundtrack (completed 2026-09-02)

- [x] Import the 11 supplied MP3s byte-for-byte under the game-owned `/media/audio/games/slop-machine` path with their stable production filenames.
- [x] Extend the one shared-display audio coordinator with state-driven lobby, writing, spinner, voting, Telephone, Comments, scoreboard, final, countdown, and victory playback.
- [x] Crossfade into the countdown from authoritative writing deadlines, recover at the correct sub-20-second offset, and stop immediately after early completion without affecting matching or voting timers.
- [x] Preserve phase continuity without restarts, prevent stale snapshots and concurrent long-form playback, preload likely cues, and degrade silently after one useful missing-track warning.
- [x] Persist mute/autoplay state, deduplicate victory cues across reconnects by game instance, and allow later rematches to play their own celebration.
- [x] Treat a machine/human tie as a human final outcome so the machine cue requires an outright machine-only first place.
- [x] Document the complete state map, verify all source/target files and published assets, validate JavaScript, pass all 21 client tests, pass a zero-warning strict Release build, and pass all 236 .NET tests.

### Slop Machine display and log-containment hotfix (completed 2026-09-02)

- [x] Prevent machine-owned and repeated presentation entries from colliding on an empty player ID after Slop Machine submissions.
- [x] Restrict activity and non-podium result mapping to real party players while retaining every game entry for the display content boards.
- [x] Enforce error-only logging inside the production Web image and its Compose runtime configuration.
- [x] Cap every Quizizzo container at three 10 MB Docker JSON log files so repeated faults cannot consume the host drive.
- [x] Add exact mapper and deployment regressions, validate Compose, pass analyzer formatting, pass a zero-warning strict Release build, and pass all 238 .NET tests.

### Automatic game progression (completed 2026-09-02)

- [x] Put server-owned UTC deadlines on every Estimate, Majority Rules, Bullshit, AniMates, and Slop Machine phase that previously required routine host advancement.
- [x] Progress briefings, reveals, results, score reviews, celebrations, and game completion through the existing serialized system-deadline command path.
- [x] Preserve early progression when every eligible player submits and retain host controls only as clearly labelled “Continue now” skip controls.
- [x] Show the authoritative countdown on AniMates briefing displays and inside the host-controls panel as well as on existing display and phone phases.
- [x] Cover automatic results, briefing, intro, reveal, and next-round transitions; validate JavaScript, pass all 21 client tests, pass a zero-warning strict Release build, and pass all 239 .NET tests.

### AniMates final celebration (completed 2026-09-02)

- [x] Insert a dedicated, reconstructable final-results phase after the Round 2 creator reveal instead of returning directly to the lobby.
- [x] Present cumulative scores on the shared full-body character podium with winner celebration, loser crying, other-player idle animations, and tied-winner confetti.
- [x] Persist drawing time and successful human-bluff picks, then reveal animated Fastest Animator, Most Loved Animation, and Best Bluffer award cards above the final podium.
- [x] Give the finale its own presenter introduction, headings, player waiting state, host skip action, and 15-second server-owned deadline before completion.
- [x] Preserve reduced-motion behavior and automatic return to the lobby; validate JavaScript, pass all 21 client tests, pass a zero-warning strict Release build, and pass all 240 .NET tests.

### Display join-link interaction (completed 2026-09-02)

- [x] Turn the Phaser lobby join URL into a clearly marked interactive link with hover, pressed, and hand-cursor feedback.
- [x] Open the exact active-room join URL in a separate, opener-isolated browser tab/window without disturbing the host display.
- [x] Validate JavaScript and cover the interaction with the canvas-presentation contract tests.

### AniMates playback pacing (completed 2026-09-02)

- [x] Slow submitted drawing animations from 150 ms to 300 ms per frame across shared-display playback, showdown galleries, reveals, and phone previews.
- [x] Keep the playback rate server-described and align defensive client defaults without slowing character rigs or interface transitions.
- [x] Cover the canonical playback duration in the AniMates game-module tests and pass the client and .NET verification gates.

### Party-long scores and game wins (completed 2026-09-02)

- [x] Preserve the existing cumulative player score across every game played during the active lobby and label it clearly as the party score.
- [x] Persist each positive-points game victory by player, game key, and game instance so retries cannot count the same win twice.
- [x] Determine each game's winner from points earned in that game rather than inherited party score, and award a win to every tied leader.
- [x] Show total wins on lobby player cards and phone controllers, with an overall-score and per-game win breakdown in host controls.
- [x] Add the PostgreSQL game-win migration plus domain, application, persistence-model, snapshot, and presentation contract coverage.

### Slop Machine phone-controller and thumbnail hotfix (completed 2026-09-02)

- [x] Prove Fresh Slop title-writing produces the reusable text controller and title submission action.
- [x] Key every stateful phone controller to its game instance, phase, action, and visible task so an AniMates review card cannot survive into Slop Machine writing.
- [x] Preserve controller-local input during ordinary refreshes while replacing it at real game/task boundaries.
- [x] Make animation review data-driven so ordinary Slop Machine and Majority Rules votes render image/text choices instead of fake animation controls.
- [x] Prove Fresh Slop, Roulette, Telephone, Comments, and final voting expose only static image/text choices with no animation-frame payload.
- [x] Preserve generated thumbnail aspect ratios on both phone controllers and the Phaser display instead of stretching or aggressively cropping them.

### Production database-pool containment hotfix (completed 2026-09-02)

- [x] Bound the Web and migration Npgsql pools to 32 connections so the application cannot consume every PostgreSQL client slot.
- [x] Prune idle connections after at most 60 seconds while retaining normal request and SignalR recovery capacity.
- [x] Keep database capacity available for the mandatory pre-deployment backup, explicit migration, and readiness probes.

### Slop Machine comments-assignment hotfix (completed 2026-09-02)

- [x] Select each player's highest-ranked returning uploads from the complete eligible pool so a creator never receives their own upload while another player's upload exists.
- [x] Persist the exact source submission on comment assignments so duplicate thumbnail/title combinations retain unambiguous ownership across refresh and recovery.
- [x] Stress the assignment rule across 100 independently seeded four-player games.

### Slop Machine tightened five-act format (completed 2026-09-03)

- [x] Remove Curveballs and two-player support completely, enforce a 3–12 player range, and preserve one server-authoritative five-act game flow.
- [x] Make Roulette expose only a thumbnail plus a one- or two-blank format, construct the submitted title from bounded blank values on the server, and keep the optional re-spin to those two reels.
- [x] Combine each vote, reveal, and result into one reconstructable scene with automatic early progression, revised act timings, exact narrative interstitials, and full scoreboards only after Fresh Slop, Telephone, and the final.
- [x] Keep every 3–6 player eligible in one heat, split 7–12 players into balanced heats of at most four entries, and normalize only heat-winner comparisons by votes received per voting opportunity while retaining raw scores.
- [x] Award Telephone matching points to both writer and matcher, allow the matcher to vote for the pairing they matched, and award its subjective vote and winner points only to the title writer.
- [x] Assign Comments from another creator's highly viewed current-game uploads, retain the source upload and creative comment type, render a pinned YouTube-style result feed, and keep non-winning comments visible in history.
- [x] Prevent stale asynchronous phone-state loads from restoring a previous controller after a phase/game change, and keep all Slop Machine voting free of animation-review controls.
- [x] Animate in-scene view awards and winner emphasis without overlapping cards, validate JavaScript, pass all 21 client tests, pass formatting for every modified C# file, and pass all 261 .NET tests.
- [x] Delete the unreachable legacy reveal phases and timer, their obsolete display/audio mappings, the unused vote-reset helper, and write-only submission/re-spin model fields.

### Professional game picker and party playlists (completed 2026-09-03)

- [x] Replace the standings-heavy host picker with a wide, responsive game catalogue featuring distinctive game treatments, descriptions, play styles, player eligibility, and compact per-game settings.
- [x] Add quick-play and an editable ordered playlist with add, remove, and reorder controls plus a compact active-game “up next” view.
- [x] Persist the bounded playlist and each entry's configuration as reconstructable PostgreSQL JSONB party state behind a migration.
- [x] Authorize and serialize every playlist mutation through the existing party mutation coordinator and validate game availability and player limits server-side.
- [x] Finalize scores and wins, dequeue, and start the next game under one party mutation lease so players move between games without an editable lobby stop.
- [x] Preserve accumulated player identities and scores across automatic handoffs, retain queued games after immediate play, and clear the playlist when the party closes.
- [x] Document the lifecycle, format all modified C# files, pass all 21 client tests, pass a zero-warning strict Release build, and pass all 267 .NET tests.

### Pile-Up Panic engine deliveries (Stages 1–4, 2026-09-03)

- [x] Add an isolated, unregistered game project with a server-owned 9×17-visible arena and three hidden spawn rows.
- [x] Define twelve original mixed-size scrap clusters, a separately shuffled accessible material palette, and a reproducible recent-excluding generator.
- [x] Implement movement, generic clockwise rotation correction, collision, instant/soft drop, locking, circuit completion/collapse, scoring, charge, and overload rules.
- [x] Implement bounded junk, queue scramble, single-use shield, validated targeting, sequenced/rate-limited authenticated inputs, disconnect grace/forfeit, timeout ranking, fixed-step simulation entry, and complete semantic snapshots.
- [x] Add IP design notes plus architecture, configuration, fairness, scoring, scheduler-boundary, and remaining-stage documentation.
- [x] Format the new C# slice, pass 21 focused rules tests, pass a zero-warning strict Release build, and pass all 288 .NET tests.
- [x] Add an opt-in bounded recurring actor scheduler whose deterministic system commands use the existing serialized command, persistence, recovery, and observer path.
- [x] Round-trip the complete arena/match simulation state, retain monotonic input rejection across actor recovery, activate server lock delay, and separate incoming-junk warning from later junk application.
- [x] Add the unregistered `IGameModule` adapter with role-secret views, automatic best-of-three/first-to-two progression, 4/2/1/0 local placement points, and one terminal conversion to cumulative party views.
- [x] Format the engine slice, pass a zero-warning strict Release build, and pass all 299 .NET tests including scheduler restart and full automatic match coverage.
- [x] Add reconstructable introduction, controller-ready, arena-reveal, countdown, round-result, standings, final-winner, and celebration phases with automatic deadlines and early all-ready progression.
- [x] Add a reusable semantic arcade-controller contract and compact Pointer Event phone renderer with held-input cadence, monotonic sequencing, targeting, charge/ability state, and a low-latency SignalR submission path.
- [x] Keep arena grids and upcoming clusters out of phone views while exposing the complete bounded arena snapshot only to the display renderer boundary.
- [x] Format the lifecycle/controller slice, pass all 21 client tests, pass a zero-warning strict Release build, and pass all 303 .NET tests.
- [x] Forward opaque display-only module state through the shared Phaser bridge while retaining the existing role-specific secrecy boundary.
- [x] Add responsive two-, three-, and four-player Phaser scrapyards with server-described settled/active scrap, queues, charge, abilities, shields, junk, presence, overloads, and authoritative clocks.
- [x] Add Pile-specific introduction, ready, round result, match standings, final survivor, and reduced-motion-aware winner celebration scenes using the shared production character rig.
- [x] Add presentation mapper and static renderer contracts, validate JavaScript, pass all 21 client tests, pass a zero-warning strict Release build, and pass all 305 .NET tests.
- [x] Import Falling Blocks Fever as immutable game-owned media, keep one uninterrupted background loop across all live Pile-Up phases, pass all 22 client tests, pass a zero-warning strict Release build, and pass all 306 .NET tests.
- [x] Wire first/last durable-player presence through the serialized actor, forfeit disconnected piles after grace, and keep overloaded players in a spectator waiting controller.
- [x] Add reduced-motion-safe active-piece reconciliation, physical-keyboard controls, and retain the supplied uninterrupted Falling Blocks Fever track without inventing unsupplied one-shot audio.
- [x] Register Pile-Up Panic for production quick play and playlists only after exact 2/3/4-player Edge journeys passed at 320×568 and 667×375 with held input plus controller/display refresh recovery.
- [x] Prevent high-frequency simulation hints from repeatedly re-pairing displays, verify clean error-only container logs, pass all 22 client tests, pass a zero-warning strict Release build, and pass all 309 .NET tests.

### Pile-Up Panic low-latency phone arena (completed 2026-09-04)

- [x] Render each player's own reconstructable 9×17 scrapyard and upcoming queue on their phone while retaining every arena on the shared display.
- [x] Move pointer and keyboard arcade input out of Blazor event callbacks and send it directly over the existing authenticated SignalR connection.
- [x] Bind the fast hub path to the durable player connection identity and authorize it only against the current server-provided Arcade controller and action kind.
- [x] Predict active-piece movement, rotation correction, soft drop, and hard drop locally without moving locks, circuits, junk, abilities, scoring, time, or winners out of the server.
- [x] Reconcile local pending inputs through the authoritative last-accepted sequence and reconstruct cleanly after refresh, rejection, gravity, lock, junk, or reconnect.
- [x] Coalesce bursty player/display refresh hints and shorten the shared-display correction tween from 180 ms to 60 ms.
- [x] Add behavioral prediction, role-secrecy, responsive-layout, controller-transport, and display-presentation coverage.

### Pile-Up Panic shape and gravity tuning (completed 2026-09-04)

- [x] Remove the awkward five-cell `flag-post` and `split-anvil` clusters from new generation while retaining their definitions for active-snapshot recovery.
- [x] Slow initial gravity from 850 ms to 1,100 ms per row and remove elapsed-time acceleration.
- [x] Advance one shared match speed by 100 ms for every circuit completed by any player, applying it to all arenas together down to a 200 ms floor.
- [x] Cover the playable subset, deterministic generation, legacy recent-history recovery, shared gravity, and the bounded speed curve.
- [x] Pass all 25 client tests, the analyzer style gate, a zero-warning strict Release build, and all 311 .NET tests.

### VoiceChoon MIDI pipeline foundation (completed 2026-09-04)

- [x] Add an isolated, unregistered 3–8 player VoiceChoon project using the supplied original `quizizzo_coop_showdown.mid` as its embedded default song.
- [x] Parse arbitrary readable Standard MIDI streams with tempo-aware note timing, track names, markers, percussion channels, and deterministic fallback roles.
- [x] Assign the eight preferred logical parts across three to eight players while load-balancing unknown or duplicate tracks instead of discarding them.
- [x] Generate deterministic four-lane charts with semantic drum mapping, ordered melodic pitch bands, 500 ms holds, two-pad chord limits, and bounded input density.
- [x] Preserve every original target MIDI note independently from its gameplay lane so recorded samples can reproduce the composition through pitch shifting.
- [x] Define low/high melodic, sustained, percussion, and fallback recording prompts including the noises expected for each instrument role.
- [x] Add nearest-root selection, plus/minus 18-semitone octave folding, Web Audio playback-rate plans, and sustained 30–70 percent loop metadata.
- [x] Record the complete mechanics, timing, scoring, recording, privacy, recovery, display, future-MIDI, and staged-delivery decisions in `docs/architecture/voicechoon.md`.
- [x] Cover the supplied two-minute song, an unrelated synthetic MIDI, all party sizes, chart constraints, drum semantics, sound prompts, and pitch plans with focused tests.
- [x] Keep VoiceChoon out of the production catalogue until its server runtime, secure recording flow, phone/Web Audio controller, and shared-display presentation are complete.

### VoiceChoon complete product runtime (completed 2026-09-04)

- [x] Add reconstructable Briefing → Recording → Controller Ready → Countdown → Playing → Results → Completed phases with server-owned UTC deadlines.
- [x] Persist immutable per-player charts, accepted judgements, monotonic input sequences, cooperative score, combo, energy, and terminal score awards.
- [x] Add a reusable Recording controller contract with server-issued prompts, microphone capture, silence trimming, normalization, fades, replay, replace, and explicit lock-in.
- [x] Store bounded browser audio behind opaque player-owned metadata, private delivery, idempotent registration, one-day retention, and isolated persistent storage.
- [x] Add a reusable Rhythm controller with a four-lane canvas, Pointer Events multi-touch, keyboard controls, authoritative UTC positioning, direct authenticated SignalR input, and reconnect sequencing.
- [x] Retain original MIDI pitch in every chart note and play accepted samples through server-described playback-rate and sustained-loop plans.
- [x] Add a dedicated Phaser band stage with full-body avatars, assigned instruments, marker-driven sections, progress, score, combo, energy, recording readiness, and results.
- [x] Register VoiceChoon for production only after the runtime, secure recording, phone audio, and shared-display surfaces were complete.

### VoiceChoon difficulty and solo test mode (completed 2026-09-04)

- [x] Add server-owned Easy, Medium, and Hard profiles with progressively bounded note density, same-lane spacing, simultaneous pads, and judgement windows.
- [x] Preserve MIDI tempo, sections, target pitch, duration, recorded timbre, and playback-rate plans across every difficulty.
- [x] Persist difficulty in quick-play, playlist, game-state, and phone-controller snapshots with Medium as the default.
- [x] Add an explicit temporary one-player autoplay test mode while retaining the normal three-to-eight-player rule when it is disabled.
- [x] Assign all eight MIDI tracks and all 18 recording prompts to the solo tester, extend recording time, and hide manual pads during playback.
- [x] Award every generated note a server-owned Perfect judgement and schedule the private samples from the authoritative song clock through an audio context unlocked during recording.

### VoiceChoon rhythm readability and soundtrack polish (completed 2026-09-04)

- [x] Keep the four scrolling lanes aligned with every audible solo-autoplay MIDI note and show the selected mouth-sound/source-track label for debugging.
- [x] Collapse qualifying rapid same-lane tap runs into one sustained hold target on Easy and Medium while retaining the original tap-heavy Hard chart.
- [x] Keep burst compression deterministic, profile-owned, and server-authoritative for scoring and recovery.
- [x] Silence the generic shared-display game soundtrack throughout VoiceChoon and route every instrument sample through the paired display while keeping phones silent.
- [x] Pass all 30 client tests, 33 focused VoiceChoon tests, and a zero-warning strict Release Web build; deploy locally and confirm a healthy service with no display background audio.

### VoiceChoon song catalogue (completed 2026-09-04)

- [x] Embed `quizizzo_wubquake.mid` as a second selectable song while retaining Co-op Showdown as the default.
- [x] Persist the selected song key through host configuration, playlists, authoritative state, refresh, and recovery; reject unknown keys server-side.
- [x] Configure song-specific briefing and mouth-noise guidance in the catalog and show the selected guidance in host controls and player views.
- [x] Document the repeatable MIDI import, catalog, guidance, validation, and deployment workflow in `docs/architecture/voicechoon-songs.md`.
- [x] Pass 35 focused VoiceChoon tests, 30 client tests, a zero-warning strict Release Web build, and live host-selector verification.
- [x] Embed `gs.mid` as a third selectable song, analyze its five-track MIDI as melody/chords/bass/light percussion, and request only those instrument prompt families.
- [x] Pass 36 focused VoiceChoon tests, a zero-warning strict Release Web build, deploy the song, and verify Greensleeves appears in the live host selector.

### Pile-Up Panic speed and survivor celebration (completed 2026-09-04)

- [x] Slow the shared gravity increase after circuits while preserving the bounded minimum fall interval.
- [x] Remove round timeout ranking so an active round continues until only one operational player remains.
- [x] Add a large winner/crying-player interstitial with a flying `PLAYER WINS` banner during the server-owned winner celebration phase.

## Verification requirements

Every milestone ends with restore/build/tests. Tests ultimately cover room codes, transitions, scoring, invalid/late/duplicate actions, submissions, connection states, recovery, completion, and the full host/display/player integration path. AniMates additionally covers frame count, ownership, phase/deadline, fallback frames, self-vote rejection, scoring, payload limits, and reconnect both before and after submission. Canvas interaction should gain browser/E2E coverage.

The MVP acceptance scenario is the 34-step end-to-end flow from host registration through party creation/pairing, four-player join, Estimate with player reconnect, score persistence, AniMates drawing/draft recovery/playback/voting/reveal, starting another game without rejoin, display recovery, and persisted party completion.

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
| 2026-08-30 | Add a private Redis container and SignalR backplane before multi-replica actor coordination | Cross-process SignalR hint delivery was requested ahead of Milestone 13 | Establishes the supported SignalR scale-out transport and readiness boundary without moving authoritative state | Adds an ephemeral service and password; multiple Web replicas remain unsupported because actor and presence ownership are still process-local and sticky sessions are not yet configured |
| 2026-08-30 | Replace generated polygon player characters with semantic Kenney atlas selections | Players need to design persistent avatars that work as both full actors and face-only reaction portraits | One reconstructable character definition can drive join previews, the bottom portrait rail, emotes, podiums, and winner animations | Requires a player migration and a controlled compatibility mapping for characters created before the designer ships |
| 2026-09-02 | Replace the shared display's complete HTML fallback with a canvas-only Phaser presentation | The product direction requires one consistent TV composition and an explicit unsupported-browser failure state | Eliminates duplicate/stale presentation layers and keeps every audience state in the same renderer | Browsers that cannot initialize Phaser no longer receive a functional display fallback |
