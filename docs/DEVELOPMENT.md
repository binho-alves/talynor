# Development workflow

## Project context

- Project: Talynor.
- Local development convention: `C:\dev\Talynor`.
- Intended GitHub owner: `binho-alves`.
- Current stage: Phase 3, 4.1 and 4.2 are complete, approved and published. Phase 4 is incomplete; 4.3A — Persisted Map Discovery is implemented and in review. Phase 4.3 — Persistent Graph Frontend Integration remains blocked until 4.3A is approved and published; 4.4 — Authoring UX is future.

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

Final evidence: backend build PASS (zero warnings/errors), 33 unit and 111 integration tests PASS, frontend build/lint PASS, EF pending-model changes NONE, and Git clean at validation completion. No code correction or dependency was needed. API/Vite were stopped and the disposable database, sidecars, and PEM/key copies removed; the original Windows certificate was preserved. The procedure above remains available for regressions. Real-device touch, full accessibility, Kestrel body-limit enforcement, and deployment security are not established by this smoke validation. At Phase 3 closure, Phase 4 was next and not started; current 4.1 work is recorded below.

## Phase 4.1 - Knowledge Map Persistence Foundation

Complete, approved and published. Review AddPersistentKnowledgeGraph Up/Down, Designer and Snapshot before applying it to explicitly selected disposable data. Migration order is InitialIdentity → AddPersonalWorkspace → AddPersistentKnowledgeGraph. Use the existing absolute-path environment variable and `dotnet tool run dotnet-ef database update --project src/backend/Talynor.Infrastructure`; startup and build still never initialize/migrate/seed a database. The implementation only applies migrations to test databases, not the Development database.

Tests explicitly create workspaces/maps/nodes/edges through the Infrastructure DbContext in a test-only KnowledgeGraphFixture. There is no map seed command/default account or public write endpoint. Real SQLite tests cover roundtrips across contexts, positions/child links, composite endpoint FKs, cross-workspace child triggers on insert/update and map moves, foreign/missing 404 equality, concurrent session reads, anonymous 401, no-store, removed accounts, reader/native-stamp failure sanitization, deletes and migration rollback/reapplication. Existing workspace insertion-concurrency and demo/session regressions remain applicable.

Run tool restore, solution restore/build/tests, frontend build/lint and the safe in-memory pending-model command above. Do not regenerate existing migrations or use EnsureCreated to skip them. SQLite equality triggers are explicitly migration-owned; EF pending-model checks do not independently inspect live trigger bodies. Review trigger SQL and test behavior on every rebuild/rollback/provider transition. Phase 4.1 did not implement an editing-concurrency policy. [ADR-012](DECISIONS.md#adr-012---knowledge-graph-authoring-api) records the Phase 4.2 baseline and controlled handling of expected relation constraints; advanced optimistic concurrency remains future work, with no rowversion, ETag or If-Match.

Manual private-read verification requires trusted HTTPS, a disposable registered account and explicitly prepared persisted fixture data; 4.1 introduced no frontend authoring/rendering, and the frontend is still unchanged in 4.2. Obtain the cookie using the existing CSRF/login procedure, then GET `/api/maps/<fixture map Guid>` with the same in-memory jar, without WorkspaceId or a CSRF header. Expect only id/title/description/nodes/edges, node position:{x,y}/childMapId and edge endpoint IDs; never owner/workspace IDs. Own maps return 200; unknown/foreign maps return identical 404; anonymous sessions return 401; operational failures return sanitized 500. All private responses are no-store. Never print cookies, tokens, credentials or real private map content. Follow links only through the same authenticated endpoint. GET may lazily initialize an empty workspace as previously approved; it never seeds maps.

The unchanged frontend still loads `/api/knowledge-maps/software` and `/csharp`. Both anonymous demo regressions remain; they do not grant public access to persisted maps. Phase 3.5 browser evidence is historical and does not constitute manual browser validation of the new backend-only endpoint.

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

Creates return 201 with projections, map Location points to its read; updates/deletes return empty 204. Target missing/foreign resources return indistinguishable 404, relation failures 400 invalid_child_map/invalid_edge_nodes, input 400 validation_failed, native CSRF 400 csrf_invalid, and referenced map/node deletion 409 map_in_use/node_in_use. Unknown provider failures remain sanitized 500; every matched private endpoint is no-store. Remove edges before deleting a referenced node; clear incoming ChildMap links before deleting that map. No implicit edge removal, full JSON Patch, authoring limiter, frontend UI or list API exists.

Run tool/solution restore, build/test, frontend build/lint and the safe in-memory pending-model command above. New unit/HTTP/persistence tests cover mutations, timestamps, omitted/set/clear PATCH, each endpoint's anonymous/CSRF behavior, IDOR, same-map/same-workspace references, deletes, real constraint failures after relation removal and negative operational-fault classification. The existing reader/session/demo tests remain. Stored same-column updates are last committed write wins without tokens; the interleaved-context regression is not a broad concurrency/performance guarantee. The authoring HTTP tests characterize the native 32 KiB configuration on all eight MVC actions and fail if the limit is removed or changed. They do not send a payload above 32 KiB or prove oversized-body rejection/status/sanitization: the current TestServer harness does not supply Kestrel's native body-size enforcement. Real-server enforcement remains a separate verification. Do not log private content or credentials.

## Phase 4.3A - Persisted Map Discovery

Implemented and in review; 4.1 and 4.2 are complete, approved and published. Use the same explicitly migrated disposable SQLite and trusted HTTPS login procedure. With the in-memory authenticated cookie jar, GET /api/maps without a CSRF header. Expect 200 and { "maps": [{ "id": "...", "title": "..." }] }, or { "maps": [] } when the current workspace has no maps. Discovery may resolve/create the previously approved empty personal workspace, but never creates or selects a map.

The query returns only current-workspace summaries, ordered by database Title ascending then stored Id ascending. SQLite currently uses default BINARY title collation, including case-sensitive ordering; this is not a locale/user-defined sort. Equal titles use ID as the deterministic tie-breaker. There is no pagination/search/filter/sort input. No result is a default/root map and callers must not assume the first item is selected.

Future frontend flow: login → workspace → GET /api/maps → user selects a map → GET /api/maps/{id}. The React frontend still uses demos and does not consume discovery in 4.3A. Phase 4.3 remains blocked until discovery is approved and published; 4.4 Authoring UX is future.

All responses are no-store. Anonymous/invalid/removed-account sessions get sanitized 401; a missing account triggers existing local sign-out. Account/reader/provider failures and native stamp failures get sanitized 500, never a misleading empty list. WorkspaceId/OwnerUserId query parameters cannot override server ownership and are not returned. An empty list describes only this user's workspace. GET needs no CSRF token; create+discover uses the existing CSRF-protected POST /api/maps followed by this read.

Run tool restore, solution restore/build/tests, safe in-memory EF pending-model verification and unchanged frontend build/lint. Discovery unit tests cover current account, lazy workspace, zero/one/many summaries, reader-order/cancellation forwarding and failures. Real SQLite/HTTP tests cover create+discover, exact envelope/summary shape, two-user isolation with query overrides, an empty third user, title/ID ordering, no-store, untracked projection without graph-table reads, removed accounts, account/reader/native-stamp failures, Development OpenAPI and demo/authoring regressions. Tests use temporary migrated databases with cleanup, never Development data. No schema/migration/dependency/frontend change exists; unpaginated collection size and future provider collation behavior need review as real usage grows.
