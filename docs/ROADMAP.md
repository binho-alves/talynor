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

**Status:** started; not completed. Phase 3.1 and all Phase 3.2 Identity and Session Backend slices are completed, approved, and published. **3.3 — Personal Workspace and Ownership** is implemented and pending review. Frontend authentication is not implemented. VS-001 remains a separate completed demonstration slice.

**Phase 3 incremental slices:**

- **3.2A — Session HTTP Foundation and CSRF Bootstrap:** Identity services/stores over the existing schema, application-cookie and authorization infrastructure, native antiforgery, anonymous `GET /api/auth/csrf`, and integration verification. Completed, approved, and published.
- **3.2B.1 — Account Registration:** register without auto-login, email=username, password policy, account adapter, native CSRF, registration-only rate limiting, concurrent uniqueness and validation tests. Completed, approved, and published.
- **3.2B.2A — Security Stamp Compatibility:** completed, approved, and published. Registers only the native technical TwoFactorRememberMe handler to fix the reproduced Identity 10.0.12 rejection exception, preserving the native validator and Application defaults; regression requires HTTP 401. That slice introduced no functional MFA or session endpoint.
- **3.2B.2B — Session Flow:** login/logout/me, current-user abstraction, session adapter, native login lockout/rate limiting, and cookie/CSRF/session tests. Completed, approved, and published. The native Security Stamp regressions and fixed original eight-hour ticket deadline are preserved.
- **3.3 — Personal Workspace and Ownership:** implemented and pending review. One workspace per authenticated user, lazy/idempotent creation, EF Core/SQLite persistence with unique owner index, and `GET /api/workspace`. Knowledge Maps remain anonymous/global demos. Frontend authentication remains reserved for 3.4.
- **3.4 — Authenticated Frontend:** remains reserved for browser integration and authentication UI; not started.
- **3.5 — Validation / Phase Closure:** future validation and closure of Phase 3; not started and requires a separate scoped task.

**Goal:** establish account and knowledge workspace workflows with explicit access boundaries.

One personal workspace per user is approved and implemented in 3.3 under ADR-009; 3.1 introduced only account persistence. Atlas remains provisional and its relationship to Workspace is not finalized; this does not define Atlas as a separate entity.

**Expected outputs:** reviewed authentication and authorization design; account and workspace capabilities; defined ownership rules and relevant access-control validation.

**Approved incremental direction:** local ASP.NET Core Identity and future browser cookies, recorded in [ADR-008](DECISIONS.md#adr-008---incremental-local-identity-and-persistence). Phase 3.2 introduces scoped identity/session HTTP behavior; Phase 3.3 introduces the personal workspace. Approval of this direction does not authorize implementing those slices in 3.1.

**Current slice (3.3) non-goals:** workspace CRUD, sharing, teams, roles, map ownership/persistence/CRUD, frontend authentication, Security Stamp customization, JWT, functional MFA, email verification/reset, global session revocation, or a session table. Phase 3.4 — Authenticated Frontend remains future work. Social networking, public marketplace, and real-time collaboration remain outside this phase.

## Phase 4 - Knowledge Graph

**Goal:** support connected knowledge nodes and map navigation beyond a folder hierarchy.

**Expected outputs:** reviewed graph semantics; node and relationship capabilities; map/submap navigation integrated with the validated visual experience; persistence and validation of authorized graph behavior.

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
