# Talynor

Talynor is a visual learning and personal knowledge platform where users build interconnected knowledge maps, organize learning content, and track their studies.

**Current status:** Phase 3 — Identity, Session, Workspace and Authenticated Frontend is **COMPLETE**. Foundation, Phases 1 and 2, VS-001, and Phase 3.1 through 3.4 are completed, approved, and published. Phase 3.5 — Validation and Phase Closure is validated and completed.

Phase 3 delivers local account registration, cookie-based sessions with CSRF, login/me/logout, native lockout and process-local rate limiting, Security Stamp compatibility, one persistent personal workspace per user, and the authenticated React frontend. Real-browser HTTPS, cookie, session, graph, responsive-layout, and accessibility smoke validation passed. Knowledge Maps remain anonymous, global in-memory demos; they are not private workspace content.

**Next:** Phase 4 — Persistent Knowledge Authoring, not started. Persisted maps, nodes, edges, positions, workspace ownership, authoring flows, and persistent submap navigation require a separately scoped implementation task. They are not implemented yet. See [the roadmap](docs/ROADMAP.md#phase-4---persistent-knowledge-authoring).

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
