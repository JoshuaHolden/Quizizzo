# Pile-Up Panic IP design notes

Pile-Up Panic is an independently designed competitive falling-cluster game. This file records engineering precautions; it is not a legal-clearance opinion.

## Deliberate design differences

- The arena is 9 columns by 17 visible rows, with three hidden server-owned spawn rows.
- The recoverable catalogue retains 12 original `scrap cluster` definitions spanning two, three, four, and five cells. New games generate from a simpler 10-cluster subset that excludes the awkward `flag-post` and `split-anvil` shapes.
- Materials come from a separately shuffled eight-colour palette. A cluster has a high-contrast outline in the future renderer, and colour never carries rule information.
- The deterministic generator excludes the four most recently emitted cluster definitions and shuffles materials independently. It is not a seven-item bag.
- Clockwise rotation uses a small generic correction search local to the desired placement. It contains no copied named rotation system or established kick table.
- The arena language and rules use scrap clusters, circuits, junk, chaos charge, and overload.
- Circuit scoring, chaos abilities, opponent targeting, survival ranking, and party-view conversion are original systems documented in the architecture note.
- No hold mechanic is present in the first version.
- Gravity begins at 1,100 ms per row and accelerates by 100 ms whenever any player completes a circuit, down to a 200 ms floor. The resulting shared match speed applies to every arena at the same time.

## Assets and dependencies

The rules use project-authored C# and the existing Quizizzo project infrastructure. The shared display soundtrack uses the supplied Falling Blocks Fever track under the game-owned media path. Any later assets must follow Quizizzo's local asset conventions and record their source and licence here.

## Realtime presentation boundary

- The phone receives only its own settled cells, active cluster, upcoming queue, and the bounded cluster-shape catalogue. Opponent grids and queues remain display-only.
- Pointer and keyboard controls run in browser JavaScript and send ordered arcade actions directly over the player's existing authenticated SignalR connection. The hub binds that connection to the durable player identity established during `ConnectPlayer` and accepts the fast path only while the authoritative player view exposes the matching Arcade controller.
- The phone predicts active-cluster movement, rotation correction, soft drop, and hard drop for immediate visual feedback. Prediction never locks clusters, completes circuits, awards views, applies junk, activates abilities, advances time, or chooses winners.
- Every authoritative player snapshot carries the last accepted input sequence. The phone discards acknowledged predictions, reapplies only pending inputs over the latest server arena, and therefore converges after rejected input, reconnect, refresh, gravity, lock, junk, or ability changes.
- The shared display remains the authoritative aggregate of every arena. Bursty state hints are coalesced into latest-state reloads, and Phaser uses only a short correction tween rather than extending perceived input delay.
