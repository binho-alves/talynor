# Architecture Decision Record index

This index records approved decisions from the project foundation and subsequent explicitly authorized tasks. The records below are contained in this document; no separate ADR files are required at this stage. Record date: 2026-10-12. All listed records are **accepted**.

| Record | Decision | Status |
| --- | --- | --- |
| [ADR-001](#adr-001---react-frontend) | React selected for the frontend | Accepted |
| [ADR-002](#adr-002---net-10-backend) | .NET 10 selected for the backend | Accepted |
| [ADR-003](#adr-003---modular-monolith-first) | Modular monolith first | Accepted |
| [ADR-004](#adr-004---sqlite-first-postgresql-production-target) | SQLite first; PostgreSQL production target | Accepted |
| [ADR-005](#adr-005---knowledge-graph-without-an-initial-graph-database) | Knowledge graph domain without a graph database initially | Accepted |
| [ADR-006](#adr-006---no-premature-distributed-infrastructure) | No premature distributed infrastructure | Accepted |
| [ADR-007](#adr-007---initial-xyflow-graph-visualization) | Initial XYFlow / React Flow graph visualization | Accepted |
| [ADR-008](#adr-008---incremental-local-identity-and-persistence) | Local Identity, future cookies, and incremental EF Core/SQLite persistence | Accepted |

## ADR-001 - React frontend

**Context:** Talynor requires a highly visual, responsive learning interface with interactive knowledge maps.

**Decision:** use React with TypeScript for a responsive SPA and standalone reusable UI components.

**Implications:** SPIKE-001 validated the initial graph experience. XYFlow / React Flow is adopted as the initial visualization library under ADR-007. State management approach, frontend testing tools, and package manager remain open.

## ADR-002 - .NET 10 backend

**Context:** the backend needs an explicit application and domain boundary supporting learning workflows and persistence.

**Decision:** use .NET 10, ASP.NET Core, and C#, with EF Core and REST APIs documented using OpenAPI.

**Implications:** the authorized Phase 2 task created `Talynor.slnx` with Api, Application, Domain, Infrastructure, UnitTests, and IntegrationTests projects, `global.json`, and `Directory.Build.props`. Phase 2 is completed and approved. VS-001 explicitly authorizes a read-only demonstration endpoint, Application read models and reader interface, an Infrastructure in-memory implementation, and explicit mapping to independent HTTP contracts. Domain remains without artificial models. Phase 3.1 provides Identity persistence; 3.2A composes cookie/antiforgery infrastructure under ADR-008 without account/session endpoints. Product module boundaries and further implementation patterns remain open.

## ADR-003 - Modular monolith first

**Context:** the project is starting from zero and prioritizes simplicity and explicit responsibilities.

**Decision:** begin with a modular monolith.

**Implications:** keep business logic outside controllers and dependencies intentional. Module boundaries require later design; no microservice decomposition is approved.

## ADR-004 - SQLite first; PostgreSQL production target

**Context:** development and prototyping need an initial persistence approach, with a separate production target.

**Decision:** use SQLite initially and target PostgreSQL for production.

**Implications:** avoid unnecessary provider-specific behavior. Provider changes and migrations must be explicit, controlled, and validated. PostgreSQL infrastructure is not authorized at this stage and requires a later explicit request. Merely advancing roadmap phases does not authorize introducing it.

## ADR-005 - Knowledge graph without an initial graph database

**Context:** knowledge must support relationships beyond folders and trees; a node may open another map.

**Decision:** model knowledge as a graph domain concept while using the approved relational database strategy initially.

**Implications:** do not introduce a graph database. The data model and final relationship taxonomy require later decisions; the example relationships do not finalize them.

## ADR-006 - No premature distributed infrastructure

**Context:** infrastructure complexity must be justified by actual project needs.

**Decision:** do not introduce microservices, Redis, RabbitMQ, Kafka, Kubernetes, or Elasticsearch in the initial scope. Background workers require later justification.

**Implications:** address requirements within the approved modular monolith first. Any architectural departure requires explicit approval and an updated decision record.

## ADR-007 - Initial XYFlow graph visualization

**Context:** Talynor needs interactive knowledge maps with custom concepts, connections, and navigation between levels of detail. SPIKE-001 evaluated XYFlow / React Flow using React, TypeScript, and local mocks before the application skeleton. At adoption, the spike was completed and approved and Phase 2 had not started.

**Decision:** adopt XYFlow / React Flow as Talynor's initial graph visualization library. This is an accepted visualization-layer decision and may be reevaluated if concrete limitations emerge.

**Evidence:** SPIKE-001 validated custom nodes, edges, selection, dragging, zoom, pan, minimap, drill-down, TypeScript, basic responsiveness, and basic accessibility. Build and lint passed. These results establish initial suitability; the two five-node maps do not establish performance capacity for larger graphs or full accessibility compliance. Historical results and limitations remain in [the spike README](../src/frontend/talynor-web/README.md).

**Positive consequences:** reuse the validated interactions and custom React components; maintain typed visual data and callbacks; support incremental learning and implementation without an additional UI framework or application-level global state library. Drill-down remains application-controlled navigation, not a library-defined domain hierarchy.

**Isolation rule:** XYFlow is confined to the frontend visualization layer. Domain and application logic must not depend on XYFlow-specific types. API contracts and persistence must not expose or store XYFlow-specific Node/Edge structures as their domain or contract models. The spike's view types are presentation data, not domain entities; adopting the library does not decide relationship semantics or storage representation.

**VS-001 follow-through:** the explicitly authorized validation slice uses an independent HTTP contract, native fetch with cancellation, and a frontend adapter into XYFlow. Demonstration coordinates remain exclusively in the frontend. A configurable Vite development proxy supplies relative `/api` URLs without introducing backend CORS. No additional package manager, state library, automatic layout, persistence, or relationship semantics are selected.

**Risks and lock-in:** visual components, handles, event handling, Node/Edge view types, viewport behavior, and library CSS create integration costs. A future replacement would require adapting the visual layer and interactions; isolation reduces but does not eliminate that cost. Larger or denser graphs, richer nodes, and frequent updates may increase rendering cost. Real touchscreen behavior and full keyboard/screen-reader usability need further validation; narrow-screen overview text currently requires zoom. Dependency compatibility and ongoing maintenance remain considerations.

**Reevaluation criteria:** revisit adoption if representative product graphs remain unacceptably slow or memory-intensive after proportionate improvements; required touch or accessibility interactions cannot be achieved; an essential visualization requirement cannot be supported; library coupling obstructs domain evolution; or recurring compatibility, security, or maintenance problems undermine continued use. No artificial node-count limit is set.

**Still open:** automatic layout, final interaction design, relationship semantics and persistence, and the official package manager. pnpm was used because it was available for the spike; npm, pnpm, or another option may be chosen later. This decision neither starts Phase 2 nor authorizes additional dependencies or infrastructure.

## ADR-008 - Incremental local Identity and persistence

**Context:** VS-001 is completed and published, with anonymous, read-only demonstration maps. Phase 3 needs durable account storage before identity/session HTTP workflows and personal-workspace ownership. The scoped Phase 3.1 task explicitly approves the strategy but authorizes only its persistence foundation.

**Decided:** use local ASP.NET Core Identity as the initial account system, EF Core with SQLite initially, browser cookies rather than JWT, and own HTTP endpoints rather than mapping the complete Identity endpoint set. A future single personal workspace per user is approved; no second User/ApplicationUser entity is introduced without a concrete need. PostgreSQL remains the production target under ADR-004; infrastructure requires a later explicit request. No graph database is introduced.

**Implemented now (3.1):** Infrastructure owns `TalynorDbContext`, based on `IdentityUserContext<IdentityUser>` without roles; SQLite provider and configurable local connection string; explicit `InitialIdentity` migration; design-time factory; local versioned `dotnet-ef`; tests of migrated schema, repeat application, cross-context persistence through the role-free Identity store, and API startup without database creation. Identity/EF types remain outside Domain and Application. No automatic startup migration, `EnsureCreated`, seed, or default credentials exist.

**Implemented now (3.2A, completed and published):** Phase 3.1 is completed and published. The API composes IdentityCore, existing EF stores, and SignInManager, with only the `Identity.Application` cookie scheme. `__Host-Talynor.Auth` is HttpOnly, Secure Always, SameSite Lax, Path `/`, without Domain, with an eight-hour ticket lifetime and no sliding expiration. Redirect events return 401/403 while preserving security-stamp validation. Authorization has no global fallback policy. Native antiforgery uses `X-CSRF-TOKEN` and a separate `__Host-Talynor.Csrf` cookie (HttpOnly, Secure Always, SameSite Strict, Path `/`, no Domain). Anonymous `GET /api/auth/csrf` returns only the request token with no-store; a native MVC filter protects unsafe actions. Default local Data Protection remains in use. No login emits an auth cookie yet, no account is seeded, and 3.2A introduced no schema/migration/dependency/frontend change.

**Approved and implemented (3.2B.1, pending review):** registration uses own `POST /api/auth/register`, without auto-login or an auth cookie. Email and username are identical after trimming external whitespace; Identity owns normalization and RequireUniqueEmail is true. The default username character whitelist is disabled to match the email contract, without provider-specific rules. HTTP limits are 256 UTF-16 units for trimmed email and 128 for password; password is never trimmed, case-changed, normalized, or truncated. RequiredLength is 15, RequiredUniqueChars is 1, and uppercase/lowercase/digit/non-alphanumeric requirements are false. Native Identity still rejects whitespace-only passwords. No blocklist or custom hashing is added, and this policy does not claim full security-standard compliance.

**Approved registration behavior:** Application's RegisterUser and creation-only IIdentityAccountService remain independent of Identity/HTTP/EF; Infrastructure uses UserManager and specific result mapping. The existing unique NormalizedUserName index is the concurrency guarantee while all writes maintain email=username. Only its exact SQLite UNIQUE failure becomes Duplicate; other database failures stay operational. No migration or unique NormalizedEmail index is introduced. Registration requires native CSRF and uses a native five-request/ten-minute fixed window per remote IP, QueueLimit zero, IPv4-mapped normalization, shared restricted missing-IP partition, and no forwarded-header trust. Responses: 201 without body/Location, 400 validation_failed or generic registration_failed, 409 already_authenticated, 400 csrf_invalid, 429 rate_limited, and sanitized 500 unexpected_error; no-store. A native per-action 8 KiB request limit is applied where supported by the HTTP server.

**Future, not implemented:** login/logout/me and current user in 3.2B.2, including non-persistent sign-in, login lockout/rate limiting, and CSRF refresh across session changes; workspace storage and ownership in 3.3; frontend authentication in 3.4. Their direction is not permission to implement them in 3.2B.1. The potential native Security Stamp rejection-path issue with TwoFactorRememberMe absent is deferred: reproduce against the real configuration before any customization. Recovery, verification, and additional account capabilities remain outside this slice.

**Consequences:** account storage can be verified independently of login and frontend behavior. The existing project-reference directions remain unchanged. The schema includes Identity's user, claim, login, and token support tables but no roles, workspace, or knowledge-content tables. The framework schema is not permission to expose additional account features. Knowledge Maps remain in-memory and the demonstration endpoint stays anonymous.

**Risks and boundaries:** Identity's account schema introduces framework coupling in Infrastructure. SQLite and PostgreSQL have different provider/migration behavior; a future transition needs explicit review and validation. Local databases contain sensitive data and must remain untracked. 3.2B.1 creates unconfirmed accounts, without session login or production readiness. Generic duplicate errors do not eliminate enumeration through success/failure or timing. Blocklist, Unicode handling, password-hash cost, email verification, proxy/NAT behavior, and deployment security must be reviewed before public exposure. No private-data endpoint is introduced. Local Data Protection key persistence depends on the host profile; deploy/key storage, actual browser TLS behavior, and complete session security need later verification. Cookie/token/password values must never be logged.

## Decision discipline

Future records must distinguish proposals from accepted decisions. Do not present unreviewed options as commitments. Initial XYFlow / React Flow adoption is accepted under ADR-007; local Identity, registration/password policy, future cookie sessions, and incremental persistence/workspace direction are accepted under ADR-008. Further session/account workflows, automatic layout, final interaction design, knowledge relationship taxonomy and persistence, frontend testing tools, package manager, and deployment topology remain open.
