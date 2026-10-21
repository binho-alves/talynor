import { ApiError } from '../api/http.ts'
import { flashcardError, matchingReview, type FlashcardsClient } from '../api/flashcardsApi.ts'
import type { Answer, FlashcardPrompt, PendingReview, ReviewEnvelope } from '../types/flashcard.ts'

export type FlashcardStudyState = { message: string } & (
  | { status: 'idle' | 'loading' | 'locking' | 'empty' | 'error' | 'conflict' }
  | { status: 'prompt' | 'revealing'; item: FlashcardPrompt }
  | { status: 'revealed'; item: FlashcardPrompt; back: string }
  | { status: 'submitting' | 'checking'; pending: PendingReview }
  | { status: 'uncertain'; pending: PendingReview; canRetry: boolean }
)

// One mounted node visit, using the same scoped coordinator pattern as Study Sessions.
// The state transition is synchronous, so two clicks cannot allocate two ReviewIds.
export function createFlashcardStudyState(client: FlashcardsClient, map: string, node: string,
  revalidate: () => Promise<void>, newId = () => crypto.randomUUID()) {
  let state: FlashcardStudyState = { status: 'idle', message: '' }
  let mounted = false, generation = 0, read: AbortController | null = null
  const listeners = new Set<() => void>()
  function set(next: FlashcardStudyState) { state = next; listeners.forEach(fn => fn()) }
  const valid = (visit: number) => mounted && visit === generation
  function clear() { generation++; read?.abort(); read = null; set({ status: 'idle', message: '' }) }
  async function session(error: unknown) {
    if (error instanceof ApiError && error.status === 401) await revalidate()
  }
  async function next(message = '') {
    if (!mounted) return
    read?.abort(); const controller = new AbortController(); read = controller
    const visit = ++generation
    set({ status: 'loading', message: message || 'Carregando flashcard…' })
    try {
      const item = await client.next(map, node, controller.signal)
      if (valid(visit)) set(item ? { status: 'prompt', item, message } : { status: 'empty', message: 'Este conceito ainda não tem flashcards.' })
    } catch (error) {
      if (!valid(visit) || controller.signal.aborted) return
      await session(error)
      if (valid(visit)) set({ status: 'error', message: flashcardError(error) })
    } finally { if (read === controller) read = null }
  }
  async function reveal() {
    if (!mounted || state.status !== 'prompt') return
    const item = state.item, visit = generation
    const controller = new AbortController(); read = controller
    set({ status: 'revealing', item, message: 'Carregando resposta…' })
    try {
      const result = await client.reveal(map, node, item.id, item.contentRevision, controller.signal)
      if (valid(visit)) set({ status: 'revealed', item, back: result.back, message: 'Resposta revelada.' })
    } catch (error) {
      if (!valid(visit) || controller.signal.aborted) return
      if (error instanceof ApiError && (error.code === 'flashcard_changed' || error.status === 404)) {
        await next(flashcardError(error)); return
      }
      await session(error)
      if (valid(visit)) set({ status: 'prompt', item, message: flashcardError(error) })
    } finally { if (read === controller) read = null }
  }
  async function confirmed(pending: PendingReview, result: ReviewEnvelope, visit: number) {
    if (!valid(visit)) return
    if (!matchingReview(pending, result)) {
      set({ status: 'conflict', message: 'A resposta recebida não corresponde à enviada. Reinicie o estudo; a operação não será repetida.' }); return
    }
    // These are current counters, not a historical snapshot of the confirmed event.
    const current = result.currentReviewState
    await next(`Resposta confirmada. Revisão atual ${current.contentRevision}: ${current.reviewCount} respostas, ${current.correctCount} corretas.`)
  }
  async function lookup(pending: PendingReview, visit: number) {
    if (!valid(visit)) return
    read?.abort(); const controller = new AbortController(); read = controller
    set({ status: 'checking', pending, message: 'Conferindo se a resposta foi gravada…' })
    try { await confirmed(pending, await client.lookup(map, node, pending, controller.signal), visit) }
    catch (error) {
      if (!valid(visit) || controller.signal.aborted) return
      await session(error)
      if (valid(visit)) set({ status: 'uncertain', pending,
        canRetry: error instanceof ApiError && error.status === 404,
        message: error instanceof ApiError && error.status === 404
          ? 'A resposta ainda não foi localizada. A gravação anterior pode estar em andamento. Confira novamente ou reenvie explicitamente a mesma resposta.'
          : 'Não foi possível conferir a resposta. Confira novamente; nenhum envio será repetido automaticamente.' })
    } finally { if (read === controller) read = null }
  }
  async function submit(pending: PendingReview, visit: number) {
    // Sent writes intentionally are not aborted on navigation; discard late UI results instead.
    try { await confirmed(pending, await client.review(map, node, pending, new AbortController().signal), visit) }
    catch (error) {
      if (!valid(visit)) return
      if (error instanceof ApiError && (error.code === 'flashcard_changed' || error.status === 404)) {
        await next(flashcardError(error)); return
      }
      if (error instanceof ApiError && error.code === 'review_id_conflict') {
        set({ status: 'conflict', message: flashcardError(error) }); return
      }
      await session(error)
      if (!valid(visit)) return
      // A received HTTP status remains deterministic even when its body is unrecognized.
      if (!(error instanceof ApiError) || error.status === undefined) {
        await lookup(pending, visit)
      } else set({ status: 'error', message: flashcardError(error) })
    }
  }
  async function answer(result: Answer) {
    if (!mounted || state.status !== 'revealed') return
    const item = state.item, visit = generation
    // Drop Back immediately and acquire the synchronous lock BEFORE allocating the ID.
    set({ status: 'locking', message: 'Enviando resposta…' })
    let reviewId: string
    try { reviewId = newId() } catch { set({ status: 'error', message: 'Não foi possível gerar o identificador da resposta. Reinicie o estudo.' }); return }
    const pending = { reviewId, flashcardId: item.id, contentRevision: item.contentRevision, result }
    set({ status: 'submitting', pending, message: 'Enviando resposta…' })
    await submit(pending, visit)
  }
  return {
    getSnapshot: () => state,
    subscribe: (fn: () => void) => { listeners.add(fn); return () => { listeners.delete(fn) } },
    mount() { mounted = true },
    dispose() { mounted = false; clear() },
    stop: clear,
    start: () => state.status === 'idle' || state.status === 'empty' || state.status === 'error' || state.status === 'conflict' ? next() : Promise.resolve(),
    reveal, answer,
    check: () => state.status === 'uncertain' ? lookup(state.pending, generation) : Promise.resolve(),
    retry: () => {
      if (!mounted || state.status !== 'uncertain' || !state.canRetry) return Promise.resolve()
      const pending = state.pending
      set({ status: 'submitting', pending, message: 'Reenviando a mesma resposta por sua solicitação…' })
      return submit(pending, generation)
    },
  }
}
