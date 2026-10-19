import { ApiError } from '../api/http.ts'
import { studyError, type StudySessionsClient } from '../api/studySessionsApi.ts'
import type { StudySession } from '../types/studySession.ts'

export type StudyHistoryState = {
  status: 'loading' | 'ready' | 'error'; sessions: StudySession[]; page: number
  hasMore: boolean; loadingMore: boolean; message: string
}
// Specific paginated node history. Disposing a node visit rejects its late responses.
export function createStudyHistoryState(client: StudySessionsClient, mapId: string, nodeId: string, revalidate: () => Promise<void>) {
  let state: StudyHistoryState = { status: 'loading', sessions: [], page: 0, hasMore: false, loadingMore: false, message: '' }
  let current: AbortController | null = null, mounted = false
  const listeners = new Set<() => void>()
  function update(patch: Partial<StudyHistoryState>) { state = { ...state, ...patch }; listeners.forEach(listener => listener()) }
  async function load(page: number): Promise<void> {
    if (!mounted) return
    current?.abort()
    const controller = new AbortController(); current = controller
    const valid = () => mounted && current === controller && !controller.signal.aborted
    update(page === 1 ? { status: 'loading', sessions: [], page: 0, hasMore: false, message: '', loadingMore: false }
      : { loadingMore: true, message: '' })
    try {
      const result = await client.history(mapId, nodeId, page, controller.signal)
      if (!valid()) return
      const existing = page === 1 ? [] : state.sessions
      const ids = new Set(existing.map(session => session.id))
      update({ status: 'ready', sessions: [...existing, ...result.sessions.filter(session => !ids.has(session.id))],
        page: result.page, hasMore: result.hasMore, loadingMore: false })
    } catch (error) {
      if (!valid()) return
      if (error instanceof ApiError && error.status === 401) await revalidate()
      if (valid()) update({ status: page === 1 ? 'error' : 'ready', message: studyError(error), loadingMore: false })
    } finally { if (current === controller) current = null }
  }
  return {
    getSnapshot: () => state,
    subscribe: (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener) } },
    mount() { mounted = true },
    dispose() { mounted = false; current?.abort(); current = null },
    reload: () => load(1),
    more: () => !current && state.hasMore ? load(state.page + 1) : Promise.resolve(),
  }
}
