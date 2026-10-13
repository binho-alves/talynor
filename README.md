# Talynor

Talynor is a visual learning and personal knowledge platform where users build interconnected knowledge maps, organize learning content, and track their studies.

**Current status:** foundation, Phase 1 — SPIKE-001, Phase 2 — Solution and Application Skeleton, VS-001, Phase 3.1 — Persistence Foundation, and **3.2A — Session HTTP Foundation and CSRF Bootstrap** are completed and approved; VS-001, 3.1, and 3.2A are published. **3.2B.1 — Account Registration** is completed, approved, and published: anonymous `POST /api/auth/register` creates local Identity accounts with native CSRF protection and registration-only rate limiting, without auto-login or an auth cookie. **3.2B.2A — Security Stamp Compatibility** is the current slice, implemented and pending final review and publication. Login, logout, `/me`, current user, and Workspace are not implemented. React still reads the two in-memory demonstration maps through the .NET API; frontend behavior, progress, and study time are unchanged.

See [Development](docs/DEVELOPMENT.md#phase-31---identity-persistence) for local EF tooling, database configuration, and explicit migration commands. Build and API startup do not create or migrate the database.

See [the 3.2A development procedure](docs/DEVELOPMENT.md#phase-32a---session-http-foundation-and-csrf-bootstrap) for HTTPS and the CSRF bootstrap, and [registration](docs/DEVELOPMENT.md#phase-32b1---account-registration) for safe verification. Account registration exists; session workflows remain reserved for 3.2B.2B.

See [the frontend spike](src/frontend/talynor-web/README.md) for local commands, interactions, limitations, and React learning notes.

## Documentation

- [Product vision and MVP](docs/PRODUCT.md)
- [Approved high-level architecture](docs/ARCHITECTURE.md)
- [Preliminary roadmap](docs/ROADMAP.md)
- [Architecture decision record index](docs/DECISIONS.md)
- [Secure-development principles](docs/SECURITY.md)
- [Development workflow](docs/DEVELOPMENT.md)
- [Coding agent operating contract](AGENTS.md)
