import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../api/http.ts'
import { contentLabel, getContent, listContents, reconcileSelectedContent } from '../api/knowledgeContentsApi.ts'
import { contentPatch, contentWriteError, type KnowledgeContentAuthoringClient } from '../api/knowledgeContentAuthoringApi.ts'
import type { ContentInput, ContentSummary } from '../types/knowledgeContent.ts'
import { ContentForm } from './ContentForm.tsx'
import { useContentRead } from './useContentRead.ts'
import type { useAuthoring } from './useAuthoring.ts'

export function ContentSection({ mapId, nodeId, nodeTitle, editing, client, mutation, revalidate }:
  { mapId: string; nodeId: string; nodeTitle: string; editing: boolean; client: KnowledgeContentAuthoringClient;
    mutation: ReturnType<typeof useAuthoring>; revalidate: () => Promise<void> }) {
  const [selected, setSelected] = useState<string | null>(null)
  const [editor, setEditor] = useState<'create' | 'edit' | 'delete' | null>(null)
  const [focusRevision, setFocusRevision] = useState(0)
  const [reconciledList, setReconciledList] = useState<ContentSummary[] | null>(null)
  const alive = useRef(false)
  const heading = useRef<HTMLHeadingElement>(null)
  useEffect(() => { alive.current = true; return () => { alive.current = false } }, [])
  useEffect(() => { if (focusRevision > 0) heading.current?.focus() }, [focusRevision])
  const loadList = useCallback((signal: AbortSignal) => listContents(mapId, nodeId, signal), [mapId, nodeId])
  const loadDetail = useCallback((signal: AbortSignal) => getContent(mapId, nodeId, selected!, signal), [mapId, nodeId, selected])
  const list = useContentRead(`${mapId}:${nodeId}`, loadList, revalidate)
  const detail = useContentRead(selected, loadDetail, revalidate)
  // Only reconcile a newly applied current list, never the previous snapshot during create/reload.
  // Adjust local state before commit so obsolete detail/editor cannot flash in the DOM.
  if (list.state.status === 'ready' && list.state.data !== reconciledList) {
    setReconciledList(list.state.data)
    const nextSelected = reconcileSelectedContent(selected, list.state.data)
    if (nextSelected !== selected) {
      setSelected(nextSelected)
      if (editor !== 'create') setEditor(null)
    }
  }
  function focusHeading() { setFocusRevision((value) => value + 1) }
  function refresh() {
    if (mutation.isPending()) return
    setEditor(null); list.reload(); if (selected) detail.reload()
  }
  const errors = { errorMessage: contentWriteError, onError: (error: unknown) => {
    if (alive.current && error instanceof ApiError && error.status === 404) {
      setSelected(null); setEditor(null); list.reload(); focusHeading()
    }
  } }
  function save(input: ContentInput) {
    if (!editing || mutation.isPending()) return
    if (editor === 'create') {
      void mutation.run((signal) => client.createContent(mapId, nodeId, input, signal), (created) => {
        if (!alive.current) return
        setEditor(null); setSelected(created.id); list.reload(); focusHeading()
      }, errors)
    } else if (editor === 'edit' && selected && detail.state.status === 'ready') {
      const patch = contentPatch(detail.state.data, input)
      if (!Object.keys(patch).length) return
      void mutation.run((signal) => client.updateContent(mapId, nodeId, selected, patch, signal), () => {
        if (!alive.current) return
        setEditor(null); list.reload(); detail.reload(); focusHeading()
      }, errors)
    }
  }
  function remove() {
    if (!editing || editor !== 'delete' || !selected || mutation.isPending()) return
    void mutation.run((signal) => client.deleteContent(mapId, nodeId, selected, signal), () => {
      if (!alive.current) return
      setSelected(null); setEditor(null); list.reload(); focusHeading()
    }, errors)
  }
  const listState = list.state
  const detailState = detail.state
  return <section className="content-section" aria-labelledby="content-heading">
    <h3 id="content-heading" tabIndex={-1} ref={heading}>Conteúdos de {nodeTitle}</h3>
    <p role="status" className={listState.status === 'loading' ? '' : 'sr-only'}>
      {listState.status === 'loading' ? 'Carregando conteúdos…' : listState.status === 'ready' ? 'Lista de conteúdos carregada.' : listState.message}
    </p>
    <div aria-busy={listState.status === 'loading'}>
      {listState.status === 'error' && <div><p className="session-feedback">{listState.message}</p>
        <button disabled={mutation.pending} onClick={list.reload}>Tentar carregar conteúdos novamente</button></div>}
      {listState.status === 'ready' && <>
        {listState.data.length === 0 ? <p>Ainda não há conteúdo.</p> : <ul className="content-list">
          {listState.data.map((content) => <li key={content.id}><button disabled={mutation.pending} aria-pressed={selected === content.id}
            onClick={() => { if (!mutation.isPending()) { setSelected(content.id); setEditor(null) } }}>
            <span className="content-kind">{content.type === 'note' ? 'Nota' : 'Link'}</span> {contentLabel(content)}
          </button></li>)}
        </ul>}
        <div className="editor-actions"><button disabled={mutation.pending} onClick={refresh}>Atualizar conteúdos</button>
          {editing && <button disabled={mutation.pending} onClick={() => { if (!mutation.isPending()) setEditor('create') }}>Adicionar conteúdo</button>}</div>
      </>}
    </div>
    {selected && editor !== 'create' && <>
      <p role="status" className={detailState.status === 'loading' ? '' : 'sr-only'}>
        {detailState.status === 'loading' ? 'Carregando detalhe…' : detailState.status === 'ready' ? 'Detalhe carregado.' : detailState.message}
      </p>
      <div aria-busy={detailState.status === 'loading'}>
        {detailState.status === 'error' && <div><p className="session-feedback">{detailState.message}</p>
          <button disabled={mutation.pending} onClick={detail.reload}>Tentar carregar detalhe novamente</button></div>}
        {detailState.status === 'ready' && <article className="content-detail" aria-label="Detalhe do conteúdo">
          <h4>{contentLabel(detailState.data)}</h4>
          {detailState.data.type === 'note' ? <p className="content-body">{detailState.data.body}</p>
            : <a href={detailState.data.url} target="_blank" rel="noopener noreferrer" className="content-url"
              aria-label={`Abrir link em nova aba: ${contentLabel(detailState.data)} — ${detailState.data.url}`}>{detailState.data.url}</a>}
          {editing && <div className="editor-actions">
            <button disabled={mutation.pending} onClick={() => { if (!mutation.isPending()) setEditor('edit') }}>Editar conteúdo</button>
            <button disabled={mutation.pending} onClick={() => { if (!mutation.isPending()) setEditor('delete') }}>Excluir conteúdo</button>
          </div>}
          {editing && editor === 'edit' && <ContentForm key={selected} initial={detailState.data} pending={mutation.pending}
            onSave={save} onCancel={() => { if (!mutation.isPending()) setEditor(null) }} />}
          {editing && editor === 'delete' && <section aria-label="Confirmar exclusão de conteúdo">
            <h4>Excluir {detailState.data.type === 'note' ? 'nota' : 'link'} “{contentLabel(detailState.data)}”?</h4>
            <p>A exclusão é permanente.</p><div className="editor-actions">
              <button disabled={mutation.pending} onClick={remove}>Confirmar exclusão do conteúdo</button>
              <button disabled={mutation.pending} onClick={() => { if (!mutation.isPending()) setEditor(null) }}>Cancelar exclusão do conteúdo</button>
            </div></section>}
        </article>}
      </div>
    </>}
    {editing && editor === 'create' && <ContentForm key="create" pending={mutation.pending} onSave={save}
      onCancel={() => { if (!mutation.isPending()) setEditor(null) }} />}
  </section>
}
