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
