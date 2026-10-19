import { useEffect, useState, useSyncExternalStore, type ReactNode } from 'react'
import type { StudySessionsClient } from '../api/studySessionsApi.ts'
import { createStudySessionState } from './studySessionState.ts'
import { StudySessionContext } from './studySessionContext.ts'

export function StudySessionProvider({ client, revalidate, children }:
  { client: StudySessionsClient; revalidate: () => Promise<void>; children: ReactNode }) {
  const [actions] = useState(() => createStudySessionState(client, revalidate))
  const state = useSyncExternalStore(actions.subscribe, actions.getSnapshot)
  useEffect(() => {
    actions.mount()
    const focus = () => actions.focus()
    window.addEventListener('focus', focus)
    return () => { window.removeEventListener('focus', focus); actions.dispose() }
  }, [actions])
  return <StudySessionContext value={{ state, actions }}>{children}</StudySessionContext>
}
