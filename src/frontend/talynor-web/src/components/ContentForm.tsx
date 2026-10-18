import { useId, useState } from 'react'
import { contentInput, contentPatch } from '../api/knowledgeContentAuthoringApi.ts'
import type { ContentDetail, ContentInput } from '../types/knowledgeContent.ts'

export function ContentForm({ initial, pending, onSave, onCancel }:
  { initial?: ContentDetail; pending: boolean; onSave: (input: ContentInput) => void; onCancel: () => void }) {
  const id = useId()
  const [type, setType] = useState<'note' | 'link'>(initial?.type ?? 'note')
  const [title, setTitle] = useState(initial?.title ?? '')
  const [value, setValue] = useState(initial ? initial.type === 'note' ? initial.body : initial.url : '')
  const [error, setError] = useState('')
  const input = contentInput(type, title, value)
  const noop = !!initial && !!input && Object.keys(contentPatch(initial, input)).length === 0
  return <form className="authoring-form" aria-labelledby={`${id}-heading`} aria-describedby="authoring-feedback" onSubmit={(event) => {
    event.preventDefault()
    if (pending || noop) return
    if (!input) { setError('Confira o título opcional (até 200 caracteres), a nota obrigatória (até 4000) ou a URL HTTP(S) (até 2048).'); return }
    setError(''); onSave(input)
  }}>
    <h4 id={`${id}-heading`}>{initial ? 'Editar conteúdo' : 'Novo conteúdo'}</h4>
    <fieldset disabled={pending}>
      {initial ? <p>Tipo: {type === 'note' ? 'Nota' : 'Link'} (não editável)</p>
        : <><label htmlFor={`${id}-type`}>Tipo de conteúdo</label><select id={`${id}-type`} value={type} onChange={(event) => {
          setType(event.target.value as 'note' | 'link'); setValue(''); setError('')
        }}><option value="note">Nota</option><option value="link">Link</option></select></>}
      <label htmlFor={`${id}-title`}>Título do conteúdo (opcional)</label>
      <input id={`${id}-title`} value={title} onChange={(event) => setTitle(event.target.value)} maxLength={200} />
      <label htmlFor={`${id}-value`}>{type === 'note' ? 'Texto da nota' : 'URL do link'}</label>
      {type === 'note' ? <textarea id={`${id}-value`} value={value} onChange={(event) => setValue(event.target.value)} required maxLength={4000} rows={7} />
        : <input id={`${id}-value`} type="text" inputMode="url" value={value} onChange={(event) => setValue(event.target.value)} required maxLength={2048} />}
      {error && <p role="alert" className="session-feedback">{error}</p>}
      {noop && <p>Nenhuma alteração para salvar.</p>}
      <div className="editor-actions"><button type="submit" disabled={noop}>Salvar conteúdo</button>
        <button type="button" onClick={onCancel}>Cancelar conteúdo</button></div>
    </fieldset>
  </form>
}
