# Production-readiness review

This checkpoint reviews Milestones 1–12 before delivery automation. It strengthens the single-node modular monolith without crossing the MVP boundary into distributed actors, a message broker, or multiple databases.

## Reliability and persistence

- Production game snapshots use PostgreSQL JSONB with optimistic revisions; the in-memory store is test-only.
- Actors recover lazily after process replacement, completed actors are evicted, and interrupted completion is reconciled back into party/player rows.
- Per-game channels and player command histories are configurable and bounded. Invalid transport payloads enter the same idempotency history as valid semantic actions.
- Completed/orphan game snapshots, drawing bytes, and drawing metadata all have bounded retention workers with structured failure logging and cancellation-safe shutdown.
- Party admission and game start share a keyed coordinator, preventing local concurrent joins from exceeding capacity or crossing a game-start transition.

## Security and resource bounds

- Production Identity, player, display, and antiforgery cookies require HTTPS; Data Protection keys persist in a dedicated container volume.
- One trusted forwarded-header hop supports the loopback reverse proxy. Host filtering, HSTS, framing, MIME-sniffing, referrer, and browser-permission headers are configured.
- Join and multipart drawing requests have explicit body limits. Drawing uploads, player joins, and asset reads have NAT-compatible bounded rate limits.
- PNG submissions validate the complete chunk structure, dimensions, critical fields, CRCs, and terminal marker before storage. Physical keys remain path-safe and opaque.
- The runtime container uses a non-root user; PostgreSQL has no published port; local secrets and generated data are excluded from the image build context.

## Maintainability

- `latest-recommended` .NET analyzers and code-style checks apply repository-wide, with warnings promoted to errors in CI/release verification.
- Choice and vote controllers share one option-selection component. Explicit per-round selection scopes prevent stale local choices while preserving a selection across harmless state refreshes.
- Persistence-specific duplicate handling remains inside Infrastructure rather than leaking EF/Npgsql failure state into endpoint orchestration.
- Template demo pages were removed and the home page now describes Quizizzo's real flows.

## Supported scale

The production target is one Web container plus one private PostgreSQL container on the Hetzner VPS. It can host many independent parties concurrently because each active game has a small isolated actor and async I/O. PostgreSQL provides durable restarts, not distributed actor ownership. Multiple Web replicas are unsupported until a later design adds coordinated command ownership and a SignalR backplane; vertical scaling is the safe MVP path.
