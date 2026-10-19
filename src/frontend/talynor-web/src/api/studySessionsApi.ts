import { ApiError, isRecord, isText, readJson, request, requestNoContent } from './http.ts'
import { isMapId } from './knowledgeMapsApi.ts'
import type { WithCsrf } from '../session/sessionClient.ts'
import type { ActiveStudySessionEnvelope, StudySession, StudySessionHistory } from '../types/studySession.ts'

function timestamp(value: unknown): value is string {
  return typeof value === 'string'
    && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value)
    && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 19) === value.slice(0, 19)
}
export function isStudySession(value: unknown): value is StudySession {
  if (!isRecord(value) || !isMapId(value.id) || !isMapId(value.knowledgeNodeId) || !timestamp(value.startedAtUtc)) return false
  if (value.endedAtUtc === null) return value.durationSeconds === null
  return timestamp(value.endedAtUtc) && Date.parse(value.endedAtUtc) >= Date.parse(value.startedAtUtc)
    && typeof value.durationSeconds === 'number' && Number.isFinite(value.durationSeconds) && value.durationSeconds >= 0
    // Date.parse has millisecond precision; the API can return fractional .NET ticks.
    && Math.abs(value.durationSeconds - (Date.parse(value.endedAtUtc) - Date.parse(value.startedAtUtc)) / 1000) <= 0.001
}
export function isActiveStudySession(value: unknown): value is ActiveStudySessionEnvelope {
  if (!isRecord(value) || !timestamp(value.serverTimeUtc)) return false
  const active = value.active
  return active === null || isRecord(active) && isStudySession(active.session) && active.session.endedAtUtc === null
    && isMapId(active.mapId) && isMapId(active.nodeId) && active.nodeId.toLowerCase() === active.session.knowledgeNodeId.toLowerCase()
    && isText(active.nodeTitle)
}
export function isStudyHistory(value: unknown): value is StudySessionHistory {
  if (!isRecord(value) || !Number.isSafeInteger(value.page) || (value.page as number) < 1
    || !Number.isSafeInteger(value.pageSize) || (value.pageSize as number) < 1 || (value.pageSize as number) > 100
    || typeof value.hasMore !== 'boolean' || !Array.isArray(value.sessions) || value.sessions.length > (value.pageSize as number)) return false
  const ids = new Set<string>()
  return value.sessions.every((item) => {
    if (!isStudySession(item) || item.endedAtUtc === null || ids.has(item.id.toLowerCase())) return false
    ids.add(item.id.toLowerCase()); return true
  })
}
function copySession(value: StudySession): StudySession {
  return { id: value.id.toLowerCase(), knowledgeNodeId: value.knowledgeNodeId.toLowerCase(),
    startedAtUtc: value.startedAtUtc, endedAtUtc: value.endedAtUtc, durationSeconds: value.durationSeconds }
}
export function studySessionPath(mapId: string, nodeId: string, id?: string): string {
  if (!isMapId(mapId) || !isMapId(nodeId) || id !== undefined && !isMapId(id)) throw new ApiError('invalid-response')
  return `/api/maps/${mapId}/nodes/${nodeId}/study-sessions${id === undefined ? '' : `/${id}`}`
}
export function createStudySessionsClient(withCsrf: WithCsrf) {
  return {
    async active(signal: AbortSignal): Promise<ActiveStudySessionEnvelope> {
      const value = await readJson(await request('/api/study-sessions/active', signal), signal, isActiveStudySession)
      const active = value.active
      return { serverTimeUtc: value.serverTimeUtc, active: active === null ? null : {
        session: copySession(active.session), mapId: active.mapId.toLowerCase(), nodeId: active.nodeId.toLowerCase(), nodeTitle: active.nodeTitle } }
    },
    async history(map: string, node: string, page: number, signal: AbortSignal): Promise<StudySessionHistory> {
      if (!Number.isSafeInteger(page) || page < 1) throw new ApiError('invalid-response')
      const value = await readJson(await request(`${studySessionPath(map, node)}?page=${page}&pageSize=20`, signal), signal, isStudyHistory)
      if (value.page !== page || value.pageSize !== 20
        || value.sessions.some(s => s.knowledgeNodeId.toLowerCase() !== node.toLowerCase())) throw new ApiError('invalid-response')
      return { sessions: value.sessions.map(copySession), page: value.page, pageSize: value.pageSize, hasMore: value.hasMore }
    },
    async detail(map: string, node: string, id: string, signal: AbortSignal): Promise<StudySession> {
      const value = await readJson(await request(studySessionPath(map, node, id), signal), signal, isStudySession)
      if (value.id.toLowerCase() !== id.toLowerCase() || value.knowledgeNodeId.toLowerCase() !== node.toLowerCase()) throw new ApiError('invalid-response')
      return copySession(value)
    },
    start: (map: string, node: string, signal: AbortSignal): Promise<StudySession> => withCsrf(async (token, signal) => {
      const response = await request(studySessionPath(map, node), signal, { method: 'POST', headers: { 'X-CSRF-TOKEN': token } })
      if (response.status !== 201) throw new ApiError('invalid-response', response.status)
      let value: unknown
      try { value = await response.json() } catch { signal.throwIfAborted(); throw new ApiError('invalid-response') }
      signal.throwIfAborted()
      if (!isStudySession(value) || value.endedAtUtc !== null || value.knowledgeNodeId.toLowerCase() !== node.toLowerCase()) throw new ApiError('invalid-response')
      return copySession(value)
    }, signal),
    complete: (map: string, node: string, id: string, signal: AbortSignal) => withCsrf((token, signal) =>
      requestNoContent(`${studySessionPath(map, node, id)}/complete`, signal, { method: 'POST', headers: { 'X-CSRF-TOKEN': token } }), signal),
    remove: (map: string, node: string, id: string, signal: AbortSignal) => withCsrf((token, signal) =>
      requestNoContent(studySessionPath(map, node, id), signal, { method: 'DELETE', headers: { 'X-CSRF-TOKEN': token } }), signal),
  }
}
export type StudySessionsClient = ReturnType<typeof createStudySessionsClient>

export function studyError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401) return 'A sessão foi reavaliada. Entre novamente ou atualize os estudos.'
    if (error.code === 'csrf_invalid') return 'A proteção da sessão precisa ser renovada. Tente novamente por uma ação explícita.'
    if (error.code === 'active_session_exists') return 'Já existe uma sessão de estudo ativa. Confira o indicador de estudo.'
    if (error.code === 'study_session_clock_conflict') return 'O relógio do servidor está anterior ao início. Tente concluir mais tarde.'
    if (error.status === 404) return 'Sessão de estudo ou conceito indisponível.'
    if (error.code === 'validation_failed') return 'Não foi possível validar a operação de estudo.'
    return error.message
  }
  return 'Não foi possível confirmar a operação. Atualize os estudos antes de tentar novamente.'
}
