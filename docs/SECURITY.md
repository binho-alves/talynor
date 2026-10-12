# Secure-development principles

Talynor has an approved frontend spike and backend skeleton, and the completed/published VS-001 exposes only fixed demonstration maps through an anonymous read-only endpoint. Phase 3.1 implements role-free Identity persistence with EF Core/SQLite and explicit migrations, pending review. No HTTP authentication, session, authorization, workspace, or ownership is implemented; no accounts are seeded. Persistence does not protect HTTP endpoints. The demonstration endpoint does not establish an access policy for future private knowledge data. These principles do not claim that full security controls have already been implemented.

## Secrets and configuration

- Never put passwords, tokens, connection credentials, private keys, or other secrets in source control.
- Keep secrets outside committed files and use an appropriate environment-specific delivery mechanism when implementation is scoped.
- Separate development, test, and production configuration. Do not silently fall back to development credentials or insecure production defaults.
- Use placeholders in documentation and examples; do not copy real secrets into command output or reports.

## Authentication and authorization

- Local ASP.NET Core Identity and future browser cookies are approved under [ADR-008](DECISIONS.md#adr-008---incremental-local-identity-and-persistence). Phase 3.1 stores only the account model; login, cookies, CSRF defenses, and authorization are not configured. Design and validate these controls in their explicitly scoped slices before handling private HTTP data.
- Review identity design when account workflows are scoped, including relevant credential, session, and account recovery behavior.
- Make authorization explicit for every protected operation, including workspace, node, content, study activity, and assessment access.
- Validate ownership and permissions on the backend. Client-side interface visibility is not sufficient authorization.
- Prefer fail-closed behavior when critical access decisions are ambiguous.

## Input and content validation

- Validate external input at application boundaries and enforce domain invariants on the backend.
- Define accepted values, sizes, and formats when the corresponding feature is designed.
- Treat links, notes, uploaded files, and other user content as untrusted. Review safe rendering and file handling before enabling the relevant content types.

## Dependency hygiene

- Add frameworks and dependencies only with a documented need and review of their maintenance and security implications.
- Keep the dependency footprint proportionate to the feature.
- Review supported versions and vulnerabilities as part of ongoing maintenance and production readiness.

## Secure logging

- Use structured logging with deliberate choices about recorded fields.
- Do not log secrets, authentication tokens, sensitive personal information, private learning content, or unrestricted request bodies.
- Ensure diagnostics and error responses do not expose sensitive configuration or internal details.

## Database and migrations

- The configurable SQLite file and its journal/WAL/SHM files are local sensitive data, never source artifacts. The Development default uses `talynor.local.sqlite` under the API content root, covered by the existing ignore rules. No credential is included in this connection string.
- Apply the Identity migration explicitly; build and application startup must not create/migrate the database or seed accounts. Persistence tests use disposable SQLite databases and remove their files. Do not log user secrets or sensitive data when later account workflows are introduced.
- Review migrations and their data implications before execution.
- Apply migrations only to the intended environment with explicit, controlled procedures.
- Make database provider changes explicit and validate the resulting behavior.
- Define backup and recovery procedures before production data is placed at risk; do not assume a destructive change is safe.

## Least privilege

- Give application identities, database access, and operational tools only the permissions they need.
- Keep development access and production access distinct.
- Avoid unnecessary public exposure of services or stored content.

Concrete controls, operational procedures, and security tooling must be designed in their relevant phases. This document does not authorize infrastructure setup during foundation.
