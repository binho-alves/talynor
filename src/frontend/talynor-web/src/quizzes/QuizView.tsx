import { useEffect, useState, useSyncExternalStore } from 'react'
import type { QuizzesClient } from '../api/quizzesApi.ts'
import { createQuizListState } from './quizReadState.ts'
import { QuizTaking } from './QuizTaking.tsx'
import { QuizHistory } from './QuizHistory.tsx'

export function QuizView({ mapId, nodeId, client, revalidate }:
  { mapId: string; nodeId: string; client: QuizzesClient; revalidate: () => Promise<void> }) {
  const [list] = useState(() => createQuizListState(client, mapId, nodeId, revalidate))
  const state = useSyncExternalStore(list.subscribe, list.getSnapshot)
  const [selected, setSelected] = useState<{ id: string; mode: 'take' | 'history'; visit: number } | null>(null)
  useEffect(() => { list.mount(); void list.reload(); return () => list.dispose() }, [list])
  return <section className="quiz-section" aria-labelledby="quiz-view-heading"><h3 id="quiz-view-heading" tabIndex={-1}>Quizzes do conceito</h3>
    <p role="status">{state.status === 'loading' || state.loadingMore ? 'Carregando quizzes…' : state.message}</p>
    <div aria-busy={state.status === 'loading' || state.loadingMore}>
      {state.status === 'ready' && (state.items.length === 0 ? <p>Nenhum quiz disponível neste conceito.</p> : <ol className="quiz-list">{state.items.map(q => <li key={q.id}>
        <strong>{q.title}</strong><div className="editor-actions">
          <button onClick={() => setSelected(old => ({ id: q.id, mode: 'take', visit: (old?.visit ?? 0) + 1 }))}>Responder {q.title}</button>
          <button onClick={() => setSelected(old => ({ id: q.id, mode: 'history', visit: (old?.visit ?? 0) + 1 }))}>Histórico de {q.title}</button>
        </div></li>)}</ol>)}
      {state.hasMore && <button disabled={state.loadingMore} onClick={() => { void list.more() }}>Carregar mais quizzes</button>}
      <button disabled={state.loadingMore} onClick={() => { setSelected(null); void list.reload() }}>Atualizar quizzes</button>
    </div>
    {selected && <div key={`${selected.id}:${selected.mode}:${selected.visit}`}>
      {selected.mode === 'take' ? <QuizTaking mapId={mapId} nodeId={nodeId} quizId={selected.id} client={client} revalidate={revalidate} />
        : <QuizHistory mapId={mapId} nodeId={nodeId} quizId={selected.id} client={client} revalidate={revalidate} />}
      <button onClick={() => { setSelected(null); document.getElementById('quiz-view-heading')?.focus() }}>Fechar quiz</button>
    </div>}
  </section>
}
