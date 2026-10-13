import { isRecord, isText, readJson, request } from './http.ts'
import type { PersonalWorkspace } from '../types/session'

export async function getPersonalWorkspace(signal: AbortSignal): Promise<PersonalWorkspace> {
  return readJson(await request('/api/workspace', signal), signal, (value): value is PersonalWorkspace => {
    if (!isRecord(value) || !isText(value.id) || !isText(value.createdAtUtc)) return false
    return /^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i.test(value.id)
      && value.id !== '00000000-0000-0000-0000-000000000000'
      && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|\+00:00)$/.test(value.createdAtUtc)
      && Number.isFinite(Date.parse(value.createdAtUtc))
      && new Date(value.createdAtUtc).toISOString().slice(0, 10) === value.createdAtUtc.slice(0, 10)
  })
}
