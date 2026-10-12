# Architecture

## Status and scope

This document records the approved high-level direction. Foundation and Phase 1 — SPIKE-001 are completed and approved. Phase 2 has started: the .NET 10 skeleton is created and awaits review; the phase is not yet complete. The separate frontend spike continues to use local mocks. No persistence or authentication is implemented. Further implementation must be authorized through a scoped task and follow these boundaries.

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

## High-level responsibilities

- **Frontend:** presents knowledge maps and learning workflows, handles visual interaction and navigation, and communicates with the API.
- **API:** exposes REST operations and translates requests and responses at the application boundary. Business logic must remain outside controllers.
- **Application/domain:** implements use cases and business rules, including the knowledge graph and learning concepts.
- **Infrastructure:** supplies technical integrations and environment-dependent capabilities required by the application.
- **Persistence:** stores and retrieves application data using EF Core and the explicitly selected database provider.

The authorized Phase 2 skeleton uses `Talynor.slnx`, with Api, Application, Domain, and Infrastructure projects under `src/backend`, and UnitTests and IntegrationTests under `tests`. Application references Domain; Infrastructure references Application; Api references Application and Infrastructure for host composition. UnitTests references Domain and Application; IntegrationTests references Api. Infrastructure has no direct Domain reference. This initial responsibility split does not finalize product module boundaries or broader implementation patterns.

`global.json` selects SDK 10.0.401 with `latestPatch` roll-forward and prerelease SDKs disabled. `Directory.Build.props` enables nullable reference types and implicit usings without treating all warnings as errors. The API enables controllers, JSON console logging, `/health` without external dependencies, and OpenAPI only in Development. No business endpoints, EF Core configuration, database, migrations, authentication, or CORS are introduced. The React frontend remains separate under `src/frontend/talynor-web`.

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

Authentication provider, product module boundaries, automatic layout, final interaction design, final relationship taxonomy and persistence representation, concrete data model, business API contracts, frontend test tooling, package manager, deployment topology, and migration procedures remain undecided. Resolve them when their roadmap phases are scoped; do not silently treat them as approved decisions.
