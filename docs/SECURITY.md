# Secure-development principles

Talynor has an approved frontend spike and backend skeleton, and the completed/published VS-001 exposes only fixed demonstration maps through an anonymous read-only endpoint. Phase 3.1 role-free Identity persistence is completed and published. Phase 3.2A cookie/authorization infrastructure and native antiforgery bootstrap are implemented, pending review. Register, login, logout, me, workspace, and ownership are not implemented; no accounts are seeded or passwords accepted over HTTP. Infrastructure alone does not protect future private knowledge data. These principles do not claim that full account/session security has been delivered.

## Secrets and configuration

- Never put passwords, tokens, connection credentials, private keys, or other secrets in source control.
- Keep secrets outside committed files and use an appropriate environment-specific delivery mechanism when implementation is scoped.
- Separate development, test, and production configuration. Do not silently fall back to development credentials or insecure production defaults.
- Use placeholders in documentation and examples; do not copy real secrets into command output or reports.

## Authentication and authorization

- Local Identity and browser cookies are approved under [ADR-008](DECISIONS.md#adr-008---incremental-local-identity-and-persistence). 3.2A configures only `Identity.Application`: `__Host-Talynor.Auth`, HttpOnly, Secure Always, SameSite Lax, Path `/`, no Domain, eight-hour ticket validity without sliding expiration. No login issues this cookie yet; 3.2B must use non-persistent sign-in. SecurityStampValidator remains wired, and challenge/forbid return 401/403 without redirects.
- Native antiforgery uses header `X-CSRF-TOKEN` and `__Host-Talynor.Csrf` (HttpOnly, Secure Always, SameSite Strict, Path `/`, no Domain). Anonymous `GET /api/auth/csrf` returns only a request token with no-store. The cookie token is not exposed in JSON. The MVC controller filter skips GET and prepares future unsafe actions; negative account-POST tests and refresh after login/logout belong to 3.2B. A CSRF token does not authenticate a user.
- There is no global fallback authorization policy. Health, demonstration maps, and CSRF bootstrap remain anonymous; OpenAPI remains Development-only. No CORS, rate limiting, JWT, account-specific password/lockout configuration, or current-user implementation is introduced by 3.2A.
- Review identity design when account workflows are scoped, including relevant credential, session, and account recovery behavior.
- Make authorization explicit for every protected operation, including workspace, node, content, study activity, and assessment access.
- Validate ownership and permissions on the backend. Client-side interface visibility is not sufficient authorization.
- Prefer fail-closed behavior when critical access decisions are ambiguous.

## Local Data Protection and HTTPS

Use ASP.NET Core Data Protection defaults for the current local single-instance host, without a custom key ring. On Windows with an available user profile, keys normally persist under `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys` and are protected with DPAPI. This is host-dependent, not a deploy guarantee. Keep keys outside Git and logs; losing them can invalidate cookies/tokens. Future deployment must review persistent key storage, protection, and application isolation before relying on sessions.

Secure cookies remain required even in Development. Use the existing HTTPS API profile and trusted development certificate; do not weaken cookie attributes or TLS validation. A Vite HTTPS proxy target does not make the browser-to-Vite connection HTTPS. Real browser/session integration requires later validation; in-process HTTPS-address tests do not verify actual TLS or browser SameSite enforcement. 3.2A does not install/trust certificates or modify the frontend.

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
