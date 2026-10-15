import { useId, useState } from 'react'
import type { MapFields } from '../api/knowledgeMapAuthoringApi.ts'
import type { KnowledgeMapSummaryDto } from '../types/persistedKnowledgeMap.ts'

export function MetadataForm({ heading, initial, pending, onSave, onCancel, childMaps, childMapId = null }:
  { heading: string; initial: MapFields; pending: boolean; onSave: (fields: MapFields, child: string | null) => void; onCancel: () => void;
    childMaps?: KnowledgeMapSummaryDto[]; childMapId?: string | null }) {
  const id = useId()
  const [title, setTitle] = useState(initial.title)
  const [description, setDescription] = useState(initial.description ?? '')
  const [child, setChild] = useState(childMapId ?? '')
  const [error, setError] = useState('')
  return <form className="authoring-form" aria-labelledby={`${id}-heading`} aria-describedby="authoring-feedback" onSubmit={(event) => {
    event.preventDefault()
    if (pending) return
    if (!title.trim() || title.trim().length > 200 || description.length > 4000) { setError('Informe título (até 200 caracteres) e descrição de até 4000 caracteres.'); return }
    setError('')
    onSave({ title: title.trim(), description: description === '' ? null : description }, child || null)
  }}>
    <h3 id={`${id}-heading`}>{heading}</h3>
    <fieldset disabled={pending}>
      <label htmlFor={`${id}-title`}>Título</label>
      <input id={`${id}-title`} value={title} onChange={(e) => setTitle(e.target.value)} required maxLength={200} aria-describedby={error ? `${id}-error` : undefined} />
      <label htmlFor={`${id}-description`}>Descrição (opcional)</label>
      <textarea id={`${id}-description`} value={description} onChange={(e) => setDescription(e.target.value)} maxLength={4000} rows={4} />
      {childMaps && <><label htmlFor={`${id}-child`}>Submapa</label><select id={`${id}-child`} value={child} onChange={(e) => setChild(e.target.value)}>
        <option value="">Nenhum submapa</option>
        {child && !childMaps.some((m) => m.id === child) && <option value={child}>Submapa atual (indisponível na lista)</option>}
        {childMaps.map((m) => <option key={m.id} value={m.id}>{m.title}</option>)}
      </select></>}
      {error && <p id={`${id}-error`} role="alert" className="session-feedback">{error}</p>}
      <div className="editor-actions"><button type="submit">Salvar</button><button type="button" onClick={onCancel}>Cancelar</button></div>
    </fieldset>
  </form>
}
