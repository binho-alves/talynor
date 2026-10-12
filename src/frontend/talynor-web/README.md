# Talynor Web — SPIKE-001 and VS-001

**Status:** SPIKE-001 completed and approved; VS-001 implemented and pending review. XYFlow / React Flow is adopted as Talynor's initial graph visualization library; see [ADR-007](../../../docs/DECISIONS.md#adr-007---initial-xyflow-graph-visualization). The limitations below remain applicable, and the package manager decision remains open.

Initial React + TypeScript frontend spike using Vite and @xyflow/react.
VS-001 loads knowledge content and connections from `GET /api/knowledge-maps/{mapId}` for `software` and `csharp`. Progress and study time remain demonstration values supplied by the in-memory backend. No database, authentication, or production graph semantics are implemented. The original spike validation below is historical.

## Run locally

From src/frontend/talynor-web, with a Vite-compatible Node.js version and pnpm available:

Start the API from the repository root:

~~~powershell
dotnet run --project src/backend/Talynor.Api --launch-profile https
~~~

In another terminal, from this frontend directory with the existing dependencies installed:

~~~powershell
$env:TALYNOR_API_PROXY_TARGET = 'https://localhost:7117'
pnpm dev
pnpm build
pnpm lint
~~~

The target is configurable and the example matches the API HTTPS launch profile. React fetches relative `/api` URLs through the Vite development proxy, without backend CORS. The target is required for the dev server, not build/lint; it is not a production or preview routing solution. TLS validation remains enabled by default. If the proxy reports a localhost certificate trust error, identify it first. On the validated Node 24 Windows environment, setting `$env:NODE_OPTIONS = '--use-system-ca'` before starting Vite resolved trust using the existing system certificate store. If trust still cannot be established, the permitted development-only fallback is `$env:TALYNOR_API_PROXY_INSECURE = 'true'`. Configuration restricts this exception to HTTPS loopback targets. It does not change application or production TLS, and contains no secret.

Created with Node.js 24 and pnpm 11. The lockfile records installed versions.
pnpm was used in SPIKE-001 because it was available in the environment; it has
not been selected as Talynor's official package manager. npm, pnpm, or another
option may be selected later. pnpm-lock.yaml remains in the spike to reproduce
the exact dependency versions used in this execution.
No automatic test suite is configured yet.

## Explore

1. Select a concept to see its description, mock progress, and mock study time.
2. Drag nodes or the background; use the zoom and fit controls.
3. Select C# and choose **Explorar C#** to load its demonstration submap from the API. Double-clicking C# is a desktop shortcut.
4. Use **Voltar ao mapa anterior** to return.

On desktop, details appear beside the map. On tablet and mobile, details appear below it.
The minimap is hidden on narrow screens; zoom and fit controls remain available.
Node positions and selection survive map navigation in memory. Reloading resets them.
Each map is fitted again when entered; viewport position is not preserved between maps.
Connections are illustrative, without defining prerequisites or a relationship taxonomy.

## Read the code in small steps

1. src/types/knowledge.ts describes view data, not persistence entities.
2. src/types/knowledgeMapResponse.ts and src/api/knowledgeMaps.ts define the independent HTTP contract, native fetch, and basic runtime validation.
3. src/components/KnowledgeNodeCard.tsx renders a typed custom node.
4. src/components/NodeDetails.tsx renders information received through props.
5. src/App.tsx owns map navigation and node state, and wires React Flow to them.
6. src/App.css and src/index.css provide responsive layout without a UI framework.

VS-001 adds `src/data/knowledgeMapPositions.ts` for local demonstration coordinates and `src/adapters/knowledgeMapAdapter.ts` for explicit HTTP DTO → XYFlow adaptation. Missing positions fail explicitly; the API never supplies visual coordinates, handles, or styles. Local content mocks have been removed.

Each map visit and retry requests content again. Visual models, including their content in `data`, remain temporarily in memory during the session; after a new HTTP response arrives, previous positions and selection are reapplied to the new visual nodes. This is not a persistent cache or reuse of HTTP responses instead of fetching. Loading is announced, 404 is distinct, errors offer retry, and there is no silent mock fallback. Every navigation and retry gets a new request identity, so even a quick return shows loading until its new response arrives. Effect cleanup aborts obsolete requests and ignores stale results, including StrictMode development cycles. Runtime validation checks required fields, numeric ranges, unique IDs, and valid edge references; TypeScript alone does not validate incoming JSON.

## React concepts and approximate Angular equivalents

| Concept | Use and reason | Approximate Angular equivalent |
| --- | --- | --- |
| Function components and JSX | App, cards, and details split the UI into readable pieces. | Components and their templates. |
| Typed props and callbacks | Details receives the selected node and a navigation callback; no global state is needed. | Inputs and outputs/event bindings. Callback props are functions, not EventEmitter objects. |
| useState | App owns the active map and node arrays. Updates request a render. | Component-local signals or fields bound to a template. |
| Derived state | Selection is found in the active node array rather than duplicated. | A computed signal or derived template value. |
| Immutable functional updates | Node changes use previous state and replace the affected array. | Signal update returning a new object. |
| useCallback | Stabilizes the node-change handler while the active map is unchanged. | No direct equivalent; component methods typically retain identity. |
| Conditional rendering | Back button, empty details, and submap action depend on state. | @if in a template. |
| key | Remounts React Flow to fit the new map; App retains node positions. | View identity via track is related, but not an exact lifecycle equivalent. |
| StrictMode | The Vite entry point enables development checks for impure behavior. | No direct equivalent; Angular development checks share some intent. |

## XYFlow assessment

XYFlow supplies custom React nodes, typed node/edge data, zoom, pan, selection,
dragging, controls, and a minimap. Drill-down is local map navigation implemented
by App, rather than a database hierarchy or automatic domain feature.

The spike informed the approved initial adoption of XYFlow / React Flow.
The library remains isolated to frontend visualization; domain and application
logic, API contracts, and persistence must not depend on its specific Node/Edge
types. Larger graph performance, touch behavior on real devices, and full
accessibility need further validation. Concrete limitations may justify
reevaluating adoption. No artificial node-count limit, automatic layout solution,
or final relationship semantics or persistence model is defined by this decision.
XYFlow has internal state dependencies; the application does not import or configure
Redux, Zustand, or another global state library.

Official references:

- [Custom nodes](https://reactflow.dev/learn/customization/custom-nodes)
- [TypeScript](https://reactflow.dev/learn/advanced-use/typescript)
- [Viewport](https://reactflow.dev/learn/concepts/the-viewport)
- [Built-in components](https://reactflow.dev/learn/concepts/built-in-components)
- [Releases](https://github.com/xyflow/xyflow/releases)

## Initial validation — 2026-10-12

- TypeScript/Vite build and generated ESLint configuration passed.
- Production bundle: JavaScript 405.58 kB (127.87 kB gzip), CSS 19.72 kB (3.80 kB gzip). These totals include React, XYFlow, and application code; they are not an isolated XYFlow measurement.
- One-off browser checks used an existing Microsoft Edge installation, without adding an E2E suite or browser dependency to this project.
- Selection, mock details, dragging, zoom, pan, drill-down, return, and parent node position preservation passed.
- Desktop (1440 × 1000), tablet (768 × 1024), and mobile (390 × 844) layouts were checked. Tablet/mobile had no horizontal page overflow; mobile drill-down via the details button passed.
- No uncaught browser errors were observed. Real touchscreen gestures, larger graphs, and a full accessibility review remain unvalidated. On narrow screens the initial overview requires zoom for readable node text.
