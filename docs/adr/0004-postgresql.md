# ADR-0004: PostgreSQL persistence

Status: Accepted (supersedes the initial SQL Server choice before production data existed)

PostgreSQL and EF Core migrations are used for Identity and durable product data through Npgsql. PostgreSQL runs in a dedicated Compose service with its own volume and private network. This keeps local and production behavior aligned and has a modest VPS footprint, at the cost of requiring PostgreSQL for full integration testing and replacing the original provider-specific migration.
