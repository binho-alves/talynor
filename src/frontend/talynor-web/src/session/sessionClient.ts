import { getCsrf, getCurrentUser, postLogin, postLogout } from '../api/authApi.ts'
import { ApiError } from '../api/http.ts'
import { getPersonalWorkspace } from '../api/workspaceApi.ts'
import { createKnowledgeMapAuthoringClient } from '../api/knowledgeMapAuthoringApi.ts'
import type { CurrentUser, ResolvedSession } from '../types/session'

export class SessionBootstrapError extends Error {
  constructor() { super('Não foi possível carregar sua sessão.') }
}

export type WithCsrf = <T>(action: (token: string, signal: AbortSignal) => Promise<T>, signal: AbortSignal) => Promise<T>

// One instance per hook, never a module-global session. CSRF stays private and only in memory.
export function createSessionClient() {
  let csrfToken: string | undefined
  function clearCsrf() { csrfToken = undefined }

  async function renewCsrf(signal: AbortSignal): Promise<string> {
    clearCsrf()
    const response = await getCsrf(signal)
    signal.throwIfAborted()
    csrfToken = response.token
    return csrfToken
  }

  async function resolveWorkspace(user: CurrentUser, signal: AbortSignal): Promise<ResolvedSession> {
    try {
      const workspace = await getPersonalWorkspace(signal)
      // Copy only contract fields; never retain extra JSON fields in session state.
      return { status: 'authenticated', user: { id: user.id, email: user.email },
        workspace: { id: workspace.id, createdAtUtc: workspace.createdAtUtc } }
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        clearCsrf()
        return { status: 'anonymous', message: 'Sua sessão expirou. Entre novamente.' }
      }
      throw error
    }
  }

  async function bootstrap(signal: AbortSignal): Promise<ResolvedSession> {
    clearCsrf()
    let user: CurrentUser
    try { user = await getCurrentUser(signal) } catch (error) {
      if (error instanceof ApiError && error.status === 401) return { status: 'anonymous' }
      throw error
    }
    return resolveWorkspace(user, signal)
  }

  async function login(email: string, password: string, signal: AbortSignal): Promise<ResolvedSession> {
    // Fetch immediately before each unsafe action to also handle identity changes in another tab.
    const token = await renewCsrf(signal)
    try { await postLogin(email, password, token, signal) } catch (error) {
      if (error instanceof ApiError && error.code === 'csrf_invalid') {
        await renewCsrf(signal) // One renewal; never automatically resend the password.
      } else if (error instanceof ApiError && error.code === 'already_authenticated') {
        // Confirm identity first; do not switch accounts or automatically sign out.
        try {
          const user = await getCurrentUser(signal)
          await renewCsrf(signal)
          return await resolveWorkspace(user, signal)
        } catch {
          signal.throwIfAborted()
          throw new SessionBootstrapError()
        }
      }
      throw error
    }
    clearCsrf()
    try {
      await renewCsrf(signal)
      return await bootstrapAfterLogin(signal)
    } catch {
      signal.throwIfAborted()
      throw new SessionBootstrapError()
    }
  }

  async function bootstrapAfterLogin(signal: AbortSignal): Promise<ResolvedSession> {
    try { return await resolveWorkspace(await getCurrentUser(signal), signal) } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        clearCsrf()
        return { status: 'anonymous', message: 'Sua sessão expirou. Entre novamente.' }
      }
      throw error
    }
  }

  async function sendLogout(token: string, signal: AbortSignal): Promise<void> {
    try {
      await postLogout(token, signal)
    } catch (error) {
      // Only the logout POST can confirm that the session has already ended.
      if (!(error instanceof ApiError) || error.status !== 401) throw error
    }
  }

  async function logout(signal: AbortSignal): Promise<ResolvedSession> {
    const token = await renewCsrf(signal)
    try { await sendLogout(token, signal) } catch (error) {
      if (!(error instanceof ApiError) || error.code !== 'csrf_invalid') throw error
      const renewedToken = await renewCsrf(signal)
      await sendLogout(renewedToken, signal) // At most one safe logout retry.
    }
    clearCsrf()
    // New anonymous CSRF is fetched on the next login attempt, not reused from this identity.
    return { status: 'anonymous' }
  }

  // Authoring uses the same private CSRF lifecycle. Never replay a mutation, even after a CSRF failure.
  const withCsrf: WithCsrf = async (action, signal) => {
    const token = await renewCsrf(signal)
    try { return await action(token, signal) } catch (error) {
      if (error instanceof ApiError && error.code === 'csrf_invalid') clearCsrf()
      throw error
    }
  }

  const authoring = createKnowledgeMapAuthoringClient(withCsrf)
  return { bootstrap, login, logout, clearCsrf, authoring }
}

export function loginErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'invalid_credentials': return 'Email ou senha inválidos.'
      case 'validation_failed': return 'Confira o email e a senha informados.'
      case 'csrf_invalid': return 'A proteção da sessão foi renovada. Tente entrar novamente.'
      case 'rate_limited': return 'Muitas tentativas. Aguarde um pouco e tente novamente.'
    }
    return error.message
  }
  return 'Não foi possível entrar. Tente novamente.'
}
