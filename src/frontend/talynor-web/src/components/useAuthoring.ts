import { useEffect, useRef, useState } from 'react'
import { ApiError } from '../api/http.ts'
import { authoringErrorMessage } from '../api/knowledgeMapAuthoringApi.ts'

// One in-flight mutation per mounted editor. Writes are not aborted on navigation:
// cancellation cannot prove that a server-side write did not commit.
export function useAuthoring(revalidate: () => Promise<void>) {
  const alive = useRef(false)
  const inFlight = useRef(false)
  const [pending, setPending] = useState(false)
  const [feedback, setFeedback] = useState('')
  useEffect(() => { alive.current = true; return () => { alive.current = false } }, [])
  async function run<T>(action: (signal: AbortSignal) => Promise<T>, apply: (result: T) => void): Promise<boolean> {
    if (inFlight.current || !alive.current) return false
    inFlight.current = true
    setPending(true)
    setFeedback('Salvando…')
    try {
      const result = await action(new AbortController().signal)
      if (alive.current) { apply(result); setFeedback('Alteração salva.') }
      return true
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) await revalidate()
      if (alive.current) setFeedback(authoringErrorMessage(error))
      return false
    } finally {
      inFlight.current = false
      if (alive.current) setPending(false)
    }
  }
  return { run, pending, feedback, isPending: () => inFlight.current, isActive: () => alive.current }
}
