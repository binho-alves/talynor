# Talynor

Talynor is a visual learning and personal knowledge platform where users build interconnected knowledge maps, organize learning content, and track their studies.

**Current status:** foundation, Phase 1 — SPIKE-001, Phase 2 — Solution and Application Skeleton, VS-001, and Phase 3.1 — Persistence Foundation are completed and approved; VS-001 and 3.1 are published. Phase 3.2 has started with **3.2A — Session HTTP Foundation and CSRF Bootstrap**, implemented and pending review. It composes Identity services/stores, application-cookie and authorization infrastructure, and native antiforgery with anonymous `GET /api/auth/csrf`. Register, login, logout, `/me`, and Workspace are not implemented. React still reads the two in-memory demonstration maps through the .NET API; frontend behavior, progress, and study time are unchanged.

See [Development](docs/DEVELOPMENT.md#phase-31---identity-persistence) for local EF tooling, database configuration, and explicit migration commands. Build and API startup do not create or migrate the database.

See [the 3.2A development procedure](docs/DEVELOPMENT.md#phase-32a---session-http-foundation-and-csrf-bootstrap) for HTTPS and the CSRF bootstrap. No account/session workflow is available yet.

See [the frontend spike](src/frontend/talynor-web/README.md) for local commands, interactions, limitations, and React learning notes.

## Documentation

- [Product vision and MVP](docs/PRODUCT.md)
- [Approved high-level architecture](docs/ARCHITECTURE.md)
- [Preliminary roadmap](docs/ROADMAP.md)
- [Architecture decision record index](docs/DECISIONS.md)
- [Secure-development principles](docs/SECURITY.md)
- [Development workflow](docs/DEVELOPMENT.md)
- [Coding agent operating contract](AGENTS.md)
