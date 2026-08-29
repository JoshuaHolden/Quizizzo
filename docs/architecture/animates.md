# AniMates

AniMates—animation with your mates—is a server-authoritative three-frame drawing and voting game built on the reusable drawing framework. Each participant receives a private action prompt. The display sees only completion progress during drawing, then anonymous animations during voting, and creator names only after the reveal.

## State machine

The explicit phases are `Drawing`, `Voting`, `Results`, and `Completed`. Drawing and voting carry UTC deadlines. Submitting all animations starts voting early; submitting every eligible vote reveals results early. Deadline commands travel through the same per-game command channel as all other mutations. The host can finish only from results.

The module derives submission ownership from the durable player actor. A submission accepts one to three completed frame references and normalizes it to exactly three frames by repeating the latest completed frame. Votes target the submission owner's durable player ID, are accepted once, and reject self-votes. Creators receive rank awards of 1,000/600/300 points when they have at least one vote.

## Secure submission and recovery

The browser rasterizes each logical frame to a 512×512 PNG and uploads it as multipart form data directly to the same-origin drawing endpoint; image bytes never cross the Blazor circuit or SignalR. The endpoint authenticates the durable player cookie, reconstructs the current player game view, verifies game instance, drawing scope, phase/controller, dimensions, type, per-frame size, total size, and ownership, then stores the bytes behind `IDrawingAssetStore`.

Each draft has a stable UUID submission ID in local storage. Retrying after a lost response reuses that ID. PostgreSQL enforces one metadata row per submission/game/player/round/frame, and the game engine independently makes the semantic command ID idempotent. SignalR refuses direct drawing-controller actions, preventing clients from bypassing asset validation.

Asset metadata contains opaque IDs, storage keys, ownership, frame number, UTC creation, and UTC expiry. The default one-day TTL and hourly cleanup remove both bytes and rows. Orphaned uploads from a late or rejected command are therefore bounded.

## Playback and role views

Game state contains opaque asset IDs, never physical paths. Player voting views receive anonymous options excluding their own submission. The display presentation receives three-frame semantic animations: Phaser loads the local asset URLs, cycles frames at 150 ms, rotates through submissions, and adds creator/vote text only in reveal mode. Accessible HTML uses the same frame references as a fallback, with reduced motion showing the first frame.
