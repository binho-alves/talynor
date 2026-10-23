import { ApiError } from '../api/http.ts'
import { getNodeProgress } from '../api/nodeProgressApi.ts'
import type { NodeProgress, ProgressDays } from '../types/nodeProgress.ts'

type State = { identity: string; days: ProgressDays } & (
  | { status: 'closed' | 'loading' | 'session-check' }
  | { status: 'ready'; data: NodeProgress }
  | { status: 'error' | 'not-found'; message: string })

// One coordinator per account/map/node visit, as in the existing quiz read coordinators.
export function createNodeProgressState(account: string, map: string, node: string, revalidate: () => Promise<void>, load = getNodeProgress) {
  let generation = 0, mounted = false, current: AbortController | null = null
  let state: State = { identity: '', days: 30, status: 'closed' }
  const listeners = new Set<() => void>()
  function update(next: State) { state = next; listeners.forEach(fn => fn()) }
  function cancel() { generation++; current?.abort(); current = null }
  async function read(days: ProgressDays) {
    if (!mounted) return
    cancel()
    const requestGeneration = generation, controller = new AbortController()
    current = controller
    const identity = JSON.stringify([account, map, node, days, requestGeneration])
    const valid = () => mounted && generation === requestGeneration && current === controller && !controller.signal.aborted
    update({ identity, days, status: 'loading' }) // Never leave obsolete values visible while reading.
    try {
      const data = await load(map, node, days, controller.signal)
      if (valid()) update({ identity, days, status: 'ready', data })
    } catch (error) {
      if (!valid()) return
      if (error instanceof ApiError && error.status === 401) {
        update({ identity, days, status: 'session-check' })
        try { await revalidate() } catch { /* Session recovery still requires an explicit retry. */ }
      }
      if (valid()) update(error instanceof ApiError && error.status === 404
        ? { identity, days, status: 'not-found', message: 'Conceito indisponível ou não encontrado.' }
        : { identity, days, status: 'error', message: error instanceof ApiError && error.status === 401
          ? 'A sessão foi reavaliada. Entre novamente ou tente carregar os dados novamente.'
          : 'Não foi possível carregar a atividade e os resultados. Tente novamente.' })
    } finally { if (current === controller) current = null }
  }
  return {
    getSnapshot: () => state,
    subscribe(fn: () => void) { listeners.add(fn); return () => { listeners.delete(fn) } },
    mount() { mounted = true },
    dispose() { mounted = false; cancel(); update({ identity: '', days: state.days, status: 'closed' }) },
    open: () => state.status === 'closed' ? read(state.days) : Promise.resolve(),
    close() { cancel(); update({ identity: '', days: state.days, status: 'closed' }) },
    selectDays(days: ProgressDays) { return days !== state.days && state.status !== 'closed' ? read(days) : Promise.resolve() },
    refresh: () => state.status !== 'closed' ? read(state.days) : Promise.resolve(),
  }
}
