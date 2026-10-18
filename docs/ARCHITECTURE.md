# Architecture

## Phase 5 - Knowledge Content closure

Complete, approved and published for the agreed Note/Link scope through c450b91, 5aa7d4e and d4e5c76. KnowledgeNode 1 → N KnowledgeContent, private relational persistence, separate authenticated list/detail reads and CSRF-protected authoring satisfy the roadmap's initial types, create/view/edit flows and validation/access boundaries. Required node FKs prevent orphan content; permitted node deletion cascades contents and permitted map deletion cascades through nodes, preserving node_in_use/map_in_use restrictions.

File upload/storage was not included and remains future work, as do specialized Article/Video, Flashcard, Quiz, Exercise and Checklist capabilities. Unpaginated reads, no search/reordering, no content-driven automatic layout, limited frontend lifecycle coverage and the existing last-committed-write baseline remain known limits rather than unmet exit requirements. There is no ETag/rowversion or cross-tab coordination guarantee. The next official phase is Phase 6 — Study Sessions, not started and requiring separate implementation approval; PostgreSQL infrastructure still requires an explicit request.

## Phase 5 - Frontend Content Integration

Complete, approved and published in d4e5c76 as authorized follow-through of ADR-013; the agreed Phase 5 scope is complete. The published backend/schema is unchanged. App/sessionClient exposes a content authoring client bound to the same private WithCsrf lifecycle. Own discriminated Note/Link DTOs and runtime validators reject invalid IDs/types/timestamps/payloads before retaining only contract fields. GET map is not expanded and XYFlow stays presentation-only.

MapWorkspace owns node selection and the single synchronous useAuthoring mutation coordinator. ContentSection mounts per persisted node and view/edit mode, owning selected content and form state; useContentRead separates list/detail loading, errors and explicit retry. Each read has AbortController, cleanup and render identity; node/map/session changes discard obsolete data/drafts. Reads revalidate session once on 401 without replay. List is fetched only for a selected node; opening an item fetches its detail rather than all Note bodies.

Edit map enables create/edit/confirmed hard delete. Fields use existing UTF-16 limits; title/URL normalization and an explicit PATCH builder omit unchanged fields, use clearTitle and disable material no-op saves. Creates wait for validated 201; PATCH/delete wait for 204. Lists are refetched in server order after mutations, affected detail is fetched again without fabricated timestamps, and deleting the last item returns the normal empty state. One shared lock prevents incompatible content and graph mutations; sent writes are not aborted and completions from old node contexts are ignored.

Content selection adds no URL or History entry. Notes render as React-escaped plain text with preserved line breaks; Links show the destination and open only explicitly with target=_blank and rel=noopener noreferrer. No preview/fetch/iframe exists. Content read/write failures preserve the shell/map; csrf_invalid invalidates the private token and no write is replayed after CSRF, 401, network or 500. Ambiguous results require checking current state before another explicit attempt. No backend, migration, dependency, new framework or broader concurrency policy is introduced.

## Phase 5 - Content Authoring Backend

Complete, approved and published in 5aa7d4e as approved follow-through of ADR-013; the agreed Phase 5 scope is complete. Foundation/read is complete, approved and published in c450b91. CreateKnowledgeContent, UpdateKnowledgeContent and DeleteKnowledgeContent reuse validated current-account/lazy workspace resolution and a separate IKnowledgeContentStore. Infrastructure scopes node/content lookups by workspace/map/node in SQL, tracks writes and uses one atomic SaveChanges without an explicit transaction wrapper. Only confirmed create-parent FK disappearance or zero-row update/delete becomes content_not_found; unrelated constraints/provider failures remain operational errors. The reader stays no-tracking and its SQL ordering stays unchanged.

Domain reuses the same complete-candidate validation for creation and UpdateDetails; identity, node, type and creation time remain immutable. Applicable omitted/null PATCH fields preserve state; clearTitle removes the optional title. Inapplicable body/url members are rejected even when explicitly null. A material no-op is validation_failed without saving or changing UpdatedAtUtc; valid changes use Application TimeProvider. Unlike the older graph PATCH baseline, repeating identical content values does not advance its timestamp. Failed validation never partially mutates a tracked candidate.

A separate authorized native-antiforgery controller exposes POST collection (201, Location and the same detail DTO/mapping as GET), PATCH detail and DELETE detail (204). Strict request DTOs reject unknown JSON, type changes and client identities/ownership/timestamps; type values are exactly lowercase note/link. Existing endpoint-only input/CSRF sanitization, cookie/stamp handling and no-store apply. Native 32 KiB metadata covers all three actions as in graph authoring; TestServer metadata checks do not establish Kestrel enforcement. No new token, automatic replay, schema/migration, dependency or frontend source change is introduced. Existing node/map conflicts and cascades remain. Modified-column last-committed-write behavior remains without ETag/rowversion; disjoint changes can merge and timestamps are not commit ordering.

## Phase 5 - Knowledge Content Foundation

Foundation/read is complete, approved and published in c450b91 under [ADR-013](DECISIONS.md#adr-013---initial-node-owned-knowledge-content). The first approved slice is Note and Link Content; backend authoring is published and frontend integration is described above. Domain KnowledgeContent belongs to exactly one KnowledgeNode, with no duplicated workspace/map/owner IDs, no structured cross-content references and no XYFlow dependency. Factories validate bounded optional title, literal Note body or HTTP(S) Link URL and take an explicit UTC-normalized timestamp. Foundation originally added no write use case or frontend.

Infrastructure maps one KnowledgeContents table with explicit note/link enum conversion, payload/type CHECKs, required node FK Cascade and (KnowledgeNodeId, CreatedAtUtc, Id) index. Content-only timestamp conversion to UTC DateTime storage permits SQL ordering while Domain/API retain UTC DateTimeOffset; existing timestamp mappings stay unchanged. Node deletion still requires removal of edges first; permitted map→node deletion cascades contents without bypassing ChildMap restrictions. Migration AddKnowledgeContent is explicit and startup does not migrate/create/seed.

ListKnowledgeContentsForNode and GetKnowledgeContentForNode reuse existing account/workspace resolution, then IKnowledgeContentReader performs SQL ownership filtering through map/node. List uses a parent-and-summary read snapshot (two queries, no N+1); detail uses one scoped projection. Summaries omit Note bodies, details expose only the applicable body/url; HTTP contracts contain no ownership IDs. Two authorized GETs under /api/maps/{mapId}/nodes/{nodeId}/contents return no-store without CSRF; foreign/missing/mismatched paths share 404 content_not_found, empty owned nodes return an empty envelope, and operational/stamp errors remain sanitized 500. Map read DTOs are unchanged. Content UI is described above; files, Markdown/HTML, embeds, previews and study features remain unimplemented. The 32 KiB authoring limit above is not applied to GET.

## Phase 4.4 - Authoring UX

Complete, approved and published in `c427c6e` over the unchanged Phase 4.2 contracts. MapWorkspace owns per-visit visual state and explicit edit mode; KnowledgeGraph receives callbacks and performs no HTTP/auth/ownership work. MetadataForm holds local drafts, CreateMap handles the empty/picker entry, and knowledgeMapAuthoringApi translates own request/201 DTOs, validates responses and uses the existing native-fetch transport. No new dependency or architecture ADR is needed.

The per-hook session client exposes a CSRF-protected action using its existing private in-memory token lifecycle. Every authoring attempt renews CSRF, sends one mutation and never replays automatically, including csrf_invalid, 401 and ambiguous network/500 responses. Login/logout policies stay unchanged. A 401 invokes existing session revalidation; a valid session retains a recoverable error and explicit retry.

Creates wait for validated 201; metadata/deletes wait for 204 before updating local state. PATCH sends only changed metadata, with explicit clearDescription/clearChildMap. Child choices come from current-workspace discovery; self links, cycles, self edges and duplicate edges follow backend rules. Initial node position is the predictable (node count × 240, 0), without a layout engine. Edit-mode drag stays local until stop; the PATCH translates only finite x/y, stores the confirmed position and rolls back on failure. Keyboard movement has an explicit Save position action. View-mode movement remains temporary.

One mutation per mounted editor is admitted synchronously; controls and dragging are disabled while pending. Per-node position versions and per-mount completion guards prevent old responses overwriting a newer visit. Sent writes are not aborted on navigation, because abort cannot establish rollback; completions from an unmounted editor are ignored. Returning to the picker refetches server-ordered discovery, keeping create/rename/delete summaries authoritative without a persistent cache. Drafts may be discarded by navigation; no unsaved-change blocker or cross-tab concurrency guarantee is introduced. Reopen/reload reconciles ambiguous outcomes. The original URL/History strategy remains.

## Status and scope

This document records the approved direction. Phase 3 and Phase 4, including 4.1, 4.2, 4.3A, 4.3 and 4.4, are complete, approved and published. Phase 4 delivers persistent private maps/nodes/edges, frontend discovery and explicit picker, History API/deep links, authoring, persisted positions and ChildMap, with authenticated CSRF-protected writes and server-derived workspace ownership. Existing limitations remain; this is not production readiness. Phase 5 — Knowledge Content and all three approved increments are complete, approved and published under ADR-013. Phase 6 — Study Sessions is next/planned and not started. Later implementation requires scoped authorization.

The published backend includes Identity with EF Core/SQLite, cookie authentication, antiforgery, and persistent owned PersonalWorkspace records. The React frontend includes session bootstrap, login, me/workspace loading, logout, an authenticated shell, and the persisted KnowledgeGraph view. Real-browser HTTPS validation is completed for these Phase 3 local flows. Phase 4.1 adds private persistent knowledge content alongside the unchanged anonymous demo source; the current authenticated frontend consumes private persistence under Phase 4.3 below.

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

In VS-001, the frontend used native fetch, basic runtime JSON validation, and an explicit `knowledgeMapAdapter`. Coordinates remain local by mapId/nodeId; unknown layout entries fail explicitly. Dragged positions and selection survive navigation as local visual state; content is fetched again on navigation without a response cache or mock fallback. VS-001 introduces no automatic layout, persistence, authentication, authorization, or backend CORS policy. Vite proxies relative `/api` requests only during local development. Phase 3.1 does not change this flow or persist knowledge maps; the demonstration endpoint remains anonymous.

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

OwnerUserId is a logical Identity reference with no navigation/FK/cascade. Account removal is not a workspace cleanup mechanism: orphan records can remain, and deletion lifecycle requires future design. The current account check prevents an already-removed account from accessing its workspace; it is not an atomic account-deletion lifecycle transaction. Phase 3.3 kept Knowledge Maps as global, anonymous, in-memory demos; Phase 4.1 adds the separate private persisted source below. Frontend authentication is described below. Migrations stay explicit; startup never migrates, initializes, or seeds a database.

## Phase 3.4 - Authenticated Frontend

Completed, approved, and published under [ADR-010](DECISIONS.md#adr-010---authenticated-frontend-session-flow), with local real-browser validation completed in Phase 3.5. App uses a small `useSession` custom hook and conditional rendering; no Context/Provider, router, form framework, or application state library is needed. `sessionClient` coordinates the HTTP sequence independently of React; `authApi`, `workspaceApi`, and a small native-fetch helper retain status/error categories and validate external JSON. Known ProblemDetails codes are mapped to local messages; raw server titles/details are not rendered.

Bootstrap calls `/api/auth/me`, then `/api/workspace` only after a valid user response. Only id/email and workspace id/createdAtUtc are retained in state. A 401 on either protected read leads to anonymous login; network/500/protocol failures lead to an error/retry gate, not an inferred logout. Anonymous CSRF is fetched when submitting login. Login uses X-CSRF-TOKEN and on 204 refreshes CSRF before reading me/workspace. A 409 rechecks me without switching accounts. Invalid login CSRF gets one renewal and requires a new user submission; credentials are never automatically replayed.

The browser owns the HttpOnly auth cookie. Fetch uses relative `/api`, credentials same-origin, and no-store; no auth token or CSRF is persisted in browser storage. CSRF remains private to a per-hook session client, is never rendered/logged, and is renewed before unsafe actions and after login. Logout may renew/retry once after csrf_invalid; 204/401 clears local session and old CSRF, with new anonymous CSRF deferred until next login. Other logout failures preserve the shell with recoverable feedback. AbortController and request identity protect retry/unmount/StrictMode against stale state updates; there is no cross-tab synchronization or polling.

In the historical 3.4 slice, `KnowledgeGraph` contained demo map navigation, selection, dragged positions, loading/retry, adapter, and XYFlow behavior. At that stage it mounted after session/workspace bootstrap and demo errors remained separate. The gate did not authorize demo content; Phase 4.3 below replaces the authenticated client source and handles private-read 401 through session revalidation.

Vite retains the development same-origin proxy, now requires an HTTPS API target and trusted external certificate/key paths for browser-to-Vite HTTPS, and rejects the historical insecure proxy flag. It does not generate/trust certificates or alter the backend. Production same-origin routing/TLS, frontend test tooling, package manager, real-device interaction, and full accessibility remain separately scoped.

## Phase 3.5 - Validation and Phase Closure

Completed on 2026-10-13 using explicitly migrated disposable SQLite and an existing trusted localhost certificate, with exported PEM/key files outside the repository. Browser session, CSRF, workspace, refresh/logout, graph/error isolation, desktop/tablet/mobile layout, and accessibility smoke checks passed. Chrome Storage and Network metadata were confirmed through assisted manual inspection. Backend build, 33 unit tests, 111 integration tests, frontend build/lint, and the unchanged EF model passed; no code correction or weakened TLS was required. This closes Phase 3, without a public-deployment or full-accessibility claim.

## Knowledge graph

Knowledge nodes and their relationships are domain concepts. A node may lead to another map, and relationships must eventually represent connections beyond a tree or folder hierarchy.

A graph-shaped domain does not require a graph database. ADR-011 defines the initial relational storage, positions, same-map edge endpoints and same-workspace drill-down. Broader cross-map behavior, cycle rules and relationship semantics remain open. Examples such as contains, prerequisite, related to, and depends on do not establish a final relationship taxonomy.

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

Local ASP.NET Core Identity, cookie/antiforgery infrastructure, account registration/password policy, and Session Flow/lockout are approved under ADR-008; the initial personal workspace is defined in ADR-009 and persistent knowledge storage in ADR-011. Account recovery and verification, workspace lifecycle, and future workspace operations still require scoped implementation/design. Product module boundaries, automatic layout, final interaction design, relationship taxonomy, authoring concurrency, further business API contracts, frontend test tooling, package manager, deployment topology, and production migration procedures remain open. Migrations are explicit; PostgreSQL infrastructure remains unauthorized.

## Phase 4.1 - Knowledge Map Persistence Foundation

Complete, approved and published under [ADR-011](DECISIONS.md#adr-011---persistent-knowledge-graph-foundation). The minimal relational KnowledgeMap/Node/Edge model, Guid identities, bounded titles/descriptions, finite positions, timestamps and same-workspace ChildMap are unchanged structurally. 4.2 passes timestamps from Application to Domain factories/mutations; TimeProvider is not injected into entities. Initial update time equals creation time; CreatedAtUtc is preserved.

Ownership is CurrentUser → validated existing account → PersonalWorkspace → KnowledgeMap → nodes/edges. `GetKnowledgeMapForCurrentWorkspace` reuses approved lazy workspace resolution; first access may initialize an empty workspace but never seed a map. `IPersistedKnowledgeMapReader` accepts only the server-resolved workspace and map ID. The scoped EF reader's GetAsync uses no-tracking projections and one read transaction for a consistent map/nodes/edges snapshot; 4.3A adds the separate summary query described below. Application has no EF, HTTP, SQLite or XYFlow types. There is no unused write repository; explicit test fixtures persist through the Infrastructure DbContext.

Private `GET /api/maps/{mapId:guid}` returns own API contracts: id/title/description/nodes/edges; nodes include id/title/description/position:{x,y}/childMapId; edges include id/sourceNodeId/targetNodeId. Workspace/owner IDs, Identity data and demo study metrics are absent. Anonymous/invalid/deleted-account sessions return 401; unknown and foreign-workspace map IDs return the same 404 body, never existence-revealing 403. Responses are no-store. The restricted exception handler also covers this route before native cookie validation; operational errors are sanitized 500, not missing maps.

Explicit FKs: workspace→map Restrict; map→nodes/edges Cascade; child map→node Restrict; source/target node→edge composite (KnowledgeMapId, node ID) NoAction. Deleting a referenced node requires removing edges first; deleting a referenced child requires clearing incoming links first. 4.2 adds controlled delete endpoints without changing these constraints. A composite node alternate key enforces same-map edges; minimal workspace, child and endpoint indexes support these constraints/reads. No unique edge-pair rule, self-loop ban or acyclic hierarchy is invented.

SQLite triggers reject cross-workspace child links on insert/update and map-workspace changes, without duplicated workspace data on nodes. A finite-position CHECK complements Domain validation. Trigger definitions are explicitly migration-owned, represented in EF metadata and reviewed during rollback; future table rebuilds/provider transitions must preserve or replace them. A read transaction is not authoring concurrency control: rowversion, ETags and optimistic update handling are deferred.

AddPersistentKnowledgeGraph follows InitialIdentity and AddPersonalWorkspace, adding only graph tables/constraints/indexes/triggers. No startup migration, EnsureCreated, personal-map seed, PostgreSQL or graph database is added. `/api/knowledge-maps/software` and `/csharp` remain anonymous in-memory demo regressions; the frontend used them through Phase 3. 4.2 added backend writes; 4.3 adds private persisted frontend rendering below. At Phase 4.1 delivery, browser drag-save and authoring UI were future work; they are now delivered by 4.4 above. Layout automation remains open.

## Phase 4.2 - Map/Node/Edge Authoring Backend

Eight small Application use cases and IKnowledgeMapStore expose map/node create, PATCH and delete and edge create/delete over the existing schema. All store lookups filter the nested map/current workspace in SQL. Application resolves TimeProvider and supplies timestamps; Domain validates complete candidates before mutation. IDs, ownership/map membership and CreatedAtUtc cannot change. Detail/position/ChildMap mutations advance node UpdatedAtUtc; map metadata mutations advance its timestamp, and edges stay immutable.

A separate write controller uses Authorize, native AutoValidateAntiforgeryToken, the existing endpoint-only input/CSRF sanitizer and pre-auth exception handler. Its native 32 KiB body limit accommodates bounded text including JSON Unicode escapes; unknown DTO fields are rejected. Creates return 201 with own projections; map creation supplies Location to its read endpoint. PATCH/delete returns 204; 4.2 added no list/global node/edge endpoint or authoring limiter; 4.3A adds only the authenticated collection read below.

PATCH uses optional DTO values plus clearDescription/clearChildMap flags: omitted/null values keep state; clear flags remove nullable values. Clear-plus-value or empty PATCH is validation_failed. position:{x,y} replaces the coordinate pair. Child maps must exist in the same workspace; edge endpoints in the same map. Self-loops and exact duplicate edges remain allowed. Target missing/foreign resources get uniform 404; missing/foreign relation references get uniform 400 invalid_child_map or invalid_edge_nodes.

One native atomic SaveChanges persists each operation. Application prechecks deletion references; existing FK/trigger defenses remain. Referenced child-map/node deletes return 409 map_in_use/node_in_use with no silent edge removal. Specific SQLite FK/known equality-trigger errors are rechecked after rollback; only confirmed expected conditions are translated. Busy, PK, unknown/unclassified failures remain sanitized 500. Zero-row tracked UPDATE/DELETE returns not found.

No concurrency token/ETag/If-Match is added. Same changed-column updates are last committed write wins; EF writes only properties detected as changed, allowing disjoint changes to merge. UpdatedAtUtc is not a monotonic commit sequence. Constraints protect relation races without a blanket retry policy. Phase 4.2 left the DbContext model, migration/snapshot, reader, providers and frontend unchanged. Later read/frontend extensions are described below.

## Phase 4.3A - Persisted Map Discovery

Complete, approved and published. Authenticated GET /api/maps returns an own API envelope { "maps": [{ "id": "...", "title": "..." }] }; an empty workspace returns 200 with { "maps": [] }, never 404 or an automatically created map. This adds discovery without a default/root map, automatic selection, list UI or React integration.

ListKnowledgeMapsForCurrentWorkspace reuses GetOrCreatePersonalWorkspace: ICurrentUser → existing account → server-resolved PersonalWorkspace → workspace-scoped summaries. IPersistedKnowledgeMapReader is extended with ListAsync rather than introducing another store. Infrastructure issues one AsNoTracking SELECT, filtering WorkspaceId in SQL and projecting only ID/title without loading graph tables. Ordering is database Title ascending (current SQLite default BINARY collation), then stored Id ascending for ties. This is deterministic database ordering, not culture-aware or user-defined ordering, and does not select a map.

The existing private read controller maps Application summaries to independent HTTP DTOs. All discovery responses are no-store; anonymous/invalid/removed accounts get sanitized 401 with the existing local sign-out behavior, and operational/account/reader/pre-controller stamp failures remain sanitized 500 rather than empty maps. Client query parameters cannot choose a workspace. GET requires no CSRF header; existing unsafe authoring protections remain unchanged.

There is no pagination, search, filter, sort parameter or metadata beyond maps; these may need scoped design as real scale grows. Phase 4.3A left schema, DbContext, snapshot, triggers, dependencies, demos and frontend unchanged. Phase 4.3 integration is described below; 4.4 Authoring UX is complete, approved and published above, completing the approved Phase 4 scope.

## Phase 4.3 - Persistent Graph Frontend Integration

Complete, approved and published in `f19c3d5`. The following describes the historical read-only Phase 4.3 delivery; current authoring is documented in Phase 4.4 above. After me/workspace bootstrap, PersistedMaps handles authenticated discovery, explicit choice and map reads. With no map URL it loads GET /api/maps and shows a picker or a stable empty state, never a default map or silent demo fallback. A validated `?map=<guid>` can load GET /api/maps/{id} directly. Native URLSearchParams/History API and a small navigation hook support pushState, popstate, F5 and Back/Forward without React Router. History records visits rather than a parent tree, so A → B → A is valid. Internal Back is limited to the current mounted navigation scope; direct entry/reload offers Todos os mapas.

Independent persisted DTOs and manual runtime validators check GUID formats, nullable descriptions/child links, finite positions, unique node/edge IDs and valid edge references. The pure adapter maps those contracts into visualization types only. KnowledgeGraph receives map/callback props and owns local node drag/selection, zoom/pan/minimap and details. Persisted coordinates initialize each successful visit; revisit/refetch/F5 resets local drag and selection. No response/position cache or browser storage is introduced. Demo-only client/coordinates/contracts and mock study metrics are removed from the authenticated frontend; backend demo endpoints stay unchanged.

Map requests use relative same-origin native fetch, no-store, AbortSignal, effect cleanup and a per-visit/retry identity. Loading/error/404 stays inside the shell; unknown server details are never rendered. A private-read 401 rechecks me/workspace through useSession, without automatically replaying map reads or creating a revalidation loop. Real session failure uses the existing gate; operational map errors preserve email/workspace/logout and offer retry. All ownership remains server-side. No backend, schema, dependency, authoring, autosave or study feature is added.
