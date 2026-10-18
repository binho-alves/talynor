# Talynor

Talynor is a visual learning and personal knowledge platform where users build interconnected knowledge maps, organize learning content, and track their studies.

**Current status:** Phase 5 — Knowledge Content is **COMPLETE / APPROVED / PUBLISHED** for the agreed Note and Link scope under [ADR-013](docs/DECISIONS.md#adr-013---initial-node-owned-knowledge-content). Foundation/read (c450b91), Content Authoring Backend (5aa7d4e) and Frontend Content Integration (d4e5c76) are published. Private node-owned content supports viewing, creation, editing and confirmed deletion through the API and existing Edit map experience. This scoped completion does not imply production readiness.

**Current implementation phase:** Phase 6 — Study Sessions is **IN PROGRESS** under [ADR-014](docs/DECISIONS.md#adr-014---study-session-ownership-lifecycle-and-active-session-invariant). Checkpoint 1 is implemented / under review: node-owned StudySession foundation, explicit EF/SQLite migration, authenticated completed-history list and detail reads, and node/map delete protection. Duration/status are derived and completion means only an ended time record. Start/complete/discard, active lookup, the transactional one-active invariant and all Study Session frontend behavior remain unimplemented and require a later scoped task. Phase 6 is not complete or published.

**Published Phase 4:** Phase 4 — Persistent Knowledge Authoring is **COMPLETE / APPROVED / PUBLISHED**. Its approved slices 4.1, 4.2, 4.3A, 4.3 and 4.4 deliver private persistent maps, discovery, navigation and authoring. This is scoped phase completion, not production readiness.

**Previous foundation:** Phase 3 — Identity, Session, Workspace and Authenticated Frontend is complete. Foundation, Phases 1 and 2, VS-001, and Phase 3.1 through 3.4 are completed, approved, and published. Phase 3.5 — Validation and Phase Closure is validated and completed.

Phase 3 delivers local account registration, cookie-based sessions with CSRF, login/me/logout, native lockout and process-local rate limiting, Security Stamp compatibility, one persistent personal workspace per user, and the authenticated React frontend. Real-browser HTTPS, cookie, session, graph, responsive-layout, and accessibility smoke validation passed. At Phase 3 closure the frontend consumed anonymous global demo maps; Phase 4.3 replaces that authenticated experience with private persisted maps.

**Published foundation:** Phase 4.1 — Knowledge Map Persistence Foundation is complete, approved and published. The private persisted read model, explicit migration and integrity rules are recorded in [ADR-011](docs/DECISIONS.md#adr-011---persistent-knowledge-graph-foundation). Demos remain separate; startup does not seed maps.

**Published authoring:** Phase 4.2 — Map/Node/Edge Authoring Backend is complete, approved and published. Private nested create/PATCH/delete, node positions/ChildMap set-clear and edge create/delete enforce session-derived ownership, native CSRF and controlled conflicts. See [ADR-012](docs/DECISIONS.md#adr-012---knowledge-graph-authoring-api) and [Development](docs/DEVELOPMENT.md#phase-42---mapnodeedge-authoring-backend).

**Published discovery:** Phase 4.3A — Persisted Map Discovery is complete, approved and published. Authenticated `GET /api/maps` returns only current-workspace map summaries as `{ "maps": [{ "id": "...", "title": "..." }] }`, including an empty maps array when appropriate. There is no root/default map or automatic selection.

**Published frontend:** Phase 4.3 — Persistent Graph Frontend Integration is complete, approved and published. Session/workspace bootstrap, explicit discovery/picker, private persisted rendering, ChildMap and native URL/History navigation are preserved.

**Published authoring UX:** Phase 4.4 — Authoring UX is complete, approved and published in `c427c6e`; Phase 4.3 was published in `f19c3d5`. Explicit edit mode adds map/node CRUD, ChildMap set/clear, edge creation/deletion and position saving on drag stop through the published API. Writes use existing session CSRF, explicit retries and pessimistic creates/deletes. Advanced authoring, layout and broader validation remain separately scoped limitations, not unmet Phase 4 completion requirements.

**Phase 5 boundary:** Note + Link, node ownership and initial lifecycle/validation rules are explicitly approved in ADR-013. Authenticated list/detail reads remain available; content writes use existing CSRF, server-derived ownership, strict own JSON contracts and a 32 KiB request-size configuration. Note/Link content UI uses literal text, explicit safe links, existing CSRF and manual retries. Files, rich rendering and study features remain absent. File upload/storage remains future work and was not required by the agreed Note/Link delivery. Graph contracts and map URL/History semantics are preserved.

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
