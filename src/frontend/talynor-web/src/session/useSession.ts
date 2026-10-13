import { useCallback, useEffect, useRef, useState } from 'react'
import { createSessionClient, loginErrorMessage, SessionBootstrapError } from './sessionClient.ts'
import type { SessionState } from '../types/session'

// A custom hook composes React state/effects; it does not make that state global.
export function useSession() {
  const [client] = useState(createSessionClient)
  const [session, setSession] = useState<SessionState>({ status: 'bootstrapping' })
  const [busy, setBusy] = useState<'login' | 'logout' | null>(null)
  const [operationError, setOperationError] = useState<string>()
  const activeRequest = useRef<AbortController | null>(null)

  const loadSession = useCallback((controller: AbortController) =>
    client.bootstrap(controller.signal).then(
      (result) => {
        if (activeRequest.current === controller && !controller.signal.aborted) setSession(result)
      },
      () => {
        if (activeRequest.current === controller && !controller.signal.aborted) setSession({ status: 'error' })
      },
    ).finally(() => {
      if (activeRequest.current === controller) activeRequest.current = null
    }), [client])

  const retryBootstrap = useCallback(() => {
    activeRequest.current?.abort()
    const controller = new AbortController()
    activeRequest.current = controller
    setSession({ status: 'bootstrapping' })
    setBusy(null)
    setOperationError(undefined)
    return loadSession(controller)
  }, [loadSession])

  useEffect(() => {
    const controller = new AbortController()
    activeRequest.current = controller
    void loadSession(controller)
    // Cleanup plus request identity make StrictMode/retry/unmount ignore stale results.
    return () => {
      activeRequest.current?.abort()
      activeRequest.current = null
      client.clearCsrf()
    }
  }, [client, loadSession])

  async function login(email: string, password: string): Promise<void> {
    // The ref prevents two POSTs even before React has rendered the disabled button.
    if (activeRequest.current || session.status !== 'anonymous') return
    const controller = new AbortController()
    activeRequest.current = controller
    setBusy('login')
    setOperationError(undefined)
    try {
      const result = await client.login(email, password, controller.signal)
      if (activeRequest.current === controller && !controller.signal.aborted) setSession(result)
    } catch (error) {
      if (activeRequest.current !== controller || controller.signal.aborted) return
      if (error instanceof SessionBootstrapError) setSession({ status: 'error' })
      else setOperationError(loginErrorMessage(error))
    } finally {
      if (activeRequest.current === controller) {
        activeRequest.current = null
        setBusy(null)
      }
    }
  }

  async function logout(): Promise<void> {
    if (activeRequest.current || session.status !== 'authenticated') return
    const controller = new AbortController()
    activeRequest.current = controller
    setBusy('logout')
    setOperationError(undefined)
    try {
      const result = await client.logout(controller.signal)
      if (activeRequest.current === controller && !controller.signal.aborted) setSession(result)
    } catch {
      if (activeRequest.current === controller && !controller.signal.aborted) {
        // A failed logout is not proof of an ended session. Keep the shell and offer another attempt.
        setOperationError('Não foi possível encerrar sua sessão. Tente sair novamente.')
      }
    } finally {
      if (activeRequest.current === controller) {
        activeRequest.current = null
        setBusy(null)
      }
    }
  }

  return { session, busy, operationError, login, logout, retryBootstrap }
}
