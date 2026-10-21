import { useEffect, useRef, useState, useSyncExternalStore } from 'react'
import type { FlashcardsClient } from '../api/flashcardsApi.ts'
import { createFlashcardStudyState } from './flashcardStudyState.ts'

export function FlashcardStudy({ mapId, nodeId, client, revalidate }:
  { mapId: string; nodeId: string; client: FlashcardsClient; revalidate: () => Promise<void> }) {
  const [flow] = useState(() => createFlashcardStudyState(client, mapId, nodeId, revalidate))
  const state = useSyncExternalStore(flow.subscribe, flow.getSnapshot)
  const focus = useRef<HTMLHeadingElement>(null)
  useEffect(() => { flow.mount(); return () => flow.dispose() }, [flow])
  useEffect(() => { if (state.status === 'prompt' || state.status === 'revealed') focus.current?.focus() }, [state])
  const busy = ['loading', 'revealing', 'locking', 'submitting', 'checking'].includes(state.status)
  return <section aria-labelledby="flashcard-study-heading" className="flashcard-section">
    <h3 id="flashcard-study-heading">Estudar flashcards</h3>
    <p role="status">{state.message || (state.status === 'idle' ? 'Revele a resposta quando estiver pronto e avalie sua lembrança.' : '')}</p>
    <div aria-busy={busy}>
      {(state.status === 'idle' || state.status === 'empty' || state.status === 'error' || state.status === 'conflict') &&
        <button onClick={() => { void flow.start() }}>{state.status === 'idle' ? 'Estudar flashcards deste conceito' : 'Carregar flashcards novamente'}</button>}
      {(state.status === 'prompt' || state.status === 'revealing' || state.status === 'revealed') && <article>
        <h4 tabIndex={-1} ref={state.status === 'revealed' ? undefined : focus}>Pergunta</h4>
        <p className="flashcard-text">{state.item.front}</p>
        <p>Revisão {state.item.contentRevision}: {state.item.reviewState.reviewCount} respostas, {state.item.reviewState.correctCount} corretas.</p>
        {(state.status === 'prompt' || state.status === 'revealing') && <button disabled={busy} onClick={() => { void flow.reveal() }}>Revelar resposta</button>}
        {state.status === 'revealed' && <>
          <h4 tabIndex={-1} ref={focus}>Resposta</h4><p className="flashcard-text">{state.back}</p>
          <div className="editor-actions"><button onClick={() => { void flow.answer('correct') }}>Lembrei corretamente</button>
            <button onClick={() => { void flow.answer('incorrect') }}>Não lembrei</button></div>
        </>}
      </article>}
      {state.status === 'uncertain' && <div className="editor-actions">
        <button onClick={() => { void flow.check() }}>Conferir resposta novamente</button>
        {state.canRetry && <button onClick={() => { void flow.retry() }}>Reenviar explicitamente a mesma resposta</button>}
      </div>}
    </div>
    {state.status !== 'idle' && <button onClick={flow.stop}>Encerrar estudo de flashcards</button>}
  </section>
}
