# VoiceChoon architecture and game design

VoiceChoon is normally a three-to-eight-player cooperative rhythm game. A temporary, explicitly selected one-player autoplay mode supports production-path testing. A Standard MIDI file defines composition, timing, pitch, track structure, and sections. Players record short vocal or physical noises on their phones; those recordings, not MIDI synthesizer instruments, become the audible band.

VoiceChoon is registered as a production game after implementing the reusable musical pipeline, server-owned runtime, recording flow, phone controller, Web Audio playback, and shared-display presentation.

## Core joke and design rule

The music should remain recognisable while the timbre comes from people saying things such as `BOOM`, `BWAAAA`, `BEEP`, `WAAH`, and `NEEOOW`.

The four phone pads are gameplay lanes, not four musical pitches. Every generated `RhythmNote` stores both:

- `Lane`, from zero through three, for readable four-button input;
- `TargetMidiNote`, retaining the original MIDI pitch for audio playback.

The audio engine must never quantise playback to four pitches. When a note is hit, it selects an appropriate player sample and derives playback rate from the retained target pitch:

$$
playbackRate = 2^{\frac{targetMidiNote-rootMidiNote}{12}}
$$

## Pipeline boundaries

```text
MIDI stream
  -> MidiParser
  -> RawMidiSong
  -> InstrumentAssignmentService
  -> player/track assignments + recording prompts
  -> ChartGenerator
  -> PlayerChart[]
  -> future server RhythmGameEngine
  -> role-specific snapshots
  -> future phone ControllerRenderer + Web Audio AudioEngine
```

- `MidiParser` owns Standard MIDI decoding and tempo-aware conversion. It has no knowledge of players, UI, game phases, or audio playback.
- `InstrumentAssignmentService` owns deterministic distribution of parsed tracks across three to eight players.
- `ChartGenerator` owns playability transformations. It does not alter the target MIDI pitch.
- `PitchShiftPlanner` chooses a recorded root, octave-folds extreme requests, and describes one-shot or sustained playback.
- `VoiceChoonGameModule` owns song phase, authoritative start time, accepted inputs, hit judgements, combo, band score, energy, and completion.
- The browser audio boundary owns microphone capture, silence trimming, normalization, fades, explicit setup previews, decoded `AudioBuffer` objects, and main-display sample scheduling. Gameplay phones remain silent and do not award points.

This separation lets another MIDI be loaded without changing game rules or Blazor components.

## Generic MIDI ingestion

The parser accepts any readable MIDI `Stream` and source name. The supplied `quizizzo_coop_showdown.mid` is only the embedded default.

Supported Stage 1 input:

- Standard MIDI files with ticks-per-quarter-note timing;
- Format 0 or Format 1 layouts supported by DryWetMidi;
- arbitrary tempo maps;
- named or unnamed note tracks;
- marker events used as song sections;
- General MIDI percussion channel detection;
- unknown track names through deterministic role fallback.

SMPTE time division is rejected explicitly rather than being interpreted incorrectly. Files with no playable notes are rejected. Track names are hints, not schema: recognised names map to VoiceChoon roles, channel 10 maps to drums, low unnamed tracks can map to bass, and remaining tracks stay `Other`. Unknown and duplicate tracks are load-balanced rather than discarded.

The default asset is Standard MIDI Format 1, 480 ticks per quarter note, eight playable tracks plus one conductor/meta track. Its source and checksum are recorded in `src/Quizizzo.Games.VoiceChoon/ASSET_SOURCE.md`.

## Instrument assignment

Known roles use the preferred assignment below. Player indexes describe assignment order, not a permanent identity.

| Players | Assignment |
|---|---|
| 3 | P1 Drums + Percussion FX; P2 Bass + Chords; P3 Lead A + Lead B + Arp + Vocal Stabs |
| 4 | P1 Drums; P2 Bass; P3 Chords + Arp; P4 Lead A + Lead B + Percussion FX + Vocal Stabs |
| 5 | P1 Drums; P2 Bass; P3 Chords; P4 Lead A; P5 Lead B + Arp + Percussion FX + Vocal Stabs |
| 6 | P1 Drums; P2 Bass; P3 Chords; P4 Lead A; P5 Lead B; P6 Percussion FX + Arp + Vocal Stabs |
| 7 | P1 Drums; P2 Bass; P3 Chords; P4 Lead A; P5 Lead B; P6 Percussion FX + Vocal Stabs; P7 Arp |
| 8 | One logical role per player in score order |

For unfamiliar files, known roles take their preferred owner and unrecognised or duplicate tracks go to the player with the smallest current raw-note load. A later balancing pass may move secondary notes when active seconds differ materially, but it must not silently change the primary assignment.

## What players record

The recording setup is generated from assigned roles. Duplicate prompts are removed and one player receives at most four recording tasks.

| Role | Suggested noises | Style | Design intent |
|---|---|---|---|
| Drums | `BOOM`, `KAH`, `TSH`, `PAH` | Four percussion samples | Semantic kick, snare, hi-hat, and other/FX mapping; normally no melodic pitch shift |
| Bass | `BWAAAA`, `MMMM` | Low/high sustained roots | Stable vowel-like sounds that can loop cleanly |
| Chords | `AAAAH`, `OOOOH` | Low/high sustained roots | Multiple pitched copies may play together to reconstruct chords |
| Lead A | `BEEP`, `WEEEE` | Low/high one-shots | Bright, quick attack for the call voice |
| Lead B | `WAAH`, `NEEOOW` | Low/high one-shots | Contrasting response voice |
| Percussion FX | `POP`, `TCHK` | Two percussion samples | Very short clicks/pops; tiny variation may avoid repetition |
| Arp | `TING`, `DING` | Low/high one-shots | Short enough to survive fast decorative passages |
| Vocal Stabs | `HEY`, `BAH` | Low/high one-shots | Clean, emphatic consonant starts |
| Unknown | `BOOP`, `WAAH` | Low/high one-shots | Safe generic fallback for imported tracks |

Recording UI requirements for the next stage:

- replay and replace each sample before confirming;
- trim leading/trailing silence;
- normalize gain without clipping;
- add very short fades to avoid clicks;
- keep samples private to the active party/game;
- never log recording URLs, credentials, or raw bytes;
- reject oversized, malformed, or wrong-owner uploads;
- clear recordings through the same bounded asset-retention model used elsewhere.

## Pitch shifting and sustained samples

For melodic roles, the nearest raw root recording is selected first. The requested note is then octave-folded toward that root until the shift is within plus or minus 18 semitones. The requested MIDI pitch remains in the chart for scoring, analytics, and future improved synthesis; the folded playback pitch is only an audio-rendering choice.

One-shot samples use `AudioBufferSourceNode.playbackRate`. Pitch and duration therefore change together, which is acceptable and funny for short syllables.

Sustained samples use a stable middle loop region, initially 30–70 percent of the normalized recording:

```text
attack -> loop loop loop -> release fade
```

A hold of at least 500 ms requests looping. The audio engine stops and fades the source at the chart duration. Percussion samples use playback rate 1.0 by default; optional deterministic variation must stay small and must not change chart judgement.

The display preloads decoded samples and retains one scheduler for the immutable game/start-time identity. A 50 ms rolling look-ahead schedules notes on the Web Audio clock; snapshot refreshes update presentation state without stopping active voices or replaying elapsed notes. Polyphonic voices use conservative gain before the output compressor, and phase/mute shutdowns use short gain ramps rather than hard cuts. Loop bounds come from each decoded recording rather than an assumed duration and are snapped to nearby zero crossings.

## Four-lane chart generation

Melodic pitches are ranked within each source track and divided into four ordered bands. Ascending phrases therefore tend to move from lane 1 toward lane 4. Repeated runs may shift one lane for readability, but `TargetMidiNote` is untouched.

Drums use General MIDI semantics:

- notes 35/36: lane 1, kick;
- notes 38/39/40: lane 2, snare/clap;
- notes 42/44/46: lane 3, hi-hat;
- toms, cymbals, and other percussion: lane 4.

The generator currently:

- quantizes tiny timing noise to a configurable 10 ms grid;
- removes duplicate same-lane events at the same instant;
- applies the selected Easy, Medium, or Hard density profile without changing retained notes' MIDI timing, pitch, duration, or playback plan;
- prioritizes lead, drums, bass, chords, percussion, arp, then stabs when merged tracks collide;
- limits Easy to two presses per rolling second, one pad at once, and 400 ms between notes in the same lane;
- limits Medium to three presses per rolling second, one pad at once, and 250 ms between notes in the same lane;
- limits Hard to five presses per rolling second, two pads at once, and 80 ms between notes in the same lane;
- converts notes of at least 500 ms into holds;
- emits stable note IDs from player, track, tick, pitch, and lane;
- computes an activity measure for later balancing and `GET READY` cues.

Future tuning should preserve strong beats and recognizable phrases before decorative notes. It must be deterministic so recovery produces the same chart.

## Phone controller

The active controller is landscape-first. Four visually distinct pads occupy roughly the bottom third, with the hit line immediately above. Notes are positioned every animation frame from authoritative song time, never from CSS animation completion.

```text
lane 1       lane 2       lane 3       lane 4
   note                    note
               note
                                         note
------------------------------------------------ hit line
   BOOP         BOOP         BOOP          BOOP
```

Pads use Pointer Events on down/up, support true multi-touch, send timestamped semantic input over the existing authenticated SignalR connection, and optionally provide haptics. Gameplay audio plays only on the paired main display. No click delay is allowed. Portrait mode shows a concise rotate-device gate without destroying reconstructable state.

The note travel time starts at two seconds. Inactive players receive a bounded `GET READY` countdown before their next phrase.

## Authoritative timing and judgement

The server will publish a UTC song start and immutable chart identity. Clients estimate server-clock offset from repeated timestamp samples and render against that clock. Browser audio is scheduled against `AudioContext.currentTime` using the measured offset. SignalR messages remain hints; refresh reloads the complete role-specific chart window, accepted judgements, combo, and song position.

Server-owned tap windows scale with difficulty:

| Difficulty | Perfect | Great | Good |
|---|---:|---:|---:|
| Easy | 90 ms | 180 ms | 300 ms |
| Medium | 70 ms | 140 ms | 250 ms |
| Hard | 60 ms | 120 ms | 200 ms |

The selected mode is persisted in the game snapshot and playlist configuration. It changes playable density and judgement tolerance, not song tempo, section timing, source pitches, recorded timbre, or pitch-shift plans.

## Temporary solo autoplay test

The host can explicitly enable `Solo autoplay test` in VoiceChoon settings. This is a removable test seam, not normal game balance:

- exactly one player is accepted; normal mode still requires three to eight;
- every MIDI track and all 18 distinct sound prompts are assigned to that player;
- recording time is tripled to accommodate the complete sound set;
- the server creates one Perfect judgement per generated chart note and rejects manual rhythm input;
- the phone schedules the player's private samples from the authoritative song clock and hides the gameplay pads;
- the rhythm controller reuses the audio context unlocked by the required recording gesture to satisfy browser autoplay policy.

A hold scores start accuracy (40), maintained duration (up to 40), and release accuracy (20), with a 100 ms interruption grace period. The server owns every judgement and ignores forged note IDs, wrong lanes, impossible chronology, duplicate input IDs, and notes outside the player's chart.

Hold presses create persisted active-hold state and do not award a completed judgement. Release is a separate sequenced semantic action; only then does the server combine start, maintained-duration, and release accuracy. The phone defers release for the 100 ms interruption window so a momentary pointer interruption can resume without creating a second press.

Calibration is optional for first play. A later setup phase estimates per-device audio/input offset from repeated beat taps and stores it only for that durable browser session, with a manual adjustment control.

## Cooperative scoring and display

Individual accuracy is retained for feedback, but the primary result is Band Score. Planned contributors are tap judgement, hold completion, full-band synchronized hits, measure streaks, section accuracy, and cooperative combo.

Energy levels are `Awkward`, `Getting Somewhere`, `Banging`, and `Absolute Scenes`. Misses reduce energy but zero energy does not end the song; it triggers a temporary embarrassing collapse state and allows recovery.

The display shows a ridiculous live band rather than duplicating phone charts: player avatars, assigned instruments, section/progress, combo, energy, solo/callout emphasis, and occasional performance lines. The MIDI markers drive Intro, Verse A, Call/Response, Chorus 1, Breakdown, Build, Final Chorus, and Outro presentation. Lead A and Lead B receive explicit call/response focus while accompaniment continues.

## Authority and recovery

The server owns assignment, generated charts, song start, phase, accepted inputs, judgements, holds, combo, score, energy, and completion. Phones own only microphone capture, local sample buffers, immediate audio feedback, visual interpolation, and draft recording recovery.

A refresh must reconstruct:

- the player's assignment and recording requirements;
- which recordings are accepted;
- chart identity and a bounded upcoming-note window;
- authoritative song position and clock offset inputs;
- accepted note/hold state;
- individual and band score state.

Player recordings are not sent through SignalR and are never stored in game JSON snapshots. Upload uses a bounded asset endpoint and opaque IDs; snapshots reference those IDs.

## Delivery stages

1. **Pipeline foundation (complete):** generic MIDI parsing, default embedded song, role inference, 3–8 assignment, sound prompts, chart generation, pitch planning, and tests.
2. **Server runtime (complete):** reconstructable recording/ready/countdown/playing/results phases, immutable chart persistence, UTC song clock, semantic inputs, scoring, energy, and completion.
3. **Recording (complete):** secure microphone UI, local processing/preview, bounded idempotent upload, ownership metadata, and sample recovery.
4. **Phone/audio (complete):** four-lane canvas renderer, Pointer Events, multi-touch, Web Audio playback, pitch plans, and reconnect sequencing.
5. **Display (complete):** Phaser band performance, MIDI sections, progress, energy, combo, results, and reduced-motion behavior.
6. **Browser proof:** complete three-phone two-minute song, refresh/reconnect, latency calibration, no-scroll landscape layout, and audio scheduling checks.

VoiceChoon enters the production catalogue only after Stages 2–5 are complete. The three-player browser journey remains the final acceptance gate.
