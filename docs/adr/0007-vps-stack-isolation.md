# ADR-0007: Coexisting Docker stacks on one VPS

Status: Accepted

Quizizzo uses the fixed Compose project name `quizizzo`, uniquely named private network and database volume, no host-published PostgreSQL port, and a configurable loopback-only application port. An existing containerized reverse proxy may instead join Quizizzo through an explicitly selected external network override. Operational commands target only this project; global prune and commands that enumerate and stop unrelated containers are forbidden. This needs one free loopback port or an existing proxy network, but prevents resource-name and port collisions with other sites.
