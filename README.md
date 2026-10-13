# Talynor

Talynor is a visual learning and personal knowledge platform where users build interconnected knowledge maps, organize learning content, and track their studies.

**Current status:** foundation, Phases 1 and 2, VS-001, Phase 3.1, and Phase 3.2 — Identity and Session Backend are completed, approved, and published. Phase 3.3 — Personal Workspace and Ownership is implemented and pending review: one persistent personal workspace per authenticated user, created lazily and idempotently through `GET /api/workspace`. Register/login remain independent of workspace creation. The React frontend and the anonymous, global in-memory demonstration maps are unchanged; frontend authentication remains future 3.4.

See [Development](docs/DEVELOPMENT.md#phase-31---identity-persistence) for local EF tooling, database configuration, and explicit migration commands. Build and API startup do not create or migrate the database.

See [the 3.2A development procedure](docs/DEVELOPMENT.md#phase-32a---session-http-foundation-and-csrf-bootstrap) for HTTPS and the CSRF bootstrap, [registration](docs/DEVELOPMENT.md#phase-32b1---account-registration), and [Session Flow](docs/DEVELOPMENT.md#phase-32b2b---session-flow) for safe verification. See [Personal Workspace](docs/DEVELOPMENT.md#phase-33---personal-workspace-and-ownership) for explicit migration and authenticated endpoint verification. Phase 3.4 — Authenticated Frontend remains future work.

See [the frontend spike](src/frontend/talynor-web/README.md) for local commands, interactions, limitations, and React learning notes.

## Documentation

- [Product vision and MVP](docs/PRODUCT.md)
- [Approved high-level architecture](docs/ARCHITECTURE.md)
- [Preliminary roadmap](docs/ROADMAP.md)
- [Architecture decision record index](docs/DECISIONS.md)
- [Secure-development principles](docs/SECURITY.md)
- [Development workflow](docs/DEVELOPMENT.md)
- [Coding agent operating contract](AGENTS.md)
