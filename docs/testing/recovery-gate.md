# Milestone 5 recovery gate

Milestone 5 blocks game-engine work until every browser role can reconstruct its logical state after an HTTP refresh and can replace its SignalR transport without replacing its application identity.

The integration tests use the production Razor page pipeline, cookie middleware, application services, `PartyHub`, group registration, and `PartyConnectionRegistry`. Test-only dependency overrides provide durable in-memory repositories and a host authentication scheme, so the gate is deterministic and does not require a running PostgreSQL container.

| Role | Durable identity | Refresh proof | Transport-replacement proof |
|---|---|---|---|
| Host | Authenticated Identity user ID | Two requests to the owned party route reconstruct the same party and room code | Two distinct SignalR connection IDs register as one host subject; closing either transport does not remove the other |
| Display | HttpOnly display session token, stored as a hash | Two display requests reconstruct the same paired display, room code, and roster | Two distinct connection IDs register as one display session and can be replaced without changing the display ID |
| Player | HttpOnly anonymous player token, stored as a hash | Repeated player requests reconstruct the same player ID, name, character, party, score, and status | Replacement within the grace period cancels disconnection; after grace expiry, refresh reconnects the same durable player rather than creating another |

The tests deliberately use SignalR long polling because ASP.NET Core's in-memory test server does not provide a real network WebSocket. Hub negotiation and group/identity behavior are transport-independent; production WebSocket proxy requirements are covered in the Hetzner coexistence guide.

Run only this gate with:

```powershell
dotnet test tests/Quizizzo.IntegrationTests/Quizizzo.IntegrationTests.csproj --filter FullyQualifiedName~RoleRecoveryTests
```

Connection IDs are asserted to differ and are never persisted or passed to application services as identities.
