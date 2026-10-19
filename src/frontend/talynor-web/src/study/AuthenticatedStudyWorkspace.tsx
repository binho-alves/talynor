import { useState } from 'react'
import type { StudySessionsClient } from '../api/studySessionsApi.ts'
import type { KnowledgeMapAuthoringClient } from '../api/knowledgeMapAuthoringApi.ts'
import type { KnowledgeContentAuthoringClient } from '../api/knowledgeContentAuthoringApi.ts'
import type { StudyNodeVisit } from '../types/studySession.ts'
import { PersistedMaps } from '../components/PersistedMaps.tsx'
import { StudySessionProvider } from './StudySessionProvider.tsx'
import { ActiveStudySessionIndicator } from './ActiveStudySessionIndicator.tsx'

export function AuthenticatedStudyWorkspace({ client, revalidate, authoring, contentAuthoring }:
  { client: StudySessionsClient; revalidate: () => Promise<void>; authoring: KnowledgeMapAuthoringClient; contentAuthoring: KnowledgeContentAuthoringClient }) {
  const [returnTo, setReturnTo] = useState<StudyNodeVisit | null>(null)
  return <StudySessionProvider client={client} revalidate={revalidate}>
    <ActiveStudySessionIndicator onOpenNode={node => setReturnTo(previous => ({ ...node, requestId: (previous?.requestId ?? 0) + 1 }))} />
    <PersistedMaps onAuthenticationRequired={revalidate} authoring={authoring} contentAuthoring={contentAuthoring}
      studySessions={client} returnTo={returnTo} />
  </StudySessionProvider>
}
