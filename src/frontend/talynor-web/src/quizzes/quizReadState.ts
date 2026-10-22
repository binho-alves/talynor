import { ApiError } from '../api/http.ts'
import { quizError, type QuizzesClient } from '../api/quizzesApi.ts'
import type { QuizSummary } from '../types/quiz.ts'
import type { QuizAttemptSummary } from '../types/quizAttempt.ts'

// Shared only by the two paginated quiz reads. Duplicates stop append and request refresh:
// offset pages can overlap after concurrent changes; never silently merge contradictory rows.
function paged<T>(load: (page: number, signal: AbortSignal) => Promise<{ items: T[]; page: number; hasMore: boolean }>,
  id: (value: T) => string, revalidate: () => Promise<void>) {
  let state: { status: 'loading' | 'ready' | 'error'; items: T[]; page: number; hasMore: boolean; loadingMore: boolean; message: string } =
    { status: 'loading', items: [], page: 0, hasMore: false, loadingMore: false, message: '' }
  let mounted = false, current: AbortController | null = null
  const listeners = new Set<() => void>()
  function update(patch: Partial<typeof state>) { state = { ...state, ...patch }; listeners.forEach(fn => fn()) }
  async function read(page: number) {
    if (!mounted) return
    current?.abort(); const controller = new AbortController(); current = controller
    const valid = () => mounted && current === controller && !controller.signal.aborted
    update(page === 1 ? { status: 'loading', items: [], page: 0, hasMore: false, loadingMore: false, message: '' } : { loadingMore: true, message: '' })
    try {
      const result = await load(page, controller.signal)
      if (!valid()) return
      const prior = page === 1 ? [] : state.items, ids = new Set(prior.map(id))
      if (result.items.some(item => ids.has(id(item)))) {
        update({ loadingMore: false, hasMore: false, message: 'A lista mudou entre páginas. Atualize a lista para continuar.' }); return
      }
      update({ status: 'ready', items: [...prior, ...result.items], page: result.page, hasMore: result.hasMore, loadingMore: false })
    } catch (error) {
      if (!valid()) return
      if (error instanceof ApiError && error.status === 401) { try { await revalidate() } catch { /* Keep explicit retry. */ } }
      if (valid()) update({ status: page === 1 ? 'error' : 'ready', loadingMore: false, message: quizError(error) })
    } finally { if (current === controller) current = null }
  }
  return {
    getSnapshot: () => state,
    subscribe: (fn: () => void) => { listeners.add(fn); return () => { listeners.delete(fn) } },
    mount() { mounted = true },
    dispose() { mounted = false; current?.abort(); current = null },
    reload: () => read(1), more: () => !current && mounted && state.hasMore ? read(state.page + 1) : Promise.resolve(),
  }
}
export function createQuizListState(client: QuizzesClient, map: string, node: string, revalidate: () => Promise<void>) {
  return paged<QuizSummary>(async (p, signal) => { const v = await client.list(map, node, p, signal); return { ...v, items: v.quizzes } }, q => q.id, revalidate)
}
export function createQuizHistoryState(client: QuizzesClient, map: string, node: string, quiz: string, revalidate: () => Promise<void>) {
  return paged<QuizAttemptSummary>(async (p, signal) => { const v = await client.history(map, node, quiz, p, signal); return { ...v, items: v.attempts } }, a => a.attemptId, revalidate)
}
