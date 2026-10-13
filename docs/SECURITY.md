# Secure-development principles

Talynor has an approved frontend spike and backend skeleton, and the completed/published VS-001 exposes only fixed demonstration maps through an anonymous read-only endpoint. Phase 3.1, Phase 3.2A, Phase 3.2B.1 account registration, and Phase 3.2B.2A Security Stamp Compatibility are completed, approved, and published. Phase 3.2B.2B Session Flow is implemented and pending review: HTTPS login/logout/me, native cookie/CSRF, lockout, and login rate limiting. Registration remains without auto-login. Workspace, ownership, and frontend authentication are not implemented; no accounts are seeded. These principles do not claim complete session security or production readiness.

## Secrets and configuration

- Never put passwords, tokens, connection credentials, private keys, or other secrets in source control.
- Keep secrets outside committed files and use an appropriate environment-specific delivery mechanism when implementation is scoped.
- Separate development, test, and production configuration. Do not silently fall back to development credentials or insecure production defaults.
- Use placeholders in documentation and examples; do not copy real secrets into command output or reports.

## Authentication and authorization

- Local Identity and browser cookies are approved under [ADR-008](DECISIONS.md#adr-008---incremental-local-identity-and-persistence). `Identity.Application` remains the sole application authentication scheme and default for authenticate/challenge/forbid/sign-in: `__Host-Talynor.Auth`, HttpOnly, Secure Always, SameSite Lax, Path `/`, no Domain, eight-hour ticket validity without sliding expiration. B.2B login uses non-persistent sign-in with no browser Expires/Max-Age. SecurityStampValidator remains wired, and challenge/forbid return sanitized JSON 401/403 with no-store, without redirects.
- 3.2B.2A registers only the additional technical `Identity.TwoFactorRememberMe` handler using native `AddTwoFactorRememberMeCookie()`. This fixes the reproduced missing-handler exception during Identity 10.0.12 stamp rejection. The native validator/callback and 30-minute interval remain unchanged; invalid stamps after the interval now return 401, delete cookies, and reject the original ticket. Within the interval, stamp changes are not yet revalidated. External and TwoFactorUserId remain absent. No Talynor flow issues the auxiliary cookie; deletion headers during rejection are not functional issuance. No MFA is implemented. Its retained native defaults are name `Identity.TwoFactorRememberMe`, HttpOnly, Secure SameAsRequest, SameSite Lax, no Domain, Path derived from request PathBase (`/` on the current host), 14-day ticket lifetime, and sliding expiration. These defaults do not weaken or replace the main cookie policy; functional use would require a separate security review. Tests use real SQLite/cookies with a controlled clock, and diagnostic endpoints exist only in the test factory, returning 404 in the normal API host.
- Native antiforgery uses header `X-CSRF-TOKEN` and `__Host-Talynor.Csrf` (HttpOnly, Secure Always, SameSite Strict, Path `/`, no Domain). Anonymous `GET /api/auth/csrf` returns only a request token with no-store. The cookie token is not exposed in JSON. The MVC controller filter skips GET and protects register/login/logout; missing, invalid, and mismatched-client tokens are rejected before the action. Obtain a new request token after login/logout to match the new identity. Registration does not change identity, so a new token after successful registration is optional. Tokens are not one-time/global revocation mechanisms and do not authenticate a user.
- There is no global fallback authorization policy. Health, demonstration maps, and CSRF bootstrap remain anonymous; OpenAPI remains Development-only. No CORS, rate limiting, JWT, account-specific password/lockout configuration, or current-user implementation is introduced by 3.2A.
- Review identity design when account workflows are scoped, including relevant credential, session, and account recovery behavior.
- Make authorization explicit for every protected operation, including workspace, node, content, study activity, and assessment access.
- Validate ownership and permissions on the backend. Client-side interface visibility is not sufficient authorization.
- Prefer fail-closed behavior when critical access decisions are ambiguous.

## Phase 3.2B.1 registration security

- Anonymous register creates an unconfirmed Identity account without auto-login. An authenticated request is rejected with 409; no implicit account switch, logout, or auth-cookie issuance occurs. Email=username after outer trim, RequireUniqueEmail=true, Identity normalization, and the existing unique NormalizedUserName index protect equivalent/concurrent registrations. No Gmail/provider rules are applied. Email is not proof of ownership or identity; any future ownership uses account ID.
- Email accepts at most 256 UTF-16 units after trim; password accepts at most 128 and is preserved exactly. Identity's minimum is 15 UTF-16 units with RequiredUniqueChars=1, without required uppercase/lowercase/digit/non-alphanumeric composition. Passphrases, spaces within passwords, and Unicode accepted by Identity are supported; Identity rejects whitespace-only passwords. There is no Unicode normalization, blocklist, external compromised-password service, or custom hash. UTF-16 length is not Unicode-code-point counting; this is not a full standards-compliance claim.
- Before public exposure, review compromised/common-password blocking, Unicode handling, hash cost, email verification/recovery, enumeration/timing, and full session/deployment protection. B.2B introduces native login/lockout below. EmailConfirmed stays false; no sender or confirmation endpoint exists.
- Duplicate response is generic 400 registration_failed, without duplicate_email/email_exists/username_taken or raw Identity details. Structural/policy failures use validation_failed; native CSRF failures use csrf_invalid. Unknown validation/provider failures are sanitized 500 unexpected_error, never relabeled as duplicate. Public registration responses are sanitized and do not include submitted email/password/token, provider diagnostics, or exceptions. Explicit logs added by the registration flow must not include submitted email/password/token or raw exceptions/provider diagnostics. Framework exceptions and diagnostics may still be emitted internally according to the logging configuration; sensitive data must never be deliberately logged. This slice does not introduce a global logging or redaction policy/framework. Generic errors still permit residual enumeration through registration success/failure and timing.
- The registration-only native limiter permits five requests per ten-minute fixed window per remote IP, QueueLimit=0. It normalizes mapped IPv4 and shares a restricted missing-IP bucket; forwarded headers are ignored. Middleware runs before MVC/CSRF: even rejected CSRF requests consume quota, and the sixth request can be 429 rate_limited before CSRF validation. NAT/proxies can share quota; counters are in-memory per instance and reset with the process. This is neither distributed protection nor comprehensive DoS defense. Fixed-window boundaries can permit bursts.
- The per-action native request-size limit is 8 KiB where the HTTP server supports the feature. Tests using TestServer do not establish actual Kestrel body-limit/TLS enforcement. All registration responses are no-store; requests/tokens/passwords must not be logged or embedded in shell history. Health, maps, and bootstrap remain anonymous and outside the registration quota.
- Registration remains unchanged by 3.2B.2A: 201 without auto-login, application-cookie issuance, or functional auxiliary-cookie issuance. The separate compatibility regression proves native stamp rejection rather than relying only on scheme-configuration assertions.

## Phase 3.2B.2B session security

- Login requires a native CSRF pair and applies its own native limiter: ten requests/minute per normalized remote IP, QueueLimit=0, shared restricted missing-IP bucket, no X-Forwarded-For trust. Registration's quota is unchanged and independent. An admitted request consumes quota before MVC/CSRF. Rejection is no-store 429 rate_limited with Retry-After when available. Fixed windows permit boundary bursts; counters are per process and reset; NAT/proxies share quota. Distributed/proxy protection is not implemented.
- Native PasswordSignInAsync uses isPersistent=false and lockoutOnFailure=true. New users have lockout enabled, five failures trigger five minutes, and success resets the counter according to Identity. Lockout can be abused to deny a known account access; IP limiting reduces attempts but is not comprehensive DoS protection. Public failures for missing account, wrong password, lockout, and not allowed are identical 401 invalid_credentials without counters/reasons. Compensatory hashing is deferred, so timing remains distinguishable. Registration success/failure also permits residual enumeration.
- Email is trimmed and validated with a 256 UTF-16 maximum; passwords are required/nonempty with a 128-unit maximum, preserved without trim/normalization/truncation or reapplying creation policy. The native 8 KiB login request limit applies where the server supports it. An authenticated login returns 409 without implicit logout/account switching. Errors use the existing endpoint-only sanitizer; operational errors become 500 unexpected_error, never invalid credentials. Added logs contain only generic operation names, without raw exceptions, input, credentials, or cookie/token/stamp values.
- Me/logout require Authorize and return no-store. Me uses a fail-closed unique authenticated NameIdentifier and reads current Identity account data by ID, returning only id/email. Missing account/invalid identifier causes local sign-out/401; provider failure is sanitized 500. Logout requires native CSRF and calls SignOutAsync; it removes the current browser cookie only. Other browsers remain authenticated, and a retained original ticket is not globally revoked by logout. No session table, global logout, or single-session policy is added.
- Login explicitly allows unconfirmed email; EmailConfirmed remains false. Email is not proof of ownership. No verification, reset, email sender, MFA, JWT, or CORS is introduced. The auxiliary TwoFactorRememberMe cookie is never issued by normal login. Native stamp validation still occurs after the 30-minute interval and can refresh the principal/cookie and IssuedUtc while preserving the original ExpiresUtc. With SlidingExpiration=false, the effective ticket deadline remains eight hours from the original login; stamp refresh is not sliding expiration and does not grant a new eight-hour window. Invalid-stamp rejection, expiry, and preservation of the original deadline after valid refresh are covered using real-login cookies and a controlled test clock, while the B.2A regressions remain unchanged.
- Browser/client CSRF tokens must be refreshed after login and logout. Old tokens from another identity cannot be assumed valid; no frontend consumption is implemented yet. In-process tests use real Identity and migrated temporary SQLite, not EF InMemory or a Development database. They do not prove actual TLS/browser SameSite enforcement, Kestrel body limits, multi-instance protection, or production readiness.
- Native exception handling applies only to login/me/logout and also sanitizes failures before controllers, including database outages during native stamp validation. It does not classify operational errors as credentials or customize the validator. Framework diagnostics may still be logged internally under the existing logging configuration; this slice does not introduce a global redaction policy.
- Failed login responses suppress positive application-cookie renewal even when the native validator has revalidated an existing session. The response callback applies only to login, preserves cookie-deletion headers, and does not replace native validation or normal successful-read renewal. No account switch or authentication-cookie issuance is allowed on a 409 login.

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
