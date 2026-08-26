# Hetzner deployment alongside an existing website

Quizizzo is isolated as the Compose project `quizizzo`. Its PostgreSQL service has no published host port, and its volume/network names are Quizizzo-specific. The existing `logiagraph.com` site is protected: Quizizzo deployment must not stop or recreate its containers, change its existing proxy route, reuse its volumes, or take over its host ports.

## Host-based reverse proxy

Set an unused loopback port in `.env`, for example `QUIZIZZO_HTTP_PORT=8081`, then run only:

```text
docker compose --project-name quizizzo up -d --build
```

Point only the new Quizizzo hostname in the existing Nginx/Caddy configuration to `http://127.0.0.1:8081`. Keep the `logiagraph.com` hostname, TLS configuration, and upstream unchanged. Verify `/health/live` and `/health/ready` before enabling public traffic.

## Containerized reverse proxy

If the existing reverse proxy is a container, discover its existing external Docker network without changing it. Set `QUIZIZZO_PROXY_NETWORK` to that exact network name and run:

```text
docker compose --project-name quizizzo -f compose.yaml -f compose.proxy.yaml up -d --build
```

Add a separate Quizizzo hostname route to `http://quizizzo-web:8080` from that proxy. Do not edit the `logiagraph.com` router/service definition. The override only attaches the Quizizzo web service; PostgreSQL remains on its private network.

## Safe operations

- Use `docker compose --project-name quizizzo ...` for every Quizizzo lifecycle command.
- Capture `docker ps`, the existing proxy configuration, and the `logiagraph.com` health response before deployment; compare them after deployment.
- Inspect the rendered model first with `docker compose --project-name quizizzo config`.
- Never run global `docker system prune`, broad container stop/remove commands, or reuse another stack's volume.
- Do not publish PostgreSQL port 5432 on the VPS.
- Back up `quizizzo-postgres-data` before migrations and deploy immutable image tags.
- A rollback changes only the Quizizzo image tag and stack; it must not modify `logiagraph.com` containers or its proxy route.
