# ADR-0003: Serialized channel command processing

Status: Accepted

Each active party/game processes semantic commands through a bounded single-reader `Channel<GameCommand>`. Player actions, host actions, and deterministic UTC deadline actions enter the same queue, making their ordering explicit without coarse locking. Accepted and rejected command IDs are recorded in each optimistically versioned snapshot so retries cannot repeat transitions or scoring. The runtime exposes semantic results and role views but has no SignalR, Blazor, Phaser, EF Core, or concrete-game dependency.
