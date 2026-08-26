# ADR-0006: Drawing asset storage abstraction

Status: Accepted

Drawing metadata and references live in PostgreSQL while bytes are handled by `IDrawingAssetStore`. A persistent filesystem implementation can serve MVP deployment and later be replaced by object storage. This adds an interface and metadata model but avoids oversized base64 database fields and game-to-storage coupling.
