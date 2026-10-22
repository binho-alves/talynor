// Taking has its own shape with no answer-key fields or authoring dependency.
export type QuizTake = { quizId: string; title: string; contentRevision: number; questions: {
  id: string; text: string; position: number; options: { id: string; text: string; position: number }[]
}[] }
export type QuizSelection = { questionId: string; selectedOptionId: string }
export type PendingAttempt = Readonly<{ attemptId: string; quizId: string; contentRevision: number; answers: readonly Readonly<QuizSelection>[] }>
export type QuizAttemptSummary = { attemptId: string; quizId: string; quizTitle: string; contentRevision: number;
  submittedAtUtc: string; questionCount: number; correctCount: number; accuracyPercentage: number }
export type QuizAttempt = QuizAttemptSummary & { answers: { questionId: string; position: number; text: string;
  options: { id: string; position: number; text: string }[]; selectedOptionId: string; correctOptionId: string; isCorrect: boolean }[] }
export type QuizAttemptPage = { attempts: QuizAttemptSummary[]; page: number; pageSize: number; hasMore: boolean }
