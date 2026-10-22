import { useEffect, useRef, useState, useSyncExternalStore } from 'react'
import type { QuizzesClient } from '../api/quizzesApi.ts'
import { completeQuizAnswers, createQuizTakingState } from './quizTakingState.ts'
import { QuizResult } from './QuizResult.tsx'

export function QuizTaking({ mapId, nodeId, quizId, client, revalidate }:
  { mapId: string; nodeId: string; quizId: string; client: QuizzesClient; revalidate: () => Promise<void> }) {
  const [flow] = useState(() => createQuizTakingState(client, mapId, nodeId, quizId, revalidate))
  const state = useSyncExternalStore(flow.subscribe, flow.getSnapshot)
  const heading = useRef<HTMLHeadingElement>(null)
  useEffect(() => { flow.mount(); void flow.start(); return () => flow.dispose() }, [flow])
  useEffect(() => { if (['ready', 'changed', 'conflict', 'error', 'uncertain'].includes(state.status)) heading.current?.focus() }, [state.status])
  const busy = ['loading', 'locking', 'submitting', 'checking'].includes(state.status)
  return <section aria-label="Responder quiz">
    <h4 tabIndex={-1} ref={heading}>{state.status === 'ready' ? state.take.title : 'Responder quiz'}</h4>
    <p role="status">{state.message}</p>
    <div aria-busy={busy}>
      {state.status === 'ready' && <form onSubmit={e => { e.preventDefault(); void flow.submit() }}>
        <p>Responda todas as perguntas. Suas escolhas ficam apenas nesta visita; sair descarta o rascunho.</p>
        {state.take.questions.map(q => <fieldset key={q.id} className="quiz-question"><legend className="quiz-text">{q.position}. {q.text}</legend>
          {q.options.map(o => <label key={o.id} className="quiz-radio quiz-text"><input type="radio" name={`quiz-${q.id}`} required
            checked={state.selections[q.id] === o.id} onChange={() => flow.select(q.id, o.id)} />{o.text}</label>)}
        </fieldset>)}
        {!completeQuizAnswers(state.take, state.selections) && <p>Responda todas as perguntas para habilitar o envio.</p>}
        <button type="submit" disabled={!completeQuizAnswers(state.take, state.selections)}>Enviar tentativa</button>
      </form>}
      {state.status === 'result' && <QuizResult result={state.result} />}
      {state.status === 'uncertain' && <div className="editor-actions"><button onClick={() => { void flow.check() }}>Conferir tentativa novamente</button>
        {state.canRetry && <button onClick={() => { void flow.retry() }}>Reenviar explicitamente a mesma tentativa</button>}</div>}
      {state.status === 'error' && state.pending && <button onClick={() => { void flow.check() }}>Verificar resultado deste envio</button>}
      {['error', 'changed', 'conflict', 'result', 'idle'].includes(state.status) && <button onClick={() => { void flow.start() }}>
        {state.status === 'result' ? 'Responder novamente' : 'Carregar quiz novamente'}</button>}
    </div>
  </section>
}
