# Coding agent operating contract

## Authority and preparation

- Project documentation is authoritative for product scope and approved architecture. Read README.md and relevant documents in docs/ before implementation.
- Read and follow this contract before changing the project.
- Treat ROADMAP.md as planned work, not permission to implement every phase.
- Distinguish accepted decisions from open questions. Do not invent requirements, a final relationship taxonomy, or architectural decisions.
- When a task conflicts with documented architecture without explicit authorization, stop the affected work, explain the conflict, and request approval before changing architecture. When the current task itself explicitly approves the architectural change, that approval is sufficient; do not request it again. Agents must not change architecture autonomously. After an explicitly approved architectural change, update the relevant project documentation as part of the same logical change unless the task explicitly says otherwise.
- Stop dependent implementation when information required for safe behavior is missing. State what is missing; do not guess on critical decisions. Independent authorized work may continue.
- Prefer fail-closed behavior for critical ambiguous decisions, especially authentication, authorization, ownership, and destructive data operations.

## Change discipline

- Make the smallest change necessary to satisfy the approved task. Keep work small and reviewable.
- Do not change architecture without explicit approval.
- Do not introduce frameworks or dependencies without justification. Explain their purpose and implications, and respect the task's installation and architecture constraints.
- Keep business logic outside controllers and dependencies intentional.
- Do not make unrelated cleanup changes or overwrite unrelated user work.
- Preserve backwards compatibility unless a breaking change is explicitly approved. When relevant, consider API consumers, persisted data, configuration, and migrations.
- Follow SECURITY.md. Never expose secrets in files, source control, logs, commands, or reports.
- Keep database migrations and provider changes explicit and controlled.
- Respect the initial architecture exclusions. Do not add microservices, Redis, RabbitMQ, Kafka, Kubernetes, Elasticsearch, graph databases, or unjustified background workers.
- Do not add AI, marketplace, social networking, or other excluded product features under an unrelated task.

## Current foundation boundary

Until a later implementation task is explicitly authorized, maintain the documentation foundation only:

```text
AGENTS.md
README.md
docs/
  PRODUCT.md
  ARCHITECTURE.md
  ROADMAP.md
  DECISIONS.md
  SECURITY.md
  DEVELOPMENT.md
```

Do not generate application code, create the .NET solution or React application, install packages, create Docker infrastructure, configure databases, create migrations, or create CI during foundation.

## Verification and reporting

- Follow the diagnose, scope, implement, build, test, inspect, and report workflow in DEVELOPMENT.md.
- Run appropriate builds and tests when application code exists. For documentation-only work, perform appropriate document checks and explicitly report application builds and tests as not applicable.
- If already in a Git repository, run `git diff --check`, inspect `git status`, and review `git diff` as applicable. Directly inspect untracked files because normal Git diff does not include them.
- If the folder is not a Git repository, explicitly report it. Do not initialize Git without instruction, and skip Git status and diff commands.
- Always report every changed file.
- Always report commands executed and their outcomes.
- Always report tests and other verification executed, including failures and checks not run.
- Always report warnings and errors, including tool failures and material verification limitations. Do not claim an unperformed check passed.

## Git authorization and review

- Do not commit unless explicitly instructed. Permission to create or modify files is not commit permission.
- Do not push unless explicitly instructed. Commit permission is not push permission.
- Do not initialize Git or create a remote repository unless explicitly instructed.
- After the current foundation task, show the file tree, summarize each document, report changed files and verification, and stop for review.
