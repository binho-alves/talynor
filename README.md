# Talynor

Talynor is a visual learning and personal knowledge platform where users build interconnected knowledge maps, organize learning content, and track their studies.

**Current implementation — Phase 9 In Progress:** CP0 baseline is published; CP1 read foundation is Published in 7c4069d; CP2 API integration is Implemented / under review; CP3–CP4 are Not started. [Accepted ADR-017](docs/DECISIONS.md#adr-017---derived-node-activity-and-results-summary) defines a minimal node-level activity/results view from existing Study Sessions, Flashcard reviews and Quiz attempts. It defines no new persisted progress state, completion score or retention claim. See [scope, checkpoints and acceptance](docs/DEVELOPMENT.md#phase-9---progress-and-analytics-design-baseline). Phase 8 remains Complete / Published.

**Current status — Phase 8 Complete / Published for approved scope:** [ADR-016](docs/DECISIONS.md#adr-016---node-owned-quizzes-revision-scoped-grading-and-immutable-attempts) remains Accepted. CP0 (39ff3aa), CP1 (4b6dec0), CP2 (e17aa97) and CP3 Frontend authoring + answering/history (84e01b4) are Published. CP4 integrated validation is Completed / Validated / Published on 2026-10-22; closure documentation was formally approved and published in 80bbae3. Delivered scope includes node-owned single-choice quiz authoring, key-free answering, server grading, immutable results/history and explicit same-AttemptId reconciliation. StudySession stays independent; accuracy is not mastery or node completion. Phase 9 CP0 baseline is published under Accepted ADR-017; CP1 read foundation is Published in 7c4069d; CP2 API integration is Implemented / under review; CP3–CP4 are Not started. See [checkpoints/evidence](docs/DEVELOPMENT.md#phase-8---assessments-design-baseline). Phases 1-7 are complete and published; Phase 7 closure is bc7a4b6.

**Published Phase 5:** Phase 5 — Knowledge Content is **COMPLETE / APPROVED / PUBLISHED** for the agreed Note and Link scope under [ADR-013](docs/DECISIONS.md#adr-013---initial-node-owned-knowledge-content). Foundation/read (c450b91), Content Authoring Backend (5aa7d4e) and Frontend Content Integration (d4e5c76) are published. Private node-owned content supports viewing, creation, editing and confirmed deletion through the API and existing Edit map experience. This scoped completion does not imply production readiness.

**Published Phase 6:** Phase 6 — Study Sessions is **COMPLETE / APPROVED / PUBLISHED** for the scoped node-owned study-period workflow under [ADR-014](docs/DECISIONS.md#adr-014---study-session-ownership-lifecycle-and-active-session-invariant). Checkpoint 1 foundation/read and delete protection (881d12a), checkpoint 2 lifecycle/active backend (29ba218) and checkpoint 3 frontend (493d39a) are approved and published. Users can start, recover, complete or explicitly discard a study period, return to its concept, and read/paginate/delete completed history. The timer is visual and server-anchored; logout does not end a study period. Existing ownership, native CSRF and no-store protections apply, without automatic mutation replay. Integrated closure was approved and published in 6045391 after validation on 2026-10-19. Completion means only an ended time record and scoped delivery, not learning mastery or production readiness.

**Published Phase 7:** the accepted design baseline in [ADR-015](docs/DECISIONS.md#adr-015---node-owned-flashcards-and-review-progression) and Checkpoints 1 (f375141), 2 (8af676d) and 3 (f58d0fe) are published. Checkpoint 4 integrated validation completed on 2026-10-21. The approved scope delivers private node-owned Flashcard CRUD, persisted revision-scoped progress/events, deterministic continuous study, explicit Reveal and same-ID recovery without automatic replay. ADR-015 remains Accepted. Phase 7 closure was formally approved and published in bc7a4b6; this is scoped completion, not production readiness. Phase 8 is Complete / Published for the approved scope under ADR-016; CP1 is Published in 4b6dec0, CP2 in e17aa97, CP3 in 84e01b4 and CP4 is Completed / Validated / Published in 80bbae3.

**Published Phase 4:** Phase 4 — Persistent Knowledge Authoring is **COMPLETE / APPROVED / PUBLISHED**. Its approved slices 4.1, 4.2, 4.3A, 4.3 and 4.4 deliver private persistent maps, discovery, navigation and authoring. This is scoped phase completion, not production readiness.

**Previous foundation:** Phase 3 — Identity, Session, Workspace and Authenticated Frontend is complete. Foundation, Phases 1 and 2, VS-001, and Phase 3.1 through 3.4 are completed, approved, and published. Phase 3.5 — Validation and Phase Closure is validated and completed.

Phase 3 delivers local account registration, cookie-based sessions with CSRF, login/me/logout, native lockout and process-local rate limiting, Security Stamp compatibility, one persistent personal workspace per user, and the authenticated React frontend. Real-browser HTTPS, cookie, session, graph, responsive-layout, and accessibility smoke validation passed. At Phase 3 closure the frontend consumed anonymous global demo maps; Phase 4.3 replaces that authenticated experience with private persisted maps.

**Published foundation:** Phase 4.1 — Knowledge Map Persistence Foundation is complete, approved and published. The private persisted read model, explicit migration and integrity rules are recorded in [ADR-011](docs/DECISIONS.md#adr-011---persistent-knowledge-graph-foundation). Demos remain separate; startup does not seed maps.

**Published authoring:** Phase 4.2 — Map/Node/Edge Authoring Backend is complete, approved and published. Private nested create/PATCH/delete, node positions/ChildMap set-clear and edge create/delete enforce session-derived ownership, native CSRF and controlled conflicts. See [ADR-012](docs/DECISIONS.md#adr-012---knowledge-graph-authoring-api) and [Development](docs/DEVELOPMENT.md#phase-42---mapnodeedge-authoring-backend).

**Published discovery:** Phase 4.3A — Persisted Map Discovery is complete, approved and published. Authenticated `GET /api/maps` returns only current-workspace map summaries as `{ "maps": [{ "id": "...", "title": "..." }] }`, including an empty maps array when appropriate. There is no root/default map or automatic selection.

**Published frontend:** Phase 4.3 — Persistent Graph Frontend Integration is complete, approved and published. Session/workspace bootstrap, explicit discovery/picker, private persisted rendering, ChildMap and native URL/History navigation are preserved.

**Published authoring UX:** Phase 4.4 — Authoring UX is complete, approved and published in `c427c6e`; Phase 4.3 was published in `f19c3d5`. Explicit edit mode adds map/node CRUD, ChildMap set/clear, edge creation/deletion and position saving on drag stop through the published API. Writes use existing session CSRF, explicit retries and pessimistic creates/deletes. Advanced authoring, layout and broader validation remain separately scoped limitations, not unmet Phase 4 completion requirements.

**Phase 5 boundary:** Note + Link, node ownership and initial lifecycle/validation rules are explicitly approved in ADR-013. Authenticated list/detail reads remain available; content writes use existing CSRF, server-derived ownership, strict own JSON contracts and a 32 KiB request-size configuration. Note/Link content UI uses literal text, explicit safe links, existing CSRF and manual retries. Files and rich rendering remain absent; Study Sessions are delivered separately by Phase 6. File upload/storage remains future work and was not required by the agreed Note/Link delivery. Graph contracts and map URL/History semantics are preserved.

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
