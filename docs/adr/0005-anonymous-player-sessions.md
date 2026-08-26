# ADR-0005: Anonymous durable player sessions

Status: Accepted

Players do not create accounts. A cryptographically secure application credential maps to a durable player record; only a hash is stored where practical. SignalR connection IDs remain disposable transport metadata. Credential rotation/recovery adds complexity but enables refresh, sleep, and network-switch recovery.
