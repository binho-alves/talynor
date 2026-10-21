import { ApiError, isRecord, readJson, request, requestNoContent } from './http.ts'
import { isMapId } from './knowledgeMapsApi.ts'
import type { WithCsrf } from '../session/sessionClient.ts'
import type { Flashcard, FlashcardInput, FlashcardPage, FlashcardPrompt, FlashcardReveal, PendingReview, ReviewEnvelope, ReviewState } from '../types/flashcard.ts'

const int32 = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v > 0 && v <= 2147483647
const text = (v: unknown, max: number): v is string => typeof v === 'string' && !!v.trim() && v.length <= max
const utc = (v: unknown): v is string => typeof v === 'string'
  && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)
  && Number.isFinite(Date.parse(v)) && new Date(v).toISOString().slice(0, 19) === v.slice(0, 19)
export function isFlashcard(v: unknown): v is Flashcard {
  return isRecord(v) && isMapId(v.id) && isMapId(v.knowledgeNodeId) && text(v.front, 2000) && text(v.back, 4000)
    && int32(v.contentRevision) && utc(v.createdAtUtc) && utc(v.updatedAtUtc)
}
export function isReviewState(v: unknown): v is ReviewState {
  if (!isRecord(v) || !int32(v.contentRevision) || typeof v.reviewCount !== 'number' || !Number.isInteger(v.reviewCount)
    || v.reviewCount < 0 || v.reviewCount > 2147483647 || typeof v.correctCount !== 'number' || !Number.isInteger(v.correctCount)
    || v.correctCount < 0 || v.correctCount > v.reviewCount) return false
  return v.reviewCount === 0 ? v.correctCount === 0 && v.lastReviewedAtUtc === null : utc(v.lastReviewedAtUtc)
}
export function isFlashcardPage(v: unknown): v is FlashcardPage {
  return isRecord(v) && int32(v.page) && int32(v.pageSize) && v.pageSize <= 100 && typeof v.hasMore === 'boolean'
    && Array.isArray(v.flashcards) && v.flashcards.length <= v.pageSize && v.flashcards.every(isFlashcard)
    && new Set(v.flashcards.map(card => card.id.toLowerCase())).size === v.flashcards.length
}
export function isStudyNext(v: unknown): v is { item: FlashcardPrompt | null } {
  if (!isRecord(v)) return false
  const item = v.item
  return item === null || isRecord(item) && !Object.hasOwn(item, 'back') && isMapId(item.id)
    && text(item.front, 2000) && int32(item.contentRevision) && isReviewState(item.reviewState)
    && item.reviewState.contentRevision === item.contentRevision && !Object.hasOwn(item.reviewState, 'back')
}
export function isReveal(v: unknown): v is FlashcardReveal {
  return isRecord(v) && isMapId(v.flashcardId) && int32(v.contentRevision) && text(v.back, 4000)
}
export function isReviewEnvelope(v: unknown): v is ReviewEnvelope {
  if (!isRecord(v) || !isReviewState(v.currentReviewState) || !isRecord(v.review)) return false
  const r = v.review
  return isMapId(r.reviewId) && isMapId(r.flashcardId) && int32(r.contentRevision)
    && (r.result === 'correct' || r.result === 'incorrect') && utc(r.reviewedAtUtc)
}
function copyState(v: ReviewState): ReviewState {
  return { contentRevision: v.contentRevision, reviewCount: v.reviewCount, correctCount: v.correctCount, lastReviewedAtUtc: v.lastReviewedAtUtc }
}
function copyCard(v: Flashcard): Flashcard {
  return { id: v.id.toLowerCase(), knowledgeNodeId: v.knowledgeNodeId.toLowerCase(), front: v.front, back: v.back,
    contentRevision: v.contentRevision, createdAtUtc: v.createdAtUtc, updatedAtUtc: v.updatedAtUtc }
}
function copyReview(v: ReviewEnvelope): ReviewEnvelope {
  const r = v.review
  return { review: { reviewId: r.reviewId.toLowerCase(), flashcardId: r.flashcardId.toLowerCase(), contentRevision: r.contentRevision,
    result: r.result, reviewedAtUtc: r.reviewedAtUtc }, currentReviewState: copyState(v.currentReviewState) }
}
export function flashcardPath(map: string, node: string, id?: string): string {
  if (!isMapId(map) || !isMapId(node) || id !== undefined && !isMapId(id)) throw new ApiError('invalid-response')
  return `/api/maps/${map}/nodes/${node}/flashcards${id === undefined ? '' : `/${id}`}`
}
export function flashcardInput(front: string, back: string): FlashcardInput | null {
  // string.length counts UTF-16 code units, like .NET; preserve the original text.
  return text(front, 2000) && text(back, 4000) ? { front, back } : null
}
export function flashcardPatch(previous: FlashcardInput, next: FlashcardInput): Partial<FlashcardInput> {
  return { ...(previous.front === next.front ? {} : { front: next.front }), ...(previous.back === next.back ? {} : { back: next.back }) }
}
export function matchingReview(pending: PendingReview, value: ReviewEnvelope): boolean {
  const event = value.review
  return event.reviewId.toLowerCase() === pending.reviewId.toLowerCase() && event.flashcardId.toLowerCase() === pending.flashcardId.toLowerCase()
    && event.contentRevision === pending.contentRevision && event.result === pending.result
}
export function flashcardError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code === 'flashcard_changed') return 'O flashcard mudou. A versão atual será carregada.'
    if (error.code === 'review_id_conflict') return 'Conflito no identificador da resposta. Reinicie o estudo deste conceito.'
    if (error.status === 404) return 'Flashcard ou conceito indisponível. Atualize para conferir o estado atual.'
    if (error.status === 401) return 'A sessão foi reavaliada. Tente novamente por uma ação explícita.'
    if (error.code === 'csrf_invalid') return 'A proteção da sessão precisa ser renovada. A operação não foi repetida.'
    if (error.code === 'validation_failed' || error.status === 413) return 'Confira os textos e limites informados. Nenhuma alteração válida foi confirmada.'
  }
  return 'Não foi possível confirmar a operação. Confira o estado atual antes de tentar novamente.'
}
export function createFlashcardsClient(withCsrf: WithCsrf) {
  function write(method: string, token: string, body?: unknown): RequestInit {
    return { method, headers: { 'X-CSRF-TOKEN': token, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }) }
  }
  return {
    async list(map: string, node: string, page: number, signal: AbortSignal): Promise<FlashcardPage> {
      const v = await readJson(await request(`${flashcardPath(map, node)}?page=${page}&pageSize=20`, signal), signal, isFlashcardPage)
      if (v.page !== page || v.pageSize !== 20 || v.flashcards.some(card => card.knowledgeNodeId.toLowerCase() !== node.toLowerCase())) throw new ApiError('invalid-response')
      return { flashcards: v.flashcards.map(copyCard), page: v.page, pageSize: v.pageSize, hasMore: v.hasMore }
    },
    async detail(map: string, node: string, id: string, signal: AbortSignal): Promise<Flashcard> {
      const v = await readJson(await request(flashcardPath(map, node, id), signal), signal, isFlashcard)
      if (v.id.toLowerCase() !== id.toLowerCase() || v.knowledgeNodeId.toLowerCase() !== node.toLowerCase()) throw new ApiError('invalid-response')
      return copyCard(v)
    },
    create: (map: string, node: string, input: FlashcardInput, signal: AbortSignal): Promise<Flashcard> => withCsrf(async (token, signal) => {
      const response = await request(flashcardPath(map, node), signal, write('POST', token, { front: input.front, back: input.back }))
      if (response.status !== 201) throw new ApiError('invalid-response', response.status)
      let v: unknown
      try { v = await response.json() } catch { signal.throwIfAborted(); throw new ApiError('invalid-response') }
      signal.throwIfAborted()
      if (!isFlashcard(v)) throw new ApiError('invalid-response')
      if (v.knowledgeNodeId.toLowerCase() !== node.toLowerCase()) throw new ApiError('invalid-response')
      return copyCard(v)
    }, signal),
    update: (map: string, node: string, id: string, patch: Partial<FlashcardInput>, signal: AbortSignal) =>
      withCsrf((token, signal) => requestNoContent(flashcardPath(map, node, id), signal, write('PATCH', token, patch)), signal),
    remove: (map: string, node: string, id: string, signal: AbortSignal) =>
      withCsrf((token, signal) => requestNoContent(flashcardPath(map, node, id), signal, write('DELETE', token)), signal),
    async next(map: string, node: string, signal: AbortSignal): Promise<FlashcardPrompt | null> {
      const { item } = await readJson(await request(`${flashcardPath(map, node)}/study/next`, signal), signal, isStudyNext)
      return item === null ? null : { id: item.id.toLowerCase(), front: item.front, contentRevision: item.contentRevision, reviewState: copyState(item.reviewState) }
    },
    async reveal(map: string, node: string, id: string, revision: number, signal: AbortSignal): Promise<FlashcardReveal> {
      const v = await readJson(await request(`${flashcardPath(map, node, id)}/study/reveal?contentRevision=${revision}`, signal), signal, isReveal)
      if (v.flashcardId.toLowerCase() !== id.toLowerCase() || v.contentRevision !== revision) throw new ApiError('invalid-response')
      return { flashcardId: v.flashcardId.toLowerCase(), back: v.back, contentRevision: v.contentRevision }
    },
    review: (map: string, node: string, pending: PendingReview, signal: AbortSignal): Promise<ReviewEnvelope> => withCsrf(async (token, signal) => {
      if (!isMapId(pending.reviewId)) throw new ApiError('invalid-response')
      return copyReview(await readJson(await request(`${flashcardPath(map, node, pending.flashcardId)}/reviews/${pending.reviewId}`, signal,
        write('PUT', token, { contentRevision: pending.contentRevision, result: pending.result })), signal, isReviewEnvelope))
    }, signal),
    async lookup(map: string, node: string, pending: PendingReview, signal: AbortSignal): Promise<ReviewEnvelope> {
      if (!isMapId(pending.reviewId)) throw new ApiError('invalid-response')
      return copyReview(await readJson(await request(`${flashcardPath(map, node, pending.flashcardId)}/reviews/${pending.reviewId}`, signal), signal, isReviewEnvelope))
    },
  }
}
export type FlashcardsClient = ReturnType<typeof createFlashcardsClient>
