# Slop Machine

Slop Machine is a server-authoritative party game in which every player runs a fictional content channel. Its tagline is **Feed the algorithm. Harvest the views.** It uses Quizizzo's existing game actor, SignalR hints, reconstructable role views, generic phone controllers, Phaser display, character rig, score-review podiums, reactions, and party return flow. It does not make AI requests during a game.

## Flow

The module is `Quizizzo.Games.SlopMachine.SlopMachineGameModule` and supports 2–12 players. Every timed phase stores a UTC deadline in the game snapshot. Assignments, submissions, option IDs, votes, pairings, machine guesses, awards, and used thumbnail IDs are also snapshot state, so refreshing or replacing a connection reconstructs the same private view.

1. **Fresh Slop** has two heats. Everyone captions one shared image. A vote is worth 1,000 views and every tied heat winner receives a 1,000-view Viral Bonus.
2. **Algorithm Roulette** assigns a server-selected thumbnail, data-backed title format, and curveball. Each player may re-spin exactly one reel once. Objective curveballs are validated on the server. Entries are split into balanced heats above six players. A vote is worth 1,000 views and tied heat winners receive a 1,000-view Algorithm Bonus.
3. **Thumbnail Telephone** assigns unique images, then deranges titles to different matchers. Choices contain one intended image and three unique, metadata-related decoys. A correct match awards 1,500 views to both participants. With at least three players, pairing votes award 500 views to each contributor and tied winners receive a 1,000-view Telephone Disaster Bonus each. The two-player game uses the objective score and skips this vote.
4. **Comments Section** returns strong earlier uploads and avoids self-assignment where possible. A vote is worth 1,000 views and tied winners receive a 1,000-view Engagement Bonus.
5. **Beat the Machine** mixes every human title with two stored machine titles. Human votes earn 2,000 views each. If no machine title is a public winner, every tied best human receives a 3,000-view Humanity Bonus. Each correctly identified machine title then earns 1,000 views.

Score updates are locked before each score-review phase. The shared score presentation counts earned views into the total and supports tied positions and joint final winners. The dedicated winner phase uses the shared full-body character and celebration animation. Text is whitespace-normalized and length-limited to 90 characters for titles and 140 for comments; markup is retained only as plain data and safely rendered by Blazor/Phaser.

## Thumbnail manifest

The embedded manifest is `src/Quizizzo.Games.SlopMachine/Assets/thumbnails.json`. The corresponding public files are under `src/Quizizzo.Web/wwwroot/media/games/slop-machine/thumbnails/` and are served with immutable public caching suitable for Cloudflare. A record has this shape:

```json
{
  "id": "cb-000001",
  "imageUrl": "/media/games/slop-machine/thumbnails/cb-000001.webp",
  "alternativeText": "Description of the generated thumbnail",
  "category": "animal-chaos",
  "composition": "wide-angle scene",
  "aiTitles": ["Machine title one", "Machine title two"]
}
```

IDs must be unique and path-safe. Images must be WebP. Every item needs useful alternative text and at least two non-empty `aiTitles`; the final round never generates titles at runtime. Category and composition guide Telephone decoy selection. The game stores used IDs and never repeats a thumbnail inside one session.

Import a regenerated collection from the repository root with:

```bash
scripts/assets/import-slop-thumbnails /path/to/thumbnails.json /path/to/generated-webp-directory
```

The importer validates the schema and every referenced image before replacing the game collection. Run the game-engine and integration tests after an import; they verify the embedded catalogue and the public static-asset contract.

## Presentation and soundtrack

The display uses a toxic red/yellow/cyan factory treatment, hero thumbnail/title-feed and gallery layouts, the shared channel avatars, and the standard sequential score podium. Phone views reuse Choice and Text controllers and expose the existing rate-limited reaction transport, including FAKE, UNSUBSCRIBE, and REPORT THIS SLOP.

The shared display is the sole owner of long-form music. Phone controllers do not download or play these tracks. The central `presentationAudio.js` coordinator derives the desired music from each reconstructable display snapshot, retains a track across compatible phase and roster updates, and permits two background elements only during the deliberate 600 ms crossfade.

Assets live in `src/Quizizzo.Web/wwwroot/media/audio/games/slop-machine/`:

| Track | State |
| --- | --- |
| `slop-lobby.mp3` | Lobby and `GameIntro` |
| `slop-writing.mp3` | Fresh Slop and Algorithm Roulette writing |
| `slop-countdown.mp3` | Authoritative final 20 seconds of every Slop Machine writing phase |
| `slop-spinner.mp3` | Algorithm Roulette reel spinning |
| `slop-voting.mp3` | Fresh Slop and Roulette reveal/voting |
| `slop-telephone.mp3` | Telephone introduction through results, except its writing countdown |
| `slop-comments.mp3` | Comments introduction through results, except its writing countdown |
| `slop-scoreboard.mp3` | All four round reviews and the final score review |
| `slop-final.mp3` | Beat the Machine introduction through results, except its writing countdown |
| `slop-machine-victory.mp3` | One-shot cue only when machine titles alone hold first place |
| `slop-human-victory.mp3` | One-shot overall winner celebration, including joint winners |

Countdown timing comes from `PhaseEndsAtUtc`; reconnecting inside the last 20 seconds seeks to the matching offset. A changed phase ends a countdown immediately. Reveal and voting phases share session keys so ordinary state updates do not restart them. Victory-cue keys include the game instance and are remembered for the browser session, preventing reconnect replay without suppressing a later rematch.

Music uses configured central volumes, 400 ms normal fades, a 600 ms crossfade, and 40% ducking beneath cues. The existing display control persists mute state and doubles as the browser audio-unlock affordance. Load failures warn once per track, mark it unavailable, and leave the authoritative game running silently. Audio paths inherit the immutable one-year browser and Cloudflare cache policy used by other static media.

Reduced-motion users receive the same state without essential information depending on animation.
