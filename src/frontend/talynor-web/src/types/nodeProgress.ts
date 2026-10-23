export type ProgressDays = 7 | 30 | 90

export type NodeProgress = {
  calculatedAtUtc: string
  period: { days: ProgressDays; fromUtc: string; toUtc: string }
  study: { completedSessionCount: number; totalDurationSeconds: number }
  flashcards: { reviewCount: number; correctCount: number; correctPercentage: number | null }
  quizzes: { attemptCount: number; answeredQuestionCount: number; correctAnswerCount: number; accuracyPercentage: number | null }
}
