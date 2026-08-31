# AniMates

AniMates—animation with your mates—is a server-authoritative three-frame drawing and guessing game built on the reusable drawing framework. Every participant receives a different private absurd prompt and draws simultaneously. The display sees completion activity but not prompts or live work, then walks through the saved animations one at a time.

## State machine

Round 1 uses `Briefing`, `Drawing`, `Guessing`, `Choosing`, and `Results`. The presenter briefing has no deadline and the host explicitly starts play after its speech bubble; the copy is carried as semantic snapshot data so voice can replace or accompany it later. Briefings use a distinct stage background and a generic accessible drawing tutorial covering frame navigation, onion skin, undo, eraser, preview, and submission. Drawing happens once for everyone; each submitted animation then receives its own guessing, answer-choice, and result cycle. Drawing, guessing, and choosing carry UTC deadlines. Completing all required actions advances early. Deadline commands travel through the same per-game command channel as all other mutations.

The module derives submission ownership from the durable player actor. A submission accepts one to three completed frame references and normalizes it to exactly three frames by repeating the latest completed frame. Everyone except the current animator writes one bounded guess. The server persists and shuffles opaque answer options containing the real prompt and the guesses. Phone controllers use the same stable A/B/C labels as the display, hide a player's own guess, and reject forged self-choices. Each pick of a fake answer awards its writer 100 points. A correct pick awards 50 points to the chooser and 100 points to the animator.

## Secure submission and recovery

The browser rasterizes each logical frame to a 512×512 PNG and uploads it as multipart form data directly to the same-origin drawing endpoint; image bytes never cross the Blazor circuit or SignalR. The endpoint authenticates the durable player cookie, reconstructs the current player game view, verifies game instance, drawing scope, phase/controller, dimensions, type, per-frame size, total size, and ownership, then stores the bytes behind `IDrawingAssetStore`.

Each draft has a stable UUID submission ID in local storage. Retrying after a lost response reuses that ID. PostgreSQL enforces one metadata row per submission/game/player/round/frame, and the game engine independently makes the semantic command ID idempotent. SignalR refuses direct drawing-controller actions, preventing clients from bypassing asset validation.

Asset metadata contains opaque IDs, storage keys, ownership, frame number, UTC creation, and UTC expiry. The default one-day TTL and hourly cleanup remove both bytes and rows. Orphaned uploads from a late or rejected command are therefore bounded.

## Playback and role views

Game state contains opaque asset IDs, never physical paths. The display presentation receives only the current three-frame animation: Phaser loads local asset URLs and cycles frames at 150 ms. During choosing, all answers appear together as high-contrast lettered cards. Results identify the correct answer and guess writers.

During the shared drawing phase, role snapshots mark unfinished players as thinking and submitted players as idle. Phaser renders that semantic activity without owning completion state. The HTML overlay fixes the server-deadline countdown at the top right. After every animation result, cumulative scores drive a score-proportional podium; first place cheers and the lowest rank cries. Reduced-motion mode retains the static podium and standings while suppressing animated movement and reactions. Accessible HTML uses the same frame and answer data as the fallback.

AniMates uses a phase-specific broadcast visual system: locally served Fredoka display type and Nunito supporting type, distinct briefing/drawing/guessing/choice/showdown/result palettes, layered geometric stages, framed animation cards, short phase stingers, paper-like answer cards, and restrained camera/particle accents. Fonts are pinned npm build dependencies copied into `wwwroot/fonts`; production never requests a font CDN. Reduced-motion suppresses stingers, camera movement, looping decorative motion, and celebration movement while preserving typography, hierarchy, and all logical information.

## Round 2 — Same Prompt Showdown

After the final Round 1 reveal, the host opens a second presenter briefing. Every player then receives the same prompt, **“A grandma escaping from prison,”** and a five-frame drawing controller. All five-frame submissions remain anonymous while the TV plays each animation for three complete loops. The host opens voting only after playback. Phone vote options contain animation previews labelled A/B/C and exclude the player's own submission; forged self-votes are rejected by the module.

Every vote awards its animation creator 100 points. Every animation tied for the highest vote count receives the transparent 200-point winner bonus. The result snapshot reveals all creators and ranks together. Phaser renders the animation grid, enlarges the winner, runs the winning avatar in, and emits confetti; reduced-motion keeps the complete static creator/rank view.
