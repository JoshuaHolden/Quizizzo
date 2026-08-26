# ADR-0002: Blazor and Phaser presentation split

Status: Accepted

Blazor owns application state, SignalR, accessible HTML, overlays, and orchestration. One long-lived Phaser instance owns animation-heavy rendering and reacts to reconstructable semantic snapshots. Phaser compares snapshots to select its own tweens, particles, and camera effects; it never receives scoring decisions or pixel commands. This requires a dedicated interop boundary but prevents presentation code from becoming authoritative game logic and leaves a complete HTML fallback when canvas rendering is unavailable.
