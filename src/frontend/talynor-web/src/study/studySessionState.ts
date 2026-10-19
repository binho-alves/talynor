import { ApiError } from '../api/http.ts'
import { studyError, type StudySessionsClient } from '../api/studySessionsApi.ts'
import type { ActiveStudySession, StudyNode } from '../types/studySession.ts'
import type { ServerClockAnchor } from './studyTime.ts'

type Mutation = 'start' | 'complete' | 'discard' | 'delete-history'
export type StudyState = {
  status: 'loading' | 'ready' | 'error'
  active: ActiveStudySession | null
  anchor: ServerClockAnchor | null
  busy: Mutation | null
  message: string
  historyRevision: number
}
const initialState = (): StudyState => ({ status: 'loading', active: null, anchor: null, busy: null, message: '', historyRevision: 0 })

// One coordinator per authenticated shell, independent of the selected map/node.
// Only this specific workflow is shared; this is not a general application store.
export function createStudySessionState(client: StudySessionsClient, revalidateSession: () => Promise<void>, now = Date.now) {
  let state = initialState(), mounted = false, generation = 0
  let read: { controller: AbortController; promise: Promise<boolean> } | null = null
  let write: AbortController | null = null
  const listeners = new Set<() => void>()
  function update(patch: Partial<StudyState>) {
    if (!mounted) return
    state = { ...state, ...patch }; listeners.forEach(listener => listener())
  }
  async function readActive(force = false): Promise<boolean> {
    if (!mounted) return false
    if (read && !force) return read.promise
    read?.controller.abort()
    const controller = new AbortController(), epoch = generation
    update({ status: 'loading' })
    const current = () => mounted && epoch === generation && read?.controller === controller && !controller.signal.aborted
    // Defer I/O so the identity is assigned even with an immediately resolving mock.
    const promise = Promise.resolve().then(async () => {
      try {
        const envelope = await client.active(controller.signal)
        if (!current()) return false
        update({ status: 'ready', active: envelope.active,
          anchor: { serverMs: Date.parse(envelope.serverTimeUtc), receivedAt: now() },
          ...(state.busy === null ? { message: '' } : {}),
          historyRevision: state.historyRevision + 1 })
        return true
      } catch (error) {
        if (!current()) return false
        if (error instanceof ApiError && error.status === 401) await revalidateSession()
        if (current()) update({ status: 'error', message: studyError(error) })
        return false
      } finally { if (read?.controller === controller) read = null }
    })
    read = { controller, promise }
    return promise
  }
  async function mutate(kind: Mutation, target: StudyNode, id?: string): Promise<void> {
    if (!mounted || write) return
    const controller = new AbortController(), epoch = generation
    write = controller
    const current = () => mounted && epoch === generation && write === controller && !controller.signal.aborted
    update({ busy: kind, message: 'Conferindo o estado do estudo…' })
    try {
      if (!await readActive(true) || !current()) return
      const active = state.active
      if (kind === 'start' && active) {
        update({ message: 'Já existe uma sessão de estudo ativa. Continue, conclua ou descarte pelo indicador.' }); return
      }
      if ((kind === 'complete' || kind === 'discard') && active?.session.id !== id) {
        update({ message: 'Essa sessão não está mais ativa. O estado foi atualizado; confira o histórico.' }); return
      }
      update({ message: kind === 'start' ? 'Iniciando estudo…' : kind === 'complete' ? 'Concluindo estudo…' : 'Excluindo sessão de estudo…' })
      let failure: unknown
      try {
        if (kind === 'start') {
          const session = await client.start(target.mapId, target.nodeId, controller.signal)
          if (current()) update({ active: { ...target, session } })
        } else if (kind === 'complete') await client.complete(target.mapId, target.nodeId, id!, controller.signal)
        else await client.remove(target.mapId, target.nodeId, id!, controller.signal)
      } catch (error) { failure = error }
      if (!current()) return
      // Never resend a write. An uncertain outcome is reconciled with a read only.
      if (failure instanceof ApiError && failure.status === 401) {
        await revalidateSession()
        if (current()) update({ status: 'error', message: studyError(failure) })
        return
      }
      const confirmed = await readActive(true)
      if (!current()) return
      if (!confirmed) {
        update({ message: 'Não foi possível conferir o estado final. Atualize os estudos antes de outra ação; nada foi reenviado.' })
      } else if (failure) {
        const ambiguous = !(failure instanceof ApiError) || failure.kind !== 'http' || (failure.status ?? 0) >= 500
        const reconciled = kind === 'start'
          ? ambiguous && state.active?.mapId === target.mapId && state.active?.nodeId === target.nodeId
          : (kind === 'complete' || kind === 'discard') && ambiguous && state.active?.session.id !== id
        update({ message: reconciled
          ? 'Estado recuperado do servidor. Confira a sessão ativa e o histórico; a operação não foi repetida.'
          : studyError(failure) })
      } else update({ message: kind === 'start' ? 'Estudo iniciado.' : kind === 'complete' ? 'Sessão de estudo concluída.' : 'Sessão de estudo excluída.' })
    } finally {
      if (write === controller) { write = null; update({ busy: null }) }
    }
  }
  return {
    getSnapshot: () => state,
    subscribe: (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener) } },
    mount() { mounted = true; generation++; void readActive(true) },
    dispose() {
      mounted = false; generation++; read?.controller.abort(); read = null
      // Aborting on logout/unmount clears local I/O, never promises rollback of a sent write.
      write?.abort(); write = null; state = initialState()
    },
    refresh: () => write ? Promise.resolve(false) : readActive(),
    focus: () => { if (!write) void readActive() },
    start: (node: StudyNode) => mutate('start', node),
    complete: (active: ActiveStudySession) => mutate('complete', active, active.session.id),
    discard: (active: ActiveStudySession) => mutate('discard', active, active.session.id),
    deleteHistory: (node: StudyNode, id: string) => mutate('delete-history', node, id),
  }
}
export type StudySessionState = ReturnType<typeof createStudySessionState>
