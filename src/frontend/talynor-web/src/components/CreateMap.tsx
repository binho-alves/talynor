import { useState } from 'react'
import type { CreatedMap, KnowledgeMapAuthoringClient } from '../api/knowledgeMapAuthoringApi.ts'
import { MetadataForm } from './MetadataForm.tsx'
import { useAuthoring } from './useAuthoring.ts'

export function CreateMap({ authoring, revalidate, onCreated }:
  { authoring: KnowledgeMapAuthoringClient; revalidate: () => Promise<void>; onCreated: (map: CreatedMap) => void }) {
  const [open, setOpen] = useState(false)
  const mutation = useAuthoring(revalidate)
  return <section className="map-message" aria-label="Criar mapa">
    <button disabled={mutation.pending} onClick={() => setOpen(true)}>Criar mapa</button>
    <p id="authoring-feedback" role="status" className={mutation.feedback ? 'mutation-feedback' : 'sr-only'}>{mutation.feedback}</p>
    {open && <MetadataForm heading="Novo mapa" initial={{ title: '', description: null }} pending={mutation.pending}
      onCancel={() => setOpen(false)} onSave={(fields) => {
        void mutation.run((signal) => authoring.createMap(fields, signal), onCreated)
      }} />}
  </section>
}
