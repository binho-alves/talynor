# Architecture Decision Record index

This index records approved decisions from the project foundation and subsequent explicitly authorized tasks. The records below are contained in this document; no separate ADR files are required at this stage. Initial decisions were recorded on 2026-10-12; subsequent ADRs record their own dates. All listed records are **accepted**.

| Record | Decision | Status |
| --- | --- | --- |
| [ADR-001](#adr-001---react-frontend) | React selected for the frontend | Accepted |
| [ADR-002](#adr-002---net-10-backend) | .NET 10 selected for the backend | Accepted |
| [ADR-003](#adr-003---modular-monolith-first) | Modular monolith first | Accepted |
| [ADR-004](#adr-004---sqlite-first-postgresql-production-target) | SQLite first; PostgreSQL production target | Accepted |
| [ADR-005](#adr-005---knowledge-graph-without-an-initial-graph-database) | Knowledge graph domain without a graph database initially | Accepted |
| [ADR-006](#adr-006---no-premature-distributed-infrastructure) | No premature distributed infrastructure | Accepted |
| [ADR-007](#adr-007---initial-xyflow-graph-visualization) | Initial XYFlow / React Flow graph visualization | Accepted |
| [ADR-008](#adr-008---incremental-local-identity-and-persistence) | Local Identity, browser cookie sessions, and incremental EF Core/SQLite persistence | Accepted; implemented and published |
| [ADR-009](#adr-009---personal-workspace-and-ownership) | One personal workspace per user; lazy creation and authenticated ownership | Accepted; implemented and published |
| [ADR-010](#adr-010---authenticated-frontend-session-flow) | Cookie-based frontend session bootstrap with private in-memory CSRF | Accepted; implemented and published |

## ADR-001 - React frontend

**Context:** Talynor requires a highly visual, responsive learning interface with interactive knowledge maps.

**Decision:** use React with TypeScript for a responsive SPA and standalone reusable UI components.

**Implications:** SPIKE-001 validated the initial graph experience. XYFlow / React Flow is adopted as the initial visualization library under ADR-007. ADR-010 uses local React state/custom hook for the scoped session flow, without selecting a broader state framework. Frontend testing tools and package manager remain open.

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

**Status:** accepted; Identity persistence and session decisions are implemented and published in Phases 3.1 and 3.2. The approved workspace and frontend follow-through is published under ADR-009/010; Phase 3.5 validation and Phase 3 closure are completed.

**Context:** VS-001 is completed and published, with anonymous, read-only demonstration maps. Phase 3 needs durable account storage before identity/session HTTP workflows and personal-workspace ownership. The scoped Phase 3.1 task explicitly approves the strategy but authorizes only its persistence foundation.

**Decided:** use local ASP.NET Core Identity as the initial account system, EF Core with SQLite initially, browser cookies rather than JWT, and own HTTP endpoints rather than mapping the complete Identity endpoint set. One personal workspace per user was approved and is implemented under ADR-009; no second User/ApplicationUser entity is introduced without a concrete need. PostgreSQL remains the production target under ADR-004; infrastructure requires a later explicit request. No graph database is introduced.

**Implemented and published (3.1):** Infrastructure owns `TalynorDbContext`, based on `IdentityUserContext<IdentityUser>` without roles; SQLite provider and configurable local connection string; explicit `InitialIdentity` migration; design-time factory; local versioned `dotnet-ef`; tests of migrated schema, repeat application, cross-context persistence through the role-free Identity store, and API startup without database creation. Identity/EF types remain outside Domain and Application. No automatic startup migration, `EnsureCreated`, seed, or default credentials exist.

**Implemented and published (3.2A):** Phase 3.1 is completed and published. The API initially composed IdentityCore, existing EF stores, and SignInManager with only the `Identity.Application` cookie scheme; 3.2B.2A adds the technical auxiliary handler described below. `__Host-Talynor.Auth` is HttpOnly, Secure Always, SameSite Lax, Path `/`, without Domain, with an eight-hour ticket lifetime and no sliding expiration. Redirect events return 401/403 while preserving security-stamp validation; 3.2B.2B adds sanitized JSON bodies and no-store. Authorization has no global fallback policy. Native antiforgery uses `X-CSRF-TOKEN` and a separate `__Host-Talynor.Csrf` cookie (HttpOnly, Secure Always, SameSite Strict, Path `/`, no Domain). Anonymous `GET /api/auth/csrf` returns only the request token with no-store; a native MVC filter protects unsafe actions. Default local Data Protection remains in use. 3.2A added no login, seed, schema/migration/dependency/frontend change.

**Implemented and published (3.2B.1):** registration uses own `POST /api/auth/register`, without auto-login or an auth cookie. Email and username are identical after trimming external whitespace; Identity owns normalization and RequireUniqueEmail is true. The default username character whitelist is disabled to match the email contract, without provider-specific rules. HTTP limits are 256 UTF-16 units for trimmed email and 128 for password; password is never trimmed, case-changed, normalized, or truncated. RequiredLength is 15, RequiredUniqueChars is 1, and uppercase/lowercase/digit/non-alphanumeric requirements are false. Native Identity still rejects whitespace-only passwords. No blocklist or custom hashing is added, and this policy does not claim full security-standard compliance.

**Approved registration behavior:** Application's RegisterUser and IIdentityAccountService remain independent of Identity/HTTP/EF; Infrastructure uses UserManager and specific result mapping. The interface started as creation-only; 3.2B.2B adds only lookup by ID. The existing unique NormalizedUserName index is the concurrency guarantee while all writes maintain email=username. Only its exact SQLite UNIQUE failure becomes Duplicate; other database failures stay operational. No migration or unique NormalizedEmail index is introduced. Registration requires native CSRF and uses a native five-request/ten-minute fixed window per remote IP, QueueLimit zero, IPv4-mapped normalization, shared restricted missing-IP partition, and no forwarded-header trust. Responses: 201 without body/Location, 400 validation_failed or generic registration_failed, 409 already_authenticated, 400 csrf_invalid, 429 rate_limited, and sanitized 500 unexpected_error; no-store. A native per-action 8 KiB request limit is applied where supported by the HTTP server.

**Approved compatibility correction (3.2B.2A, completed, approved, and published):** a real SQLite/cookie/SignInManager diagnostic reproduced Identity 10.0.12's invalid-stamp rejection throwing InvalidOperationException because it explicitly signs out the absent `Identity.TwoFactorRememberMe` scheme. Register only that technical auxiliary cookie handler through the official `AddTwoFactorRememberMeCookie()` helper. Preserve the native `SecurityStampValidator<IdentityUser>`, application callback, 30-minute interval, and application-cookie policy; no custom validator or exception interception is used. Exactly Application and TwoFactorRememberMe are registered; authenticate, challenge, forbid, and sign-in remain Application defaults. External and TwoFactorUserId remain absent. No Talynor flow issues the auxiliary cookie, and no functional MFA is implemented. The regression requires native rejection, HTTP 401, cookie deletion, and rejection of original-ticket replay; valid-stamp renewal and registration without auto-login remain covered. Native auxiliary-cookie defaults are retained for this unused handler; any future functional use requires a separate security review. That slice introduced no schema, dependency, frontend, or session endpoint change.

**Implemented and published Session Flow (3.2B.2B):** own login/logout/me endpoints, Application LoginUser/GetCurrentUser and small ISessionService/ICurrentUser contracts. Scoped API adapters encapsulate HTTP/SignInManager and fail-closed NameIdentifier lookup; Infrastructure adds only account lookup by ID into AccountSummary. Native PasswordSignInAsync uses isPersistent=false and lockoutOnFailure=true, preserving the password and trimming only email. Existing authenticated login is 409 without switching; success is empty 204, and missing account/wrong password/lockout/not-allowed all return uniform 401 invalid_credentials. Operational failures remain sanitized 500, not invalid credentials. Login does not reapply creation policy.

**Session protections:** native lockout allows new users, with five failed attempts and five-minute lockout. Native login-only rate limiting permits ten requests per minute per remote IP, QueueLimit=0, IPv4-mapped normalization, shared missing-IP bucket, and no forwarded-header trust; registration quota remains separate and unchanged. Login/logout require native CSRF. Clients obtain a new CSRF request token after each identity change. Me/logout require Authorize, challenges/forbids remain 401/403 without redirects, and all auth responses are no-store. Me returns only ID and current email read from Identity; absent accounts cause local sign-out/401. The main cookie retains its policy and normal login has no Expires/Max-Age; the technical auxiliary cookie is never issued.

**Session boundaries:** multiple independent browser sessions are allowed; login does not invalidate prior sessions and logout deletes only the current browser cookie, without global ticket revocation or a session table. Login explicitly permits unconfirmed email; EmailConfirmed remains false, with no verification/reset workflow or provider. Account existence/lockout is not disclosed in errors, but timing remains distinguishable; compensatory hashing is explicitly deferred. Native stamp validation/renewal remains unchanged. Personal workspace/ownership is completed and published in 3.3 under ADR-009; frontend session integration is completed and published in 3.4 under ADR-010, with real-browser validation completed in 3.5. Recovery, verification, MFA, JWT, and other excluded account capabilities remain outside this slice.

**Consequences:** account storage can be verified independently of login and frontend behavior. The existing project-reference directions remain unchanged. The 3.1 schema includes Identity's user, claim, login, and token support tables but no roles, workspace, or knowledge-content tables; 3.3 adds only the personal-workspace table under ADR-009. The framework schema is not permission to expose additional account features. Knowledge Maps remain in-memory and the demonstration endpoint stays anonymous.

**Risks and boundaries:** Identity's account schema introduces framework coupling in Infrastructure. SQLite and PostgreSQL have different provider/migration behavior; a future transition needs explicit review and validation. Local databases contain sensitive data and must remain untracked. Accounts remain unconfirmed and sessions do not imply production readiness. Generic duplicate/authentication errors do not eliminate enumeration through success/failure or timing. Blocklist, Unicode handling, password-hash cost, email verification, proxy/NAT behavior, and deployment security must be reviewed before public exposure. No private knowledge-data endpoint is introduced. Local Data Protection key persistence depends on the host profile; local browser TLS/cookie behavior was validated in 3.5, while deployment/key storage and comprehensive security still require review. Cookie/token/password values must never be logged.

## ADR-009 - Personal Workspace and Ownership

**Date/status:** 2026-10-13. Accepted by the scoped Phase 3.3 implementation request; implementation is completed, approved, and published. Phase 3.2 is completed, approved, and published.

**Context:** the local Identity/session foundation is available and a single personal workspace per user was approved in ADR-008. This slice needs authenticated ownership and persistent, concurrency-safe initialization without coupling account registration to workspace creation.

**Decision:** implement a minimal Domain PersonalWorkspace (Guid Id, string OwnerUserId, UTC CreatedAtUtc). Use Application GetOrCreatePersonalWorkspace and IPersonalWorkspaceStore; ownership is resolved only from ICurrentUser. Validate current account existence through the existing identity-independent account port. Creation time comes from TimeProvider. Resolve/create lazily through authorized `GET /api/workspace`, returning only id/createdAtUtc, always no-store. Register/login remain unchanged and do not initialize a workspace. Atlas terminology and its product relationship remain provisional.

**Persistence/concurrency:** EF Core/SQLite stores PersonalWorkspaces with required fields, an application-generated PK, and a UNIQUE owner index. Application first reads by owner; absent workspaces are inserted. The store recovers only the exact owner-index SQLite UNIQUE conflict, detaches the failed candidate, and rereads the existing workspace so concurrent first accesses return the same ID. No process-global lock, provider switch, generic repository, or new dependency is introduced. Other failures remain operational, sanitized HTTP 500.

**Account reference/lifecycle:** keep OwnerUserId as a logical reference without an Identity navigation, FK, or implicit cascade. This keeps Domain independent and avoids defining deletion behavior prematurely. Deleted accounts cannot pass the current-account check, but existing workspace rows are not deleted automatically. The check and workspace operation are not atomic with account deletion; lifecycle and orphan cleanup must be designed when delete-account is scoped.

**Consequences:** exactly one persisted workspace per owner; sequential access is idempotent and simultaneous inserts converge through database uniqueness. The endpoint accepts no client-supplied owner/workspace ID and does not expose owner ID, email, or claims. It can initialize an empty workspace through GET as explicitly approved. Tests use real migrated, temporary SQLite and deterministic insert synchronization, without sleeps or EF InMemory. AddPersonalWorkspace is an explicit migration; startup remains migration-free.

**Boundaries/risks:** Knowledge Maps remain anonymous, global, in-memory demonstration content, without ownership or new persistence. No workspace CRUD/sharing/teams/roles, frontend auth, map CRUD, JWT, MFA, or PostgreSQL is introduced. SQLite conflict classification is provider-specific and must be adapted/tested during an explicitly authorized PostgreSQL transition. Concurrency recovery is not a blanket retry for database busy/outage or other constraint failures. Authentication/deployment limitations documented in SECURITY.md remain applicable; no production-readiness claim is made.

## ADR-010 - Authenticated Frontend Session Flow

**Date/status:** 2026-10-13. Accepted by the scoped Phase 3.4 implementation request; implementation is completed, approved, and published. Phase 3.3 is completed, approved, and published; Phase 3.5 validation and Phase 3 closure are completed.

**Context:** the published backend provides login/logout/me, native antiforgery, and lazy personal workspace resolution. The existing React demo graph needs a session gate without changing its public map API or introducing new libraries.

**Decision:** use the browser-managed HttpOnly cookie, never an authentication token in JavaScript storage. Keep CSRF only in a private, in-memory session client. Use native fetch with relative same-origin URLs, credentials same-origin, no-store, explicit known error categories, and manual runtime validation. Bootstrap me before workspace; retain only the user's id/email and workspace id/createdAtUtc. Use React state, one custom hook, and conditional rendering without a router/state/form library or Context where no prop-drilling need exists.

**Transitions:** login obtains CSRF, sends credentials once, refreshes CSRF after 204, then reads me/workspace. A 409 rechecks the current account; it does not switch accounts or sign out. csrf_invalid renews once without replaying credentials. Logout obtains current CSRF, retries at most once after csrf_invalid, and treats 204/401 as ended locally. Known protected-read 401 clears local session; network/500/protocol errors are recoverable without assuming logout. New anonymous CSRF is deferred until login. Password remains transient form input, cleared after attempts/unmount, never persisted or logged.

**Consequences:** session and graph state remain separate. The existing Knowledge Maps are still anonymous, global demos, not personal workspace content. Conditional UI is not backend authorization. Request cancellation and identity guards handle StrictMode, retries, and unmounts; session changes discard graph state. Development requires trusted browser-to-Vite HTTPS using externally supplied certificate/key paths and verified Vite-to-API HTTPS, with no insecure proxy fallback.

**Risks/boundaries:** no cross-tab synchronization, polling, production hosting, token persistence, remember-me, frontend registration, workspace CRUD, or additional dependency. Another tab discovers changes only on a protected request/reload; an ambiguous POST/network failure can leave the browser session changed, requiring explicit session retry/reload. Local real-browser cookie/TLS, desktop/tablet/mobile layout, and accessibility smoke checks passed in Phase 3.5, including assisted Chrome Storage/Network inspection. Real touchscreen gestures and full accessibility remain unvalidated. Frontend test tooling and package manager remain open. Phase 3.5 completed Phase 3 closure without a production-readiness claim.

## Decision discipline

Future records must distinguish proposals from accepted decisions. Do not present unreviewed options as commitments. Initial XYFlow / React Flow adoption is accepted under ADR-007; local Identity, registration/password policy, Session Flow protections, and incremental persistence/workspace direction are accepted under ADR-008. ADR-009 defines the initial personal workspace, lazy creation, and authenticated ownership; ADR-010 records the scoped frontend session flow. Further account capabilities, automatic layout, final interaction design, knowledge relationship taxonomy and persistence, frontend testing tools, package manager, and deployment topology remain open.
