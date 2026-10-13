# Development workflow

## Project context

- Project: Talynor.
- Local development convention: `C:\dev\Talynor`.
- Intended GitHub owner: `binho-alves`.
- Current stage: Phases 1 and 2, VS-001, and Phase 3.1 completed and approved; VS-001 and 3.1 published. Phase 3.2 started with 3.2A — Session HTTP Foundation and CSRF Bootstrap, implemented and pending review. Account/session endpoints and workspace workflows have not started.

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

```powershell
$env:TALYNOR_API_PROXY_TARGET = 'https://localhost:7117'
pnpm dev
```

The example target matches the current HTTPS launch profile; update local configuration if its port changes. Vite proxies `/api` to this target, while React uses relative URLs. Build/lint do not require the target. The proxy is not a production hosting or `vite preview` configuration. No backend CORS is needed for this development setup.

TLS verification is enabled by default. If Node reports a certificate trust error for the local development certificate, diagnose it and establish correct certificate trust. In the validated Node 24 Windows environment, `$env:NODE_OPTIONS = '--use-system-ca'` let the proxy trust the existing development certificate without disabling verification. Set it before starting Vite when needed.

Historical VS-001 context only: `$env:TALYNOR_API_PROXY_INSECURE = 'true'` was a permitted local fallback for the anonymous demonstration experiment; configuration rejects it for non-loopback or non-HTTPS targets. It disables certificate verification on the Vite-to-API proxy connection and is not authorized for authenticated flows. Do not use it to bypass TLS problems in Phase 3.2 or future session integrations. Establish correct certificate trust instead; Secure cookies and TLS validation must not be weakened. Never store secrets in these variables or tracked files.

VS-001 frontend verification uses `pnpm build`, `pnpm lint`, and browser checks for navigation, request errors, retry, cancellation, and responsive layout. pnpm remains the available tool, not the official package manager decision.

## Phase 3.1 - Identity persistence

Identity/EF Core/SQLite packages and the local `dotnet-ef` tool use version 10.0.12, aligned with the existing Microsoft packages and installed runtime. Restore the versioned `.config/dotnet-tools.json` from the repository root:

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

The backend registers Identity services/stores, only the application-cookie scheme, authorization without a fallback policy, and native antiforgery. Only anonymous `GET /api/auth/csrf` is available under `/api/auth`. Register/login/logout/me, current user, account policy, rate limiting, and frontend consumption are not implemented. Application startup and CSRF bootstrap still do not initialize/migrate the database.

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

The response is `{ "token": "..." }` with no-store and an HttpOnly/Secure/SameSite Strict antiforgery cookie. This does not create an authenticated session. Future account requests will use `X-CSRF-TOKEN`; the future frontend will retain the request token in memory and fetch a new one after authentication changes. Do not assume that fetching a new token makes all prior tokens one-time or immediately revoked.

Data Protection uses host defaults in application code; no key ring, key database, or custom certificate is configured. Key persistence and protection depend on the host profile, and deployment will need an explicit review. New integration tests use an ephemeral provider only for isolation. They verify bootstrap/token-pair validity, cookie/scheme/401/403 configuration, and the unchanged runtime EF model. Existing tests verify anonymous demo/health/OpenAPI behavior and startup/bootstrap without database creation. No artificial POST or unit test is added; account-POST rejection belongs to 3.2B. WebApplicationFactory's HTTPS address does not validate a real certificate or browser cookie enforcement.

After building, check the unchanged design-time model from the repository root without creating a file:

```powershell
$env:ConnectionStrings__Talynor = 'Data Source=:memory:'
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/backend/Talynor.Infrastructure --no-build
Remove-Item Env:ConnectionStrings__Talynor -ErrorAction SilentlyContinue
```

The current Vite proxy and React remain unchanged. `NODE_OPTIONS=--use-system-ca` helps Node trust the HTTPS API; it does not enable HTTPS on the frontend dev server. Real browser authentication integration is reserved for 3.4. Do not weaken Secure/TLS settings to accommodate it.

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
