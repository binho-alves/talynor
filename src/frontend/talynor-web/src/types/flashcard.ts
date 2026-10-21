export type FlashcardInput = { front: string; back: string }
export type Flashcard = FlashcardInput & {
  id: string; knowledgeNodeId: string; contentRevision: number; createdAtUtc: string; updatedAtUtc: string
}
export type FlashcardPage = { flashcards: Flashcard[]; page: number; pageSize: number; hasMore: boolean }
export type ReviewState = { contentRevision: number; reviewCount: number; correctCount: number; lastReviewedAtUtc: string | null }
// Deliberately independent of the authoring DTO: no Back can enter the prompt model.
export type FlashcardPrompt = { id: string; front: string; contentRevision: number; reviewState: ReviewState }
export type FlashcardReveal = { flashcardId: string; back: string; contentRevision: number }
export type Answer = 'correct' | 'incorrect'
export type PendingReview = { reviewId: string; flashcardId: string; contentRevision: number; result: Answer }
export type ReviewEnvelope = {
  review: PendingReview & { reviewedAtUtc: string }; currentReviewState: ReviewState
}
