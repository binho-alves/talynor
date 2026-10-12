# Architecture Decision Record index

This index records decisions already approved in the project foundation request. The records below are contained in this document; no separate ADR files are required at this stage. Record date: 2026-10-12. All listed records are **accepted**.

| Record | Decision | Status |
| --- | --- | --- |
| [ADR-001](#adr-001---react-frontend) | React selected for the frontend | Accepted |
| [ADR-002](#adr-002---net-10-backend) | .NET 10 selected for the backend | Accepted |
| [ADR-003](#adr-003---modular-monolith-first) | Modular monolith first | Accepted |
| [ADR-004](#adr-004---sqlite-first-postgresql-production-target) | SQLite first; PostgreSQL production target | Accepted |
| [ADR-005](#adr-005---knowledge-graph-without-an-initial-graph-database) | Knowledge graph domain without a graph database initially | Accepted |
| [ADR-006](#adr-006---no-premature-distributed-infrastructure) | No premature distributed infrastructure | Accepted |

## ADR-001 - React frontend

**Context:** Talynor requires a highly visual, responsive learning interface with interactive knowledge maps.

**Decision:** use React with TypeScript for a responsive SPA and standalone reusable UI components.

**Implications:** validate the graph experience through the dedicated frontend spike. A visualization library, state management approach, and frontend testing tools have not been selected.

## ADR-002 - .NET 10 backend

**Context:** the backend needs an explicit application and domain boundary supporting learning workflows and persistence.

**Decision:** use .NET 10, ASP.NET Core, and C#, with EF Core and REST APIs documented using OpenAPI.

**Implications:** create the solution only during an authorized application skeleton task. Project structure and concrete implementation patterns remain open.

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

## Decision discipline

Future records must distinguish proposals from accepted decisions. Do not present unreviewed options as commitments. Authentication provider, graph visualization library, relationship taxonomy, frontend testing tools, and deployment topology remain undecided.
