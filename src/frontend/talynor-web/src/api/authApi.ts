import { isRecord, isText, readJson, request, requestNoContent } from './http.ts'
import type { CsrfResponse, CurrentUser } from '../types/session'

export async function getCsrf(signal: AbortSignal): Promise<CsrfResponse> {
  return readJson(await request('/api/auth/csrf', signal), signal,
    (value): value is CsrfResponse => isRecord(value) && isText(value.token))
}

export async function getCurrentUser(signal: AbortSignal): Promise<CurrentUser> {
  return readJson(await request('/api/auth/me', signal), signal,
    (value): value is CurrentUser => isRecord(value) && isText(value.id) && isText(value.email))
}

export async function postLogin(email: string, password: string, token: string, signal: AbortSignal): Promise<void> {
  return requestNoContent('/api/auth/login', signal, {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
    body: JSON.stringify({ email, password }),
  })
}

export async function postLogout(token: string, signal: AbortSignal): Promise<void> {
  return requestNoContent('/api/auth/logout', signal, { method: 'POST', headers: { 'X-CSRF-TOKEN': token } })
}
