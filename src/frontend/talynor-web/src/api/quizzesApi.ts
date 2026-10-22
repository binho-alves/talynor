import { ApiError, isRecord, readJson, request, requestNoContent } from './http.ts'
import { isMapId as hasGuidShape } from './knowledgeMapsApi.ts'
import { trimQuizWhitespace } from '../quizzes/quizText.ts'
import type { WithCsrf } from '../session/sessionClient.ts'
import type { QuizDefinition, QuizInput, QuizPage, QuizSummary } from '../types/quiz.ts'
import type { PendingAttempt, QuizAttempt, QuizAttemptPage, QuizAttemptSummary, QuizTake } from '../types/quizAttempt.ts'

const integer = (v: unknown, min: number, max: number): v is number => typeof v === 'number' && Number.isInteger(v) && v >= min && v <= max
const isMapId = (v: unknown): v is string => hasGuidShape(v) && v !== '00000000-0000-0000-0000-000000000000'
const revision = (v: unknown): v is number => integer(v, 1, 2147483647)
const text = (v: unknown, max: number): v is string => typeof v === 'string' && !!trimQuizWhitespace(v) && v.length <= max
const utc = (v: unknown): v is string => typeof v === 'string' && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)
  && Number.isFinite(Date.parse(v)) && new Date(v).toISOString().slice(0, 19) === v.slice(0, 19)
const keys = (v: Record<string, unknown>, allowed: string[]) => Object.keys(v).every(k => allowed.includes(k))
const unique = (ids: string[]) => new Set(ids.map(id => id.toLowerCase())).size === ids.length
function options(v: unknown): v is { id: string; text: string; position: number }[] {
  return Array.isArray(v) && v.length >= 2 && v.length <= 4 && v.every((o, i) => isRecord(o) && isMapId(o.id)
    && text(o.text, 500) && o.position === i + 1) && unique(v.map(o => o.id))
}
export function isQuizSummary(v: unknown): v is QuizSummary {
  return isRecord(v) && isMapId(v.id) && text(v.title, 200) && revision(v.contentRevision) && utc(v.createdAtUtc) && utc(v.updatedAtUtc)
}
export function isQuizDefinition(v: unknown): v is QuizDefinition {
  if (!isRecord(v)) return false
  const questions = v.questions, node = v.knowledgeNodeId
  return isQuizSummary(v) && isMapId(node) && Array.isArray(questions)
    && questions.length >= 1 && questions.length <= 10 && questions.every((q, i) => isRecord(q) && isMapId(q.id)
      && text(q.text, 1000) && q.position === i + 1 && options(q.options) && integer(q.correctOptionIndex, 0, q.options.length - 1))
    && unique(questions.map(q => q.id))
}
export function isQuizTake(v: unknown): v is QuizTake {
  // Fail closed on additional fields at every level, then copy only this key-free shape.
  return isRecord(v) && keys(v, ['quizId', 'title', 'contentRevision', 'questions']) && isMapId(v.quizId)
    && text(v.title, 200) && revision(v.contentRevision) && Array.isArray(v.questions) && v.questions.length >= 1 && v.questions.length <= 10
    && v.questions.every((q, i) => isRecord(q) && keys(q, ['id', 'text', 'position', 'options']) && isMapId(q.id)
      && text(q.text, 1000) && q.position === i + 1 && options(q.options)
      && q.options.every(o => keys(o, ['id', 'text', 'position']))) && unique(v.questions.map(q => q.id))
}
function page(v: unknown): v is { page: number; pageSize: number; hasMore: boolean } {
  return isRecord(v) && revision(v.page) && integer(v.pageSize, 1, 100) && typeof v.hasMore === 'boolean'
}
export function isQuizPage(v: unknown): v is QuizPage {
  if (!isRecord(v)) return false
  const rows = v.quizzes
  return page(v) && Array.isArray(rows) && rows.length <= v.pageSize
    && rows.every(isQuizSummary) && unique(rows.map(q => q.id))
}
export function isQuizAttemptSummary(v: unknown): v is QuizAttemptSummary {
  return isRecord(v) && isMapId(v.attemptId) && isMapId(v.quizId) && text(v.quizTitle, 200) && revision(v.contentRevision)
    && utc(v.submittedAtUtc) && integer(v.questionCount, 1, 10) && integer(v.correctCount, 0, v.questionCount)
    && typeof v.accuracyPercentage === 'number' && Number.isFinite(v.accuracyPercentage)
    && v.accuracyPercentage === Math.round(10000 * v.correctCount / v.questionCount) / 100
}
export function isQuizAttempt(v: unknown): v is QuizAttempt {
  if (!isRecord(v)) return false
  const answers = v.answers
  return isQuizAttemptSummary(v) && Array.isArray(answers) && answers.length === v.questionCount
    && answers.every((a, i) => isRecord(a) && isMapId(a.questionId) && a.position === i + 1 && text(a.text, 1000) && options(a.options)
      && isMapId(a.selectedOptionId) && isMapId(a.correctOptionId) && typeof a.isCorrect === 'boolean'
      && a.options.some(o => o.id.toLowerCase() === (a.selectedOptionId as string).toLowerCase())
      && a.options.some(o => o.id.toLowerCase() === (a.correctOptionId as string).toLowerCase())
      && a.isCorrect === (a.selectedOptionId.toLowerCase() === a.correctOptionId.toLowerCase()))
    && unique(answers.map(a => a.questionId)) && answers.filter(a => a.isCorrect).length === v.correctCount
}
export function isQuizAttemptPage(v: unknown): v is QuizAttemptPage {
  if (!isRecord(v)) return false
  const rows = v.attempts
  return page(v) && Array.isArray(rows) && rows.length <= v.pageSize
    && rows.every(isQuizAttemptSummary) && unique(rows.map(a => a.attemptId))
}
function copySummary(q: QuizSummary): QuizSummary {
  return { id: q.id.toLowerCase(), title: q.title, contentRevision: q.contentRevision, createdAtUtc: q.createdAtUtc, updatedAtUtc: q.updatedAtUtc }
}
function copyDefinition(q: QuizDefinition): QuizDefinition {
  return { ...copySummary(q), knowledgeNodeId: q.knowledgeNodeId.toLowerCase(), questions: q.questions.map(question => ({
    id: question.id.toLowerCase(), text: question.text, position: question.position, correctOptionIndex: question.correctOptionIndex,
    options: question.options.map(o => ({ id: o.id.toLowerCase(), text: o.text, position: o.position })) })) }
}
function copyAttemptSummary(a: QuizAttemptSummary): QuizAttemptSummary {
  return { attemptId: a.attemptId.toLowerCase(), quizId: a.quizId.toLowerCase(), quizTitle: a.quizTitle, contentRevision: a.contentRevision,
    submittedAtUtc: a.submittedAtUtc, questionCount: a.questionCount, correctCount: a.correctCount, accuracyPercentage: a.accuracyPercentage }
}
function copyAttempt(a: QuizAttempt): QuizAttempt {
  return { ...copyAttemptSummary(a), answers: a.answers.map(answer => ({ questionId: answer.questionId.toLowerCase(), position: answer.position,
    text: answer.text, selectedOptionId: answer.selectedOptionId.toLowerCase(), correctOptionId: answer.correctOptionId.toLowerCase(), isCorrect: answer.isCorrect,
    options: answer.options.map(o => ({ id: o.id.toLowerCase(), position: o.position, text: o.text })) })) }
}
export function matchingAttempt(pending: PendingAttempt, attempt: QuizAttempt): boolean {
  return pending.attemptId.toLowerCase() === attempt.attemptId.toLowerCase() && pending.quizId.toLowerCase() === attempt.quizId.toLowerCase()
    && pending.contentRevision === attempt.contentRevision && pending.answers.length === attempt.answers.length
    && unique(pending.answers.map(a => a.questionId))
    && pending.answers.every(a => attempt.answers.some(b => a.questionId.toLowerCase() === b.questionId.toLowerCase()
      && a.selectedOptionId.toLowerCase() === b.selectedOptionId.toLowerCase()))
}
export function quizError(error: unknown): string {
  if (error instanceof QuizSubmissionNotSentError) return quizError(error.reason)
  if (error instanceof ApiError) {
    if (error.code === 'quiz_changed') return 'O quiz mudou. Carregue a definição atual e responda novamente; suas respostas antigas foram descartadas.'
    if (error.code === 'attempt_id_conflict') return 'Conflito no identificador da tentativa. O envio não será repetido.'
    if (error.status === 404) return 'Quiz ou tentativa indisponível. Atualize para conferir o estado atual.'
    if (error.status === 401) return 'A sessão foi reavaliada. Tente novamente por uma ação explícita.'
    if (error.code === 'csrf_invalid') return 'A proteção da sessão precisa ser renovada. A operação não foi repetida.'
    if (error.code === 'validation_failed' || error.status === 413) return 'Confira os campos, respostas e limites informados.'
  }
  return 'Não foi possível confirmar a operação. Confira o estado atual antes de tentar novamente.'
}
export class QuizSubmissionNotSentError extends Error {
  readonly reason: unknown
  constructor(reason: unknown) { super('O envio da tentativa não foi iniciado.'); this.reason = reason }
}
export function quizPath(map: string, node: string, quiz?: string) {
  if (![map, node, ...(quiz === undefined ? [] : [quiz])].every(isMapId)) throw new ApiError('invalid-response')
  return `/api/maps/${map}/nodes/${node}/quizzes${quiz === undefined ? '' : `/${quiz}`}`
}
export function createQuizzesClient(withCsrf: WithCsrf) {
  const write = (method: string, token: string, body?: unknown): RequestInit => ({ method,
    headers: { 'X-CSRF-TOKEN': token, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }) })
  const inputBody = (input: QuizInput) => ({ title: input.title, questions: input.questions.map(q => ({ text: q.text, options: [...q.options], correctOptionIndex: q.correctOptionIndex })) })
  function attemptPath(map: string, node: string, quiz: string, attempt?: string) {
    if (attempt !== undefined && !isMapId(attempt)) throw new ApiError('invalid-response')
    return `${quizPath(map, node, quiz)}/attempts${attempt === undefined ? '' : `/${attempt}`}`
  }
  async function result(response: Response, signal: AbortSignal, quiz: string, attempt: string) {
    const v = await readJson(response, signal, isQuizAttempt)
    if (v.quizId.toLowerCase() !== quiz.toLowerCase() || v.attemptId.toLowerCase() !== attempt.toLowerCase()) throw new ApiError('invalid-response', response.status)
    return copyAttempt(v)
  }
  return {
    async list(map: string, node: string, p: number, signal: AbortSignal): Promise<QuizPage> {
      const v = await readJson(await request(`${quizPath(map, node)}?page=${p}&pageSize=20`, signal), signal, isQuizPage)
      if (v.page !== p || v.pageSize !== 20) throw new ApiError('invalid-response', 200)
      return { quizzes: v.quizzes.map(copySummary), page: v.page, pageSize: v.pageSize, hasMore: v.hasMore }
    },
    async detail(map: string, node: string, id: string, signal: AbortSignal): Promise<QuizDefinition> {
      const v = await readJson(await request(quizPath(map, node, id), signal), signal, isQuizDefinition)
      if (v.id.toLowerCase() !== id.toLowerCase() || v.knowledgeNodeId.toLowerCase() !== node.toLowerCase()) throw new ApiError('invalid-response', 200)
      return copyDefinition(v)
    },
    create: (map: string, node: string, input: QuizInput, signal: AbortSignal): Promise<QuizDefinition> => withCsrf(async (token, signal) => {
      const response = await request(quizPath(map, node), signal, write('POST', token, inputBody(input)))
      if (response.status !== 201) throw new ApiError('invalid-response', response.status)
      let value: unknown
      try { value = await response.json() } catch { throw new ApiError('invalid-response', response.status) }
      signal.throwIfAborted()
      if (!isQuizDefinition(value) || value.knowledgeNodeId.toLowerCase() !== node.toLowerCase()) throw new ApiError('invalid-response', response.status)
      return copyDefinition(value)
    }, signal),
    replace: (map: string, node: string, id: string, expectedRevision: number, input: QuizInput, signal: AbortSignal) =>
      withCsrf((token, signal) => requestNoContent(quizPath(map, node, id), signal, write('PUT', token, { expectedRevision, ...inputBody(input) })), signal),
    remove: (map: string, node: string, id: string, signal: AbortSignal) =>
      withCsrf((token, signal) => requestNoContent(quizPath(map, node, id), signal, write('DELETE', token)), signal),
    async take(map: string, node: string, id: string, signal: AbortSignal): Promise<QuizTake> {
      const v = await readJson(await request(`${quizPath(map, node, id)}/take`, signal), signal, isQuizTake)
      if (v.quizId.toLowerCase() !== id.toLowerCase()) throw new ApiError('invalid-response', 200)
      return { quizId: v.quizId.toLowerCase(), title: v.title, contentRevision: v.contentRevision, questions: v.questions.map(q => ({
        id: q.id.toLowerCase(), text: q.text, position: q.position, options: q.options.map(o => ({ id: o.id.toLowerCase(), text: o.text, position: o.position })) })) }
    },
    async submit(map: string, node: string, pending: PendingAttempt, signal: AbortSignal): Promise<QuizAttempt> {
      const path = attemptPath(map, node, pending.quizId, pending.attemptId)
      let sent = false
      try {
        return await withCsrf(async (token, signal) => {
          sent = true
          return result(await request(path, signal, write('PUT', token, { contentRevision: pending.contentRevision,
            answers: pending.answers.map(a => ({ questionId: a.questionId, selectedOptionId: a.selectedOptionId })) })), signal, pending.quizId, pending.attemptId)
        }, signal)
      } catch (error) { if (!sent) throw new QuizSubmissionNotSentError(error); throw error }
    },
    async history(map: string, node: string, id: string, p: number, signal: AbortSignal): Promise<QuizAttemptPage> {
      const v = await readJson(await request(`${attemptPath(map, node, id)}?page=${p}&pageSize=20`, signal), signal, isQuizAttemptPage)
      if (v.page !== p || v.pageSize !== 20 || v.attempts.some(a => a.quizId.toLowerCase() !== id.toLowerCase())) throw new ApiError('invalid-response', 200)
      return { attempts: v.attempts.map(copyAttemptSummary), page: v.page, pageSize: v.pageSize, hasMore: v.hasMore }
    },
    async lookup(map: string, node: string, quiz: string, attempt: string, signal: AbortSignal): Promise<QuizAttempt> {
      return result(await request(attemptPath(map, node, quiz, attempt), signal), signal, quiz, attempt)
    },
  }
}
export type QuizzesClient = ReturnType<typeof createQuizzesClient>
