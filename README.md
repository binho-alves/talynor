# Talynor

Talynor is a visual learning and personal knowledge platform where users build interconnected knowledge maps, organize learning content, and track their studies.

**Current status:** foundation, Phases 1 and 2, VS-001, Phase 3.1, Phase 3.2 — Identity and Session Backend, and Phase 3.3 — Personal Workspace and Ownership are completed, approved, and published. Phase 3.4 — Authenticated Frontend is implemented and pending review: session bootstrap, login, workspace loading, and logout using the existing HttpOnly cookie and in-memory CSRF. Knowledge Maps remain anonymous, global in-memory demos behind the frontend session gate. Phase 3.5 — Validation / Phase Closure remains future work.

See [Development](docs/DEVELOPMENT.md#phase-31---identity-persistence) for local EF tooling, database configuration, and explicit migration commands. Build and API startup do not create or migrate the database.

See [the 3.2A development procedure](docs/DEVELOPMENT.md#phase-32a---session-http-foundation-and-csrf-bootstrap) for HTTPS and the CSRF bootstrap, [registration](docs/DEVELOPMENT.md#phase-32b1---account-registration), and [Session Flow](docs/DEVELOPMENT.md#phase-32b2b---session-flow) for safe verification. See [Personal Workspace](docs/DEVELOPMENT.md#phase-33---personal-workspace-and-ownership) for explicit migration and authenticated endpoint verification, and [Authenticated Frontend](docs/DEVELOPMENT.md#phase-34---authenticated-frontend) for trusted browser/proxy HTTPS and login verification.

See [the frontend spike](src/frontend/talynor-web/README.md) for local commands, interactions, limitations, and React learning notes.

## Documentation

- [Product vision and MVP](docs/PRODUCT.md)
- [Approved high-level architecture](docs/ARCHITECTURE.md)
- [Preliminary roadmap](docs/ROADMAP.md)
- [Architecture decision record index](docs/DECISIONS.md)
- [Secure-development principles](docs/SECURITY.md)
- [Development workflow](docs/DEVELOPMENT.md)
- [Coding agent operating contract](AGENTS.md)
