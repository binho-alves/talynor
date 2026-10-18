import { useEffect, useState } from 'react'
import { ApiError } from '../api/http.ts'

type State<T> = { identity: string } & ({ status: 'loading' } | { status: 'ready'; data: T }
  | { status: 'error'; message: string })

// The caller memoizes load. Render identity hides old data immediately, before effect cleanup.
export function useContentRead<T>(key: string | null, load: (signal: AbortSignal) => Promise<T>, revalidate: () => Promise<void>) {
  const [revision, setRevision] = useState(0)
  const identity = `${key}:${revision}`
  const [loaded, setLoaded] = useState<State<T>>({ identity: '', status: 'loading' })
  useEffect(() => {
    if (key === null) return
    const controller = new AbortController()
    let current = true
    void (async () => {
      try {
        const data = await load(controller.signal)
        if (current && !controller.signal.aborted) setLoaded({ identity, status: 'ready', data })
      } catch (error) {
        if (!current || controller.signal.aborted) return
        if (error instanceof ApiError && error.status === 401) await revalidate()
        if (current && !controller.signal.aborted) setLoaded({ identity, status: 'error', message:
          error instanceof ApiError && error.status === 404 ? 'Conteúdo ou conceito indisponível.'
            : error instanceof ApiError && error.status === 401 ? 'A sessão foi reavaliada. Tente carregar novamente.'
              : error instanceof ApiError ? error.message : 'Não foi possível carregar o conteúdo. Tente novamente.' })
      }
    })()
    return () => { current = false; controller.abort() }
  }, [key, identity, load, revalidate])
  const state: State<T> = loaded.identity === identity ? loaded : { identity, status: 'loading' }
  return { state, reload: () => setRevision((value) => value + 1) }
}
