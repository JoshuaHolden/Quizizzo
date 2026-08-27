# Reusable drawing framework

Milestone 9 provides a game-neutral phone drawing controller. It deliberately does not implement Animate This prompts, authoritative submissions, playback, voting, reveals, or scoring; those remain game-module work.

## Canvas and frame configuration

Every document uses fixed logical coordinates (512×512 by default) regardless of the canvas's CSS or device-pixel size. The JavaScript bridge translates Pointer Events from touch, stylus, and mouse input into logical points, uses pointer capture and coalesced samples, and schedules redraws to animation frames at the browser device-pixel ratio. Blazor is never called for individual pointer samples.

Frame count is configuration, from 1 through 12. A one-frame drawing is a first-class mode: it uses the same document, canvas, strokes, draft, and export APIs, while frame navigation and onion-skin controls disappear and onion skin is forced off. Animate This can request three frames later without making three frames a framework assumption.

The current frame is vector data made from bounded strokes and points. Pen colour and width persist between frames. Erasing is represented as a stroke using destination-out composition, so returning to the pen restores the previous colour and width. Undo and confirmed clear affect only the selected frame. Onion skin renders only the preceding frame on a separate transparent canvas at 20 percent opacity and is never baked into current-frame data.

## Blazor and JavaScript boundary

`DrawingController` owns accessible controls, prompt/timer slots, toolbar state, safe frame navigation, the two-step clear confirmation, and lifecycle. `drawingCanvas.js` owns Pointer Events, rendering, the in-memory vector document, serialization, and browser drafts. `drawingDocument.mjs` contains the bounded, browser-independent document model and is tested directly with Node.

The player UI selects this controller from `PlayerControllerKind.Drawing` plus `DrawingControllerConfiguration`; it does not switch on a game name. Its public export and draft-clear methods are the seam Milestone 10 will use after an authoritative, idempotent submission succeeds.

## Draft recovery

Draft JSON contains a schema version, party ID, game-instance ID, round ID, player ID, logical dimensions, configured frame count, vector frames, selected colour and size, tool, onion setting, and `LastUpdatedAt`. Storage keys contain the same durable scope. A refresh restores only a structurally valid draft with an exact identity/configuration match. Starting a later round removes obsolete drafts for that player within the same game instance; successful submission can explicitly clear the active draft.

Drafts are a client recovery convenience, not authoritative submissions. Bounds on frames, strokes, points, coordinates, colours, and widths limit malformed local data before it can be restored. Milestone 10 must independently validate ownership, phase, deadline, idempotency, and payload size on the server.

## Asset storage

`IDrawingAssetStore` is an Application abstraction. The initial Infrastructure adapter writes opaque WebP or PNG bytes under generated, validated keys, enforces a configurable byte limit, uses an atomic temporary-file move, and rejects path traversal. Every returned reference carries `CreatedAtUtc` and `ExpiresAtUtc`; the default retention period is one day. A hosted sweep runs hourly, removes expired files and empty shard directories, skips unrecognized paths and links, and retries files temporarily held by readers on its next pass.

Compose mounts `/app/assets/drawings` from the dedicated `quizizzo-drawing-assets` volume. Games depend on the abstraction, never the filesystem path; a future object-store adapter can replace it without changing game contracts. Drawing bytes are not stored in PostgreSQL. When Milestone 10 persists submission metadata, it must persist the supplied expiry and remove expired metadata with the asset so neither storage layer grows indefinitely.
