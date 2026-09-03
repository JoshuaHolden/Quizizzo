# Party playlists

The host display can either start one game immediately or assemble an ordered playlist of up to twelve games. Each playlist entry stores a stable queue-item ID, game key, and bounded JSON configuration on the party. The queue is PostgreSQL-backed, included in the reconstructable host party view, and is never held only in a Blazor circuit.

## Authority and lifecycle

- Only the authenticated party owner can replace or start the playlist.
- Queue changes are serialized through the party mutation coordinator and are accepted only while the party is in the lobby.
- The application validates every game key and its current player-count eligibility before saving or starting the queue.
- Starting a playlist removes its first entry and starts that game. The remaining ordered entries stay on the party.
- When a game completes, score and win persistence, return-to-lobby, dequeue, and startup of the next game happen under the same party mutation lease. Clients therefore reconstruct either the completed playlist or the next active game, not an editable lobby between them.
- Player identities and cumulative party scores are carried into every queued game. The existing game module still owns that game's phases, timers, actions, and scoring.
- Choosing **Play now** starts that game immediately. If a playlist already exists, its first item follows when the immediate game ends; otherwise the party returns to the game picker. Closing the party clears the playlist.

SignalR continues to send only state-change hints. A refreshed host or display reads the active game and remaining playlist from authoritative server state.
