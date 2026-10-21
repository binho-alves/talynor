import { useEffect, useRef, useState, useSyncExternalStore } from 'react'
import { flashcardError, flashcardInput, flashcardPatch, type FlashcardsClient } from '../api/flashcardsApi.ts'
import type { Flashcard, FlashcardInput } from '../types/flashcard.ts'
import type { useAuthoring } from '../components/useAuthoring.ts'
import { createFlashcardListState } from './flashcardListState.ts'

export function FlashcardForm({ initial, pending, onSave, onCancel }:
  { initial?: Flashcard; pending: boolean; onSave: (input: FlashcardInput) => void; onCancel: () => void }) {
  const [front, setFront] = useState(initial?.front ?? ''), [back, setBack] = useState(initial?.back ?? '')
  const input = flashcardInput(front, back)
  const changed = !initial || !!Object.keys(flashcardPatch(initial, { front, back })).length
  return <form className="authoring-form" onSubmit={event => { event.preventDefault(); if (input && changed && !pending) onSave(input) }}>
    <h4>{initial ? 'Editar flashcard' : 'Novo flashcard'}</h4><fieldset disabled={pending}>
      <label htmlFor="flashcard-front">Pergunta</label><textarea id="flashcard-front" required maxLength={2000} value={front} onChange={e => setFront(e.target.value)} aria-describedby="flashcard-limits" />
      <label htmlFor="flashcard-back">Resposta</label><textarea id="flashcard-back" required maxLength={4000} value={back} onChange={e => setBack(e.target.value)} aria-describedby="flashcard-limits" />
      <p id="flashcard-limits">Pergunta: {front.length}/2000; resposta: {back.length}/4000 unidades UTF-16. Textos somente em branco não são válidos.</p>
      <div className="editor-actions"><button type="submit" disabled={!input || !changed}>Salvar flashcard</button>
        <button type="button" onClick={onCancel}>Cancelar flashcard</button></div>
    </fieldset>
  </form>
}

export function FlashcardAuthoring({ mapId, nodeId, client, mutation, revalidate }:
  { mapId: string; nodeId: string; client: FlashcardsClient; mutation: ReturnType<typeof useAuthoring>; revalidate: () => Promise<void> }) {
  const [list] = useState(() => createFlashcardListState(client, mapId, nodeId, revalidate))
  const state = useSyncExternalStore(list.subscribe, list.getSnapshot)
  const [editor, setEditor] = useState<'create' | { kind: 'edit' | 'delete'; card: Flashcard } | null>(null)
  const heading = useRef<HTMLHeadingElement>(null), alive = useRef(false)
  useEffect(() => { alive.current = true; list.mount(); void list.reload(); return () => { alive.current = false; list.dispose() } }, [list])
  function done() { if (alive.current) { setEditor(null); void list.reload(); heading.current?.focus() } }
  function save(input: FlashcardInput) {
    if (mutation.isPending()) return
    if (editor === 'create') void mutation.run(signal => client.create(mapId, nodeId, input, signal), done, { errorMessage: flashcardError })
    else if (editor?.kind === 'edit') {
      const patch = flashcardPatch(editor.card, input)
      if (!Object.keys(patch).length) return
      void mutation.run(signal => client.update(mapId, nodeId, editor.card.id, patch, signal), done, { errorMessage: flashcardError })
    }
  }
  return <section className="flashcard-section" aria-labelledby="flashcard-authoring-heading">
    <h3 id="flashcard-authoring-heading" tabIndex={-1} ref={heading}>Flashcards do conceito</h3>
    <p role="status">{state.status === 'loading' || state.loadingMore ? 'Carregando flashcards…' : state.message || 'Lista de flashcards atualizada.'}</p>
    <div aria-busy={state.status === 'loading' || state.loadingMore}>
      {state.status === 'error' && <button disabled={mutation.pending} onClick={() => { void list.reload() }}>Tentar carregar flashcards novamente</button>}
      {state.status === 'ready' && <>
        {state.cards.length === 0 ? <p>Ainda não há flashcards neste conceito.</p> : <ol className="flashcard-list">{state.cards.map(card => <li key={card.id}>
          <h4>Pergunta</h4><p className="flashcard-text">{card.front}</p><h4>Resposta</h4><p className="flashcard-text">{card.back}</p>
          <div className="editor-actions"><button disabled={mutation.pending || state.loadingMore} onClick={() => { if (!mutation.isPending()) setEditor({ kind: 'edit', card }) }}>Editar flashcard</button>
            <button disabled={mutation.pending || state.loadingMore} onClick={() => { if (!mutation.isPending()) setEditor({ kind: 'delete', card }) }}>Excluir flashcard</button></div>
        </li>)}</ol>}
        {state.hasMore && <button disabled={state.loadingMore || mutation.pending} onClick={() => { void list.more() }}>Carregar mais flashcards</button>}
        <div className="editor-actions"><button disabled={mutation.pending || state.loadingMore} onClick={() => { if (!mutation.isPending()) setEditor('create') }}>Adicionar flashcard</button>
          <button disabled={mutation.pending || state.loadingMore} onClick={() => { if (!mutation.isPending()) { setEditor(null); void list.reload() } }}>Atualizar flashcards</button></div>
      </>}
    </div>
    {editor === 'create' && <FlashcardForm pending={mutation.pending} onSave={save} onCancel={() => setEditor(null)} />}
    {editor && editor !== 'create' && editor.kind === 'edit' && <FlashcardForm key={editor.card.id} initial={editor.card} pending={mutation.pending} onSave={save} onCancel={() => setEditor(null)} />}
    {editor && editor !== 'create' && editor.kind === 'delete' && <section aria-label="Confirmar exclusão de flashcard">
      <p>Excluir permanentemente este flashcard e todo o seu histórico de respostas?</p><p className="flashcard-text">{editor.card.front}</p>
      <div className="editor-actions"><button disabled={mutation.pending} onClick={() => {
        if (!mutation.isPending()) void mutation.run(signal => client.remove(mapId, nodeId, editor.card.id, signal), done, { errorMessage: flashcardError })
      }}>Confirmar exclusão do flashcard</button><button disabled={mutation.pending} onClick={() => setEditor(null)}>Cancelar exclusão do flashcard</button></div>
    </section>}
  </section>
}
