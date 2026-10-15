# Preliminary roadmap

This roadmap defines phase-level goals and outputs, not detailed implementation tickets or delivery dates. Each phase requires a scoped task before implementation. Phases 0, 1, and 2 are completed and approved. VS-001 is a separately authorized validation slice between Phase 2 and Phase 3; it does not replace Identity and Workspace or renumber any phase.

## Phase 0 - Foundation

**Goal:** establish product direction, architectural boundaries, and permanent agent operating rules.

**Expected outputs:** README, AGENTS operating contract, and product, architecture, roadmap, decision, security, and development documents.

**Non-goals:** application code, .NET solution, React application, package installation, Docker, database configuration, migrations, CI, Git initialization, commits, or pushes.

## Phase 1 - Interactive Knowledge Graph UI Spike

**Status:** completed and approved. The local-mock prototype validated the scoped interactions, TypeScript, basic responsiveness, and basic accessibility; build and lint passed. XYFlow / React Flow was explicitly adopted as the initial visualization library in [ADR-007](DECISIONS.md#adr-007---initial-xyflow-graph-visualization). Remaining limitations are documented in [the spike README](../src/frontend/talynor-web/README.md).

**Goal:** validate the visual graph experience before committing to the application skeleton and detailed domain implementation.

**Expected outputs:** a scoped frontend prototype exploring nodes, edges, zoom, pan, selection, drill-down, submaps, and navigation between knowledge levels; findings about responsive interaction, usability, performance, and visualization options; documented limitations and a recommendation for review.

**Non-goals:** production backend, persistence, authentication, final relationship taxonomy, or completion of the MVP. Prototype choices are not automatically production decisions.

## Phase 2 - Solution and Application Skeleton

**Status:** completed and approved after review, commit, and publication. The .NET 10 skeleton uses `Talynor.slnx`, `global.json`, and `Directory.Build.props`. Persistence and authentication are not implemented.

**Goal:** establish the smallest application foundation consistent with the approved architecture and reviewed spike findings.

**Expected outputs:** .NET 10 / ASP.NET Core and React / TypeScript skeletons; explicit responsibility boundaries; initial REST/OpenAPI setup; nullable reference types; environment configuration, structured logging, health checks, and an initial backend test foundation. Scope early SQLite persistence setup explicitly if needed.

**Non-goals:** full learning features, distributed infrastructure, Docker or PostgreSQL infrastructure without an explicit request, and CI setup at this phase.

## VS-001 - Read-only Knowledge Graph Vertical Slice

**Status:** completed, approved, and published. This explicitly authorized validation slice runs between Phase 2 and Phase 3 without replacing or renumbering phases.

**Goal:** load the two existing demonstration maps through Talynor's HTTP API and adapt them into XYFlow while preserving the spike interactions.

**Scope:** read-only map endpoint, Application read models and reader interface, Infrastructure in-memory source, independent API contracts, native fetch with cancellation and loading/error/404 states, frontend-only positions, and Vite development proxy. Progress and study time remain demonstrations.

**Non-goals:** writing/CRUD, persistence, EF Core, migrations, identity, ownership, real learning workflows, advanced layout, response caching, distributed infrastructure, or additional application frameworks.

## Phase 3 - Identity and Workspace

**Status:** complete. Phase 3.1 through 3.4 are completed, approved, and published. Phase 3.5 — Validation and Phase Closure is validated and completed. VS-001 remains a separate completed demonstration slice.

**Phase 3 incremental slices:**

- **3.2A — Session HTTP Foundation and CSRF Bootstrap:** Identity services/stores over the existing schema, application-cookie and authorization infrastructure, native antiforgery, anonymous `GET /api/auth/csrf`, and integration verification. Completed, approved, and published.
- **3.2B.1 — Account Registration:** register without auto-login, email=username, password policy, account adapter, native CSRF, registration-only rate limiting, concurrent uniqueness and validation tests. Completed, approved, and published.
- **3.2B.2A — Security Stamp Compatibility:** completed, approved, and published. Registers only the native technical TwoFactorRememberMe handler to fix the reproduced Identity 10.0.12 rejection exception, preserving the native validator and Application defaults; regression requires HTTP 401. That slice introduced no functional MFA or session endpoint.
- **3.2B.2B — Session Flow:** login/logout/me, current-user abstraction, session adapter, native login lockout/rate limiting, and cookie/CSRF/session tests. Completed, approved, and published. The native Security Stamp regressions and fixed original eight-hour ticket deadline are preserved.
- **3.3 — Personal Workspace and Ownership:** completed, approved, and published. One workspace per authenticated user, lazy/idempotent creation, EF Core/SQLite persistence with unique owner index, and `GET /api/workspace`. Knowledge Maps remain anonymous/global demos.
- **3.4 — Authenticated Frontend:** completed, approved, and published. Session bootstrap (me then workspace), login/logout UI, cookie/CSRF integration, session error/retry handling, and the preserved demo graph. Real-browser validation passed in 3.5 using trusted HTTPS.
- **3.5 — Validation and Phase Closure:** validated and completed on 2026-10-13. Explicit migrations, real-browser HTTPS/cookies/CSRF/session/workspace/refresh/logout, graph/error isolation, responsive layouts, accessibility smoke, console, Network, and multiple-tab behavior passed. Backend build, 33 unit tests, 111 integration tests, frontend build/lint, and EF pending-model checks passed. No code correction was necessary; the Git working tree was clean after validation.

**Goal:** establish account and knowledge workspace workflows with explicit access boundaries.

One personal workspace per user is approved and implemented in 3.3 under ADR-009; 3.1 introduced only account persistence. Atlas remains provisional and its relationship to Workspace is not finalized; this does not define Atlas as a separate entity.

**Expected outputs:** reviewed authentication and authorization design; account and workspace capabilities; defined ownership rules and relevant access-control validation.

**Delivered incremental direction:** local ASP.NET Core Identity and browser cookies under [ADR-008](DECISIONS.md#adr-008---incremental-local-identity-and-persistence), personal workspace ownership under ADR-009, and authenticated frontend under ADR-010. Real-browser validation completed the phase. Phase 3 delivered persistent owned PersonalWorkspace records and preserved anonymous global demo maps; Phase 4.1 adds separate private map persistence below.

**Phase 3 non-goals:** frontend registration, workspace CRUD/switching, sharing, teams, roles, map ownership/persistence/CRUD, Security Stamp customization, JWT, functional MFA, email verification/reset, remember-me, cross-tab synchronization, global session revocation, or a session table. Knowledge authoring remains future Phase 4 work. Social networking, public marketplace, and real-time collaboration remain outside this phase.

## Phase 4 - Persistent Knowledge Authoring

**Status:** current and incomplete. Phase 4.1 — Knowledge Map Persistence Foundation and 4.2 — Map/Node/Edge Authoring Backend are complete, approved and published. Phase 4.3A — Persisted Map Discovery is complete, approved and published; 4.3 — Persistent Graph Frontend Integration is implemented and in review. Later slices need separate authorization.

**Goal:** evolve the demonstration Knowledge Maps into persistent knowledge content owned by PersonalWorkspace, supporting connected nodes beyond a folder hierarchy.

**4.1 completed/published:** relational Knowledge Maps, Nodes, Edges, persisted positions and ChildMapId; server-derived workspace ownership; private read endpoint, explicit migration, SQLite constraints and ownership/isolation tests. Demo routes coexist temporarily; frontend source is unchanged. See ADR-011 for the scoped model and read contract.

**4.2 completed/published:** private nested map/node create, PATCH and delete; position and ChildMap set-clear; edge create/delete; native CSRF, session-derived ownership and controlled errors, tests and documentation. No schema/provider/dependency/frontend change. See ADR-012.

**4.3A completed/published:** authenticated workspace-scoped `GET /api/maps` returns id/title summaries in a maps envelope, ordered by database title then ID. Empty workspaces return 200 with an empty maps array. No root/default map, automatic selection, frontend, schema or dependency change.

**4.3 implemented/in review:** authenticated discovery and explicit picker, private map rendering with backend positions, ChildMap drill-down and visit-based Back/Forward, `?map=<guid>` deep links/F5, recoverable map errors and session revalidation on 401. Drag is local only; no authoring endpoints or automatic first-map selection.

**Next/future:** Phase 4.4 — Authoring UX. Authoring and browser drag-save require their own scoped tasks. Relationship taxonomy, broader cross-map semantics, automatic layout and advanced authoring concurrency remain open.

**Non-goals:** graph database, automatic internet importing, or a relationship taxonomy assumed from the initial examples.

## Phase 5 - Knowledge Content

**Goal:** associate learning material with knowledge nodes.

**Expected outputs:** an agreed initial set of content types; creation, viewing, and editing workflows for that content; appropriate validation and access boundaries. Define file handling and its security requirements if files are included.

**Non-goals:** supporting every envisioned content type immediately, automatic content importing, marketplace, or AI content generation.

## Phase 6 - Study Sessions

**Goal:** let users register learning effort against their knowledge and content.

Study Session is the current working product term for a period of study. Its relationship to finer-grained study activities remains open.

**Expected outputs:** study session workflows; agreed study-time recording behavior; study session history linked to relevant knowledge; initial completion tracking where defined.

**Non-goals:** advanced analytics or background automation without a reviewed need.

## Phase 7 - Flashcards

**Goal:** support creating and reviewing flashcards within the learning context.

**Expected outputs:** flashcard management; review workflow; recorded review outcomes; reviewed definitions for retention measurement and review scheduling where needed.

**Non-goals:** assuming a particular scheduling algorithm, AI-generated flashcards, or advanced gamification.

## Phase 8 - Assessments

**Goal:** support simple quizzes to check understanding.

**Expected outputs:** quiz creation and answering workflows; recorded results; agreed scoring and accuracy definitions; links to relevant knowledge.

**Non-goals:** advanced examination systems, paid learning paths, or AI assessment generation.

## Phase 9 - Progress and Analytics

**Goal:** make basic learning progress visible using the activity and results already collected.

**Expected outputs:** basic progress views; reviewed definitions for study time, content completion, quiz accuracy, flashcard retention, and learning progress; documented limitations of the measures.

**Non-goals:** recommendation engine, advanced analytics platform, or unsupported claims of mastery.

## Phase 10 - UX, responsiveness and polishing

**Goal:** refine the complete learning journey and graph experience.

**Expected outputs:** responsive layout refinements; usable graph navigation; reviewed interaction, accessibility, loading, empty, and error states; resolution of observed usability and performance issues.

**Non-goals:** native mobile applications, major unapproved architecture changes, or new product scope disguised as polishing.

## Phase 11 - Production readiness

**Goal:** prepare the approved MVP for controlled production operation.

**Expected outputs:** explicitly scoped and validated transition to PostgreSQL; reviewed migration, backup, and recovery procedures; secure authentication and authorization verification; dependency and configuration review; operational logging and health verification; GitHub CI; deployment and support documentation. Evaluate OpenTelemetry and Testcontainers where justified.

**Non-goals:** production deployment without authorization, automatic provider switching, or introducing excluded distributed infrastructure without a new approved decision.
