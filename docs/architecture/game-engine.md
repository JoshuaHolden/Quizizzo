# Game engine foundation

The game engine is a transport-neutral runtime. SignalR, Blazor, EF Core, Phaser, and physical asset storage do not appear in game-module contracts.

## Module boundary

An `IGameModule` supplies a descriptor, creates its initial explicit state, applies semantic `IGameAction` values, and builds role-specific view payloads. Module state is an opaque versioned JSON document wrapped by engine-owned phase, deadline, and completion metadata. This lets the runtime snapshot any game without referencing its assembly or switching on a game name.

`GameModuleCatalog` discovers all registered `IGameModule` implementations by key and fails immediately on duplicate or invalid descriptors. Estimate is the first registered module and proves the complete number-controller round loop.

## Command flow

Each game instance owns one bounded `Channel` with a single reader:

```text
host/player/system action
          |
          v
    GameCommand envelope
          |
          v
 bounded per-game Channel
          |
          v
 validate identity, phase, deadline, and rule
          |
          v
 transition + score awards + semantic events
          |
          v
 atomically save the next snapshot revision
```

The command ID is stored with either its accepted or rejected result. A retry returns that recorded result with `IsDuplicate = true` and cannot repeat a transition or score award. Malformed transport payloads are converted into semantic rejected actions inside the actor, so their retries are idempotent too. Caller cancellation stops waiting but does not cancel an already queued authoritative mutation. Configurable queue and processed-player-command limits bound hostile traffic while reserving host/system progression.

Deadlines are UTC values. The runtime schedules a deterministic `DeadlineElapsedAction` and sends it through the same channel as player and host actions, so submission/deadline races have one ordering. A normal action received at or after the current deadline is rejected even if the timer callback has not yet reached the queue.

## State, views, and recovery

`GameRuntimeSnapshot` holds participants, module state, shared scores, processed-command results, revision, and update time. Production uses `PostgreSqlGameStateStore`: a versioned JSONB document plus indexed identity/revision columns and compare-and-swap updates. `InMemoryGameStateStore` remains a fast test adapter. Completed snapshots and stale orphan snapshots have configurable retention and are removed by a bounded background worker.

The manager lazily recreates an actor from a PostgreSQL snapshot when a command or view arrives after runtime replacement. If a process stopped after persisting completion but before updating the party row, the application finalizes scores and returns that party to its lobby during recovery. Completed actors are evicted and disposed. Host ownership and participant identity are revalidated before commands and views. Modules receive a role context and must return only that role's logical payload; the engine adds common phase, deadline, revision, completion, and score data.

The engine returns semantic events as data. Web transport code may translate a successful result into a SignalR refresh hint, but the engine never references SignalR and clients always recover through a fresh role view.

## Scaling boundary

The deployed MVP intentionally runs one active Web process. PostgreSQL snapshots make restart recovery durable and the per-game actors efficiently serialize many concurrent parties, but actor ownership, SignalR connections, and presence are process-local. Running multiple Web replicas without distributed command ownership and a SignalR backplane would create competing actors, so it is explicitly unsupported. Scale this version vertically; horizontal Web scaling requires a later coordinated design rather than a replica-count change.
