# Development workflow

## Phase 5 - Frontend Content Integration

Implemented/in review under ADR-013; foundation/read (c450b91) and backend authoring (5aa7d4e) are complete/approved/published. Phase 5 remains in progress. Start the existing trusted-HTTPS API/Vite setup against explicitly migrated disposable SQLite; no backend/schema/package change or Development seed belongs to this increment.

Select a persisted map/node to fetch only its summaries; opening a content item fetches detail. Empty is normal. View mode allows reading only. Edit map adds Note/Link creation, type-immutable editing and explicit contextual delete confirmation. Optional title clearing uses clearTitle; unchanged normalized values disable Save. Confirmed writes refetch server-ordered lists and affected detail. Selection is not persisted in URL/history. Navigation/node/mode changes discard content drafts; sent mutations may finish after navigation, so revisit/reload reconciles uncertain results. Refresh contents before repeating ambiguous writes.

Run pnpm run build, pnpm run lint and node scripts/content-checks.mjs from the frontend. The Node 24 checks use built-in assert/native TypeScript stripping without a test framework, exercising content validators/PATCH/limits/contracts/CSRF/no replay plus existing session bootstrap/login/logout, graph authoring, discovery/adapter and map URL regressions. Static checks complement browser tests and do not prove React lifecycle timing. Run the existing backend build/test, local links/anchors and Git/whitespace checks.

Browser validation uses disposable accounts/data through the published API and trusted HTTPS, never a permanent seed. Verify empty list; Note/Link CRUD and clearTitle/no-op; text line breaks and literal markup; explicit safe links; rapid node/detail changes with delayed responses; map drill-down, F5/Back/Forward; shared mutation locking; draft preservation and manual retries after 401/CSRF/network/500; generic 404; last-item delete and node cascade; labels, keyboard/focus, separate live status outside busy regions, long text/URLs and desktop/tablet/mobile overflow. Inspect only redacted request metadata: method/path/status and boolean cookie/CSRF/cache presence, never secret values or payloads. A temporary external HTTPS proxy may instrument/fault-inject test requests without changing application configuration files or disabling TLS.

Validation on 2026-10-17: backend build PASS, zero warnings/errors; 163 unit and 238 integration tests PASS; safe :memory: EF pending-model changes NONE. Frontend build/lint and 92 reproducible Node content/session/graph checks PASS; local links/anchors (54) PASS. No backend source, migration/model, package or lockfile changed. The initial lint rejected a control-character regex; equivalent explicit character checks fixed it. Browser focus verification found delayed requestAnimationFrame focus unreliable; a post-commit effect fixed it and confirmed create/delete focus passed. No full screen-reader, real-touch-device, scale, cross-tab write-coordination or Kestrel oversized-body enforcement claim is made.

Trusted-HTTPS browser checks in the integrated browser/Chrome passed Note/Link create/read/edit/delete, title clear, no-op disabled save, invalid input/draft preservation, literal markup/newlines, explicit safe new-tab link, last-item empty state, node cascade cleanup, delayed list/detail responses during node/content changes, shared mutation lock, F5 and map Back/Forward/drill-down. Chrome CSS viewports measured 1440x1000, 767x1023 and 390x843 with no horizontal overflow for long URLs/text/forms; temporary viewport overrides were reset. The integrated browser's viewport override did not change its measured 1280x720 viewport, so responsive evidence comes from Chrome. Console checks in Chrome showed no warnings/errors.

The external temporary HTTPS proxy recorded 80 content requests: 65 GET 200, one GET 404 and one GET 500; three POST 201; four PATCH 204 plus one each CSRF 400, injected 401, injected 500 and connection loss; two DELETE 204. Cookie presence, CSRF on writes/absence on GET, no Bearer/ownership override, no-store HTTP responses and zero demo requests passed. Faults affected only test proxy requests, preserving the real native CSRF rejection where the header was removed; operational failures did not replay mutations, drafts remained, and explicit read retries recovered. The first fixture helper incorrectly attempted JSON parsing on registration's empty 201 and was corrected outside the repository; redacted credential reads were replaced by directly filling generated disposable credentials. Sandbox certificate/key-ring/socket access restrictions required authorized local processes outside the sandbox. No TLS validation/trust, application configuration file or backend security policy was weakened.

## Phase 5 - Content Authoring Backend

Complete, approved and published in 5aa7d4e under ADR-013; foundation/read is complete, approved and published in c450b91. Phase 5 remains in progress; current frontend integration is described above. The following records the backend increment and its validation. Use the existing explicitly migrated disposable SQLite and trusted HTTPS session procedures below. No migration, schema, package, startup seed or frontend source change is part of this increment.

After native CSRF bootstrap/login, use POST /api/maps/{mapId}/nodes/{nodeId}/contents with type=note plus body or type=link plus url and optional title. Values note/link are lowercase and case-sensitive. POST returns 201, Location and the same detail shape as GET, without ownership IDs. PATCH .../contents/{contentId} accepts title/body/url/clearTitle and returns 204; DELETE hard-deletes/204. Obtain native CSRF for every unsafe request; GETs still need none. Cookie/session and lazy workspace resolution remain the approved baseline. Never print payloads, secrets, tokens or full headers.

Applicable omitted/null fields preserve current values; clearTitle=true removes title and cannot accompany a non-null title. An inapplicable body/url member is rejected even if null. Unknown members, type changes, client IDs/ownership/timestamps are validation_failed. The complete Domain candidate is validated before mutation. Empty/material no-op PATCH is rejected, without save/timestamp change; this does not change older graph PATCH semantics. Valid mutations set UpdatedAtUtc from Application TimeProvider and preserve CreatedAtUtc. Literal body and HTTP(S) URL policy/limits remain unchanged; no server fetch exists.

The writer has its own SQL-scoped store, tracked entities and one SaveChanges per successful command. Tests use real disposable SQLite and existing native sessions/antiforgery, checking create/read/patch/delete, exact contracts, parent/ownership isolation, null/clear/no-op semantics, timestamps, rollback-free failed candidates, cancellation, parent-disappearance FK handling, zero-row races and sanitized failures. Existing cascades/conflicts and GET regressions remain. Modified-column last-committed-write behavior has no ETag/rowversion/replay; disjoint changes can merge. A repeated POST after an ambiguous failure can create another item; do not assume idempotence.

Run dotnet restore Talynor.slnx, dotnet build Talynor.slnx --no-restore and dotnet test Talynor.slnx --no-build; restore the existing local tool and run the safe :memory: pending-model check below. Run unchanged frontend build/lint, existing link/anchor checks and Git/whitespace inspection, including new files. No browser content UI existed in this backend increment. All three MVC write actions have 32768-byte request-size metadata; tests characterize configuration only, not real Kestrel body-size rejection/status/sanitization. No real-server enforcement result is claimed.

Validation on 2026-10-16 for Content Authoring Backend: restore/build PASS with zero build warnings/errors; 163 unit tests (34 new) and 238 integration tests (40 new) PASS. Existing GET, cascade/conflict, stamp/session and SQL-order regressions passed. Local dotnet-ef 10.0.12 restored; safe :memory: pending-model changes NONE, with no new migration. Unchanged frontend build/lint and 54 local links/anchors PASS. An initial targeted integration run found a missing pre-cancelled-token guard on the writer's no-change SaveChanges path and a JsonElement timestamp assertion; the guard and value comparison were corrected, the two targeted cases passed, then the complete suite passed. The two former foundation-only expectations (no Domain update method / GET-only routes) were replaced by the approved authoring and immutable-identity coverage. Vite reported callback timing information without failing the build. Browser content UI and real-server oversized-body enforcement were not run or claimed.

## Phase 5 - Knowledge Content Foundation

Foundation/read is complete, approved and published in c450b91 under [ADR-013](DECISIONS.md#adr-013---initial-node-owned-knowledge-content). Phase 5 remains in progress with the first approved slice Note and Link Content. The subsequent backend authoring increment is published; current frontend integration is described above. The following records the original foundation scope and validation. No package or seed was introduced, and that increment left the frontend on the published Phase 4 experience.

Migration order is InitialIdentity → AddPersonalWorkspace → AddPersistentKnowledgeGraph → AddKnowledgeContent. Use the existing explicitly selected absolute disposable SQLite path and `dotnet tool run dotnet-ef database update --project src/backend/Talynor.Infrastructure`; never migrate Development merely to run tests. Startup remains migration-free. Inspect Up/Down/Designer/Snapshot: only KnowledgeContents, its node cascade FK, ordering index and type/payload CHECKs are added. Existing graph triggers and timestamp mappings must remain intact.

With an authenticated test account and test-only prepared content, GET /api/maps/{mapId}/nodes/{nodeId}/contents returns summaries in creation-time/ID order, without Note bodies; GET .../contents/{contentId} returns the applicable body/url. GETs need no CSRF and return no-store, with no client workspace selection. Unknown/foreign/mixed IDs share 404; a valid empty node returns contents: []. Foundation originally had no content HTTP writes; the subsequent backend authoring increment above permits explicit writes, still without frontend UI. Use isolated fixtures, never a permanent seed or manual Development data modification. Internal HTTP(S) URLs are stored only, never fetched or rendered by this increment.

Run the existing restore/build/test workflow, tool restore and safe in-memory EF pending-model check, plus unchanged frontend build/lint. New tests cover Domain/URL limits, scoped use cases, real SQLite migration rollback/reapplication, UTC DateTime conversion and adversarial tie-breaking, constraints/cascades, preserved node_in_use/map_in_use, HTTP JSON/auth/no-store/ownership/error behavior, and absence of content write routes. The existing schema inventory and graph-rollback tests now account for the fourth migration without changing their historical assertions. No browser content flow exists to validate yet.

Validation on 2026-10-16: backend build PASS with zero warnings/errors; 129 unit tests (50 new) and 198 integration tests (19 new) PASS. Content fixtures use different source offsets, a shared instant with fractional ticks and controlled reverse-inserted IDs to verify UTC roundtrip and SQL tie-breaking. The explicit migration Up/Down/reapply and graph-trigger preservation tests passed. dotnet-ef 10.0.12 lists all four migrations with --no-connect; the safe :memory: pending-model check reports NONE without touching Development data. Unchanged frontend build/lint PASS; local documentation links/anchors (54) PASS. The initial full run exposed two old schema/migration-count expectations; only those expectations were updated before the successful full run. No content browser UI or Kestrel write-body limit is claimed by this GET-only increment.

## Phase 4.4 - Authoring UX

Complete, approved and published in `c427c6e`; 4.3 is complete, approved and published in `f19c3d5`. Reuse the trusted HTTPS certificate, process-only Vite proxy and explicitly migrated disposable SQLite procedure below. No backend/schema/migration/package change or development seed was introduced by 4.4. Phase 4 closure records the reviewed approved scope, without claiming production readiness or removing documented limitations. Test accounts and graph content are disposable; create the main graph through the UI, using API fixtures only for auxiliary ownership scenarios.

In an empty workspace choose Criar mapa, enter title/optional description, save, then Editar mapa → Adicionar nó. View mode retains navigation/selection/local movement; edit mode enables metadata, node/edge actions and ChildMap choices. New nodes use (node count × 240, 0); use graph controls to frame the view. Titles allow up to 200 UTF-16 units after trim; descriptions up to 4000. Backend remains authoritative. Empty description/child fields produce clear flags on edit, omitted fields preserve values.

There is no keystroke autosave. Edit-mode drag PATCHes once at stop; keyboard movement uses Salvar posição do nó. Failed position writes restore the last confirmed position. Saves/creates/deletes are pessimistic, with one pending write per editor and explicit confirmations for deletions. Writes obtain fresh CSRF through the existing session client; none automatically replay, including POST after network/500/csrf_invalid/401. After ambiguous outcomes reload before creating again. Foreign/missing resources have the same unavailable feedback; map_in_use/node_in_use instruct removing references explicitly. Navigation discards drafts and does not abort writes already sent; late completions do not affect another visit. A revisit during an outstanding write may require reload after that write finishes; no cross-visit cache/concurrency coordinator is introduced.

Validation on 2026-10-15: backend build PASS (zero warnings/errors), 79 unit and 179 integration tests PASS; safe :memory: EF pending-model changes NONE. Frontend build/lint and 65 existing Node checks passed; 37 additional dependency-free checks cover real authoring validators, request/PATCH shapes, eight operations and CSRF/no replay. Five real HTTPS API/client checks cover missing CSRF and foreign map/node/child/edge references. These ad hoc checks add no framework and do not prove React lifecycle timing.

Trusted-HTTPS browser validation used a freshly migrated disposable database and a temporary fixture/proxy outside the repository. The main UI flow passed: login/empty workspace/first map; two nodes; drag-stop save; metadata; edge creation; F5 persistence of title/description/position/edge/ChildMap; drill-down/internal Back; clear description/ChildMap; real node_in_use/map_in_use; edge then node deletion; rename/picker synchronization; Back/Forward/F5; current map deletion returning to empty picker; logout. Desktop 1440×1000, tablet 768×1024 and mobile 390×844 form/control checks passed without horizontal document overflow. Keyboard movement plus explicit save and failed-position rollback passed. No full screen-reader or real touch-device audit was run.

The temporary HTTPS proxy forwarded to the unchanged API with certificate validation enabled. It recorded only method/path/status and boolean cookie/CSRF/Bearer/workspace/cache metadata, never values or bodies. UI writes carried CSRF/cookies; GETs carried no CSRF; no Bearer/workspace override/demo call or duplicated create POST appeared. One-shot proxy faults (not backend/database changes) exercised write 401 with valid session, 500, connection loss and position-save failure; shell/drafts stayed recoverable, no automatic replay occurred, manual retry succeeded, and position rollback matched the last saved value. Foreign invalid_child_map/invalid_edge_nodes were tested through the real API/client, not forced through UI selection. Production hosting/body-limit/scale and adversarial lifecycle races remain outside this smoke evidence.

## Project context

- Project: Talynor.
- Local development convention: `C:\dev\Talynor`.
- Intended GitHub owner: `binho-alves`.
- Current stage: Phase 4 and its approved slices 4.1, 4.2, 4.3A, 4.3 and 4.4 are complete, approved and published; Phase 3 remains complete. Phase 5 — Knowledge Content is in progress; Note/Link foundation/read is complete/published, Content Authoring Backend is complete/published under ADR-013 and Frontend Content Integration is implemented/in review.

The owner name and local path do not authorize creating a remote repository, initializing Git, committing, or pushing.

## Working scope

Keep tasks small and reviewable. Read [AGENTS.md](../AGENTS.md) and the relevant project documents before implementation. Resolve scope and architectural conflicts before making dependent changes.

The original foundation task created only README.md, AGENTS.md, and the six documents in docs/. It did not authorize application code, a .NET solution, a React application, package installation, Docker infrastructure, database configuration, migrations, or CI. SPIKE-001 separately authorizes the initial frontend and its necessary dependencies; see [the spike README](../src/frontend/talynor-web/README.md).

Later roadmap phases describe intended work, not standing implementation authorization.

The authorized Phase 2 skeleton uses `Talynor.slnx`, SDK selection in `global.json`, and shared nullable/implicit-usings/warnings settings in `Directory.Build.props`. The API provides `/health`, Development-only OpenAPI, and structured JSON console logging. VS-001 adds demonstration-only map reads without content persistence or authentication. Phase 3.1 adds only role-free Identity persistence; the demonstration endpoint remains anonymous/in-memory. The React frontend remains separate; the official frontend package manager remains open.

Backend verification from the repository root:

```powershell
dotnet restore Talynor.slnx
dotnet build Talynor.slnx --no-restore
dotnet test Talynor.slnx --no-build
```

Run the local API with `dotnet run --project src/backend/Talynor.Api --launch-profile https`. Integration tests use an in-process host; unit tests validate the read use case through a small fake reader.

In another PowerShell terminal, from `src/frontend/talynor-web`:

For the current authenticated frontend, first configure the trusted frontend certificate/key described in the [3.4 procedure](#phase-34---authenticated-frontend). The proxy target alone does not provide browser HTTPS.

```powershell
$env:TALYNOR_API_PROXY_TARGET = 'https://localhost:7117'
pnpm dev
```

The example target matches the current HTTPS launch profile; update local configuration if its port changes. Vite proxies `/api` to this target, while React uses relative URLs. Build/lint do not require the target. The proxy is not a production hosting or `vite preview` configuration. No backend CORS is needed for this development setup.

TLS verification is enabled by default. If Node reports a certificate trust error for the local development certificate, diagnose it and establish correct certificate trust. In the historical VS-001 Node 24 Windows environment, `$env:NODE_OPTIONS = '--use-system-ca'` let the proxy trust the existing development certificate without disabling verification. Phase 3.5 required the process-specific OpenSSL trust configuration recorded below; do not assume that every local Node runtime uses the same trust setup.

Historical VS-001 context only: `$env:TALYNOR_API_PROXY_INSECURE = 'true'` was a permitted local fallback for the anonymous demonstration experiment; the historical configuration rejected it for non-loopback or non-HTTPS targets; the current authenticated configuration rejects the flag entirely. It disables certificate verification on the Vite-to-API proxy connection and is not authorized for authenticated flows. Do not use it to bypass TLS problems in Phase 3.2 or future session integrations. Establish correct certificate trust instead; Secure cookies and TLS validation must not be weakened. Never store secrets in these variables or tracked files.

VS-001 frontend verification uses `pnpm build`, `pnpm lint`, and browser checks for navigation, request errors, retry, cancellation, and responsive layout. pnpm remains the available tool, not the official package manager decision.

## Phase 3.1 - Identity persistence

Identity/EF Core/SQLite packages and the local `dotnet-ef` tool use version 10.0.12. The shared ASP.NET Core runtime may select a compatible installed maintenance patch; the diagnostic version check accepts 10.0 patches from 10.0.12 while preserving all scheme/validator/behavior assertions. Restore the versioned `.config/dotnet-tools.json` from the repository root:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef --version
```

The tool is local; a globally installed `dotnet-ef` is not the project prerequisite. Infrastructure owns the role-free `TalynorDbContext`, design-time factory, and migrations. Phase 3.1 introduced no Identity HTTP endpoints, account/session services, workspace, or knowledge persistence; the later 3.2A infrastructure is described below.

### Database selection and explicit update

The API reads `ConnectionStrings:Talynor`. Development defaults to `Data Source=talynor.local.sqlite`, resolved against the API content root (normally `src/backend/Talynor.Api`). This database and its sidecars match the existing `*.sqlite` ignore rules. Other environments have no database default. Build, test, and API startup do not create this development file; the DbContext does not connect until used.

The design-time factory does not load API appsettings or launch the host. It requires the explicit environment variable below. Use an absolute path so tooling and the API select the same file irrespective of their working directories. From the repository root, for a local development database:

```powershell
$talynorDbPath = [IO.Path]::GetFullPath(
    'src/backend/Talynor.Api/talynor.local.sqlite',
    (Get-Location).ProviderPath
)
$env:ConnectionStrings__Talynor = "Data Source=$talynorDbPath"
dotnet tool run dotnet-ef database update --project src/backend/Talynor.Infrastructure
dotnet tool run dotnet-ef migrations list --project src/backend/Talynor.Infrastructure
```

Running `database update` again applies no new migration when the schema is current. The application never runs migrations automatically. Before each update, verify the selected path/environment and review the migration; do not substitute `EnsureCreated`.

After an explicit successful build, the initial migration was generated with the following command (already present; do not regenerate it):

```powershell
dotnet tool run dotnet-ef migrations add InitialIdentity --project src/backend/Talynor.Infrastructure --output-dir Persistence/Migrations --no-build
```

This command also requires `ConnectionStrings__Talynor`; `Data Source=:memory:` can be used for migration generation because it does not initialize a local file. Future migration names require a scoped change and review. Do not include credentials in command arguments or tracked settings. After local work, clear an override when it is no longer needed:

```powershell
Remove-Item Env:ConnectionStrings__Talynor -ErrorAction SilentlyContinue
```

### Safe local reset

Only reset disposable development data, after stopping the API and database tools. Preserve any data that matters first. Resolve and inspect the exact default path, then remove only that known database and its sidecars; never recursively delete a computed directory:

```powershell
$talynorDbPath = [IO.Path]::GetFullPath(
    'src/backend/Talynor.Api/talynor.local.sqlite',
    (Get-Location).ProviderPath
)
Write-Output $talynorDbPath
# After verifying this is the disposable local database you intend to reset:
foreach ($suffix in @('', '-wal', '-shm', '-journal')) {
    $talynorDbFile = $talynorDbPath + $suffix
    if (Test-Path -LiteralPath $talynorDbFile) {
        Remove-Item -LiteralPath $talynorDbFile
    }
}
$env:ConnectionStrings__Talynor = "Data Source=$talynorDbPath"
dotnet tool run dotnet-ef database update --project src/backend/Talynor.Infrastructure
```

Do not use this procedure against production or an overridden database without verifying its path and data implications. Migrations remain versioned source; the resulting SQLite file remains untracked/ignored. No development user or password is seeded.

### Persistence verification

Integration tests apply real migrations to SQLite, not the EF InMemory provider. They verify the role-free schema and repeat update, persist a synthetic passwordless Identity record through `UserOnlyStore`, reopen an independent context/connection, and confirm that API startup and anonymous demo reads do not create a database. File tests use unique temporary paths with pooling disabled and remove database sidecars in `finally`. This technical fixture is not a usable demo account or a registration/password-validation workflow.

Run the backend restore/build/test commands above and frontend build/lint for VS-001 regression. No PostgreSQL, Testcontainers, startup migration, or frontend dependency is introduced.

## Phase 3.2A - Session HTTP foundation and CSRF bootstrap

3.2A registered Identity services/stores, only the application-cookie scheme, authorization without a fallback policy, and native antiforgery with anonymous `GET /api/auth/csrf`. 3.2B.1 adds register, account creation policy, and registration rate limiting; B.2A adds the technical auxiliary handler, and B.2B adds login/logout/me and current user as described below. That slice did not implement frontend consumption; the current client is described in 3.4 below. Application startup and CSRF bootstrap still do not initialize/migrate the database.

Use the existing API `https` launch profile (`https://localhost:7117`). Inspect certificate trust manually when needed; these commands are not application side effects and were not run by this slice:

```powershell
dotnet dev-certs https --check --trust
# Only if trust is needed, with user approval:
dotnet dev-certs https --trust
dotnet run --project src/backend/Talynor.Api --launch-profile https
```

With the API running and certificate trusted, query bootstrap directly over HTTPS. Keep the cookie jar and request token in memory, without printing/logging their values:

```powershell
$talynorCsrfSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$talynorCsrf = Invoke-RestMethod -Uri 'https://localhost:7117/api/auth/csrf' -WebSession $talynorCsrfSession
[string]::IsNullOrWhiteSpace($talynorCsrf.token) # Expected: False
Remove-Variable talynorCsrf, talynorCsrfSession
```

The response is `{ "token": "..." }` with no-store and an HttpOnly/Secure/SameSite Strict antiforgery cookie. This does not create an authenticated session. Account requests use `X-CSRF-TOKEN`; the 3.4 frontend retains the request token only in memory and fetches a new one after authentication changes. Do not assume that fetching a new token makes all prior tokens one-time or immediately revoked.

Data Protection uses host defaults in application code; no key ring, key database, or custom certificate is configured. Key persistence and protection depend on the host profile, and deployment will need an explicit review. New integration tests use an ephemeral provider only for isolation. They verify bootstrap/token-pair validity, cookie/scheme/401/403 configuration, and the unchanged runtime EF model. Existing tests verify anonymous demo/health/OpenAPI behavior and startup/bootstrap without database creation. No artificial POST or unit test is added; account-POST rejection belongs to 3.2B. WebApplicationFactory's HTTPS address does not validate a real certificate or browser cookie enforcement.

After building, check the unchanged design-time model from the repository root without creating a file:

```powershell
$env:ConnectionStrings__Talynor = 'Data Source=:memory:'
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/backend/Talynor.Infrastructure --no-build
Remove-Item Env:ConnectionStrings__Talynor -ErrorAction SilentlyContinue
```

That backend slice left Vite and React unchanged; 3.4 extends them below. `NODE_OPTIONS=--use-system-ca` helps Node trust the HTTPS API; it does not enable HTTPS on the frontend dev server. Browser authentication integration is implemented in 3.4, with real HTTPS verification still required. Do not weaken Secure/TLS settings to accommodate it.

## Phase 3.2B.1 - Account registration

After explicitly selecting and migrating the intended disposable local database using the existing procedure, run the API's trusted HTTPS profile. Startup does not create/update the database. The in-process test suite creates its own isolated temporary SQLite files; it never uses the Development database.

Manual verification sequence, without printing secrets or embedding credentials in commands/history:

1. Create an in-memory WebRequestSession/cookie jar and call GET `/api/auth/csrf` over trusted HTTPS, as shown above.
2. Obtain a disposable email and password through interactive input (masked for password). Build the JSON request in memory; do not put literal credentials in a script, shell command, transcript, tracked file, or request log. Any conversion required for JSON briefly materializes the password in memory; clear references promptly and do not claim managed memory is securely erased.
3. POST `{ "email": "...", "password": "..." }` to `/api/auth/register` using the same cookie jar and header X-CSRF-TOKEN from bootstrap. Inspect only status and sanitized error categories, not tokens, payloads, password/hash values, or complete headers.
4. Expect 201 with an empty body, no Location, and no auth cookie. The account is unconfirmed and the client remains anonymous. Registration does not require a new CSRF token because identity is unchanged.
5. Equivalent duplicate email returns generic 400 registration_failed; invalid fields/policy return 400 validation_failed. Native CSRF failures return 400 csrf_invalid. An already-authenticated session returns 409 already_authenticated; B.2B provides the login workflow described below.
6. Five registration requests per ten minutes per remote IP are admitted; an admitted request consumes quota even if CSRF/input rejects it. The sixth returns 429 rate_limited. Do not bypass quota through untrusted forwarded headers; bootstrap/health/maps do not consume it.
7. Dispose the cookie jar and remove all token/password/request references after verification. Do not lower Secure cookies or TLS verification to accommodate a client.

Run the root restore/build/test commands and the unchanged frontend build/lint commands. Reuse the documented `has-pending-model-changes` command with `Data Source=:memory:`; expect no model change. No new dependency, migration, DbContext change, frontend code, or development account seed is introduced. Do not run migrations on the Development database just to run tests.

New unit tests exercise RegisterUser through a creation-only fake. HTTP integration tests use native CSRF and cookies, a unique temporary SQLite file per factory, explicit fixture migrations, Pooling=false, and cleanup after disposing the host. They cover policy/limits, success without authentication, generic duplicates, existing-session rejection, safe errors, real concurrent unique-index failure, negative operational-fault injection, and IP quota behavior. The concurrency interceptor synchronizes two pending inserts with bounded waits and no sleeps; busy/PK/other-unique failures are not treated as duplicates. IP overrides exist only in the test host. TestServer does not validate real TLS, browser SameSite, or actual enforcement of the native 8 KiB server request-size limit.

The published 3.2B.2A fixes the reproduced missing-TwoFactorRememberMe rejection path using only the native auxiliary cookie helper, without changing the native SecurityStampValidator or implementing MFA/session endpoints in that slice. Run `dotnet test tests/Talynor.IntegrationTests/Talynor.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~SecurityStampDiagnosticTests` first, then the full backend and frontend checks above. The regression uses a real SignInManager/cookie, temporary migrated SQLite, and a test-only TimeProvider advancing beyond the unchanged 30-minute interval. It covers valid-stamp renewal, invalid-stamp 401/cookie deletion, and original-ticket replay rejection. Both diagnostic routes return 404 outside the customized factory. No Talynor flow issues the technical cookie.

## Phase 3.2B.2B - Session Flow

Completed, approved, and published. Use the existing trusted HTTPS API profile and an explicitly migrated disposable local database; no database is created/migrated at startup. Test infrastructure uses only isolated temporary SQLite files with Pooling=false and cleanup after host disposal. RegistrationTestFactory is reused with a small optional service-configuration hook for the controlled session clock; no production header trust or time override is added.

Manual sequence with the same in-memory cookie jar throughout:

1. GET `/api/auth/csrf`, keeping its token only in memory.
2. Obtain disposable email/password through interactive input, masking password as in the registration procedure. Build `{ "email": "...", "password": "..." }` only in memory, then POST `/api/auth/login` with `X-CSRF-TOKEN`. Expect empty 204 and the HttpOnly/Secure session cookie; do not print the payload, token, cookie, ticket, or full headers. Clear credential references promptly without claiming managed memory is securely erased.
3. GET `/api/auth/csrf` again after authentication. The anonymous token must not be assumed valid for an authenticated unsafe action.
4. GET `/api/auth/me` without a CSRF header: expect 200 with only current id/email and no-store. Inspect only status and needed synthetic account fields, never internal claims/hash/stamp.
5. POST `/api/auth/logout` with the refreshed token: expect empty 204 and local cookie deletion. GET me now returns sanitized 401 without redirect. Another browser's session remains independent.
6. GET `/api/auth/csrf` again for the anonymous identity before another login. Do not assume refresh globally revokes previous tokens or makes them one-time. Dispose the jar and clear token/request references when finished.

Login failures (missing account, wrong password, locked out, or not allowed) are uniform 401 invalid_credentials; an existing session is 409 already_authenticated. Input is 400 validation_failed, native CSRF failures 400 csrf_invalid, login quota 429 rate_limited, and operational failures sanitized 500 unexpected_error. All responses are no-store. Login quota is ten requests/minute per remote IP; invalid CSRF requests also consume it. Native lockout uses five failures/five minutes; tests control LockoutEnd rather than sleeping. Registration retains its own five-request/ten-minute quota.

Run tool restore, solution restore/build/tests, unchanged frontend build/lint, and the safe in-memory pending-model command above. SessionUseCaseTests cover Application decisions and failure propagation; CurrentUserTests cover fail-closed IDs; SessionFlowTests cover real registration/login, cookie properties, login validation, CSRF transitions, lockout, independent IP quotas, account reads/removal/provider failure, multiple browsers/local logout, and real-login stamp rejection/expiry with a controlled clock. Published B.2A regressions remain unchanged. No new package, migration, frontend code, or Development seed is added. TestServer does not establish real browser SameSite/TLS, Kestrel body-size enforcement, or deployment readiness.

## Phase 3.3 - Personal Workspace and Ownership

Completed, approved, and published. Phase 3.2 is completed and published. The AddPersonalWorkspace migration adds only PersonalWorkspaces and its UNIQUE owner index. Review it, explicitly select the disposable local SQLite path using the existing database-selection procedure, then run `dotnet tool run dotnet-ef database update --project src/backend/Talynor.Infrastructure`. This explicit operation applies both existing Identity and workspace migrations as needed. It was not run against the Development database by this implementation; startup never migrates/initializes/seeds it. Account deletion does not cascade to workspaces; cleanup/lifecycle remain future work.

Manual verification uses the trusted HTTPS API and the same in-memory cookie jar from the Session Flow procedure:

1. GET `/api/auth/csrf`, retain the request token only in memory, and POST `/api/auth/login` with disposable credentials obtained interactively and the X-CSRF-TOKEN header. Registration/login do not create a workspace.
2. Refresh CSRF after login for subsequent unsafe auth actions. GET `/api/workspace` needs no CSRF header and accepts no owner/workspace ID. First access creates an empty workspace and returns 200 with only id/createdAtUtc and no-store.
3. Repeat GET workspace: the ID and original creation time stay unchanged. In a separate authenticated cookie jar for another account, expect a different workspace; each browser continues to receive its own. Query-supplied owner/workspace IDs cannot select another user's record.
4. An anonymous cookie jar receives sanitized 401. Logout remains the existing CSRF-protected operation and ends only the current browser session. Do not print cookies, credentials, CSRF tokens, tickets, full headers, or real account data. Clear in-memory references as in the existing procedure.

Run tool/solution restore, solution build/tests, frontend build/lint, and the existing safe pending-model command with `Data Source=:memory:`. PersonalWorkspaceUseCaseTests cover authentication, account existence, owner derivation, idempotent lookup, controlled creation time, and error propagation. PersonalWorkspaceTests cover real register/login, lazy initialization, exact response shape, sequential reuse, two-user isolation, synchronized simultaneous HTTP access, removed accounts, native stamp/provider failures, and Development OpenAPI. PersonalWorkspacePersistenceTests cover cross-context roundtrip, the actual UNIQUE constraint/recovery, and non-owner provider failures. RegistrationTestFactory is reused without another host factory; databases are temporary/migrated explicitly with Pooling=false, ephemeral Data Protection, and cleanup.

Phase 3.3 left Knowledge Maps and frontend unchanged; 3.4 adds frontend sessions without changing the demo map API. No production deployment/browser-security claim is made. SQLite-specific conflict recognition must be adapted when a future PostgreSQL transition is explicitly authorized. No new dependency or frontend package manager decision is made.

## Phase 3.4 - Authenticated Frontend

Completed, approved, and published; 3.5 real-browser validation and Phase 3 closure are completed. No backend, EF model, migration, or dependency is changed by 3.4. Historical slice procedures above describe what each slice introduced; current browser integration follows this section.

Use an explicitly selected/migrated disposable database and an account registered through the existing trusted HTTPS API procedure. There is no seed or frontend registration screen. Start the API from the root with its existing https profile. For the frontend, provide a trusted localhost PEM certificate and matching private key **outside the repository**. Certificate provisioning/export/trust is an explicit local prerequisite; Vite does not generate or trust certificates, and private keys must never be committed or logged.

From `src/frontend/talynor-web`, replacing the path placeholders with existing trusted files:

```powershell
$env:TALYNOR_API_PROXY_TARGET = 'https://localhost:7117'
$env:TALYNOR_HTTPS_CERT = '<absolute path outside the repository to trusted localhost certificate PEM>'
$env:TALYNOR_HTTPS_KEY = '<absolute path outside the repository to matching private key PEM>'
# When supported by Node and needed to trust the API certificate through the Windows store:
$env:NODE_OPTIONS = '--use-system-ca'
pnpm dev
```

Open the **https://localhost** address printed by Vite. Both browser-to-Vite and Vite-to-API require trusted TLS; never bypass a browser certificate warning or set TALYNOR_API_PROXY_INSECURE. Configuration now rejects that historical anonymous-spike flag and plain HTTP API targets. Certificate/key variables contain paths, not secret material, and are required only for the dev server; build/lint need neither. The proxy still routes only relative `/api`, without backend CORS. Production/preview routing is not configured.

Manual verification when these prerequisites are available:

1. Open anonymously: loading precedes login; me 401 must not request workspace. Network/500 must show session error/retry, not a false logout.
2. Submit invalid credentials using the labeled form/Enter: generic invalid-credentials feedback, no account/lockout details; 429 asks the user to wait. Credentials must not appear in console output, URLs, storage, or screenshots.
3. Submit valid disposable credentials: anonymous CSRF, login 204, renewed CSRF, me, then workspace. The header email comes from me, not the form. Workspace IDs are not displayed or client-selected.
4. Verify demo software/C# maps, selection/details, drag, zoom/pan/minimap, drill-down/back and preserved positions between map visits. Map errors/retry must leave the session intact.
5. Refresh: bootstrap me/workspace restores the authenticated shell from the HttpOnly cookie. Anonymous refresh shows login. Browser storage contains no auth/password/CSRF data; CSRF is never rendered.
6. Logout: authenticated CSRF and logout 204 return to login; me is then 401. New anonymous CSRF is obtained on next login. Logout 401 also ends local state; other failures show recoverable feedback without claiming success. Invalid logout CSRF gets at most one renewal/retry; invalid login CSRF renews once and needs another user submission.
7. Check desktop/tablet/mobile layout, keyboard focus/Enter, labels, error/status announcements. Another tab may remain visually authenticated until its next protected request/reload; no cross-tab sync or session polling is implemented.

Run `pnpm run build` and `pnpm run lint`, the root backend build/test commands, and the safe in-memory pending-model command above. No frontend test framework or test script is configured/installed; extracted HTTP/session functions permit dependency-free one-off validation with simulated fetch. Those checks do not prove real browser cookie, SameSite, TLS, or React rendering behavior. Record actual manual verification separately; absence of trusted certificates is a verification limitation, not permission to weaken TLS.

### React learning notes for session integration

| Concept | Where and why | Approximate Angular analogy |
| --- | --- | --- |
| Custom hook | `useSession` composes session state, effects and event actions without mixing graph state. | Reusable composition of component logic; a service can serve a related role, but hooks do not create shared singleton state. |
| State and discriminated union | Bootstrapping, anonymous, authenticated, and error branches prevent an authenticated flash or incomplete user/workspace render. | Component-local signals and conditional templates. |
| useEffect and cleanup | Starts read-only bootstrap after mount and cancels obsolete requests on cleanup, including StrictMode's development checks. | Lifecycle/reactive effects with teardown; not a direct ngOnInit equivalent. |
| useRef | Holds the active AbortController/request identity without causing renders; stops duplicate submissions and stale results. | A component-local mutable field, not reactive template state. |
| Props and callbacks | LoginView owns transient form state and calls the hook through App; KnowledgeGraph owns its own visual state. | Inputs/output event bindings, with function callbacks instead of EventEmitter. |

Context/Provider was evaluated and is not needed for this shallow tree. If later justified, Context/Provider can share values through the React tree, loosely analogous to service/state injection at a chosen Angular component scope; that is not a perfect equivalence. No Context, router, or state library is introduced now.

## Phase 3.5 - Validation and Phase Closure

Completed on 2026-10-13. The validated flow is: anonymous browser → GET /api/auth/me 401 → login screen → CSRF → login 204 → refreshed CSRF → me 200 → workspace 200 → authenticated app → CSRF-protected logout 204 → login screen. Protected-read 401 and logout with a removed session return safely to login. The remaining graph API is still a global in-memory demo, not persisted personal content.

Validation used disposable SQLite outside the repository, with InitialIdentity then AddPersonalWorkspace applied explicitly; startup did not migrate, initialize, or seed the database. An existing trusted localhost certificate served the API and Vite browser HTTPS, with PEM/key copies outside the repository. The local Node system-CA setup did not establish proxy trust; the working process configuration used `--use-openssl-ca` with `SSL_CERT_FILE` pointing to the already trusted public localhost PEM. TLS verification stayed enabled; no insecure proxy, certificate-warning bypass, or cookie-policy change was used. These are local process settings, not a production hosting decision.

Real-browser checks passed for anonymous bootstrap, invalid/valid login, cookie acceptance, me/workspace, refresh, graph navigation/selection/drag/zoom/pan/minimap, preserved positions, temporary map-error isolation/retry, logout, desktop/tablet/mobile layouts, keyboard/labels/focus/disabled feedback, console, and multiple-tab behavior. Chrome Storage and Network metadata were confirmed by assisted manual inspection; cookie, password, and token values were not included in reports. Request tokens remain in memory and auth data is not stored in JavaScript persistence.

Final evidence: backend build PASS (zero warnings/errors), 33 unit and 111 integration tests PASS, frontend build/lint PASS, EF pending-model changes NONE, and Git clean at validation completion. No code correction or dependency was needed. API/Vite were stopped and the disposable database, sidecars, and PEM/key copies removed; the original Windows certificate was preserved. The procedure above remains available for regressions. Real-device touch, full accessibility, Kestrel body-limit enforcement, and deployment security are not established by this smoke validation. At Phase 3 closure, Phase 4 was next and not started; the subsequent Phase 4.1 delivery is recorded below.

## Phase 4.1 - Knowledge Map Persistence Foundation

Complete, approved and published. Review AddPersistentKnowledgeGraph Up/Down, Designer and Snapshot before applying it to explicitly selected disposable data. Migration order is InitialIdentity → AddPersonalWorkspace → AddPersistentKnowledgeGraph. Use the existing absolute-path environment variable and `dotnet tool run dotnet-ef database update --project src/backend/Talynor.Infrastructure`; startup and build still never initialize/migrate/seed a database. The implementation only applies migrations to test databases, not the Development database.

Tests explicitly create workspaces/maps/nodes/edges through the Infrastructure DbContext in a test-only KnowledgeGraphFixture. There is no map seed command/default account or public write endpoint. Real SQLite tests cover roundtrips across contexts, positions/child links, composite endpoint FKs, cross-workspace child triggers on insert/update and map moves, foreign/missing 404 equality, concurrent session reads, anonymous 401, no-store, removed accounts, reader/native-stamp failure sanitization, deletes and migration rollback/reapplication. Existing workspace insertion-concurrency and demo/session regressions remain applicable.

Run tool restore, solution restore/build/tests, frontend build/lint and the safe in-memory pending-model command above. Do not regenerate existing migrations or use EnsureCreated to skip them. SQLite equality triggers are explicitly migration-owned; EF pending-model checks do not independently inspect live trigger bodies. Review trigger SQL and test behavior on every rebuild/rollback/provider transition. Phase 4.1 did not implement an editing-concurrency policy. [ADR-012](DECISIONS.md#adr-012---knowledge-graph-authoring-api) records the Phase 4.2 baseline and controlled handling of expected relation constraints; advanced optimistic concurrency remains future work, with no rowversion, ETag or If-Match.

Manual private-read verification requires trusted HTTPS, a disposable registered account and explicitly prepared persisted fixture data; 4.1 introduced no frontend authoring/rendering, and the frontend is still unchanged in 4.2. Obtain the cookie using the existing CSRF/login procedure, then GET `/api/maps/<fixture map Guid>` with the same in-memory jar, without WorkspaceId or a CSRF header. Expect only id/title/description/nodes/edges, node position:{x,y}/childMapId and edge endpoint IDs; never owner/workspace IDs. Own maps return 200; unknown/foreign maps return identical 404; anonymous sessions return 401; operational failures return sanitized 500. All private responses are no-store. Never print cookies, tokens, credentials or real private map content. Follow links only through the same authenticated endpoint. GET may lazily initialize an empty workspace as previously approved; it never seeds maps.

Through Phase 4.2, the frontend still loaded `/api/knowledge-maps/software` and `/csharp`; Phase 4.3 replaces the authenticated client source. Both anonymous demo regressions remain; they do not grant public access to persisted maps. Phase 3.5 browser evidence is historical and does not constitute manual browser validation of the new backend-only endpoint.

## Intended workflow

1. **Diagnose:** inspect the current state, relevant documentation, and the problem before editing. Read existing agent instructions and preserve unrelated work.
2. **Define scope:** identify the requested outcome, affected areas, constraints, and suitable verification. Resolve required missing information and architecture conflicts.
3. **Implement the smallest approved change:** avoid unrelated cleanup, speculative features, and unnecessary dependencies.
4. **Build:** run the appropriate build for affected code when an application exists. For documentation-only work, report the build as not applicable.
5. **Test:** run appropriate tests and required checks for the change. Report actual results, skipped checks, and limitations. Documentation-only work can use content and link verification without application tests.
6. **Run `git diff --check`:** check whitespace when the workspace is already a Git repository. Remember that untracked files are not included in a normal Git diff and require direct inspection.
7. **Run `git status`:** inspect the repository state if it is already a Git repository.
8. **Review `git diff`:** inspect tracked changes and directly review newly created, untracked files as needed.
9. **Report results:** list changed files, commands executed, tests or checks executed, results, warnings, errors, and any unresolved limitations.
10. **Commit only after explicit approval:** no instruction to edit or test implies permission to commit.
11. **Push only after explicit approval:** permission to commit does not imply permission to push.

If the folder is not a Git repository, report that fact and skip Git status and diff commands. Do not initialize Git unless explicitly instructed.

## Verification discipline

Use verification appropriate to the authorized scope. Do not install packages, generate an application, configure a database, or create CI just to validate foundation documents. Clearly distinguish successful checks from checks that were not run.

For the original foundation task, inspect all eight documents for consistency, validate local documentation links and the requested file tree, and run Git status only if the folder is already a Git repository. For SPIKE-001, build and lint the frontend, review the graph interactions, and perform the Git checks above. Report the result and stop for review without committing or pushing.

## Phase 4.2 - Map/Node/Edge Authoring Backend

Complete, approved and published; 4.1 is complete/published. Reuse the explicit migration selection and trusted HTTPS session procedure above. No new migration, schema change or seed exists. Tests use isolated migrated temporary SQLite with synthetic accounts and cleanup; never migrate Development just to run them.

With the same in-memory cookie jar, fetch a fresh CSRF token after login and send X-CSRF-TOKEN on every POST/PATCH/DELETE. Endpoint set: POST /api/maps; PATCH/DELETE /api/maps/{mapId}; POST /api/maps/{mapId}/nodes; PATCH/DELETE /api/maps/{mapId}/nodes/{nodeId}; POST /api/maps/{mapId}/edges; DELETE /api/maps/{mapId}/edges/{edgeId}. GET persisted reads/demos retain existing contracts. Phase 4.2 introduced no listing; the separately authorized 4.3A adds GET /api/maps below.

Create map body is title/optional description. Create node adds required position:{x,y} and optional childMapId. Create edge uses sourceNodeId/targetNodeId. IDs/times/ownership are server-generated; unknown request fields are rejected. PATCH omits fields to keep them; null optional values also mean keep. clearDescription:true and clearChildMap:true explicitly remove values; clear-plus-value and empty PATCH are invalid. Position must supply both coordinates. CreatedAtUtc is fixed. UpdatedAtUtc records the Application-supplied instant of an accepted map/node mutation operation; a valid PATCH repeating current material values may also change it. It is neither a concurrency token nor a global commit ordering, and does not require diff-before-update.

Creates return 201 with projections, map Location points to its read; updates/deletes return empty 204. Target missing/foreign resources return indistinguishable 404, relation failures 400 invalid_child_map/invalid_edge_nodes, input 400 validation_failed, native CSRF 400 csrf_invalid, and referenced map/node deletion 409 map_in_use/node_in_use. Unknown provider failures remain sanitized 500; every matched private endpoint is no-store. Remove edges before deleting a referenced node; clear incoming ChildMap links before deleting that map. 4.2 introduced no implicit edge removal, full JSON Patch, authoring limiter, frontend UI or list API; 4.3A subsequently added discovery.

Run tool/solution restore, build/test, frontend build/lint and the safe in-memory pending-model command above. New unit/HTTP/persistence tests cover mutations, timestamps, omitted/set/clear PATCH, each endpoint's anonymous/CSRF behavior, IDOR, same-map/same-workspace references, deletes, real constraint failures after relation removal and negative operational-fault classification. The existing reader/session/demo tests remain. Stored same-column updates are last committed write wins without tokens; the interleaved-context regression is not a broad concurrency/performance guarantee. The authoring HTTP tests characterize the native 32 KiB configuration on all eight MVC actions and fail if the limit is removed or changed. They do not send a payload above 32 KiB or prove oversized-body rejection/status/sanitization: the current TestServer harness does not supply Kestrel's native body-size enforcement. Real-server enforcement remains a separate verification. Do not log private content or credentials.

## Phase 4.3A - Persisted Map Discovery

Complete, approved and published; 4.1 and 4.2 are complete, approved and published. Use the same explicitly migrated disposable SQLite and trusted HTTPS login procedure. With the in-memory authenticated cookie jar, GET /api/maps without a CSRF header. Expect 200 and { "maps": [{ "id": "...", "title": "..." }] }, or { "maps": [] } when the current workspace has no maps. Discovery may resolve/create the previously approved empty personal workspace, but never creates or selects a map.

The query returns only current-workspace summaries, ordered by database Title ascending then stored Id ascending. SQLite currently uses default BINARY title collation, including case-sensitive ordering; this is not a locale/user-defined sort. Equal titles use ID as the deterministic tie-breaker. There is no pagination/search/filter/sort input. No result is a default/root map and callers must not assume the first item is selected.

Phase 4.3 implements login → workspace → GET /api/maps → user selects a map → GET /api/maps/{id}, or a direct map URL after session bootstrap. 4.3A itself left frontend unchanged; 4.4 Authoring UX is complete, approved and published above.

All responses are no-store. Anonymous/invalid/removed-account sessions get sanitized 401; a missing account triggers existing local sign-out. Account/reader/provider failures and native stamp failures get sanitized 500, never a misleading empty list. WorkspaceId/OwnerUserId query parameters cannot override server ownership and are not returned. An empty list describes only this user's workspace. GET needs no CSRF token; create+discover uses the existing CSRF-protected POST /api/maps followed by this read.

Run tool restore, solution restore/build/tests, safe in-memory EF pending-model verification and unchanged frontend build/lint. Discovery unit tests cover current account, lazy workspace, zero/one/many summaries, reader-order/cancellation forwarding and failures. Real SQLite/HTTP tests cover create+discover, exact envelope/summary shape, two-user isolation with query overrides, an empty third user, title/ID ordering, no-store, untracked projection without graph-table reads, removed accounts, account/reader/native-stamp failures, Development OpenAPI and demo/authoring regressions. Tests use temporary migrated databases with cleanup, never Development data. No schema/migration/dependency/frontend change exists; unpaginated collection size and future provider collation behavior need review as real usage grows.

## Phase 4.3 - Persistent Graph Frontend Integration

Complete, approved and published; 4.3A is complete, approved and published. The following records the read-only 4.3 validation; 4.4 authoring is described above. Reuse trusted HTTPS and explicitly migrated disposable SQLite. Prepare a disposable account and maps through the published CSRF-protected 4.2 API, outside application startup; do not create a permanent seed. Prepare Map A with known coordinates, two nodes/one edge and a ChildMapId to B; B may link back to A for cycle validation. A second empty account tests empty discovery and foreign-map 404.

The normal flow is login → me → workspace → discovery → explicit choice → private map read. Without ?map, zero maps means an empty message and no individual read; one/many maps still require a click. With `?map=<guid>`, F5 bootstraps session/workspace then reads that map. Invalid/duplicate map parameters show an invalid-address state without sending an invalid map request. Direct entry/reload offers Todos os mapas; current-scope visits support internal Back and browser Back/Forward. No parent identity is assumed.

Check selection/details, local drag and reset on revisit/F5, edges/known positions, A → B → A, picker return, loading, missing/foreign 404, simulated operational failure/retry preserving the shell, and private-read 401/session recheck without loops. GETs need no CSRF bootstrap/header and send no client workspace ID/Bearer. In Network inspect only metadata: HTTPS /api, automatic cookies, no-store and no demo/authoring calls during graph exploration. Never expose password/cookie/token values. Verify keyboard interaction, visible focus, status/error feedback, desktop/tablet/mobile overflow and console.

Run dotnet build Talynor.slnx --no-restore; dotnet test Talynor.slnx --no-build; the safe :memory: pending-model command above; pnpm run build; pnpm run lint; git diff --check/status/diff/stat. No new frontend test framework is installed. Dependency-free ad hoc Node checks can import validators, pure adapter and URL helpers to exercise malformed JSON, finite coordinates, duplicate/dangling IDs, URL parsing, HTTP status mapping and cancellation. They complement real browser checks; they do not prove React lifecycle or browser cookie behavior.

React notes: useEffect starts/cancels each request (roughly lifecycle/effect plus teardown in Angular); AbortController cancels obsolete I/O (related to HTTP unsubscribe, with an explicit signal); visit identity/current guard rejects stale completions, including StrictMode. usePersistedMapNavigation composes local navigation logic (roughly reusable scoped service logic, without singleton state). URLSearchParams and popstate are native browser APIs (rather than Angular Router). Props/callbacks separate orchestration from KnowledgeGraph (Inputs/Outputs analogy). Selection/loading are derived from current nodes/request identity instead of duplicated state (computed signals analogy).

Validation on 2026-10-15: backend build PASS with zero warnings/errors; 79 unit and 179 integration tests PASS; EF pending-model changes NONE; frontend build/lint PASS; 65 dependency-free Node checks PASS. Local links/anchors (49) and changed/new-file whitespace checks passed. These are current-slice results, not reused Phase 3 evidence.

Real-browser trusted-HTTPS checks passed for login, discovery/explicit picker, empty workspace, private map nodes/edge/backend coordinates, selection/details, mouse drag and keyboard movement, restored coordinates/cleared selection on F5, ChildMap A → B → A, internal Back, browser Back/Forward, picker return, deep-link reload, invalid URL, missing/foreign 404, operational map/discovery failure with retry, logout and cross-tab private-read 401/session recheck without loops. Desktop 1440×1000, tablet 768×1024 and mobile 390×844 layout/keyboard smoke checks passed without horizontal page overflow. A temporary fixture page outside the repository prepared accounts/maps through the published API; fault injection affected only the disposable SQLite and was reversed. API/Vite/fixture processes were stopped and the disposable database/certificate copies removed; the original trusted Windows certificate was preserved.

Chrome DevTools headers are not exposed directly by the automation, so network verification used an additional temporary localhost HTTPS proxy outside the project, configured only through the existing Vite process environment. It forwarded to the unchanged API with TLS verification enabled and retained only method/path/status and boolean header-presence/cache indicators, never header values or request/response bodies. Normal browser discovery/map/drill-down/return/F5 reads had cookies, no-store and no Bearer/WorkspaceId/CSRF header; no demo or authoring request occurred. Picker-before-choice and the empty workspace issued no individual map read; invalid ?map=abc issued no malformed map request; foreign/missing 404 and logout metadata also passed. The proxy/fixture was removed after verification; no production proxy or backend/configuration-file change was introduced.

Fresh validation tabs showed no relevant React/uncaught/TLS/CORS warnings. The preexisting Chrome tab retained earlier Vite WebSocket startup errors; fresh tabs did not reproduce them. Chrome screenshot capture timed out; integrated-browser screenshots supplied visual evidence outside the repository. The first cleanup stopped local services before the requested manual Network confirmation, producing expected connection errors on the leftover pages; the subsequent instrumented browser pass above resolved that verification gap without asking the user to certify unknown details. No browser smoke result establishes full accessibility, real-device touch, scale/performance capacity or production hosting readiness.
