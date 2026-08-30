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

The sudo policy permits `quizizzo-deploy` to invoke only those exact subcommands. It does not yet grant deploy, migration, rollback, arbitrary Docker, shell, editor, or file-copy authority. GitHub still has no Hetzner credential.

The manual proof passed through the restricted account. Preflight validated the immutable image, Nginx, Compose model, port, disk, and protected containers. Only private Quizizzo PostgreSQL was started; `backup` created a root-only custom-format archive and checksum, `sha256sum` verified it, and PostgreSQL `pg_restore --list` read it successfully. Logiagraph's app, PostgreSQL, and Redis containers remained healthy and unchanged throughout.

## Stage 4 — Controlled production deployment

Pending. A protected GitHub production environment will require approval before connecting to Hetzner. Deployment will pull one immutable SHA image, back up Quizizzo data, run the one-shot migration service, replace only the Quizizzo web service, and verify live and ready health endpoints plus the public HTTPS route. It must never prune Docker or alter the existing `logiagraph.com` stack.

## Stage 5 — Rollback proof

Pending. Record the previously healthy immutable image, verify a rollback command that targets only the Quizizzo Compose project, and document database compatibility limits. A failed health check must stop the workflow and present the explicit rollback action rather than making unrelated server changes.
