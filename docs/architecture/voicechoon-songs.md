# VoiceChoon songs

VoiceChoon songs are embedded MIDI assets selected when a host starts a game. The original Co-op Showdown remains the default; Wubquake is the second selectable song.

## Add a song

1. Copy the original, redistributable `.mid` file into `src/Quizizzo.Games.VoiceChoon/Assets/`.
2. Record its SHA-256 and source/provenance in `src/Quizizzo.Games.VoiceChoon/ASSET_SOURCE.md`.
3. Add a `VoiceChoonSongDefinition` entry in `VoiceChoonSongCatalog.cs`:
   - stable `Key` used in persisted game configuration;
   - human-facing `DisplayName`;
   - exact MIDI `FileName`;
   - short `BriefingMessage` shown to every player;
   - `RecordingMessage` explaining the mouth-noise style to use;
   - embedded resource name, normally `Quizizzo.Games.VoiceChoon.Assets.<file-name>`.
4. If the song needs different sound advice, update the catalog/profile guidance rather than putting song-specific logic in the UI. Keep prompt keys stable when possible so the recording and asset APIs remain generic.
5. Add the song key to the catalog tests and add a game-module test that verifies the selected MIDI name, briefing, and recording guidance.

The project file embeds every `Assets/*.mid` file automatically. `MidiParser` accepts Standard MIDI files with ticks-per-quarter-note timing. Track roles are inferred by the parser and assigned across one to eight players by `InstrumentAssignmentService`.

## Host selection

The host selects the song in the VoiceChoon settings before choosing **Play now** or adding the game to a playlist. The selected song key is persisted in the generic game configuration and copied into the authoritative VoiceChoon snapshot, so refresh and process recovery keep the same song.

Unknown song keys are rejected server-side. Do not trust the browser selection or derive song identity from a display name.

## Player guidance

The selected definition supplies two messages:

- `BriefingMessage` explains the character of the song before recording.
- `RecordingMessage` explains the sound-making strategy for the assigned prompts.

Prompt labels and examples still come from the track roles, such as `BOOM`, `TSH`, `BEEP`, or `MMMM`. Keep advice concrete: distinguish short percussive hits from steady sustained vowels, and tell players which register or texture to use. The server sends this guidance in the reconstructable player view; it is not only a transient SignalR message.

## Validate locally

```sh
npm run test:client
dotnet test tests/Quizizzo.GameEngine.Tests/Quizizzo.GameEngine.Tests.csproj --filter FullyQualifiedName~VoiceChoon
dotnet build src/Quizizzo.Web/Quizizzo.Web.csproj -c Release --warnaserror
```

For a new song, also start a one-player solo autoplay test, confirm the recording prompts describe the new sound palette, and verify that the selected song name survives refresh. Never send MIDI bytes through SignalR; the server parses and stores the logical chart, while clients receive reconstructable notes and deadlines.
