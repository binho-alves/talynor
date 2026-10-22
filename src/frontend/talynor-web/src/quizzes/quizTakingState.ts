import { ApiError } from '../api/http.ts'
import { matchingAttempt, quizError, QuizSubmissionNotSentError, type QuizzesClient } from '../api/quizzesApi.ts'
import type { PendingAttempt, QuizAttempt, QuizTake } from '../types/quizAttempt.ts'

export type QuizTakingState = { message: string } & (
  | { status: 'idle' | 'loading' | 'locking' | 'changed' | 'conflict' }
  | { status: 'ready'; take: QuizTake; selections: Record<string, string> }
  | { status: 'submitting' | 'checking'; pending: PendingAttempt }
  | { status: 'uncertain'; pending: PendingAttempt; canRetry: boolean }
  | { status: 'error'; pending?: PendingAttempt }
  | { status: 'result'; result: QuizAttempt }
)
export function completeQuizAnswers(take: QuizTake, selections: Record<string, string>) {
  return Object.keys(selections).length === take.questions.length && take.questions.every(q => q.options.some(o => o.id === selections[q.id]))
}
// One map/node/quiz visit. Synchronous state transitions lock before ID allocation.
export function createQuizTakingState(client: QuizzesClient, map: string, node: string, quiz: string,
  revalidate: () => Promise<void>, newId: () => string = () => crypto.randomUUID()) {
  let state: QuizTakingState = { status: 'idle', message: '' }, mounted = false, generation = 0
  let read: AbortController | null = null
  const listeners = new Set<() => void>()
  function set(next: QuizTakingState) { state = next; listeners.forEach(fn => fn()) }
  const valid = (visit: number) => mounted && generation === visit
  function clear() { generation++; read?.abort(); read = null; set({ status: 'idle', message: '' }) }
  async function session(error: unknown) {
    const reason = error instanceof QuizSubmissionNotSentError ? error.reason : error
    if (reason instanceof ApiError && reason.status === 401) { try { await revalidate() } catch { /* Keep controlled error. */ } }
  }
  async function start() {
    if (!mounted || ['loading', 'locking', 'submitting', 'checking', 'uncertain'].includes(state.status)) return
    read?.abort(); const controller = new AbortController(); read = controller; const visit = ++generation
    set({ status: 'loading', message: 'Carregando quiz…' })
    try { const take = await client.take(map, node, quiz, controller.signal); if (valid(visit)) set({ status: 'ready', take, selections: {}, message: '' }) }
    catch (error) {
      if (!valid(visit) || controller.signal.aborted) return
      await session(error); if (valid(visit)) set({ status: 'error', message: quizError(error) })
    } finally { if (read === controller) read = null }
  }
  function confirmed(pending: PendingAttempt, result: QuizAttempt, visit: number) {
    if (!valid(visit)) return
    set(matchingAttempt(pending, result) ? { status: 'result', result, message: 'Tentativa confirmada pelo servidor.' }
      : { status: 'conflict', message: 'O resultado não corresponde ao envio. Não haverá reenvio automático. Consulte o histórico.' })
  }
  async function lookup(pending: PendingAttempt, visit: number) {
    if (!valid(visit)) return
    read?.abort(); const controller = new AbortController(); read = controller
    set({ status: 'checking', pending, message: 'Conferindo se a tentativa foi gravada…' })
    try { confirmed(pending, await client.lookup(map, node, pending.quizId, pending.attemptId, controller.signal), visit) }
    catch (error) {
      if (!valid(visit) || controller.signal.aborted) return
      await session(error)
      if (valid(visit)) set({ status: 'uncertain', pending, canRetry: error instanceof ApiError && error.status === 404,
        message: error instanceof ApiError && error.status === 404
          ? 'A tentativa ainda não foi localizada. Isso não prova que o envio falhou. Confira novamente ou reenvie explicitamente o mesmo envio.'
          : 'Não foi possível verificar a tentativa. Confira novamente; nenhum envio será repetido automaticamente.' })
    } finally { if (read === controller) read = null }
  }
  async function send(pending: PendingAttempt, visit: number) {
    // Navigation discards presentation only; a sent write may still commit.
    try { confirmed(pending, await client.submit(map, node, pending, new AbortController().signal), visit) }
    catch (error) {
      if (!valid(visit)) return
      await session(error); if (!valid(visit)) return
      if (error instanceof ApiError && error.code === 'quiz_changed') { set({ status: 'changed', message: quizError(error) }); return }
      if (error instanceof ApiError && error.code === 'attempt_id_conflict') { set({ status: 'conflict', message: quizError(error) }); return }
      // Only rejected fetch for the PUT is ambiguous, not CSRF preparation or parsing/status failures.
      if (error instanceof ApiError && error.kind === 'network') { await lookup(pending, visit); return }
      set({ status: 'error', pending: error instanceof QuizSubmissionNotSentError ? undefined : pending,
        message: `${quizError(error)} Nenhum reenvio automático foi feito.` })
    }
  }
  async function submit() {
    if (!mounted || state.status !== 'ready') return
    if (!completeQuizAnswers(state.take, state.selections)) { set({ ...state, message: 'Responda todas as perguntas antes de enviar.' }); return }
    const take = state.take, selections = state.selections, visit = generation
    set({ status: 'locking', message: 'Preparando envio…' })
    if (!valid(visit)) return
    let attemptId: string
    try { attemptId = newId() } catch { set({ status: 'error', message: 'Não foi possível gerar o identificador da tentativa. Carregue o quiz novamente.' }); return }
    const pending: PendingAttempt = Object.freeze({ attemptId, quizId: take.quizId, contentRevision: take.contentRevision,
      answers: Object.freeze(take.questions.map(q => Object.freeze({ questionId: q.id, selectedOptionId: selections[q.id] }))) })
    set({ status: 'submitting', pending, message: 'Enviando tentativa…' })
    if (valid(visit)) await send(pending, visit)
  }
  return {
    getSnapshot: () => state,
    subscribe: (fn: () => void) => { listeners.add(fn); return () => { listeners.delete(fn) } },
    mount() { mounted = true }, dispose() { mounted = false; clear() }, stop: clear, start, submit,
    select(question: string, option: string) {
      if (!mounted || state.status !== 'ready' || !state.take.questions.some(q => q.id === question && q.options.some(o => o.id === option))) return
      set({ ...state, selections: { ...state.selections, [question]: option }, message: '' })
    },
    check: () => mounted && (state.status === 'uncertain' || state.status === 'error') && state.pending ? lookup(state.pending, generation) : Promise.resolve(),
    retry: () => {
      if (!mounted || state.status !== 'uncertain' || !state.canRetry) return Promise.resolve()
      const pending = state.pending; set({ status: 'submitting', pending, message: 'Reenviando a mesma tentativa por sua solicitação…' })
      return send(pending, generation)
    },
  }
}
