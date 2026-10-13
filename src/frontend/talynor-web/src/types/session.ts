export type CsrfResponse = { token: string }
export type CurrentUser = { id: string; email: string }
export type PersonalWorkspace = { id: string; createdAtUtc: string }

export type ResolvedSession =
  | { status: 'anonymous'; message?: string }
  | { status: 'authenticated'; user: CurrentUser; workspace: PersonalWorkspace }

export type SessionState = ResolvedSession
  | { status: 'bootstrapping' }
  | { status: 'error' }
