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

**Status:** complete, approved and published. Phase 4.1 — Knowledge Map Persistence Foundation, 4.2 — Map/Node/Edge Authoring Backend, 4.3A — Persisted Map Discovery, 4.3 — Persistent Graph Frontend Integration and 4.4 — Authoring UX are complete, approved and published.

**Goal:** evolve the demonstration Knowledge Maps into persistent knowledge content owned by PersonalWorkspace, supporting connected nodes beyond a folder hierarchy.

**4.1 completed/published:** relational Knowledge Maps, Nodes, Edges, persisted positions and ChildMapId; server-derived workspace ownership; private read endpoint, explicit migration, SQLite constraints and ownership/isolation tests. Demo routes coexist temporarily; frontend source is unchanged. See ADR-011 for the scoped model and read contract.

**4.2 completed/published:** private nested map/node create, PATCH and delete; position and ChildMap set-clear; edge create/delete; native CSRF, session-derived ownership and controlled errors, tests and documentation. No schema/provider/dependency/frontend change. See ADR-012.

**4.3A completed/published:** authenticated workspace-scoped `GET /api/maps` returns id/title summaries in a maps envelope, ordered by database title then ID. Empty workspaces return 200 with an empty maps array. No root/default map, automatic selection, frontend, schema or dependency change.

**4.3 completed/published (`f19c3d5`):** authenticated discovery and explicit picker, private map rendering with backend positions, ChildMap drill-down and visit-based Back/Forward, `?map=<guid>` deep links/F5, recoverable map errors and session revalidation on 401. At the time of Phase 4.3, drag was local only and the frontend called no authoring endpoints. No automatic first-map selection was introduced.

**4.4 completed/approved/published (`c427c6e`):** explicit edit mode, map/node CRUD, persisted drag stop with rollback, ChildMap set/clear and edge create/delete through the published API; native CSRF, explicit mutation retries without automatic replay, and delete confirmations. No backend/schema/dependency change.

**Closure:** the approved slices satisfy the phase goal: users can discover, navigate and author private persistent knowledge maps, nodes, edges, positions and ChildMap links under server-derived PersonalWorkspace ownership. Their reviewed implementation and validation complete Phase 4. Relationship taxonomy, broader cross-map semantics, automatic layout and advanced authoring concurrency remain open future work, not original exit requirements. SQLite, discarded navigation drafts, no undo/redo or cross-tab coordination, and limited frontend test tooling remain documented limitations; completion does not imply production readiness.

**Non-goals:** graph database, automatic internet importing, or a relationship taxonomy assumed from the initial examples.

## Phase 5 - Knowledge Content

**Status:** complete, approved and published for the agreed **Note and Link Content** scope under [ADR-013](DECISIONS.md#adr-013---initial-node-owned-knowledge-content). Foundation/read (c450b91), Content Authoring Backend (5aa7d4e) and Frontend Content Integration (d4e5c76) deliver private node-owned content, authenticated backend reads/writes and frontend viewing/authoring in Edit map, with validation, ownership, CSRF, safe rendering and manual retries. Map DTOs/History and existing graph protections are preserved.

**Goal:** associate learning material with knowledge nodes.

**Expected outputs:** an agreed initial set of content types; creation, viewing, and editing workflows for that content; appropriate validation and access boundaries. Define file handling and its security requirements if files are included.

**Closure:** the agreed initial types and their create/view/edit workflows, validation and access boundaries are delivered and reviewed. File was not included, so upload/storage is future work rather than a missing phase requirement. Specialized Article/Video, Flashcard, Quiz, Exercise and Checklist capabilities remain explicitly deferred. Documented scale, concurrency, testing and real-server body-limit verification limits remain; scoped completion does not imply production readiness.

**Non-goals:** supporting every envisioned content type immediately, automatic content importing, marketplace, or AI content generation.

## Phase 6 - Study Sessions

**Status:** complete, approved and published for the scoped Study Session workflow under [ADR-014](DECISIONS.md#adr-014---study-session-ownership-lifecycle-and-active-session-invariant). Checkpoints 1 (881d12a), 2 (29ba218) and 3 (493d39a) and integrated closure documentation (6045391) are approved and published; validation passed on 2026-10-19.

**Goal:** let users register learning effort against their knowledge and content.

Study Session is the current working product term for a period of study. Its relationship to finer-grained study activities remains open.

**Expected outputs:** study session workflows; agreed study-time recording behavior; study session history linked to relevant knowledge; initial completion tracking where defined.

**Delivered approved scope:** private node-owned Study Sessions with server-controlled UTC start/end, derived duration/status, at most one active session per PersonalWorkspace, explicit start/complete/discard and completed history with hard deletion. Node/map deletion is blocked while sessions exist. The implemented frontend includes a global active indicator, presentation-only timer, recovery after login/F5/navigation and paginated history by node with delete confirmation. Completion closes only the temporal record; no learning/content/node-completion definition is approved. Forgotten sessions remain open until explicit action and cannot have their timestamps edited retroactively.

**Implementation checkpoints, not new numbered phases:** checkpoint 1 foundation/read plus delete protections is published. Checkpoint 2 implements server-timed start, first-end-preserving complete, explicit hard delete/discard, workspace-scoped active lookup and the official SQLite atomic-start invariant, with authenticated/CSRF-protected writes and independent-connection concurrency tests; it is approved and published in 29ba218. Checkpoint 3 active bootstrap, shell indicator, server-anchored timer, explicit complete/discard, focus recovery and paginated completed history with confirmed delete are approved and published in 493d39a. Integrated closure is validated. No broader learning-completion behavior is delivered.

**Closure:** the published checkpoints satisfy the approved node-owned history, server-time lifecycle, official-path atomic start, ownership, delete protection and frontend recovery/history requirements. Automated regressions passed; prior Checkpoint 3 browser/manual evidence is preserved separately. Completion closes the scoped phase, with the documented SQLite, ambiguous-write, eventual multi-tab, offset-pagination, visual-timer and verification limits retained. It does not imply production readiness.

**Outside the initial scope:** direct KnowledgeContent linkage, session notes, manual/retroactive time entry, pause/resume, heartbeat/timeout/automatic completion, Pomodoro, totals by node/day/week or materialized counters, learning completion/mastery/percentages, goals/streaks/XP, notifications/calendar, Flashcards, Assessments, AI/recommendations, social/sharing, realtime/WebSockets, workers, offline sync, advanced analytics, CSV/export, mobile app and PostgreSQL infrastructure. Phases 7, 8 and 9 retain their existing goals; this phase does not predefine their models or metrics.

**Non-goals:** advanced analytics or background automation without a reviewed need.

## Phase 7 - Flashcards

**Status:** Complete for the approved scope after Checkpoint 4 integrated validation on 2026-10-21. Design approved under [ADR-015](DECISIONS.md#adr-015---node-owned-flashcards-and-review-progression); baseline b4da3f8 and Checkpoints 1 (f375141), 2 (8af676d) and 3 (f58d0fe) are approved and published. ADR-015 remains Accepted. Phase 7 closure was formally approved and published in bc7a4b6. Existing SQLite, offset-pagination, recovery, accessibility and physical-touch limitations remain; completion does not imply production readiness. Phase 8 is In Progress under ADR-016; CP1 is Published in 4b6dec0, CP2 backend is Implemented / under review and CP3-CP4 are Not started.

**Goal:** let a learner create prompt/answer cards on a knowledge node, review them, and resume from persisted per-card progress.

**Approved initial scope:** Flashcard is a separate node-owned entity, not a KnowledgeContent subtype and not dependent on Note/Link. Users can create/edit/hard-delete cards and list them with page default 1, pageSize default 20/max 100, CreatedAtUtc ASC/Id ASC and hasMore without mandatory COUNT. ContentRevision (positive Int32, initially 1) changes only with text; a material edit atomically resets current review state to unreviewed while preserving revision-tagged events. Empty/no-op PATCH changes nothing and is validation_failed. Bounded plain-text Front/Back use a 64 KiB write cap that accommodates their 2,000/4,000 UTF-16 limits even when escaped.

**Study contract:** next returns Front/revision and minimal progress, never Back. Explicit authenticated revision-matched reveal supplies Back; flashcard_changed discards the stale item and reloads next. Reviews persist their answered ContentRevision; PUT and scoped lookup expose the immutable event plus current revision/progress for same-ID reconciliation. ReviewId PK and the official SQLite IMMEDIATE transaction preserve idempotency/event-state atomicity and prevent different-ID lost updates. Text edits/reset serialize with reviews; no PostgreSQL or arbitrary-writer guarantee is implied.

**Continuous study:** no spaced-repetition scheduler, deck-complete state, persistent queue or backend run. Cards without state come first by CreatedAtUtc ASC, Id ASC, then reviewed cards by LastReviewedAtUtc ASC, CreatedAtUtc ASC, Id ASC. Never-reviewed cards intentionally have priority while present; all cards remain eligible, including a single card reappearing after review. Next recalculates persisted rotation after each answer; refresh/navigation preserves no exact cursor/run. Users leave whenever desired. Study Sessions remain independent, neither required nor automatically created. See ADR-015 for exact contracts, security and limitations.

**Implementation checkpoints (not new numbered phases):**

1. Flashcard Domain with ContentRevision from the start, AddFlashcards, atomic revision updates, paginated node-scoped CRUD/read, hard delete/cascade and tests; no review tables.
2. ReviewState/events including answered revision, AddFlashcardReviews, edit/reset integration, next/reveal, PUT review/GET lookup, same-connection BEGIN IMMEDIATE atomicity/idempotency/concurrency and tests.
3. NodeDetails paginated authoring and accessible continuous study/reveal/result/reconciliation, stale-response guards, runtime validation and responsive browser checks.
4. Integrated regression, migration Up/Down/reapply verification, security/accessibility smoke checks, documentation follow-through and formal closure review.

Each checkpoint was separately authorized. Checkpoints 1–3 are published and Checkpoint 4 integrated validation is completed. Later phases still require their own scoped authorization.

**Out of scope:** Flashcard-to-KnowledgeContent references, card types beyond plain prompt/answer text, hint/tags/media/source/explanation, Markdown/HTML, manual ordering, randomized or snapshot queues, due dates or interval scheduling, Again/Hard/Good/Easy ratings, learning mastery/aggregates, StudySession integration, shared/public decks, marketplace, Anki import, cloze/image occlusion, AI generation, collaboration/realtime, reminders/notifications, advanced analytics, achievements, leaderboards, FSRS optimization/training, offline/native mobile, PostgreSQL infrastructure or new dependencies/frameworks.

## Phase 8 - Assessments

**Status:** In Progress under [ADR-016](DECISIONS.md#adr-016---node-owned-quizzes-revision-scoped-grading-and-immutable-attempts). CP0 - Design baseline is Published in 39ff3aa. CP1 is Published in 4b6dec0; CP2 is Implemented / under review, not published; CP3 and CP4 are Not started. Phase 7 closure is published in bc7a4b6; later checkpoints require their own scoped authorization.

**Goal:** support simple quizzes to check understanding.

**Expected outputs:** quiz creation and answering workflows; recorded results; agreed scoring and accuracy definitions; links to relevant knowledge.

**Accepted initial scope:** a Quiz belongs directly to one KnowledgeNode, separate from KnowledgeContent. It has 1..10 single-choice questions, each with 2..4 options/exactly one correct. Required title/question/option limits are 200/1000/500 UTF-16 units; text/order/duplicate rules are in ADR-016. Submit the complete quiz; server grading gives one point per question with CorrectCount/QuestionCount and deterministic two-place derived accuracy. No weights/partial credit/penalties/pass-fail/mastery. A full-definition PUT uses expectedRevision; material changes increment ContentRevision once and retain immutable attempt snapshots of title/questions/all presented alternatives/order/choices/results. Stale/no-op/overflow cannot partially mutate.

**Lifecycle/security:** unsent answers/drafts are local only and may be discarded on leave/context/F5; no persisted active attempt/start/autosave/resume/timer. AttemptId supports matching original-result replay and scoped lookup after unknown outcome, never automatic write replay. Official SQLite writers serialize edit/grading with short same-connection transactions as specified in ADR-016, without arbitrary-writer/PostgreSQL/distributed guarantees. Existing ownership/auth/CSRF/no-store and native wait/cancellation limits remain. Confirmed quiz deletion and otherwise permitted node/map deletion cascade definition and history, preserving all existing blockers without adding a quiz/history blocker or allowing partial cascade. StudySession remains independent; Phase 9 aggregates/completion/mastery are not defined here.

**Implementation checkpoints, not new numbered phases:**

1. CP0 - Design baseline: accepted model/contracts/security/lifecycle, future matrix and risks; documentation only, Published in 39ff3aa.
2. CP1 - Quiz foundation + authoring/read backend: Published in 4b6dec0; Domain/EF and AddQuizzes, nested CRUD/full replacement and answer-free take reads; no attempts/frontend.
3. CP2 - Submission, grading and history backend: Implemented / under review; AddQuizAttempts, complete server grading, immutable full snapshots, idempotency/lookup and concurrency; no frontend/active draft.
4. CP3 - Frontend authoring + answering/history: existing node UX, runtime validation, local draft/locks/reconciliation, accessibility/responsiveness; no new backend/schema/dependency.
5. CP4 - Integrated validation and closure: regression/migration/security/browser evidence, limitations and formal closure; no new features.

**Exit criteria:** future [acceptance matrix A-R](DEVELOPMENT.md#phase-8-future-acceptance-matrix-a-r), checkpoint scopes/exclusions/validation/exits and [risk register](DEVELOPMENT.md#phase-8-risk-register) are defined in Development. CP0 marked no application criterion PASS. CP1/CP2 evidence covers definition and attempt backend behavior, AddQuizzes/AddQuizAttempts and scoped regressions. CP2 formal review/publication and CP3-CP4 need separate requests; no frontend/closure is started here.

**Risks:** ownership/IDOR, changed-definition grading, duplicate send, snapshot completeness/storage, accidental history deletion, SQLite contention, frontend state, migration preservation and exam-system scope creep. Existing architecture/provider and verification limitations remain; design acceptance is not production readiness.

**Non-goals/out of scope:** advanced examination systems/teacher-student model/paid paths, multiple correct/free-text/discursive/manual grading, question bank/import/media/Markdown/HTML/hints/explanations, persisted draft/start/resume/pause/autosave/timer/timeout, randomization/adaptive testing, weights/partial credit/penalties/pass thresholds/mastery, AI generation/recommendations, scheduler/FSRS/SM-2/gamification, StudySession integration/Phase 9 aggregates, realtime/social/marketplace, native mobile/offline, PostgreSQL infrastructure/graph DB/distributed infrastructure or new frameworks/dependencies. This phase does not authorize later roadmap implementation.

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
