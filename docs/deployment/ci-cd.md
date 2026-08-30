# CI/CD delivery stages

Quizizzo delivery is introduced in bounded stages. A later stage must not weaken an earlier stage's checks or gain deployment authority before its own verification is complete.

## Stage 1 — Continuous integration

`.github/workflows/ci.yml` runs for pull requests, pushes to `main`, and manual dispatches. It has read-only repository permission and does not receive deployment secrets.

The quality job performs a clean npm install and vulnerability audit, builds the locally served browser dependencies, runs client tests, validates JavaScript syntax, restores the pinned .NET 10 SDK solution, verifies analyzer-backed code style, builds Release with CI warnings treated as errors, and runs the complete .NET test suite. Only after that job passes does a separate job build the production Docker image and verify its configured runtime user is the non-root `app` user. The style-only formatter mode intentionally avoids rewriting the repository's pre-existing LF/CRLF and generated-migration encoding baseline.

Stage 1 does not publish an image, connect to Hetzner, run migrations, mutate DNS, or restart any service.

## Stage 2 — Immutable GHCR publishing

After the complete Stage 1 quality job succeeds, the container job always builds and verifies the production image. For a push to `main` only, it then authenticates to GHCR with the workflow-scoped `GITHUB_TOKEN` and publishes:

```text
ghcr.io/joshuaholden/quizizzo:sha-<full-40-character-commit-sha>
```

Pull requests and manual workflow runs build but never authenticate or publish. The job has `packages: write` only; the workflow has no SSH key, Hetzner secret, production environment access, migration step, or deployment command. No moving `latest` or environment tag is published, so every deployable reference identifies one source commit. The image also records the source repository and revision as OCI labels.

The first successful `main` publication creates the package if necessary. In the GitHub package settings, keep the package linked to this repository and choose its desired visibility. Hetzner will eventually authenticate with a separate read-only package credential if the package remains private; that credential does not belong in this publishing job.

## Stage 3 — Hetzner deployment preparation

Completed 2026-08-30. The `quizizzo-deploy` account uses a dedicated SSH key and has no direct Docker access, unrestricted sudo, or write access to `/opt/quizizzo`. Root owns the validated Compose file and mode-600 production environment. The immutable GHCR image is publicly readable, so no package credential is stored on the VPS.

The root-owned `scripts/deployment/quizizzo-ops` command exposes two bounded Stage 3 operations:

- `preflight` validates ownership, secret permissions, immutable image naming and availability, Compose configuration, Nginx configuration, disk capacity, port `8081`, and the protected Logiagraph containers.
- `backup` requires healthy Quizizzo PostgreSQL, writes a timestamped custom-format `pg_dump` through a temporary file, and records a SHA-256 checksum under root-only `/opt/quizizzo/backups`.

At the Stage 3 proof point, the sudo policy permitted `quizizzo-deploy` to invoke only those exact subcommands and GitHub held no Hetzner credential. Stage 4 later extended the same root-owned command with bounded immutable deploy and rollback operations; arbitrary Docker, shell, editor, and file-copy authority remain denied.

The manual proof passed through the restricted account. Preflight validated the immutable image, Nginx, Compose model, port, disk, and protected containers. Only private Quizizzo PostgreSQL was started; `backup` created a root-only custom-format archive and checksum, `sha256sum` verified it, and PostgreSQL `pg_restore --list` read it successfully. Logiagraph's app, PostgreSQL, and Redis containers remained healthy and unchanged throughout.

## Stage 4 — Controlled production deployment

Completed 2026-08-30. The GitHub `production` environment requires approval by `JoshuaHolden` and holds the dedicated deployment private key. Host, user, and the pinned ED25519 SSH host key are environment variables. Repository and CI jobs have no access to the deployment identity.

`.github/workflows/deploy.yml` is manual-only and requires a `deploy` or `rollback` choice plus a full lowercase commit SHA. It uses strict host-key checking and invokes only the root-owned operation allowed by the server sudo policy. The server independently validates the resulting `ghcr.io/joshuaholden/quizizzo:sha-<40 hex>` reference.

`deploy` re-runs preflight, pulls one immutable image, starts the private dependencies, creates a database backup, runs the explicit one-shot migration service, replaces only Quizizzo web, and requires both loopback live and ready health checks before recording the release. It also verifies that Docker is running the exact requested image reference; every later preflight rejects drift between the recorded and running images. `rollback` creates another backup, replaces only Quizizzo web without attempting to reverse database migrations, and applies the same health, exact-image, and protected-container checks. Neither operation prunes Docker or runs Compose outside the `quizizzo` project.

Protected run `33332467338` deployed `ghcr.io/joshuaholden/quizizzo:sha-40f726b602276e8e0e7492467a62989e4fc0caff`. Independent server verification proved that the configured and running references match exactly, both loopback health endpoints return healthy, all six migrations are recorded, the release backup checksum passes, and the three `logiagraph` containers remain healthy in their original Compose project.

Public HTTPS verification remains pending. `quizizzo.com` already uses Cloudflare DNS, but the VPS currently has only a Cloudflare origin certificate for `logiagraph.com`; that certificate must never be reused for Quizizzo. Install a separate Quizizzo origin certificate and Nginx site before enabling or testing the public route.

## Stage 5 — Rollback proof

Pending. Record the previously healthy immutable image, verify a rollback command that targets only the Quizizzo Compose project, and document database compatibility limits. A failed health check must stop the workflow and present the explicit rollback action rather than making unrelated server changes.
