# ADR-0002: Blazor and Phaser presentation split

Status: Accepted

Blazor owns application state, SignalR, accessible HTML, overlays, and orchestration. One long-lived Phaser instance owns animation-heavy rendering and reacts to semantic events. This requires a dedicated interop boundary but prevents presentation code from becoming authoritative game logic.
