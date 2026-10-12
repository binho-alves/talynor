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

## ADR-001 - React frontend

**Context:** Talynor requires a highly visual, responsive learning interface with interactive knowledge maps.

**Decision:** use React with TypeScript for a responsive SPA and standalone reusable UI components.

**Implications:** SPIKE-001 validated the initial graph experience. XYFlow / React Flow is adopted as the initial visualization library under ADR-007. State management approach, frontend testing tools, and package manager remain open.

## ADR-002 - .NET 10 backend

**Context:** the backend needs an explicit application and domain boundary supporting learning workflows and persistence.

**Decision:** use .NET 10, ASP.NET Core, and C#, with EF Core and REST APIs documented using OpenAPI.

**Implications:** create the solution only during an authorized application skeleton task. The authorized Phase 2 task created `Talynor.slnx` with Api, Application, Domain, Infrastructure, UnitTests, and IntegrationTests projects, `global.json`, and `Directory.Build.props`. The skeleton awaits review; persistence and authentication are not implemented. Product module boundaries and further implementation patterns remain open.

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

**Risks and lock-in:** visual components, handles, event handling, Node/Edge view types, viewport behavior, and library CSS create integration costs. A future replacement would require adapting the visual layer and interactions; isolation reduces but does not eliminate that cost. Larger or denser graphs, richer nodes, and frequent updates may increase rendering cost. Real touchscreen behavior and full keyboard/screen-reader usability need further validation; narrow-screen overview text currently requires zoom. Dependency compatibility and ongoing maintenance remain considerations.

**Reevaluation criteria:** revisit adoption if representative product graphs remain unacceptably slow or memory-intensive after proportionate improvements; required touch or accessibility interactions cannot be achieved; an essential visualization requirement cannot be supported; library coupling obstructs domain evolution; or recurring compatibility, security, or maintenance problems undermine continued use. No artificial node-count limit is set.

**Still open:** automatic layout, final interaction design, relationship semantics and persistence, and the official package manager. pnpm was used because it was available for the spike; npm, pnpm, or another option may be chosen later. This decision neither starts Phase 2 nor authorizes additional dependencies or infrastructure.

## Decision discipline

Future records must distinguish proposals from accepted decisions. Do not present unreviewed options as commitments. Authentication provider, automatic layout, final interaction design, relationship taxonomy and persistence, frontend testing tools, package manager, and deployment topology remain undecided. Initial XYFlow / React Flow adoption is accepted under ADR-007.
