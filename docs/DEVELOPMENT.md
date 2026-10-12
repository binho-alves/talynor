# Development workflow

## Project context

- Project: Talynor.
- Local development convention: `C:\dev\Talynor`.
- Intended GitHub owner: `binho-alves`.
- Current stage: Phases 1 and 2 and the published VS-001 completed and approved. Phase 3 — Identity and Workspace started; current slice 3.1 — Persistence Foundation implemented, pending review. HTTP identity/session and workspace workflows have not started.

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

TLS verification is enabled by default. If Node reports a certificate trust error for the local development certificate, diagnose it first. In the validated Node 24 Windows environment, `$env:NODE_OPTIONS = '--use-system-ca'` let the proxy trust the existing development certificate without disabling verification. Set it before starting Vite when needed. If trust still cannot be established, the explicitly permitted local-only fallback is `$env:TALYNOR_API_PROXY_INSECURE = 'true'`; configuration rejects this exception for non-loopback or non-HTTPS targets. It affects only the Vite development proxy, not application/production TLS. Never store secrets in these variables or tracked files.

VS-001 frontend verification uses `pnpm build`, `pnpm lint`, and browser checks for navigation, request errors, retry, cancellation, and responsive layout. pnpm remains the available tool, not the official package manager decision.

## Phase 3.1 - Identity persistence

Identity/EF Core/SQLite packages and the local `dotnet-ef` tool use version 10.0.12, aligned with the existing Microsoft packages and installed runtime. Restore the versioned `.config/dotnet-tools.json` from the repository root:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef --version
```

The tool is local; a globally installed `dotnet-ef` is not the project prerequisite. Infrastructure owns the role-free `TalynorDbContext`, design-time factory, and migrations. No Identity HTTP endpoints, account/session services, workspace, or knowledge persistence are configured.

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
