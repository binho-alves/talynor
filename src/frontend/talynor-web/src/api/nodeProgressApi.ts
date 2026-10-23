import { ApiError, isRecord, readJson, request } from './http.ts'
import { isMapId } from './knowledgeMapsApi.ts'
import type { NodeProgress, ProgressDays } from '../types/nodeProgress.ts'

function keys(value: unknown, expected: string[]): value is Record<string, unknown> {
  return isRecord(value) && Object.keys(value).length === expected.length && expected.every(key => Object.hasOwn(value, key))
}
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
function timestamp(value: unknown): value is string {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value)
    && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 19) === value.slice(0, 19)
}
const fraction = (value: string) => (value.match(/\.(\d+)/)?.[1] ?? '').padEnd(7, '0')
const sameInstant = (left: string, right: string) => Date.parse(left) === Date.parse(right) && fraction(left) === fraction(right)
function percentage(value: unknown, denominator: number): value is number | null {
  return denominator === 0 ? value === null : typeof value === 'number' && Number.isFinite(value)
    && value >= 0 && value <= 100 && Math.abs(value * 100 - Math.round(value * 100)) < 1e-8
}
export function isNodeProgress(value: unknown): value is NodeProgress {
  if (!keys(value, ['calculatedAtUtc', 'period', 'study', 'flashcards', 'quizzes'])) return false
  const { period: p, study: s, flashcards: f, quizzes: q } = value
  return timestamp(value.calculatedAtUtc)
    && keys(p, ['days', 'fromUtc', 'toUtc']) && (p.days === 7 || p.days === 30 || p.days === 90)
    && timestamp(p.fromUtc) && timestamp(p.toUtc)
    && sameInstant(p.toUtc, value.calculatedAtUtc) && fraction(p.fromUtc) === fraction(p.toUtc)
    && Date.parse(p.toUtc) - Date.parse(p.fromUtc) === p.days * 86400000
    && keys(s, ['completedSessionCount', 'totalDurationSeconds']) && count(s.completedSessionCount) && count(s.totalDurationSeconds)
    && (s.completedSessionCount !== 0 || s.totalDurationSeconds === 0)
    && keys(f, ['reviewCount', 'correctCount', 'correctPercentage']) && count(f.reviewCount) && count(f.correctCount)
    && f.correctCount <= f.reviewCount && percentage(f.correctPercentage, f.reviewCount)
    && keys(q, ['attemptCount', 'answeredQuestionCount', 'correctAnswerCount', 'accuracyPercentage'])
    && count(q.attemptCount) && count(q.answeredQuestionCount) && count(q.correctAnswerCount)
    && q.attemptCount <= q.answeredQuestionCount && q.correctAnswerCount <= q.answeredQuestionCount
    && (q.attemptCount !== 0 || q.answeredQuestionCount === 0) && percentage(q.accuracyPercentage, q.answeredQuestionCount)
}
export async function getNodeProgress(map: string, node: string, days: ProgressDays, signal: AbortSignal): Promise<NodeProgress> {
  if (!isMapId(map) || !isMapId(node) || ![7, 30, 90].includes(days)) throw new ApiError('invalid-response')
  const value = await readJson(await request(`/api/maps/${map}/nodes/${node}/progress?days=${days}`, signal), signal, isNodeProgress)
  if (value.period.days !== days) throw new ApiError('invalid-response')
  return { calculatedAtUtc: value.calculatedAtUtc, period: { ...value.period }, study: { ...value.study },
    flashcards: { ...value.flashcards }, quizzes: { ...value.quizzes } }
}
