# Phaser presentation layer

The shared display creates one Phaser 3.90 game instance when its interactive Blazor component starts. That instance survives pairing, lobby, answering, results, and return-to-lobby changes; it is destroyed only when the display component itself is disposed. Phaser uses a fixed 1280×720 logical scene with `FIT` scaling and centred letterboxing, so the same composition scales to 720p, 1080p, and 4K displays.

## Authority boundary

Blazor continues to own SignalR, role authorization, accessible HTML, deadlines, and presentation orchestration. It maps the current reconstructable display view into a small semantic snapshot containing:

- presentation mode, game key, phase, and snapshot revision;
- durable player IDs, names, server scores, connection status, and persistent character traits;
- server-produced result ranks and point awards.

The JavaScript bridge compares successive snapshots and chooses its own visual treatment. New durable player IDs produce join animations, connection-status changes fade or restore a character, server score changes pulse the score label, and newly revealed rank data triggers winner motion, a camera flash, and generated confetti. No client code calculates ranks, winners, scores, deadlines, or game progression, and no server message specifies pixels, tween durations, or particle coordinates.

The initial snapshot is rendered without transition effects. Consequently, a refreshed display reconstructs the correct current scene without requiring missed SignalR history. The accessible HTML overlay remains the complete logical display if WebGL, Canvas, JavaScript interop, or Phaser initialization is unavailable.

## Characters and assets

Characters are drawn from the persisted body, colour, eye, mouth, and accessory traits using Phaser graphics primitives. They do not require remote images, so the same player remains visually recognizable throughout the party. Phaser and SignalR are exact npm dependencies copied into local static assets by `npm run build:client`; the deployed page never depends on a CDN.

Animation is disabled when the browser reports `prefers-reduced-motion: reduce`. The canvas is decorative and excluded from the accessibility tree; names, prompts, results, scores, pairing details, and countdowns remain HTML.
