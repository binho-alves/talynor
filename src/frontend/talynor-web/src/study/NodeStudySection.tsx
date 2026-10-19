import { useEffect, useState, useSyncExternalStore } from 'react'
import type { StudySessionsClient } from '../api/studySessionsApi.ts'
import type { StudyNode, StudySession } from '../types/studySession.ts'
import { useStudySession } from './studySessionContext.ts'
import { createStudyHistoryState } from './studyHistoryState.ts'
import { formatStudyDuration } from './studyTime.ts'

const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeStyle: 'medium' })
export function NodeStudySection({ mapId, nodeId, nodeTitle, client, revalidate }:
  StudyNode & { client: StudySessionsClient; revalidate: () => Promise<void> }) {
  const { state, actions } = useStudySession()
  const [history] = useState(() => createStudyHistoryState(client, mapId, nodeId, revalidate))
  const list = useSyncExternalStore(history.subscribe, history.getSnapshot)
  const [confirmation, setConfirmation] = useState<StudySession | null>(null)
  useEffect(() => { history.mount(); return () => history.dispose() }, [history])
  useEffect(() => { void history.reload() }, [history, state.historyRevision])
  const node = { mapId, nodeId, nodeTitle }
  const locked = state.busy !== null || state.status !== 'ready'
  return <section className="study-section" aria-labelledby="study-heading">
    <h3 id="study-heading">Estudar {nodeTitle}</h3>
    {state.active ? <p>{state.active.nodeId === nodeId ? 'Estudando este conceito agora.' : 'Outro conceito tem uma sessão de estudo ativa.'}</p>
      : <button disabled={locked} onClick={() => { void actions.start(node) }}>Começar a estudar</button>}
    <h4>Histórico de estudo</h4>
    <p role="status" className={list.status === 'loading' || list.message ? '' : 'sr-only'}>
      {list.status === 'loading' ? 'Carregando histórico de estudo…' : list.message || 'Histórico de estudo atualizado.'}
    </p>
    <div aria-busy={list.status === 'loading' || list.loadingMore}>
      {list.status === 'error' && <button disabled={state.busy !== null} onClick={() => { void history.reload() }}>Tentar carregar histórico novamente</button>}
      {list.status === 'ready' && <>
        {list.sessions.length === 0 ? <p>Ainda não há sessões de estudo concluídas.</p>
          : <ol className="study-history">{list.sessions.map(session => <li key={session.id}>
            <span>Início: <time dateTime={session.startedAtUtc}>{dateFormat.format(new Date(session.startedAtUtc))}</time></span>
            <span>Término: <time dateTime={session.endedAtUtc!}>{dateFormat.format(new Date(session.endedAtUtc!))}</time></span>
            <strong>Duração: {formatStudyDuration(session.durationSeconds!)}</strong>
            <button disabled={locked} onClick={() => setConfirmation(session)}
              aria-label={`Excluir sessão iniciada em ${dateFormat.format(new Date(session.startedAtUtc))}`}>Excluir sessão</button>
          </li>)}</ol>}
        {list.hasMore && <button disabled={list.loadingMore || state.busy !== null} onClick={() => { void history.more() }}>
          {list.loadingMore ? 'Carregando mais sessões…' : 'Carregar mais sessões'}</button>}
        <button disabled={list.loadingMore || state.busy !== null} onClick={() => { setConfirmation(null); void history.reload() }}>Atualizar histórico</button>
      </>}
    </div>
    {confirmation && <section className="study-confirmation" aria-label="Confirmar exclusão de sessão concluída">
      <p>Excluir permanentemente a sessão iniciada em {dateFormat.format(new Date(confirmation.startedAtUtc))}?</p>
      <div className="editor-actions"><button disabled={locked} onClick={() => {
        const id = confirmation.id; setConfirmation(null); void actions.deleteHistory(node, id)
      }}>Confirmar exclusão da sessão</button><button disabled={state.busy !== null} onClick={() => setConfirmation(null)}>Cancelar exclusão da sessão</button></div>
    </section>}
  </section>
}
