# VoiceChoon asset source

- `quizizzo_coop_showdown.mid`
- Supplied in `~/Downloads/quizizzo_coop_showdown_pack` on 2026-09-04.
- SHA-256: `c5fd38aa20e16db7b35a1de932863fdc0b8a3728c47295bc179cd72ba1fd2435`
- Standard MIDI Format 1, nine tracks, 480 ticks per quarter note.
- The supplied README states that the composition is original and may be modified freely for this project.

- `quizizzo_wubquake.mid`
- Supplied in `~/Downloads` on 2026-09-04.
- SHA-256: `576632ad9c043c4e1f23f16c60ea81b3673dbaf9acd1173db45811686c344a31`
- Added as the selectable `Wubquake` song; its player message and mouth-noise guidance live in `VoiceChoonSongCatalog.cs`.

The MIDI is embedded in the game assembly as the default song. `MidiParser.Parse(Stream, string)` remains independent of this asset and accepts other Standard MIDI files that use ticks-per-quarter-note timing.
