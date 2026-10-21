import { ApiError } from '../api/http.ts'
import { flashcardError, type FlashcardsClient } from '../api/flashcardsApi.ts'
import type { Flashcard } from '../types/flashcard.ts'

export function createFlashcardListState(client: FlashcardsClient, map: string, node: string, revalidate: () => Promise<void>) {
  let state: { status: 'loading' | 'ready' | 'error'; cards: Flashcard[]; page: number; hasMore: boolean; loadingMore: boolean; message: string } =
    { status: 'loading', cards: [], page: 0, hasMore: false, loadingMore: false, message: '' }
  let current: AbortController | null = null, mounted = false
  const listeners = new Set<() => void>()
  function update(patch: Partial<typeof state>) { state = { ...state, ...patch }; listeners.forEach(fn => fn()) }
  async function load(page: number) {
    if (!mounted) return
    current?.abort(); const controller = new AbortController(); current = controller
    const valid = () => mounted && current === controller && !controller.signal.aborted
    update(page === 1 ? { status: 'loading', cards: [], page: 0, hasMore: false, loadingMore: false, message: '' } : { loadingMore: true, message: '' })
    try {
      const result = await client.list(map, node, page, controller.signal)
      if (!valid()) return
      const existing = page === 1 ? [] : state.cards, ids = new Set(existing.map(card => card.id))
      update({ status: 'ready', cards: [...existing, ...result.flashcards.filter(card => !ids.has(card.id))],
        page: result.page, hasMore: result.hasMore, loadingMore: false })
    } catch (error) {
      if (!valid()) return
      if (error instanceof ApiError && error.status === 401) await revalidate()
      if (valid()) update({ status: page === 1 ? 'error' : 'ready', loadingMore: false, message: flashcardError(error) })
    } finally { if (current === controller) current = null }
  }
  return {
    getSnapshot: () => state,
    subscribe: (fn: () => void) => { listeners.add(fn); return () => { listeners.delete(fn) } },
    mount() { mounted = true },
    dispose() { mounted = false; current?.abort(); current = null },
    reload: () => load(1),
    more: () => !current && state.hasMore ? load(state.page + 1) : Promise.resolve(),
  }
}
