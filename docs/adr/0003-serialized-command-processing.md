# ADR-0003: Serialized channel command processing

Status: Accepted

Each active party/game processes semantic commands through a single `Channel<GameCommand>` consumer. This makes player actions, deadlines, and host commands deterministic without coarse locking. It requires explicit lifecycle supervision and persistence after transitions.
