# Talynor

Talynor is a visual learning and personal knowledge platform where users build interconnected knowledge maps, organize learning content, and track their studies.

**Current status:** foundation, Phase 1 — SPIKE-001, Phase 2 — Solution and Application Skeleton, and VS-001 — Read-only Knowledge Graph Vertical Slice are completed and approved; VS-001 is published. Phase 3 — Identity and Workspace has started. Its current slice, 3.1 — Persistence Foundation, implements role-free Identity persistence with EF Core/SQLite, an explicit migration, and persistence tests, pending review. HTTP authentication, sessions, and workspace are not implemented. React still reads the two in-memory demonstration maps through the .NET API and adapts the independent HTTP contract to XYFlow; progress and study time remain demonstration values.

See [Development](docs/DEVELOPMENT.md#phase-31---identity-persistence) for local EF tooling, database configuration, and explicit migration commands. Build and API startup do not create or migrate the database.

See [the frontend spike](src/frontend/talynor-web/README.md) for local commands, interactions, limitations, and React learning notes.

## Documentation

- [Product vision and MVP](docs/PRODUCT.md)
- [Approved high-level architecture](docs/ARCHITECTURE.md)
- [Preliminary roadmap](docs/ROADMAP.md)
- [Architecture decision record index](docs/DECISIONS.md)
- [Secure-development principles](docs/SECURITY.md)
- [Development workflow](docs/DEVELOPMENT.md)
- [Coding agent operating contract](AGENTS.md)
