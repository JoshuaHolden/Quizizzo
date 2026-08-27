# ADR-0006: Drawing asset storage abstraction

Status: Accepted

Drawing metadata and references live in PostgreSQL while bytes are handled by the Application-level `IDrawingAssetStore`. The MVP `FileSystemDrawingAssetStore` accepts bounded WebP or PNG assets, creates opaque validated keys, and writes atomically into a persistent, Quizizzo-specific Compose volume. It rejects unsupported types, oversized assets, untrusted keys, and path traversal.

Drawing assets have a configurable time-to-live of one day by default. References expose their UTC creation and expiry times, and an hourly background sweep deletes expired bytes and empty shard directories. Submission metadata introduced with the game flow must carry the same expiry and be deleted after expiration rather than accumulating in PostgreSQL.

Game modules never receive a physical path and do not depend on Infrastructure. A future S3/R2-compatible adapter can replace the filesystem implementation without changing game rules or storing oversized base64 fields in PostgreSQL. The trade-off is that the MVP volume needs an explicit backup strategy and is tied to one application host until an object-store adapter is introduced.
