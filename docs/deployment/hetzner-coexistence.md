# Hetzner deployment alongside an existing website

Quizizzo is isolated as the Compose project `quizizzo`. Its PostgreSQL and Redis services have no published host ports, and its volume/network names are Quizizzo-specific. Redis is an ephemeral SignalR backplane and must use a separate strong `QUIZIZZO_REDIS_PASSWORD`. The existing `logiagraph.com` site is protected: Quizizzo deployment must not stop or recreate its containers, change its existing proxy route, reuse its volumes, or take over its host ports.

## Server inventory

- Hetzner server: `ubuntu-8gb-hel1-1`
- Location: Helsinki
- Public IPv4: `77.42.77.85`
- Public IPv6 allocation: `2a01:4f9:c013:91eb::/64`
- Current IPv4 reverse DNS: `static.85.77.42.77.clients.your-server.de`

The IPv6 value is the allocated `/64` prefix shown by Hetzner, not a confirmed individual host address. Confirm the server's selected IPv6 address before creating an `AAAA` record or firewall rule.

### Verified host baseline (2026-08-30)

- Ubuntu 24.04.3 LTS, Docker 29.1.4, and Docker Compose 5.0.1.
- Nginx is the host reverse proxy; Logiagraph uses `/etc/nginx/sites-enabled/logiagraph` and proxies to port `8080`.
- The existing `logiagraph` Compose project has healthy `app`, `postgres`, and `redis` containers on its own network.
- Port `8081` is available for Quizizzo's loopback-only binding and the root filesystem has approximately 132 GB free.
- UFW is currently inactive. The existing Logiagraph app and PostgreSQL ports are bound on all IPv4/IPv6 interfaces; this is a pre-existing condition and must not be changed as part of a Quizizzo deployment.
- The restricted `quizizzo-deploy` account can authenticate only with the dedicated Quizizzo key and has no unrestricted sudo, direct Docker access, or write access to `/opt/quizizzo`.
- `/opt/quizizzo/compose.yaml` is root-owned; `/opt/quizizzo/.env` is root-only and pins the immutable GHCR image, production environment, allowed hosts, loopback port, and generated database/Redis secrets.
- The immutable GHCR package is publicly readable, so the VPS requires no registry credential.
- The root-owned `/usr/local/sbin/quizizzo-ops` script and exact sudo policy permit `quizizzo-deploy` to run only bounded `preflight`, `backup`, immutable `deploy`, and immutable `rollback` operations; arbitrary shell and Docker sudo remain denied.
- A real custom-format backup in `/opt/quizizzo/backups` passed checksum and `pg_restore --list` verification while all protected Logiagraph containers remained healthy.
- The protected GitHub `production` environment requires approval and stores only the dedicated `quizizzo-deploy` key. Its host key is pinned rather than discovered during a deployment.
- Quizizzo has a separate Cloudflare Origin CA certificate covering `quizizzo.com` and `*.quizizzo.com`; its private key is root-owned with mode `600` and is not stored in the repository.
- `/etc/nginx/sites-enabled/quizizzo` uses the repository template at `scripts/deployment/quizizzo.nginx.conf`, proxies only `quizizzo.com` to loopback port `8081`, and permanently redirects `www.quizizzo.com` to the apex while preserving path and query.
- Cloudflare proxies the apex and `www` records to `77.42.77.85` in Full (strict) mode. Public apex live/ready checks and the `www` redirect were verified after cutover while the Logiagraph configuration and container identities remained unchanged.

## Host-based reverse proxy

Set `QUIZIZZO_ASPNETCORE_ENVIRONMENT=Production`, `QUIZIZZO_ALLOWED_HOSTS` to the exact new Quizizzo hostname, and an unused loopback port in the VPS `.env`, for example `QUIZIZZO_HTTP_PORT=8081`. Build or pull the immutable Quizizzo image, back up the Quizizzo database volume, apply migrations explicitly, and start only the Quizizzo services:

```text
docker compose --project-name quizizzo build quizizzo
docker compose --project-name quizizzo up -d postgres
docker compose --project-name quizizzo run --rm migrate
docker compose --project-name quizizzo up -d quizizzo
```

Point only the new Quizizzo hostname in the existing Nginx/Caddy configuration to `http://127.0.0.1:8081`. Keep the `logiagraph.com` hostname, TLS configuration, and upstream unchanged. Forward the original scheme and client address. Quizizzo trusts one forwarded-header hop because its published port is loopback-only; do not expose that port on all host interfaces. Verify `/health/live` and `/health/ready` before enabling public traffic.

SignalR requires the new Quizizzo route to support WebSocket upgrades. For Nginx, set `proxy_http_version 1.1`, forward `Upgrade` and `Connection`, and keep a suitably long `proxy_read_timeout` inside only the Quizizzo `server` block. Caddy's standard `reverse_proxy 127.0.0.1:8081` handles WebSocket upgrades automatically. These settings do not belong in, and must not change, the existing `logiagraph.com` route.

## Containerized reverse proxy

If the existing reverse proxy is a container, discover its existing external Docker network without changing it. Set `QUIZIZZO_PROXY_NETWORK` to that exact network name. Use the same explicit build, PostgreSQL, and migration steps above, adding both Compose files to each command; then start the Quizizzo web service with:

```text
docker compose --project-name quizizzo -f compose.yaml -f compose.proxy.yaml up -d quizizzo
```

Add a separate Quizizzo hostname route to `http://quizizzo-web:8080` from that proxy. Do not edit the `logiagraph.com` router/service definition. The override only attaches the Quizizzo web service; PostgreSQL remains on its private network.

Whichever proxy is used, verify the browser can negotiate `/hubs/party` and upgrade to WebSockets after deployment. Long polling remains a fallback, but a working WebSocket route is the production target.

## Safe operations

- Use `docker compose --project-name quizizzo ...` for every Quizizzo lifecycle command.
- Capture `docker ps`, the existing proxy configuration, and the `logiagraph.com` health response before deployment; compare them after deployment.
- Inspect the rendered model first with `docker compose --project-name quizizzo config`.
- Run migrations only through `docker compose --project-name quizizzo run --rm migrate`; the ordinary web entry point never changes the schema.
- Never run global `docker system prune`, broad container stop/remove commands, or reuse another stack's volume.
- Do not publish PostgreSQL port 5432 or Redis port 6379 on the VPS.
- Back up `quizizzo-postgres-data` before migrations. Preserve `quizizzo-data-protection` across deployments so authentication and anonymous-session protection remain stable. Drawing assets expire after one day, so back up `quizizzo-drawing-assets` only when short-lived in-progress animations must survive a host recovery.
- A rollback changes only the Quizizzo image tag and stack; it must not modify `logiagraph.com` containers or its proxy route.
