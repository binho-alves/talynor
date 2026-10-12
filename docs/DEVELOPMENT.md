# Development workflow

## Project context

- Project: Talynor.
- Local development convention: `C:\dev\Talynor`.
- Intended GitHub owner: `binho-alves`.
- Current stage: Phase 1 — SPIKE-001, an initial frontend spike with local mocks.

The owner name and local path do not authorize creating a remote repository, initializing Git, committing, or pushing.

## Working scope

Keep tasks small and reviewable. Read [AGENTS.md](../AGENTS.md) and the relevant project documents before implementation. Resolve scope and architectural conflicts before making dependent changes.

The original foundation task created only README.md, AGENTS.md, and the six documents in docs/. It did not authorize application code, a .NET solution, a React application, package installation, Docker infrastructure, database configuration, migrations, or CI. SPIKE-001 separately authorizes the initial frontend and its necessary dependencies; see [the spike README](../src/frontend/talynor-web/README.md).

Later roadmap phases describe intended work, not standing implementation authorization.

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
