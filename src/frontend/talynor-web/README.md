# Talynor Web — Persistent Graph and Authoring UX

## Planned Phase 9 node summary

Phase 9 — Progress and Analytics is In Progress; CP0 baseline is published, CP1 backend read foundation is Implemented / under review, and CP2–CP4 are Not started. The frontend summary remains planned. [Accepted ADR-017](../../../docs/DECISIONS.md#adr-017---derived-node-activity-and-results-summary) and [Development](../../../docs/DEVELOPMENT.md#phase-9---progress-and-analytics-design-baseline) define the approved plan; no frontend source changes belong to CP0. Phase 8 remains Complete / Published.

The proposed on-demand **Atividade e resultados** section lives in the existing selected-node panel in View and Edit modes. It reads one node-summary endpoint; it does not load authoring keys, fetch all history pages, add metrics to XYFlow/map DTOs, or change map URLs/Back/Forward. Native 7/30/90 elapsed-day presets and explicit Refresh show the effective interval, browser timezone and calculation time. Ended-period duration is attributed by its end instant; reviews are self-report across answered revisions; quiz accuracy is question-weighted. A null percentage means no recorded answers; a measured 0% means zero correct answers, never 0% competence. Content completion and retention remain explicitly unmeasured; no overall learning score is displayed.

A node/account/period-scoped hook with state, effect cleanup, AbortController and request identity is planned; these are the existing patterns (approximately component-local Angular signals and lifecycle/HTTP teardown), not new implementation. Runtime validation/copying, readable pt-BR labels, loading/empty/error/retry and keyboard/mobile states are required. Opening, changing the preset or explicit Refresh reads once; no polling, storage, router/state/chart/test dependency or new global provider is proposed. An already-open summary can become stale after writes in another panel/tab; use Refresh/reopen, never treat it as the active timer or a reconciliation lookup. Existing React learning notes and validation limitations remain unchanged below.

## Phase 8 - Assessments frontend

Under [ADR-016](../../../docs/DECISIONS.md#adr-016---node-owned-quizzes-revision-scoped-grading-and-immutable-attempts), Phase 8 is Complete / Published for the approved scope. CP0 (39ff3aa), CP1 (4b6dec0), CP2 (e17aa97) and CP3 Frontend authoring + answering/history (84e01b4) are Published. CP4 integrated validation is Completed / Validated / Published on 2026-10-22; closure review was APPROVED and documentation was published in 80bbae3. Phase 9 CP0 baseline is published under Accepted ADR-017; CP1 read foundation is Implemented / under review, and CP2–CP4 are Not started. See [checkpoints, acceptance and evidence](../../../docs/DEVELOPMENT.md#phase-8---assessments-design-baseline).

Edit map now has a separate node-owned Quizzes section: server-ordered paginated summaries, controlled create/full-definition edit and explicit hard-delete confirmation mentioning history loss. Forms enforce 200/1000/500 UTF-16 limits, 1..10 questions, 2..4 options and exactly one correct. Local stable keys are not sent. Full PUT uses the loaded expectedRevision; detected no-op is disabled and quiz_changed discards stale form authority until an explicit refetch. Unicode White_Space trimming mirrors .NET, including U+0085/U+FEFF rather than using JS trim blindly. Revisions/IDs/times are never predicted.

View mounts only QuizView/QuizTaking and history/result components. Own quizAttempt.ts taking types and recursive exact-key validation exclude authoring answers; no gabarito exists in pre-result state/DOM/attributes. Native radios require complete selections. Drafts exist only in memory and are lost on node/map/quiz/mode/session/F5 changes; no storage/autosave/beforeunload/resume. Result/history use immutable title/questions/all options/selected/correct snapshots and server counts/accuracy, displayed as e.g. 66,67%; no mastery/completion or StudySession effect.

quizzesApi uses native fetch and the session client private CSRF mechanism. Abort/request/generation guards reject stale responses. useAuthoring locks authoring before await; the taking coordinator locks synchronously before crypto.randomUUID and freezes a logical AttemptId/revision/answer set. Only rejected PUT fetch/no response triggers automatic same-ID GET lookup; failed CSRF preparation and any received HTTP/protocol/JSON response do not. Matching lookup confirms, mismatch conflicts, 404 permits explicit same-ID/payload retry only, failed lookup remains recoverable. Navigation clears local presentation without claiming rollback. Offset page overlap explicitly requests refresh rather than silently merging contradictory rows.

Final CP4 revalidation: pnpm run build/lint PASS (main chunk 487.60 kB, 149.35 kB gzip); node scripts/quiz-checks.mjs: 111 checks PASS, alongside content 103, Study Session 113 and Flashcard 95. Build/lint and unchanged backend regressions PASS; trusted-HTTPS Chrome authoring/taking/history/key-free DOM, rapid-submit metadata, keyboard/focus and 1440x1000 / 767x1022 / 390x843 CSS layout smoke PASS with long authoring/take/history text. Lists load 20 -> 22; F5/node/quiz switches discard selections; a revision-2 historical result retains its old definition after revision 3. A redacted-metadata proxy confirmed one PUT/AttemptId on rapid double-click, key-free takes, cookies/native CSRF/no-store and revision/selection-only submission. The controlled React harness is not exhaustive lifecycle evidence; full accessibility, physical touchscreen and scale remain unverified. The new panels use lazy/Suspense to avoid increasing the initial bundle past the earlier 500 kB warning. No router/state/form/test dependency was added; package manager remains open.

| React concept used | Where and why | Approximate Angular analogy |
| --- | --- | --- |
| Controlled inputs / useState | QuizForm owns text/options and stable local keys; taking selections are memory-only. | Component signals or form controls. |
| Props/callbacks | Form emits a validated full candidate; result renders an own historical DTO. | Inputs and Outputs. |
| useEffect cleanup | Scoped detail/take/history requests, abort and focus transitions. | Lifecycle/effects with teardown. |
| useRef synchronous lock | Existing useAuthoring blocks double mutations before rerender; taking uses synchronous coordinator transitions before ID allocation. | Mutable scoped guard in a component/service, not reactive state alone. |
| key / context identity | Node/mode/quiz/visit remounts discard stale drafts; server revision keys reset edited forms. | Recreating a scoped view based on identity. |
| Specific coordinator + useSyncExternalStore | Stable snapshots expose taking transitions and paginated reads without a state framework. | A scoped service with signals/observable state and subscription. |
| useId | Unique native labels and authoring radio groups; local row keys stay stable across removal. | Stable generated control IDs and tracked row identity. |
| lazy / Suspense | Load the new node panels only when selected, with a visible loading status. | Deferred views or lazy components; not an exact router equivalent. |

## Phase 6 - Study Sessions checkpoint 3

Complete, approved and published in 493d39a. Checkpoint 1 is published in 881d12a and checkpoint 2 in 29ba218; Phase 6 and its integrated closure documentation (6045391) are complete, approved and published for the scoped Study Session workflow, validated on 2026-10-19. Own Study Session contracts/runtime validators consume the published sessions/page/pageSize/hasMore history shape. No backend/schema/dependency or package-manager decision is changed.

Start studying is available in View and Edit mode. The authenticated-shell StudySessionProvider survives map/node navigation; its Context and small workflow coordinator share active state between the global indicator and node details. The visual timer uses serverTimeUtc plus local elapsed since reception, reanchors on focus and never writes ticks/duration. It has aria-live off. Continue via return to concept, explicitly complete now, or confirm/cancel permanent discard. Login/F5 restores active state from the backend; logout clears only frontend state. No browser-storage authority, continuous polling, WebSocket or automatic write replay exists.

Active state is revalidated at bootstrap, real window focus and before/after study mutations, with read deduplication, AbortController and request identity. A synchronous study lock prevents duplicate/incompatible study writes. A 409 active_session_exists or ambiguous network/5xx/protocol failure reads backend state without replay. Failed verification disables new study actions until explicit retry; 401 reuses the app session flow. Sent writes may already have committed even if logout/unmount aborts local I/O. Cross-tab awareness is eventual, not immediate.

Completed node history shows local start/end and a derived duration, page one/Load more in server order and confirmed hard delete. Repeated IDs from moving offset pages are removed, but no cross-page snapshot or omission-free pagination guarantee is claimed. Node changes abort/ignore obsolete history; complete/delete/focus revalidation reloads page one. Return to concept uses existing native History navigation and focuses the selected node; ordinary Back/Forward does not reapply an old return request.

Run node scripts/study-session-checks.mjs and node scripts/content-checks.mjs, pnpm run build and pnpm run lint. No new dependency or test framework is installed. Native Node checks cover API validation/transport, controlled-clock timer, state/read/write races, history paging and focused effect/navigation wiring. They are separate from browser evidence documented in Development.

React notes: Context shares only Study Session state at the authenticated-shell scope (roughly a component-scoped Angular injectable); useSyncExternalStore connects React to the specific workflow coordinator's stable snapshots (roughly a subscribed service state/signal). Custom hooks compose access, and useEffect owns focus listeners, read cleanup and the visual interval (lifecycle/effect with teardown). Local useState keeps confirmation/UI drafts local; keyed node sections reset their history on context changes. The coordinator is specific to this workflow and introduces no general state framework.

## Phase 5 - Frontend Content Integration

Complete, approved and published in d4e5c76; the agreed Note/Link scope of Phase 5 is complete. Select a persisted node to load content summaries, then open an item for detail. View mode is read-only. Edit map enables Note/Link creation, editing and contextual confirmed hard delete. Notes preserve literal text/newlines; Links show a wrapped HTTP(S) destination and open explicitly in a new tab with noopener/noreferrer. No Markdown/HTML rendering, embeds, previews or remote metadata fetching is added.

Title is optional (200 UTF-16 units); Note body is required (4000); Link URL is required (2048). Clear an existing title to send clearTitle. Type cannot change; unchanged normalized drafts disable Save. After confirmed writes the list/detail is fetched from the server, preserving ordering/timestamps. A shared lock coordinates graph/content writes. Reads abort on node/map changes, old completions are ignored, and local content selection never enters map URL/History. Drafts are discarded by context/mode changes.

Content errors offer explicit retry inside the map shell. Every write renews CSRF privately through sessionClient; components never receive tokens. No mutation replays after CSRF, 401, network or 500. For an ambiguous result, update the contents/check current state before sending again. Package manager remains open.

The final reconciliation fix clears obsolete selection/editor/detail only after a new current list confirms disappearance, preserving content just created. The 103 Node checks cover this and existing content/session/graph behavior; controlled component/hook checks are not exhaustive lifecycle tests. Earlier browser/network validation remains separate evidence. File upload/storage, specialized Article/Video, Flashcards, Quizzes, Exercise and Checklist are future scoped work, not missing requirements of this Note/Link delivery.

Run node scripts/content-checks.mjs with Node 24 alongside pnpm run build and pnpm run lint. Checks use only built-in assert/native TypeScript stripping; no frontend test framework or dependency is added.

| React concept | Where / reason | Approximate Angular analogy |
| --- | --- | --- |
| Discriminated unions | Content DTOs separate Note/Link and read states, preventing impossible payloads. | TypeScript unions used with Angular too. |
| Custom hook / cleanup | useContentRead isolates cancellable per-request loading/error/retry. | Scoped HTTP/service logic with lifecycle teardown. |
| Component key | ContentSection remounts per node/mode and discards obsolete detail/drafts. | Recreating a scoped view/component; not a perfect lifecycle equivalence. |
| Shared ref lock | The existing useAuthoring coordinator synchronously admits one graph/content write. | A scoped service guard plus reactive busy state. |
| Effect after commit | Focus returns to the content heading after confirmed create/delete. | A post-render focus operation. |

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

**Status:** Foundation, SPIKE-001, VS-001, Phase 3 and Phases 4.1/4.2/4.3A are complete, approved and published. Phase 4.3 is complete, approved and published. Phase 4.4 Authoring UX and Phase 4 are complete, approved and published. Phase 5 — Knowledge Content is complete, approved and published for Note/Link: foundation/read c450b91, Content Authoring Backend 5aa7d4e and Frontend Content Integration d4e5c76 under ADR-013. Phase 6 — Study Sessions is complete, approved and published for its scoped workflow: checkpoints 1 (881d12a), 2 (29ba218), 3 (493d39a) and closure (6045391) are published. Phase 7 Flashcards design remains accepted under ADR-015. Checkpoint 1 is published in f375141, Checkpoint 2 in 8af676d and Checkpoint 3 in f58d0fe. Checkpoint 4 integrated validation completed on 2026-10-21; Phase 7 is complete for the approved scope. Phase 7 closure was formally approved and published in bc7a4b6. Existing limitations remain; this is not production readiness. XYFlow is the adopted initial visualization library under [ADR-007](../../../docs/DECISIONS.md#adr-007---initial-xyflow-graph-visualization).

React + TypeScript with Vite and XYFlow now consumes private persisted maps. The historical demo-only client, coordinates and mock learning metrics have been removed from the authenticated experience. Backend demo endpoints remain available for regression. No backend, dependency or database change is introduced by this frontend slice.

## Phase 7 - Flashcards authoring and continuous study

The accepted design in [ADR-015](../../../docs/DECISIONS.md#adr-015---node-owned-flashcards-and-review-progression) has an approved/published design baseline (b4da3f8). Backend Checkpoint 1 foundation/CRUD/read is published in f375141; Checkpoint 2 review state/history and study/review backend is published in 8af676d; Checkpoint 3 frontend authoring/study is approved and published in f58d0fe; Checkpoint 4 integrated validation is completed and Phase 7 is complete for its approved scope, with closure approved and published in bc7a4b6. Checkpoint 3 implements paginated NodeDetails authoring in Edit map (page 1, pageSize 20, maximum 100; CreatedAtUtc ASC/Id ASC) and a Study action in View without preloading answers. Next returns Front/revision/progress, never Back. Explicit Reveal requests the revision-matched study/reveal endpoint; a changed card discards the stale item and reloads next. Material text edits reset current progress to unreviewed but keep events with their answered ContentRevision; no-op PATCH changes no progress/revision/time.

Study is continuous: never-reviewed cards first, then least-recently-reviewed with creation/ID ties. No deck-complete UI, persisted queue/backend run or saved refresh cursor; users leave when desired and a single card may reappear. Generate a Guid ReviewId only on an answer after a synchronous lock. Reconcile ambiguous writes by that same ID/payload using the immutable revision-tagged event and explicitly current progress; never automatically replay. Guard list/next/reveal/lookup against stale responses. Keep unrevealed answers absent from DOM/accessibility; use native focusable buttons and touch-friendly mobile controls. Front/Back remain literal text (2,000/4,000 UTF-16 units, preserving whitespace/newlines), with the Checkpoint 1 backend 64 KiB POST/PATCH cap; exact contracts live in ADR-015. Study Sessions remain independent, no scheduler/library is selected, and the package-manager decision remains open.

Run `node scripts/flashcard-checks.mjs` with the existing content/study-session checks. The 95 checks, including 19 HTTP/network/cancellation regressions, use native assertions, TypeScript and React already installed; no new framework/package. JavaScript string.length enforces 2000/4000 UTF-16 units without trimming. PATCH sends only changed text; successful writes refetch authoring page one rather than predicting revisions. Authoring uses the shared useAuthoring lock; FlashcardStudy uses a node/mode-keyed coordinator consumed by useSyncExternalStore. The coordinator changes state synchronously before allocating ReviewId, so rapid Correct/Incorrect clicks produce at most one PUT.

A PUT failure without a received HTTP response triggers lookup with the original ReviewId/card/revision/result. Any received HTTP response, including an unrecognized or invalid body, produces a deterministic error without automatic lookup or PUT replay; recognized conflicts keep their existing handling. A matching lookup event confirms and reports server currentReviewState; divergence stops with a conflict. Lookup 404 offers deliberate same-ID/payload retry and cannot prove rollback; a failed lookup offers another lookup. No automatic PUT replay occurs. Stopping/navigating clears local prompt/Back/pending presentation and ignores late results, without promising cancellation of sent writes. View mode never mounts authoring requests; Back enters the study state/DOM only after explicit Reveal. Flashcards do not start/complete Study Sessions, implement a scheduler, or declare a completed deck.

Final integrated CP4 validation on 2026-10-21: build/lint, 103 content, 113 Study Session and 95 Flashcard checks PASS; backend build has zero warnings/errors, 251 unit/405 integration PASS, EF 10.0.12 pending NONE, both migrations Up/Down/reapply PASS and 30/30 extra concurrency checks PASS. Fresh trusted-HTTPS Chrome login/CRUD/paging/reveal/review/reset/navigation/empty and keyboard/focus smoke passed on disposable data. Measured CSS viewports 1440x1000, 767x1022 and 390x843 had no horizontal overflow; captured consoles were clean. The controlled React harness is not exhaustive lifecycle evidence; full accessibility, physical touch and scale remain unverified. Phase 7 closure is approved and published in bc7a4b6; Phase 8 is Complete / Published for the approved scope under ADR-016; CP1 is Published in 4b6dec0, CP2 in e17aa97 and CP3 in 84e01b4. CP4 is Completed / Validated / Published in 80bbae3. Package manager remains open.

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
No dedicated frontend test framework is configured. The reproducible content/session/graph checks above run with Node and the existing TypeScript tooling.


## Explore persisted maps

Log in with a disposable account registered through the HTTPS API; there is no frontend registration or default account. Session bootstrap remains me → workspace, with browser-managed HttpOnly cookies and private in-memory CSRF for login/logout. There is no localStorage/sessionStorage. Authentication UI still revalidates through protected requests; Study Session awareness additionally revalidates on window focus as described above.

Without a map query, GET /api/maps shows an explicit button picker, including when there is only one map. An empty workspace shows a stable empty message, without creating/selecting a map or falling back to demos. The list order is presentation only.

Choose a map to request GET /api/maps/{id}. The address becomes `?map=<guid>`. F5 restores session/workspace then the requested map; valid direct links need no picker first. Invalid/empty/duplicate map parameters show a safe address error without sending a malformed map request. Missing and foreign maps have the same unavailable message; the URL stays until the user navigates.

Select a node to read its description. A child link enables the details button; double-click is a desktop shortcut. Drill-down pushes another visit, so cycles A → B → A work without inferring a unique parent. Internal Back follows current mounted visits; direct entry/reload offers **Todos os mapas**. Browser Back/Forward is handled through popstate. **Todos os mapas** returns to discovery, not to login.

Coordinates initially come from the backend. In view mode, mouse drag and keyboard movement change only local XYFlow state. Phase 4.4 adds explicit edit mode: drag stop saves a position PATCH; keyboard movement uses Save position. Revisit, retry/refetch or F5 reloads persisted positions and clears selection; no local position cache is used. Zoom/pan/minimap remain; the minimap hides on narrow screens and the details panel moves below the graph. Graph DTOs still carry no study/progress metrics; the separate Study Session API supplies active periods and history, without aggregates or progress percentages.

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

Discovery is currently unpaginated. View-mode drag is visual/local; edit-mode drag persists position on drag stop. Viewport state is not retained between map visits. Full accessibility, real touchscreen gestures, larger graphs and production routing/TLS remain future scoped work. Current graph authoring is described in Phase 4.4 above; current Note/Link viewing and authoring is described in the Phase 5 section. Package manager remains open; the pnpm notes above still apply.

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
