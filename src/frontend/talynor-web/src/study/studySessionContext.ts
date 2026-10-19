import { createContext, useContext } from 'react'
import type { StudySessionState, StudyState } from './studySessionState.ts'

export const StudySessionContext = createContext<{ state: StudyState; actions: StudySessionState } | null>(null)
export function useStudySession() {
  const value = useContext(StudySessionContext)
  if (!value) throw new Error('Study Session provider is required.')
  return value
}
