# Architecture

## Status and scope

This document records the approved high-level direction. Foundation, SPIKE-001, Phase 2, and the published VS-001 are completed and approved. Phase 3 has started with 3.1 — Persistence Foundation, implemented and pending review. EF Core/SQLite now supplies role-free Identity persistence; HTTP authentication, sessions, and workspace are not implemented. The separate React frontend still reads two in-memory demonstration maps from the API. Further slices require scoped tasks.

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
| Initial account strategy | Local ASP.NET Core Identity; browser cookie sessions are approved for a future slice, not configured yet. |

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

The API registers only the DbContext with SQLite. The Development connection string is configurable; relative runtime database paths resolve against the API content root. Registration does not open a connection, create a file, apply migrations, or seed users. Health and demonstration reads do not depend on the database. Other environments must configure the connection string before using persistence.

Infrastructure owns the explicit `InitialIdentity` migration and a design-time factory. The factory requires `ConnectionStrings__Talynor` rather than guessing a database, and does not start the API. EF commands use the versioned local tool manifest. See [Development](DEVELOPMENT.md#phase-31---identity-persistence) for database selection and lifecycle commands. No automatic startup migration or `EnsureCreated` is used.

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

Local ASP.NET Core Identity, future browser cookies, and a future single personal workspace per user are approved under ADR-008. Session security settings, account recovery and verification workflows, and detailed workspace behavior still require scoped design and implementation. Product module boundaries, automatic layout, final interaction design, relationship taxonomy and knowledge persistence representation, further business API contracts, frontend test tooling, package manager, deployment topology, and production migration procedures remain open. Local Identity migrations are now explicit as documented above; PostgreSQL infrastructure remains unauthorized.
