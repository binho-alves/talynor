import { useEffect, useRef, useState, useSyncExternalStore } from 'react'
import { ApiError } from '../api/http.ts'
import { quizError, type QuizzesClient } from '../api/quizzesApi.ts'
import type { QuizAttempt } from '../types/quizAttempt.ts'
import { createQuizHistoryState } from './quizReadState.ts'
import { QuizResult } from './QuizResult.tsx'

function AttemptDetail({ mapId, nodeId, quizId, attemptId, client, revalidate }: Props & { attemptId: string }) {
  const [loaded, setLoaded] = useState<QuizAttempt | null>(null), [message, setMessage] = useState(''), [retry, setRetry] = useState(0)
  const heading = useRef<HTMLHeadingElement>(null)
  useEffect(() => {
    const controller = new AbortController(); let current = true
    void client.lookup(mapId, nodeId, quizId, attemptId, controller.signal).then(result => { if (current) setLoaded(result) }, async (error: unknown) => {
      if (!current || controller.signal.aborted) return
      if (error instanceof ApiError && error.status === 401) { try { await revalidate() } catch { /* Explicit retry. */ } }
      if (current) { setMessage(quizError(error)); heading.current?.focus() }
    })
    return () => { current = false; controller.abort() }
  }, [mapId, nodeId, quizId, attemptId, client, revalidate, retry])
  return <section aria-label="Detalhe da tentativa">
    {loaded ? <QuizResult result={loaded} /> : <><h4 tabIndex={-1} ref={heading}>Detalhe histórico</h4><p role="status">{message || 'Carregando resultado histórico…'}</p>
      {message && <button onClick={() => { setMessage(''); setRetry(v => v + 1) }}>Tentar carregar resultado novamente</button>}</>}
  </section>
}
type Props = { mapId: string; nodeId: string; quizId: string; client: QuizzesClient; revalidate: () => Promise<void> }
export function QuizHistory(props: Props) {
  const { mapId, nodeId, quizId, client, revalidate } = props
  const [history] = useState(() => createQuizHistoryState(client, mapId, nodeId, quizId, revalidate))
  const state = useSyncExternalStore(history.subscribe, history.getSnapshot)
  const [selected, setSelected] = useState<string | null>(null)
  useEffect(() => { history.mount(); void history.reload(); return () => history.dispose() }, [history])
  return <section aria-label="Histórico de tentativas"><h4>Histórico de tentativas</h4>
    <p role="status">{state.status === 'loading' || state.loadingMore ? 'Carregando tentativas…' : state.message}</p>
    <div aria-busy={state.status === 'loading' || state.loadingMore}>
      {state.status === 'ready' && (state.items.length === 0 ? <p>Ainda não há tentativas deste quiz.</p> : <ol className="quiz-list">{state.items.map(a => <li key={a.attemptId}>
        <time dateTime={a.submittedAtUtc}>{new Date(a.submittedAtUtc).toLocaleString()}</time><p>{a.correctCount}/{a.questionCount} — {a.accuracyPercentage.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}% · Revisão {a.contentRevision}</p>
        <button onClick={() => setSelected(a.attemptId)}>Abrir tentativa de {new Date(a.submittedAtUtc).toLocaleString()}</button>
      </li>)}</ol>)}
      {state.hasMore && <button disabled={state.loadingMore} onClick={() => { void history.more() }}>Carregar mais tentativas</button>}
      <button disabled={state.loadingMore} onClick={() => { setSelected(null); void history.reload() }}>Atualizar tentativas</button>
    </div>
    {selected && <AttemptDetail key={selected} {...props} attemptId={selected} />}
  </section>
}
