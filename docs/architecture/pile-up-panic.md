# Pile-Up Panic architecture and staged delivery

## Current delivery boundary

The final integration registers `Quizizzo.Games.PileUpPanic` in the production module catalogue. It is available to quick play and persisted party playlists for parties of two to four players, with the same automatic handoff and cumulative party-score behavior as the other games.

The game uses the repository's existing outer boundaries:

- `GameInstanceActor` serializes semantic game commands and persists versioned reconstructable state.
- `GameRuntimeManager` owns UTC phase deadlines and module discovery.
- durable player IDs and cookies remain identity; SignalR connection IDs remain transport metadata.
- `PartyGameService` finalizes game-local awards into cumulative party views and wins.
- Blazor owns authenticated phone inputs and realtime refresh; Phaser receives semantic display snapshots only.
- `presentationAudio.js` maps semantic phase/event cues to local audio identifiers.

The shared engine now has one small opt-in extension: modules implementing `IGameSimulationModule` may request a bounded recurring interval between 20 ms and five seconds. Each due time becomes a deterministic, idempotent `SimulationTickElapsedAction` sent through the existing per-game actor. A normal 250 ms Pile-Up transition advances deterministic internal 50 ms steps, so the database and observers see four reconstructable updates per second rather than one write per physics step. Recovery starts a fresh due interval from the persisted simulation cursor and catches the rules state up authoritatively. Gravity is never driven from a phone or display.

## Implemented rules core

- A server-owned 9×20 grid: 17 visible rows plus three hidden spawn rows.
- Twelve original scrap clusters: one two-cell, three three-cell, four four-cell, and four five-cell definitions.
- An eight-material palette shuffled separately from cluster selection.
- A deterministic generator with serializable random state and a four-cluster recent exclusion window.
- One active cluster, two queued clusters, horizontal movement, clockwise rotation, soft drop, instant drop, collision, locking, circuit detection, circuit collapse, and spawn/hidden-row overload detection.
- A generic eight-position rotation correction search that is unrelated to named third-party rotation systems.
- Circuit rewards of 100/400/900/1600 views for one/two/three/four simultaneous circuits; ordinary locks award five views. Drop movement gives small survival/activity views.
- Chaos charge of 34/65/90/100 for one/two/three/four-or-more simultaneous circuits. A player stores at most one deterministically dealt ability.
- `SendJunk`, `ScrambleQueue`, and `Shield`. Junk has a server-selected opening, is bounded by queue and time-window caps, and arrives after a later lock. Scramble never changes the active cluster. Shield is single-use and non-stacking.
- Offensive targets exclude self and overloaded players. Targets automatically move when their selected opponent overloads.

## Implemented Stage 2 engine integration

`PileUpMatch` accepts an authenticated durable player ID separately from the payload. Inputs carry match ID, monotonic sequence, intention, optional target, and a diagnostic-only client timestamp. Duplicate, stale, wrong-match, disconnected, eliminated, and rate-exceeding inputs are rejected. Client clocks never advance simulation.

`AdvanceSimulation` is driven by that scheduler. Gravity accelerates at server-defined intervals and a grounded cluster observes the configured server-owned lock delay. Complete persisted state includes grids, active and queued clusters, generator/deck state, views, circuits, chaos state, status, target, connectivity, input and junk windows, grounded/lock timing, last accepted sequence, round deadline, and simulation cursor. It round-trips through JSON and resumes with the same future cluster stream. Disconnects preserve natural gravity during a configurable grace period and become overload forfeits after expiry. Timeout ranking orders by operational state, lower stack, more circuits, then more views.

`PileUpPanicGameModule` supplies the standard descriptor, semantic input decoding, role-specific views, automatic recurring ticks, automatic phase deadlines, up-to-three rounds, and first-to-two match completion. Phone views contain only the player's status, score, charge, ability, target, input sequence, and bounded opponent summaries; they never receive grid or upcoming-cluster data. The display view receives the complete match snapshot required to render every arena. Actor release/recovery retains the accepted input sequence, rejects replays, resumes simulation, and can complete the match without host interaction.

Round placement points are game-local only: 4/2/1/0. At match completion each player's party award is their accumulated arena-performance views plus 250 views per placement point; the match victor receives another 1,000 views. The engine applies that conversion once at terminal completion, so retries and result interstitials cannot duplicate party awards.

Semantic event hooks currently include match/round start and completion, input rejection, movement/drop/rotation, connection changes, locking, completed circuits, ability earned/use/block, incoming/applied junk, overload/forfeit, standings, and match victory. These are the future realtime, structured-log, presentation, and audio seams; raw credentials and unnecessary personal data are never included.

## Configuration defaults

`PileUpOptions` centralizes input rate, horizontal and soft-drop repeat, initial/minimum fall intervals, speed progression, lock delay, queued/time-window junk caps, ability cooldown, disconnect grace, 150-second round duration, and 50 ms simulation step. Validation rejects non-positive or inconsistent values.

Lock delay is active in the server simulation. Horizontal and soft-drop repeat values drive the ordinary web-controller cadence; every repeated intention still passes through monotonic sequence and server rate validation.

## Implemented Stage 3 lifecycle and controller boundary

Every match now progresses through reconstructable `Introduction`, `ControllerReady`, `ArenaReveal`, `Countdown`, `Playing`, `RoundResult`, `Standings`, `FinalWinner`, `WinnerCelebration`, and `Completed` phases. Positive server-owned UTC deadlines advance every non-playing phase. The ready check also advances early after every participant confirms; the host retains an optional `Continue now` skip rather than being required for routine flow. A new round receives a fresh arena only when countdown completes, so reveal and recovery cannot consume active play time.

The reusable `Arcade` controller contract describes semantic buttons, accessible labels, held-input cadence, monotonic next sequence, targets, selected target, available ability, and charge. Its Blazor renderer uses Pointer Events, cancellable hold repetition, a compact target rail, portrait and short-landscape layouts, and controls that do not trigger page scrolling or text selection. Arcade actions use a low-latency SignalR call without form-style controller locking or a forced state reload after every key press. The server still authenticates the durable player, serializes every command, validates sequence/rate/phase, persists accepted state, and drives display refresh through ordinary state hints.

## Implemented Stage 4 display boundary

The display payload now carries an opaque game-specific state field through the shared Phaser snapshot mapper. Pile-Up uses that display-only field for the complete match snapshot, match-local wins and points, final awards, ready IDs, and the server catalogue's cluster geometry. Player payloads remain unchanged and still contain no grid, active-position, queued-shape, or opponent-board data.

Phaser owns a dedicated industrial stage rather than forcing the game through the generic quiz layout. Two-, three-, and four-player matches receive responsive 9×17 visible arenas with settled cells, the active cluster, two material previews, live views, circuit count, chaos charge, ready ability, shield, queued junk, connection, and overload treatment. The three hidden spawn rows stay in state for authority but are clipped from the TV grid.

Introduction and controller-ready scenes use the shared production character rig. Round results, standings, final survivor, and winner celebration use match-local ordering on full-body podiums, with winner celebration, loser crying, idle breathing, confetti, and reduced-motion-safe static presentation. Active view gains and newly overloaded piles have semantic display effects; no scoring or collision decision is made in Phaser. Authoritative phase and round deadlines drive the on-screen clocks after refresh.

The supplied `Falling Blocks Fever.mp3` is stored as a game-owned immutable media asset and plays as one looping background session across every non-completed Pile-Up phase. Reconstructable phase and revision refreshes do not restart it; a new game instance receives a fresh audio session, and the shared display mute/autoplay behavior remains unchanged.

The mapper forwards only the role-specific `DisplayGameViewPayload.State`; this is an opaque presentation seam and does not make module state generally visible to phones.

## Final integration

- The first and last live transport for each durable player emit serialized presence commands through the active game actor. Multiple tabs still collapse to one subject, reconnect cancels the ordinary party disconnect grace, and transport IDs never become game identity.
- A disconnected pile continues under natural gravity for the server-owned grace window, then forfeits by overload. An overloaded player receives the reusable waiting controller and remains a spectator of the surviving arenas.
- Phaser reconciles active clusters from the previous authoritative snapshot to the next over a short 180 ms ease. Settled cells remain immediate, refresh reconstructs directly, and reduced-motion mode skips interpolation.
- Touch controls retain the server-described hold cadence. Physical keyboards add arrows/WASD, Space/Enter, and Shift/X without introducing a client-authoritative simulation path.
- The display rebinds its SignalR party group only after a real `DisplayPaired` event. High-frequency simulation hints therefore refresh reconstructable state without repeating database-backed display pairing.
- Exact two-, three-, and four-player Edge journeys cover 320×568 portrait and 667×375 short-landscape controllers, held pointer input, rotation, controller refresh, and display refresh. All three production-catalogue journeys pass without error-level container logs.
- The supplied Falling Blocks Fever loop remains the game audio. No synthetic one-shot cues were invented without supplied sound assets.

## Risks and decisions

- A 50 ms persisted actor transition would generate excessive database traffic. The implemented 250 ms persisted cadence batches fixed 50 ms rules steps while retaining serialized authority and crash recovery.
- Input batching may reduce command overhead, but every item still needs an independently checked sequence and rate budget.
- The documented `performance views + placement points × 250 + 1,000 winner bonus` mapping is deterministic but should be play-tested before production registration.
- The current fairness rule keeps natural gravity running during disconnect grace, matching the server-authoritative direction and preventing tactical disconnect pauses.
