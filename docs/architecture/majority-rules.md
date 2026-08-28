# Majority Rules

Majority Rules is a three-round, server-authoritative writing and voting game. It is also the proof that text entry and voting are reusable player controllers: the game module returns `PlayerControllerKind.Text` or `PlayerControllerKind.Vote` plus configuration, and neither the Blazor player page nor the controller components branch on the game key.

## State and command flow

Each round moves through `Answering -> Voting -> Results`; the final host advance moves to `Completed`. Answer and vote deadlines are UTC values owned by the game state. Player submissions, deadline actions, and host advances all enter the same serialized game-command channel, so the existing engine authorization, phase/deadline checks, idempotency records, score accumulation, and snapshot recovery apply without game-specific transport logic.

The server trims answers, collapses repeated whitespace, rejects empty/control-character input, and enforces the shared 200-character limit. Answer text is absent from host, display, and other-player views while writing is open. A submitted phone reconstructs a waiting controller after refresh rather than being allowed to submit again.

## Anonymous voting and reveal

When answering closes, every submitted answer receives a persisted opaque option ID. Voting views expose that option ID and answer text, but not the answer owner's durable player ID or name. A player's own answer is omitted from their options, and the module also rejects a forged self-vote authoritatively.

Votes award 500 points to the author of the selected answer. Results rank answers by vote count, reveal their authors, and emit score awards through the shared engine. Missing answers and votes are valid when a deadline expires and receive no points.

Because answers, option IDs, votes, deadlines, and results live in the versioned module snapshot, host, display, and phone refreshes reconstruct the complete phase-appropriate view. SignalR only prompts those clients to reload it.
