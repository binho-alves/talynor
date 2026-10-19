import { useEffect, useState } from 'react'
import { useStudySession } from './studySessionContext.ts'
import { elapsedSeconds, formatStudyDuration } from './studyTime.ts'
import type { ActiveStudySession, StudyNode } from '../types/studySession.ts'

export function ActiveStudySessionIndicator({ onOpenNode }: { onOpenNode: (node: StudyNode) => void }) {
  const { state, actions } = useStudySession()
  const [now, setNow] = useState(Date.now)
  const [confirmation, setConfirmation] = useState<ActiveStudySession | null>(null)
  const active = state.active
  useEffect(() => {
    if (!active) return
    const interval = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(interval)
  }, [active])
  const locked = state.busy !== null || state.status !== 'ready'
  const confirming = confirmation !== null && confirmation.session.id === active?.session.id
  if (!active && state.status === 'ready' && !state.message) return null
  return <section className="study-indicator" aria-label="Sessão de estudo">
    {active && <><div className="study-topic"><strong>Sessão de estudo ativa</strong><span>{active.nodeTitle}</span></div>
      <span className="study-timer" role="timer" aria-live="off" aria-label="Tempo decorrido de estudo">
        {state.anchor ? formatStudyDuration(elapsedSeconds(active.session.startedAtUtc, state.anchor, Math.max(now, state.anchor.receivedAt))) : '…'}
      </span>
      <div className="editor-actions">
        <button onClick={() => onOpenNode(active)}>Voltar ao conceito</button>
        <button disabled={locked} onClick={() => { setConfirmation(null); void actions.complete(active) }}>Concluir agora</button>
        <button disabled={locked} onClick={() => setConfirmation(active)}>Descartar</button>
      </div>
      {confirming && <section className="study-confirmation" aria-label="Confirmar descarte de estudo">
        <p>Descartar permanentemente esta sessão de estudo de “{confirmation.nodeTitle}”?</p>
        <div className="editor-actions"><button disabled={locked} onClick={() => {
          setConfirmation(null); void actions.discard(confirmation)
        }}>Confirmar descarte</button><button disabled={state.busy !== null} onClick={() => setConfirmation(null)}>Cancelar descarte</button></div>
      </section>}
    </>}
    <p className={state.message || state.status !== 'ready' ? 'study-feedback' : 'sr-only'} role="status">
      {state.status === 'loading' ? 'Conferindo a sessão de estudo…' : state.message || 'Sessão de estudo atualizada.'}
    </p>
    {state.status === 'error' && <button disabled={state.busy !== null} onClick={() => { void actions.refresh() }}>Atualizar estudos</button>}
  </section>
}
