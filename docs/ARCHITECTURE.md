# Architecture

## Status and scope

This document records the approved high-level direction. Foundation, Phases 1 and 2, VS-001, Phase 3.1, Phase 3.2 — Identity and Session Backend, and Phase 3.3 — Personal Workspace and Ownership are completed, approved, and published. Phase 3.4 — Authenticated Frontend is implemented and pending review. Further slices require scoped tasks. The separate React frontend now consumes session/workspace endpoints; Knowledge Maps remain anonymous, global in-memory demos.

## Approved direction

| Area | Direction |
| --- | --- |
| Frontend | React and TypeScript, delivered as a responsive single-page application. |
| User interface | Standalone reusable UI components and interactive graph visualization. |
| Graph visualization | XYFlow / React Flow as the initial frontend visualization library. |
| Backend | .NET 10, ASP.NET Core, and C#. |
| Application architecture | Modular monolith. |
| API boundary | REST APIs documented with OpenAPI. |
| Persistence technology | EF Core. |
| Early development database | SQLite for development and prototyping. |
| Production database target | PostgreSQL. |
| Initial account strategy | Local ASP.NET Core Identity; cookie infrastructure in 3.2A, registration in 3.2B.1, published session workflows in 3.2B.2B. |

## High-level responsibilities

- **Frontend:** presents knowledge maps and learning workflows, handles visual interaction and navigation, and communicates with the API.
- **API:** exposes REST operations and translates requests and responses at the application boundary. Business logic must remain outside controllers.
- **Application/domain:** implements use cases and business rules, including the knowledge graph and learning concepts.
- **Infrastructure:** supplies technical integrations and environment-dependent capabilities required by the application.
- **Persistence:** stores and retrieves application data using EF Core and the explicitly selected database provider.

The authorized Phase 2 skeleton uses `Talynor.slnx`, with Api, Application, Domain, and Infrastructure projects under `src/backend`, and UnitTests and IntegrationTests under `tests`. Application references Domain; Infrastructure references Application; Api references Application and Infrastructure for host composition. UnitTests references Domain and Application; IntegrationTests references Api. Infrastructure has no direct Domain reference. This initial responsibility split does not finalize product module boundaries or broader implementation patterns.

`global.json` selects SDK 10.0.401 with `latestPatch` roll-forward and prerelease SDKs disabled. `Directory.Build.props` enables nullable reference types and implicit usings without treating all warnings as errors. The API enables controllers, JSON console logging, `/health` without external dependencies, and OpenAPI only in Development. The React frontend remains separate under `src/frontend/talynor-web`.

## VS-001 read-only validation slice

`GET /api/knowledge-maps/{mapId}` returns `software` or `csharp` (200), or 404 for an unknown ID. The API explicitly maps Application read projections to its own HTTP contracts: map id/title/description/nodes/edges, node id/title/description/progress/studyMinutes/childMapId, and edge id/sourceNodeId/targetNodeId. No coordinates, visual styles, relationship taxonomy, or XYFlow types are exposed.

`GetKnowledgeMap` delegates to `IKnowledgeMapReader` in Application. `InMemoryKnowledgeMapReader` in Infrastructure supplies fixed demonstration content; its externally visible collections are read-only. Domain intentionally remains without artificial models. The reader is registered as a singleton and the use case as scoped. Cancellation propagates through the controller and use case to the reader.

The frontend uses native fetch, basic runtime JSON validation, and an explicit `knowledgeMapAdapter`. Coordinates remain local by mapId/nodeId; unknown layout entries fail explicitly. Dragged positions and selection survive navigation as local visual state; content is fetched again on navigation without a response cache or mock fallback. VS-001 introduces no automatic layout, persistence, authentication, authorization, or backend CORS policy. Vite proxies relative `/api` requests only during local development. Phase 3.1 does not change this flow or persist knowledge maps; the demonstration endpoint remains anonymous.

## Phase 3.1 - Identity persistence foundation

[ADR-008](DECISIONS.md#adr-008---incremental-local-identity-and-persistence) records the approved local Identity, future cookie-session, and personal-workspace direction. Only persistence is implemented in this slice.

`TalynorDbContext` lives in Infrastructure and derives from `IdentityUserContext<IdentityUser>` without roles or an empty custom user class. Its schema contains users, user claims, user logins, and user tokens; these framework tables do not authorize external login or token HTTP endpoints. No workspace, knowledge-map, node, or edge table is included. Domain and Application remain independent of Identity and EF Core, and project-reference directions are unchanged. No explicit ASP.NET Core shared-framework reference was needed in Infrastructure for this persistence-only model.

In 3.1, the API registered only the DbContext with SQLite; 3.2A adds the services described below. The Development connection string is configurable; relative runtime database paths resolve against the API content root. Registering persistence services in DI does not open a connection, create a database file, apply migrations, or seed users. API startup does not perform these database operations either. Health and demonstration reads do not depend on the database. Other environments must configure the connection string before using persistence.

Infrastructure owns the explicit `InitialIdentity` migration and a design-time factory. The factory requires `ConnectionStrings__Talynor` rather than guessing a database, and does not start the API. EF commands use the versioned local tool manifest. See [Development](DEVELOPMENT.md#phase-31---identity-persistence) for database selection and lifecycle commands. No automatic startup migration or `EnsureCreated` is used.

## Phase 3.2A - Session HTTP foundation and CSRF bootstrap

In 3.2A, the API composed `AddIdentityCore<IdentityUser>()`, the existing EF stores, and `AddSignInManager()` without roles, UI, account endpoints, extra token providers, or schema changes; Domain and Application were untouched by that slice. `AddApplicationCookie()` registers `Identity.Application` and preserves `SecurityStampValidator`; redirect callbacks return 401/403 instead of HTML navigation. 3.2B.2A adds only the technical auxiliary scheme described below; External and TwoFactorUserId remain absent. 3.2B.1 adds registration and Application account abstractions below.

The application cookie is `__Host-Talynor.Auth`, HttpOnly, Secure Always, SameSite Lax, Path `/`, with no Domain. Tickets have an eight-hour lifetime without sliding expiration. 3.2B.2B introduces non-persistent sign-in without browser Expires/Max-Age. Authorization services and middleware are registered without a global fallback policy. Health, demonstration maps, and CSRF bootstrap remain anonymous; OpenAPI remains Development-only. 3.2A introduced no CORS or rate limiter; later endpoint-only limits and explicit routing placement are described below.

3.2A introduced `GET /api/auth/csrf`, calling native `IAntiforgery.GetAndStoreTokens` and returning `{ "token": "..." }` with `Cache-Control: no-store`. The separate `__Host-Talynor.Csrf` cookie is HttpOnly, Secure Always, SameSite Strict, Path `/`, without Domain; the request header is `X-CSRF-TOKEN`. The cookie token is never included in JSON. MVC uses `AddControllersWithViews` to register native antiforgery filters, without views, Razor Pages, or Identity UI. `[AutoValidateAntiforgeryToken]` skips GET and protects register in 3.2B.1. Token refresh across authentication changes belongs to 3.2B.2B; frontend consumption belongs to 3.4.

Production code keeps the default local Data Protection setup without a custom key ring. Startup and CSRF bootstrap do not create/migrate SQLite or seed users. Tests check native token-pair validity, host cookie/scheme configuration, 401/403 callbacks, unchanged runtime EF model, and the existing anonymous endpoints.

## Phase 3.2B.1 - Account registration

`POST /api/auth/register` accepts an independent email/password DTO. The API validates structure and UTF-16 limits, applies the existing native antiforgery filter, and rejects an already authenticated request with 409. `RegisterUser` in Application trims only the email and delegates account creation to `IIdentityAccountService.CreateAsync`; 3.2B.2B adds only an account lookup by ID. Its specific Success/Invalid/Duplicate results contain no HTTP, Identity, EF, or SQLite types. Domain remains unchanged.

`IdentityAccountService` in Infrastructure creates `IdentityUser` through `UserManager.CreateAsync`. Email and username receive the same trimmed value, with Identity owning normalized fields. The adapter translates known Identity validation results; only SQLite's specific single-column UNIQUE failure on `AspNetUsers.NormalizedUserName` is translated into a concurrent duplicate. PK, busy, other constraint failures, and unknown validation failures remain operational errors. The existing migration/model is unchanged.

Success is 201 without body, Location, auto-login, or auth cookie. Duplicate registration returns generic 400 `registration_failed`; input/policy failures use 400 `validation_failed`, and operational failures use sanitized 500 `unexpected_error`. An endpoint-only result filter sanitizes automatic model-binding errors and translates native antiforgery failure to 400 `csrf_invalid`. Registration responses are no-store and use native ProblemDetails/ValidationProblemDetails; no global exception or request middleware is introduced for registration.

The native registration policy remains five requests per ten-minute fixed window per remote IP, no queue; the separate login policy is described below. IPv4-mapped IPv6 is normalized; absent IP uses a shared restricted partition. Forwarded headers are not trusted. Pipeline order is HTTPS redirection, routing, authentication, authorization, rate limiting, and controllers. An admitted request consumes quota before MVC validates CSRF/input. Rejection is 429 `rate_limited`, with Retry-After when available. The action's native request-size limit is 8 KiB on servers supporting that feature; TestServer is not a real Kestrel/body-limit verification.

The account/password policy and local-use limitations are recorded in ADR-008 and SECURITY.md. No login, logout, me, current-user abstraction, login limiter, custom lockout, frontend change, or SecurityStampValidator change is included in registration. The separately reproduced rejection-path incompatibility is addressed below.

## Phase 3.2B.2A - Security Stamp compatibility

Identity 10.0.12's native validator explicitly signs out `Identity.TwoFactorRememberMe` after rejecting an invalid stamp. With that handler absent, a real cookie/SQLite/SignInManager test reproduced an InvalidOperationException after the default 30-minute validation interval. The approved correction uses only the official `AddTwoFactorRememberMeCookie()` helper alongside `AddApplicationCookie()`. `Identity.Application` remains the sole application authentication scheme and the default for authenticate, challenge, forbid, and sign-in; `Identity.TwoFactorRememberMe` is a technical auxiliary sign-out handler. No External or TwoFactorUserId scheme is registered.

The native `SecurityStampValidator<IdentityUser>`, its callback, 30-minute interval, and all application-cookie options remain unchanged. No Talynor flow issues the auxiliary cookie, and MFA is not implemented. Native auxiliary-cookie defaults are retained; they do not replace the application-cookie policy. Regression tests use real cookies, UserManager stamp updates, and a test-only TimeProvider: a valid stamp remains authenticated and is renewed by the native validator; an invalid stamp after the interval returns 401 without exception, deletes cookies, and rejects replay of the original ticket. Both diagnostic routes exist only in the test factory and return 404 in the normal API host. No schema, dependency, frontend, or session endpoint change is introduced.

## Phase 3.2B.2B - Session Flow

`LoginUser`, `GetCurrentUser`, `ICurrentUser`, and `ISessionService` belong to Application and expose only own projections/results. `IdentitySessionService` is scoped in the API because native SignInManager needs the current HTTP response; it finds the Identity account by email and calls `PasswordSignInAsync` with isPersistent=false and lockoutOnFailure=true. Missing accounts and native authentication failures share InvalidCredentials; operational exceptions propagate to sanitized HTTP 500. There is no extra logout use case: the authorized controller delegates to the session adapter without ceremonial orchestration.

`HttpCurrentUser` is scoped in the API and reads IHttpContextAccessor. Missing context, anonymous identity, missing/blank/duplicate NameIdentifier, or multiple authenticated identities yield false/null. Email is never an identity fallback. `/me` uses this ID and the minimal `IIdentityAccountService.GetByIdAsync` extension; Infrastructure reads current Identity data and returns AccountSummary, never IdentityUser. Missing accounts/invalid IDs result in 401 and local sign-out; database errors do not become missing accounts.

Native exception handling is restricted to login/me/logout and the current-workspace paths, before authentication middleware, so a database failure during native cookie/stamp validation also returns no-store 500 unexpected_error rather than raw diagnostics. This is not a global handler or a replacement for SecurityStampValidator; registration and other endpoints retain their existing behavior. Controller-level operational failures use the same sanitized response.

A login-only response callback suppresses positive application-cookie renewal on failed login responses, including an already-authenticated 409 after the stamp interval. It preserves deletion headers and never changes the native validator/callback. Normal authenticated reads still allow native valid-stamp renewal.

Anonymous `POST /api/auth/login` accepts independent email/password input, trims only email, and validates format plus 256/128 UTF-16 maxima. Password is not normalized, truncated, or rechecked against creation policy. Success is empty 204 with the existing session cookie; native failures are uniform 401 invalid_credentials. An existing authenticated session returns 409 already_authenticated without account switching or cookie issuance. `GET /api/auth/me` and `POST /api/auth/logout` require Authorize. Me returns only id/email; logout uses native SignOutAsync and returns empty 204. All responses are no-store, including challenges (401 JSON) and forbids (403 JSON), with no redirects. The existing endpoint-only RegistrationProblemDetails filter is reused for session input/CSRF sanitization; its registration behavior is preserved.

Native lockout allows new users, uses five failures and a five-minute duration. Login has its own fixed-window policy of ten requests per minute per normalized remote IP, no queue, a shared missing-IP bucket, and no forwarded-header trust. Registration quota is unchanged. Login uses the native 8 KiB action limit where supported. Login/logout require the existing native CSRF filter; GET me does not. Clients must bootstrap a new request token after login/logout because identity changes, without assuming tokens are globally revoked or one-time.

Multiple browsers may hold independent sessions; a new login does not invalidate earlier sessions. Logout deletes the local browser cookie, not all tickets or other browsers. No session table, global revocation, or single-session enforcement is added. Unconfirmed email is explicitly permitted; no confirmation/reset sender or provider is introduced. The B.2A native validator and technical auxiliary handler are preserved without functional MFA or auxiliary-cookie issuance. Domain, EF model/migrations, production Data Protection, dependencies, and frontend are unchanged.

## Phase 3.3 - Personal Workspace and Ownership

Completed, approved, and published under [ADR-009](DECISIONS.md#adr-009---personal-workspace-and-ownership). `PersonalWorkspace` is the first Domain entity, containing only an application-generated Guid ID, immutable owner user ID string, and UTC creation timestamp. It has no dependency on Identity or EF. Atlas remains provisional; this technical workspace does not define a separate Atlas entity or finalize their product relationship.

`GetOrCreatePersonalWorkspace` in Application resolves ownership exclusively from `ICurrentUser`, verifies the current account still exists through the existing account port, and returns its own projection. It receives `TimeProvider` for creation time, reads by owner, and creates only when absent. `IPersonalWorkspaceStore` exposes owner lookup and add-or-return-existing behavior; no generic repository or HTTP/Identity/EF types are introduced in Application. Register and login do not create a workspace.

The scoped Infrastructure store uses EF Core/SQLite. `PersonalWorkspaces` has an explicit PK, required owner/time, and UNIQUE index on OwnerUserId. On the exact SQLite owner UNIQUE conflict, it detaches the losing candidate and rereads the winner; all other provider failures propagate. Independent request contexts and the database index enforce idempotence/concurrency without an in-memory lock. SQLite-specific conflict recognition remains confined to Infrastructure and requires adaptation when PostgreSQL is explicitly scoped. Existing project-reference directions remain unchanged; Infrastructure accesses Domain through its existing Application reference.

`GET /api/workspace` requires Authorize, accepts no owner or workspace identifier, lazily resolves/creates the authenticated user's workspace, and returns 200 with only id/createdAtUtc and no-store. Anonymous/invalid/deleted-account sessions return sanitized 401; absent accounts cause local sign-out. Operational failures, including native stamp validation before the controller, remain sanitized 500. The existing restricted exception handler now includes only this additional endpoint. This approved GET can initialize an empty personal workspace; it has no client-controlled mutation payload or content CRUD. Login/logout CSRF and cookie protections are unchanged.

OwnerUserId is a logical Identity reference with no navigation/FK/cascade. Account removal is not a workspace cleanup mechanism: orphan records can remain, and deletion lifecycle requires future design. The current account check prevents an already-removed account from accessing its workspace; it is not an atomic account-deletion lifecycle transaction. Knowledge Maps remain global, anonymous, demo, and in-memory. Frontend authentication is described below. Migrations stay explicit; startup never migrates, initializes, or seeds a database.

## Phase 3.4 - Authenticated Frontend

Implemented and pending review under [ADR-010](DECISIONS.md#adr-010---authenticated-frontend-session-flow). App uses a small `useSession` custom hook and conditional rendering; no Context/Provider, router, form framework, or application state library is needed. `sessionClient` coordinates the HTTP sequence independently of React; `authApi`, `workspaceApi`, and a small native-fetch helper retain status/error categories and validate external JSON. Known ProblemDetails codes are mapped to local messages; raw server titles/details are not rendered.

Bootstrap calls `/api/auth/me`, then `/api/workspace` only after a valid user response. Only id/email and workspace id/createdAtUtc are retained in state. A 401 on either protected read leads to anonymous login; network/500/protocol failures lead to an error/retry gate, not an inferred logout. Anonymous CSRF is fetched when submitting login. Login uses X-CSRF-TOKEN and on 204 refreshes CSRF before reading me/workspace. A 409 rechecks me without switching accounts. Invalid login CSRF gets one renewal and requires a new user submission; credentials are never automatically replayed.

The browser owns the HttpOnly auth cookie. Fetch uses relative `/api`, credentials same-origin, and no-store; no auth token or CSRF is persisted in browser storage. CSRF remains private to a per-hook session client, is never rendered/logged, and is renewed before unsafe actions and after login. Logout may renew/retry once after csrf_invalid; 204/401 clears local session and old CSRF, with new anonymous CSRF deferred until next login. Other logout failures preserve the shell with recoverable feedback. AbortController and request identity protect retry/unmount/StrictMode against stale state updates; there is no cross-tab synchronization or polling.

`KnowledgeGraph` contains the existing map navigation, selection, dragged positions, loading/retry, adapter, and XYFlow behavior. It mounts only after session/workspace bootstrap and unmounts on logout/expiration, aborting pending map requests and resetting visual state. Map errors remain separate and never end the session. The session gate is UX protection, not authorization of demo content: the map API remains anonymous and is not linked to personal workspace persistence.

Vite retains the development same-origin proxy, now requires an HTTPS API target and trusted external certificate/key paths for browser-to-Vite HTTPS, and rejects the historical insecure proxy flag. It does not generate/trust certificates or alter the backend. Production same-origin routing/TLS, frontend test tooling, package manager, real-device interaction, and full accessibility remain separately scoped.

## Knowledge graph

Knowledge nodes and their relationships are domain concepts. A node may lead to another map, and relationships must eventually represent connections beyond a tree or folder hierarchy.

A graph-shaped domain does not require a graph database. The initial direction uses relational persistence. Relationship types, directionality, cycle rules, map membership, and storage representation have not been finalized. Examples such as contains, prerequisite, related to, and depends on do not establish a final relationship taxonomy.

## Visual experience and early validation

The interactive graph experience is a primary technical risk. A dedicated frontend spike precedes the application skeleton to validate nodes, edges, zoom, pan, selection, drill-down, submaps, and navigation between knowledge levels.

SPIKE-001 validated custom nodes, edges, selection, dragging, zoom, pan, minimap, drill-down, TypeScript, basic responsiveness, and basic accessibility; build and lint passed. XYFlow / React Flow is now adopted as the initial graph visualization library, as recorded in [ADR-007](DECISIONS.md#adr-007---initial-xyflow-graph-visualization). Larger graph performance, touch behavior on real devices, and full accessibility remain to be validated. Adoption can be reevaluated if concrete limitations emerge; no artificial node-count limit is defined.

XYFlow belongs exclusively to the frontend visualization layer. Domain and application logic, API contracts, and persistence must not depend on XYFlow-specific types or expose its Node/Edge structures. The spike's view types do not define domain entities or storage models. Final interaction design and automatic layout remain open; adoption does not finalize relationship semantics or persistence.

## Database strategy

SQLite supports the early development and prototyping stages. PostgreSQL is the production target.

Persistence should avoid unnecessary provider-specific behavior. This does not assume that the providers are interchangeable: provider changes must be explicit, reviewed, and validated, including their data and migration implications.

Migrations must be controlled. Do not provision PostgreSQL, configure a database, or create migrations during the foundation task.

Introducing PostgreSQL infrastructure requires an explicit request; advancing to another roadmap phase does not by itself authorize it.

## Testing and operations direction

- Use xUnit for backend unit and integration testing.
- Use Testcontainers when justified by the test's purpose.
- Define frontend automated testing later.
- Use structured logging and health checks.
- Introduce OpenTelemetry when justified.
- Use environment-based configuration and keep secrets outside source control.
- Enable C# nullable reference types when the application skeleton is created.
- Introduce GitHub CI later in the roadmap.

These directions do not authorize tooling installation or operational configuration in the current phase.

## Architecture principles and exclusions

Start with a modular monolith and prefer simplicity over infrastructure complexity. Keep architecture explicit and business logic out of controllers. Do not introduce a distributed architecture prematurely.

The initial scope excludes microservices, Redis, RabbitMQ, Kafka, Kubernetes, Elasticsearch, and graph databases. Background workers require later justification and approval. AI, marketplace, and social networking features are outside the initial product scope.

## Open design questions

Local ASP.NET Core Identity, cookie/antiforgery infrastructure, account registration/password policy, and Session Flow/lockout are approved under ADR-008; the initial personal workspace is defined in ADR-009. Account recovery and verification, workspace lifecycle, and future workspace operations still require scoped implementation/design. Product module boundaries, automatic layout, final interaction design, relationship taxonomy and knowledge persistence representation, further business API contracts, frontend test tooling, package manager, deployment topology, and production migration procedures remain open. Local Identity migrations are explicit as documented above; PostgreSQL infrastructure remains unauthorized.
