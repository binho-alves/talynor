# Development workflow

## Project context

- Project: Talynor.
- Local development convention: `C:\dev\Talynor`.
- Intended GitHub owner: `binho-alves`.
- Current stage: Phases 1 and 2 completed and approved. VS-001 — Read-only Knowledge Graph Vertical Slice implemented, pending review between Phase 2 and Phase 3. Phase 3 remains Identity and Workspace and has not started.

The owner name and local path do not authorize creating a remote repository, initializing Git, committing, or pushing.

## Working scope

Keep tasks small and reviewable. Read [AGENTS.md](../AGENTS.md) and the relevant project documents before implementation. Resolve scope and architectural conflicts before making dependent changes.

The original foundation task created only README.md, AGENTS.md, and the six documents in docs/. It did not authorize application code, a .NET solution, a React application, package installation, Docker infrastructure, database configuration, migrations, or CI. SPIKE-001 separately authorizes the initial frontend and its necessary dependencies; see [the spike README](../src/frontend/talynor-web/README.md).

Later roadmap phases describe intended work, not standing implementation authorization.

The authorized Phase 2 skeleton uses `Talynor.slnx`, SDK selection in `global.json`, and shared nullable/implicit-usings/warnings settings in `Directory.Build.props`. The API provides `/health`, Development-only OpenAPI, and structured JSON console logging. VS-001 adds demonstration-only map reads without persistence or authentication. The React frontend remains separate; the official frontend package manager remains open.

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
