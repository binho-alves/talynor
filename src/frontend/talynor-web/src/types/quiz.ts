// Authoring contracts only. Never pass a QuizDefinition to the answering flow.
export type QuizSummary = { id: string; title: string; contentRevision: number; createdAtUtc: string; updatedAtUtc: string }
export type QuizPage = { quizzes: QuizSummary[]; page: number; pageSize: number; hasMore: boolean }
export type QuizDefinition = QuizSummary & { knowledgeNodeId: string; questions: {
  id: string; text: string; position: number; options: { id: string; text: string; position: number }[]; correctOptionIndex: number
}[] }
export type QuizInput = { title: string; questions: { text: string; options: string[]; correctOptionIndex: number }[] }
