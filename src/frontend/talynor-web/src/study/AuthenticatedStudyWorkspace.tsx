import { useState } from 'react'
import type { StudySessionsClient } from '../api/studySessionsApi.ts'
import type { KnowledgeMapAuthoringClient } from '../api/knowledgeMapAuthoringApi.ts'
import type { KnowledgeContentAuthoringClient } from '../api/knowledgeContentAuthoringApi.ts'
import type { StudyNodeVisit } from '../types/studySession.ts'
import type { FlashcardsClient } from '../api/flashcardsApi.ts'
import { PersistedMaps } from '../components/PersistedMaps.tsx'
import { StudySessionProvider } from './StudySessionProvider.tsx'
import { ActiveStudySessionIndicator } from './ActiveStudySessionIndicator.tsx'

export function AuthenticatedStudyWorkspace({ client, revalidate, authoring, contentAuthoring, flashcards }:
  { client: StudySessionsClient; revalidate: () => Promise<void>; authoring: KnowledgeMapAuthoringClient; contentAuthoring: KnowledgeContentAuthoringClient; flashcards: FlashcardsClient }) {
  const [returnTo, setReturnTo] = useState<StudyNodeVisit | null>(null)
  return <StudySessionProvider client={client} revalidate={revalidate}>
    <ActiveStudySessionIndicator onOpenNode={node => setReturnTo(previous => ({ ...node, requestId: (previous?.requestId ?? 0) + 1 }))} />
    <PersistedMaps onAuthenticationRequired={revalidate} authoring={authoring} contentAuthoring={contentAuthoring}
      studySessions={client} flashcards={flashcards} returnTo={returnTo} />
  </StudySessionProvider>
}
