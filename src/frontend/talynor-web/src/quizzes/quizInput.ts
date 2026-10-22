import type { QuizDefinition, QuizInput } from '../types/quiz.ts'
import { trimQuizWhitespace } from './quizText.ts'

export function quizInput(title: string, questions: QuizInput['questions']): QuizInput | null {
  // JavaScript length is UTF-16. Only title normalization and duplicate comparison trim.
  if (!trimQuizWhitespace(title) || title.length > 200 || questions.length < 1 || questions.length > 10) return null
  if (questions.some(q => !trimQuizWhitespace(q.text) || q.text.length > 1000 || q.options.length < 2 || q.options.length > 4
    || !Number.isInteger(q.correctOptionIndex) || q.correctOptionIndex < 0 || q.correctOptionIndex >= q.options.length
    || q.options.some(o => !trimQuizWhitespace(o) || o.length > 500) || new Set(q.options.map(trimQuizWhitespace)).size !== q.options.length)) return null
  return { title: trimQuizWhitespace(title), questions: questions.map(q => ({ text: q.text, options: [...q.options], correctOptionIndex: q.correctOptionIndex })) }
}
export function definitionInput(q: QuizDefinition): QuizInput {
  return { title: q.title, questions: q.questions.map(question => ({ text: question.text,
    options: question.options.map(o => o.text), correctOptionIndex: question.correctOptionIndex })) }
}
export function quizChanged(before: QuizInput, after: QuizInput) {
  return JSON.stringify(before) !== JSON.stringify(after)
}
