import { useEffect, useRef, useState, useSyncExternalStore } from 'react'
import { ApiError } from '../api/http.ts'
import { quizError, type QuizzesClient } from '../api/quizzesApi.ts'
import type { QuizDefinition, QuizInput, QuizSummary } from '../types/quiz.ts'
import type { useAuthoring } from '../components/useAuthoring.ts'
import { createQuizListState } from './quizReadState.ts'
import { definitionInput } from './quizInput.ts'
import { QuizForm } from './QuizForm.tsx'

type Props = { mapId: string; nodeId: string; client: QuizzesClient; mutation: ReturnType<typeof useAuthoring>; revalidate: () => Promise<void> }
function QuizEdit({ mapId, nodeId, client, mutation, revalidate, quiz, onDone, onCancel }: Props & { quiz: QuizSummary; onDone: () => void; onCancel: () => void }) {
  const [loaded, setLoaded] = useState<QuizDefinition | null>(null), [message, setMessage] = useState(''), [reload, setReload] = useState(0)
  const [status, setStatus] = useState<'loading' | 'ready' | 'error' | 'changed'>('loading')
  const alive = useRef(false), focus = useRef<HTMLHeadingElement>(null)
  useEffect(() => {
    alive.current = true; const controller = new AbortController(); let current = true
    void client.detail(mapId, nodeId, quiz.id, controller.signal).then(value => {
      if (current) { setLoaded(value); setStatus('ready'); setMessage('') }
    }, async (error: unknown) => {
      if (!current || controller.signal.aborted) return
      if (error instanceof ApiError && error.status === 401) { try { await revalidate() } catch { /* Explicit retry. */ } }
      if (current) { setStatus('error'); setMessage(quizError(error)) }
    })
    return () => { current = false; alive.current = false; controller.abort() }
  }, [client, mapId, nodeId, quiz.id, revalidate, reload])
  useEffect(() => { if (status === 'changed' || status === 'error') focus.current?.focus() }, [status])
  function save(input: QuizInput) {
    if (mutation.isPending() || status !== 'ready' || !loaded) return
    void mutation.run(signal => client.replace(mapId, nodeId, quiz.id, loaded.contentRevision, input, signal), () => { if (alive.current) onDone() },
      { errorMessage: quizError, onError(error) {
        if (!alive.current) return
        setMessage(quizError(error)); focus.current?.focus()
        if (error instanceof ApiError && error.code === 'quiz_changed') { setLoaded(null); setStatus('changed') }
      } })
  }
  return <section aria-label="Editar definição do quiz">
    <h4 ref={focus} tabIndex={-1}>Definição atual do quiz</h4><p role="status">{status === 'loading' ? 'Carregando definição…' : message}</p>
    {status === 'ready' && loaded && <QuizForm key={`${loaded.id}:${loaded.contentRevision}`} initial={definitionInput(loaded)} pending={mutation.pending} onSave={save} onCancel={onCancel} />}
    {(status === 'error' || status === 'changed') && <><button disabled={mutation.pending} onClick={() => { setStatus('loading'); setLoaded(null); setReload(v => v + 1) }}>Carregar definição atual</button><button disabled={mutation.pending} onClick={onCancel}>Cancelar quiz</button></>}
  </section>
}
export function QuizAuthoring(props: Props) {
  const { mapId, nodeId, client, mutation, revalidate } = props
  const [list] = useState(() => createQuizListState(client, mapId, nodeId, revalidate))
  const state = useSyncExternalStore(list.subscribe, list.getSnapshot)
  const [editor, setEditor] = useState<'create' | { kind: 'edit' | 'delete'; quiz: QuizSummary; visit: number } | null>(null)
  const alive = useRef(false), visit = useRef(0), heading = useRef<HTMLHeadingElement>(null)
  const [message, setMessage] = useState('')
  useEffect(() => { alive.current = true; list.mount(); void list.reload(); return () => { alive.current = false; list.dispose() } }, [list])
  function done() { if (alive.current) { setEditor(null); setMessage('Quiz salvo. Lista atualizada a partir do servidor.'); void list.reload(); heading.current?.focus() } }
  function cancel() { if (!mutation.isPending()) { setEditor(null); heading.current?.focus() } }
  function create(input: QuizInput) {
    if (mutation.isPending() || editor !== 'create') return
    void mutation.run(signal => client.create(mapId, nodeId, input, signal), done, { errorMessage: quizError,
      onError(error) { if (alive.current) { setMessage(quizError(error)); heading.current?.focus() } } })
  }
  return <section className="quiz-section" aria-labelledby="quiz-authoring-heading">
    <h3 id="quiz-authoring-heading" tabIndex={-1} ref={heading}>Quizzes do conceito</h3>
    <p role="status">{state.status === 'loading' || state.loadingMore ? 'Carregando quizzes…' : state.message || message || 'Lista de quizzes atualizada.'}</p>
    <div aria-busy={state.status === 'loading' || state.loadingMore}>
      {state.status === 'ready' && (state.items.length === 0 ? <p>Ainda não há quizzes neste conceito.</p> : <ol className="quiz-list">{state.items.map(q => <li key={q.id}>
        <strong>{q.title}</strong><p>Revisão {q.contentRevision}</p><div className="editor-actions">
          <button disabled={mutation.pending || state.loadingMore} onClick={() => { if (!mutation.isPending()) { setMessage(''); setEditor({ kind: 'edit', quiz: q, visit: ++visit.current }) } }}>Editar quiz {q.title}</button>
          <button disabled={mutation.pending || state.loadingMore} onClick={() => { if (!mutation.isPending()) { setMessage(''); setEditor({ kind: 'delete', quiz: q, visit: ++visit.current }) } }}>Excluir quiz {q.title}</button>
        </div></li>)}</ol>)}
      {state.hasMore && <button disabled={mutation.pending || state.loadingMore} onClick={() => { void list.more() }}>Carregar mais quizzes</button>}
      <div className="editor-actions"><button disabled={mutation.pending || state.loadingMore} onClick={() => { if (!mutation.isPending()) { setEditor('create'); setMessage('') } }}>Adicionar quiz</button>
        <button disabled={mutation.pending || state.loadingMore} onClick={() => { if (!mutation.isPending()) { setEditor(null); setMessage(''); void list.reload() } }}>Atualizar quizzes</button></div>
    </div>
    {editor === 'create' && <QuizForm pending={mutation.pending} onSave={create} onCancel={cancel} />}
    {editor && editor !== 'create' && editor.kind === 'edit' && <QuizEdit key={`${editor.quiz.id}:${editor.visit}`} {...props} quiz={editor.quiz} onDone={done} onCancel={cancel} />}
    {editor && editor !== 'create' && editor.kind === 'delete' && <section aria-label="Confirmar exclusão do quiz">
      <h4>Excluir permanentemente {editor.quiz.title}?</h4><p>O quiz e todo o histórico de tentativas e resultados serão removidos permanentemente.</p>
      <div className="editor-actions"><button disabled={mutation.pending} onClick={() => {
        if (!mutation.isPending()) void mutation.run(signal => client.remove(mapId, nodeId, editor.quiz.id, signal), () => {
          done(); if (alive.current) setMessage('Quiz e histórico excluídos. Lista atualizada a partir do servidor.')
        },
          { errorMessage: quizError, onError(error) { if (alive.current) { setMessage(quizError(error)); heading.current?.focus() } } })
      }}>Confirmar exclusão do quiz</button><button disabled={mutation.pending} onClick={cancel}>Cancelar exclusão do quiz</button></div>
    </section>}
  </section>
}
