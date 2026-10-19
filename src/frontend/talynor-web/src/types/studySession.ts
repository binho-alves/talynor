export type StudySession = {
  id: string
  knowledgeNodeId: string
  startedAtUtc: string
  endedAtUtc: string | null
  durationSeconds: number | null
}
export type ActiveStudySession = { session: StudySession; mapId: string; nodeId: string; nodeTitle: string }
export type ActiveStudySessionEnvelope = { active: ActiveStudySession | null; serverTimeUtc: string }
export type StudySessionHistory = { sessions: StudySession[]; page: number; pageSize: number; hasMore: boolean }
export type StudyNode = { mapId: string; nodeId: string; nodeTitle: string }
export type StudyNodeVisit = StudyNode & { requestId: number }
