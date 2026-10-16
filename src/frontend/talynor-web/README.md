# Talynor Web — Persistent Graph and Authoring UX

## Phase 4.4 - Authoring UX

Complete, approved and published in `c427c6e`. Use Criar mapa in the picker, then Editar mapa to enable editing. Forms edit title/optional description, add/edit nodes and choose/remove ChildMap from current-workspace discovery. Nodes start at the predictable (node count × 240, 0); no automatic layout is added. Conectar nós uses labeled source/target selects; self links and duplicate edges are allowed by the API. The edge selector provides a keyboard-accessible selection/deletion path in addition to graph selection. Node and edge selections are mutually exclusive. All deletions require explicit inline confirmation.

Creates wait for validated 201, metadata/deletes for 204, then update local state without refetching the entire graph. Clearing description or ChildMap sends explicit clear flags; unchanged fields are omitted. Picker visits refetch server-ordered discovery, so create/rename/delete are synchronized without a response cache. Creating a map opens that explicitly requested map; ordinary discovery still never auto-selects.

Drag is local in view mode. In edit mode, releasing the node sends one position PATCH; failure restores the last confirmed coordinates. Keyboard movement uses Salvar posição do nó. One mutation per editor is admitted synchronously and disables controls/drag while pending; position versions and mounted-editor guards reject stale completions. Graph rendering remains free of fetch/CSRF/ownership logic.

Every write renews CSRF through the existing per-hook session client. There is no automatic mutation retry, including POST after CSRF/network/500/401 failures. Revalidation of a 401 may return to login or retain an explicit retry; it never resends the write. Read safe conflict messages and remove references explicitly before retrying. After ambiguous results reload before creating again. Drafts are discarded on navigation; sent writes are not aborted and may finish after leaving. Reload after completion if revisiting during a pending write. No undo, cross-tab coordinator, autosave per keystroke, concurrency tokens or new dependencies are introduced.

| React concept | Use and purpose | Approximate Angular analogy |
| --- | --- | --- |
| Controlled forms / local draft | MetadataForm binds input value/onChange; Save commits and Cancel discards. | Form control state or component signals with explicit submit. |
| Mutation state | useAuthoring separates pending/feedback from page loading. A ref blocks double submit before rerender. | Component signals plus a synchronous guard. |
| Callback props | MapWorkspace translates graph movement into an API request; KnowledgeGraph has no transport logic. | Inputs and Outputs/EventEmitter. |
| Functional state updates | Add/remove/modify graph elements from the latest state after confirmed writes. | Signal update(previous => next). |
| Per-entity identity / rollback | Position versions and mounted scope reject obsolete results; failed movement restores confirmed coordinates. | Scoped request IDs and restoring the previous signal value. |
| Derived selection | Selected node/edge is read from the graph state rather than duplicated forms of identity. | Computed signals. |

Validation: backend build, 79 unit/179 integration tests and EF pending-model NONE; frontend build/lint, 65 existing plus 37 authoring Node checks and 5 real HTTPS API/client isolation/CSRF checks passed. Browser UI CRUD, persisted F5 state, ChildMap, conflicts, rollback, 401/manual retry, 500/network failures, Back/Forward, logout and three responsive sizes passed. Proxy metadata confirmed methods/CSRF/no duplicate creates. No component test framework, full screen-reader audit, real-device touch, scale benchmark or production-readiness claim is included. See Development for scope and evidence.

**Status:** Foundation, SPIKE-001, VS-001, Phase 3 and Phases 4.1/4.2/4.3A are complete, approved and published. Phase 4.3 is complete, approved and published. Phase 4.4 Authoring UX and Phase 4 are complete, approved and published. Phase 5 — Knowledge Content is in progress in the backend foundation/read increment under ADR-013; this frontend remains unchanged and has no content UI. XYFlow is the adopted initial visualization library under [ADR-007](../../../docs/DECISIONS.md#adr-007---initial-xyflow-graph-visualization).

React + TypeScript with Vite and XYFlow now consumes private persisted maps. The historical demo-only client, coordinates and mock learning metrics have been removed from the authenticated experience. Backend demo endpoints remain available for regression. No backend, dependency or database change is introduced by this frontend slice.

## Run locally

From src/frontend/talynor-web, with a Vite-compatible Node.js version and pnpm available:

Start the API from the repository root:

~~~powershell
dotnet run --project src/backend/Talynor.Api --launch-profile https
~~~

In another terminal, from this frontend directory with the existing dependencies installed:

~~~powershell
$env:TALYNOR_API_PROXY_TARGET = 'https://localhost:7117'
$env:TALYNOR_HTTPS_CERT = '<absolute path outside the repository to trusted localhost certificate PEM>'
$env:TALYNOR_HTTPS_KEY = '<absolute path outside the repository to matching private key PEM>'
pnpm dev
pnpm build
pnpm lint
~~~

The HTTPS target matches the API launch profile. React fetches relative `/api` URLs through the Vite development proxy, without backend CORS. Browser-to-Vite HTTPS uses an existing trusted localhost PEM certificate/key supplied outside this repository; open the HTTPS address Vite prints. Configuration requires these paths for dev, not build/lint, and does not generate/trust/export certificates. Never commit private keys or bypass browser trust errors. Node 24 can use `$env:NODE_OPTIONS = '--use-system-ca'` before Vite when needed to trust the API through the Windows store. TLS verification remains enabled; the historical TALYNOR_API_PROXY_INSECURE fallback is now rejected for authenticated flows. Production/preview routing is not configured. See [the 3.4 procedure](../../../docs/DEVELOPMENT.md#phase-34---authenticated-frontend) for database/account prerequisites and manual checks.

Created with Node.js 24 and pnpm 11. The lockfile records installed versions.
pnpm was used in SPIKE-001 because it was available in the environment; it has
not been selected as Talynor's official package manager. npm, pnpm, or another
option may be selected later. pnpm-lock.yaml remains in the spike to reproduce
the exact dependency versions used in this execution.
No automatic test suite is configured yet.


## Explore persisted maps

Log in with a disposable account registered through the HTTPS API; there is no frontend registration or default account. Session bootstrap remains me → workspace, with browser-managed HttpOnly cookies and private in-memory CSRF for login/logout. There is no localStorage/sessionStorage or cross-tab synchronization.

Without a map query, GET /api/maps shows an explicit button picker, including when there is only one map. An empty workspace shows a stable empty message, without creating/selecting a map or falling back to demos. The list order is presentation only.

Choose a map to request GET /api/maps/{id}. The address becomes `?map=<guid>`. F5 restores session/workspace then the requested map; valid direct links need no picker first. Invalid/empty/duplicate map parameters show a safe address error without sending a malformed map request. Missing and foreign maps have the same unavailable message; the URL stays until the user navigates.

Select a node to read its description. A child link enables the details button; double-click is a desktop shortcut. Drill-down pushes another visit, so cycles A → B → A work without inferring a unique parent. Internal Back follows current mounted visits; direct entry/reload offers **Todos os mapas**. Browser Back/Forward is handled through popstate. **Todos os mapas** returns to discovery, not to login.

Coordinates initially come from the backend. In view mode, mouse drag and keyboard movement change only local XYFlow state. Phase 4.4 adds explicit edit mode: drag stop saves a position PATCH; keyboard movement uses Save position. Revisit, retry/refetch or F5 reloads persisted positions and clears selection; no local position cache is used. Zoom/pan/minimap remain; the minimap hides on narrow screens and the details panel moves below the graph. Study/progress metrics are absent because the persisted contract does not supply them.

Loading is separate for session, discovery and map. Map network/500/protocol errors offer retry inside the shell, preserving email/workspace/logout. A private-read 401 rechecks me/workspace through the existing session client; an ended session returns to login, while a valid session leaves an explicit map retry without an automatic loop.

## Read the code in small steps

1. src/types/persistedKnowledgeMap.ts defines own HTTP DTOs; src/api/knowledgeMapsApi.ts validates external JSON and loads list/map with relative same-origin native fetch.
2. src/navigation/mapLocation.ts parses/builds map URLs; usePersistedMapNavigation.ts handles native history and visit identity without a router or state framework.
3. src/components/PersistedMaps.tsx orchestrates discovery/map requests, picker/empty/error states and session revalidation callbacks.
4. src/adapters/knowledgeMapAdapter.ts is a pure DTO → visual model transformation. API/backend/domain remain independent of XYFlow.
5. MapWorkspace.tsx owns per-visit visual authoring state, coordinates persisted mutations and position persistence, and derives node/edge selection. KnowledgeGraph.tsx presents the graph and reports interaction callbacks without HTTP/auth/ownership logic; KnowledgeNodeCard/NodeDetails render text and child navigation.
6. App/useSession/sessionClient retain the existing session gate and auth/CSRF sequencing; the map container is scoped by current user/workspace.

Runtime checks cover GUID-like IDs, nullable descriptions/child links, finite coordinates, unique IDs and valid edge endpoints. Extra response fields are discarded before state. Unknown server error detail/title is never rendered. Request cleanup uses AbortController plus a current-request guard; every visit/retry has a distinct identity, so A finishing after B cannot overwrite B, including StrictMode cleanup. Each visit fetches again; no HTTP response cache or automatic layout is added.

## React concepts and approximate Angular equivalents

| Concept | Where and why | Approximate Angular analogy |
| --- | --- | --- |
| Props/callbacks | PersistedMaps mounts MapWorkspace; MapWorkspace passes graph state and interaction callbacks to KnowledgeGraph. | Inputs and outputs; callbacks are functions rather than EventEmitter objects. |
| useState and derived state | MapWorkspace owns graph state; selection is derived from nodes/edges. Loading is derived from request identity. | Local signals and computed signals. |
| useEffect / cleanup | Fetch per visit; abort and ignore obsolete work on change/unmount/StrictMode. | Lifecycle/reactive effects with teardown; not exactly ngOnInit. |
| AbortController | Cancel obsolete HTTP work; a current guard also rejects late results. | HTTP subscription teardown is related. |
| Custom hook | usePersistedMapNavigation composes local state/listeners. | Reusable scoped service logic, without implying singleton state. |
| URLSearchParams / popstate | Native URL selection and Back/Forward, zero router package. | Browser APIs; Angular Router provides a broader abstraction. |
| key | Remount the graph per successful visit to reset selection/local positions. | View identity/track is related, not an exact lifecycle equivalent. |
| useCallback | Keeps session-recheck handlers stable. Graph interaction callbacks are supplied by MapWorkspace. | No direct equivalent; component methods normally keep identity. |

## Validation and limits

No frontend test framework was added. Build/lint and dependency-free ad hoc Node checks cover runtime validators, adapter, URL helpers, error statuses and cancellation. Real trusted-HTTPS browser checks are recorded in [Development](../../../docs/DEVELOPMENT.md#phase-43---persistent-graph-frontend-integration), separately from automated results.

Discovery is currently unpaginated. View-mode drag is visual/local; edit-mode drag persists position on drag stop. Viewport state is not retained between map visits. Full accessibility, real touchscreen gestures, larger graphs and production routing/TLS remain future scoped work. The current authoring scope is described in the Phase 4.4 section above. Package manager remains open; the pnpm notes above still apply.

## XYFlow assessment

XYFlow supplies custom React nodes, typed node/edge data, zoom, pan, selection,
dragging, controls, and a minimap. Drill-down is local map navigation implemented
through the navigation container/callbacks, rather than a database hierarchy or automatic domain feature.

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
