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

Game state contains opaque asset IDs, never physical paths. The display presentation receives only the current three-frame animation: Phaser loads local asset URLs and cycles frames at 150 ms. During choosing and reveal, the animation occupies a taped paper card on the left while every high-contrast lettered answer sits inside a separate rounded game board on the right; the compact player rail remains below both regions. Results identify the correct answer and guess writers without reflowing or overlapping the animation.

AniMates owns an embedded, validated catalogue of 1,000 drawing-prompt/distractor pairs under `Assets/drawing-prompts-1000.json`. A new game selects distinct entries and persists every Round 1 assignment plus the shared Round 2 prompt inside its opaque module state, so recovery never rerolls active prompts. Each Round 1 choice set contains the real prompt, the supplied built-in distractor, and the human guesses. The built-in distractor has no player author and awards no points when selected. It is identified as a built-in decoy only during reveal. Round 2 uses another catalogue drawing prompt but does not load or expose its paired distractor.

During the shared drawing phase, role snapshots mark unfinished players as thinking and submitted players as idle. Phaser renders that semantic activity and the server-deadline countdown without owning completion state. At server-marked round boundaries, cumulative scores drive a score-proportional podium; first place uses the shared character-rig celebration, the lowest rank cries, and the other avatars breathe gently at idle. Reduced-motion mode retains the static podium and standings while suppressing animated movement and reactions.

AniMates uses a phase-specific broadcast visual system: locally served Fredoka display type and Nunito supporting type, distinct briefing/drawing/guessing/choice/showdown/result palettes, layered geometric stages, framed animation cards, short phase stingers, paper-like answer cards, and restrained camera/particle accents. Fonts are pinned npm build dependencies copied into `wwwroot/fonts`; production never requests a font CDN. Reduced-motion suppresses stingers, camera movement, looping decorative motion, and celebration movement while preserving typography, hierarchy, and all logical information.

## Round 2 — Same Prompt Showdown

After the final Round 1 reveal, the host opens a second presenter briefing. Every player then receives the same randomly selected catalogue prompt and a five-frame drawing controller. All five-frame submissions remain anonymous while the TV plays each animation for three complete loops. The host opens voting only after playback. Phone vote options contain animation previews labelled A/B/C and exclude the player's own submission; forged self-votes are rejected by the module.

Every vote awards its animation creator 100 points. Every animation tied for the highest vote count receives the transparent 200-point winner bonus. The result snapshot reveals all creators and ranks together. Phaser keeps each animation unobscured by placing creator, votes, points, and rank in the card's reserved caption area. Cards enter from a contracted scale and settle directly at their normal size, avoiding overlap between neighbouring animations; reduced-motion keeps the complete static creator/rank view.
