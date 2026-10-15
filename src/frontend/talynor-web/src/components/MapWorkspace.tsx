import { useEffect, useRef, useState } from 'react'
import { applyNodeChanges, type EdgeChange, type NodeChange } from '@xyflow/react'
import { metadataPatch, type MapFields, type NodePatch, type KnowledgeMapAuthoringClient } from '../api/knowledgeMapAuthoringApi.ts'
import { ApiError } from '../api/http.ts'
import { listPersistedKnowledgeMaps } from '../api/knowledgeMapsApi.ts'
import type { KnowledgeMap, KnowledgeNode } from '../types/knowledge.ts'
import type { KnowledgeMapSummaryDto } from '../types/persistedKnowledgeMap.ts'
import { KnowledgeGraph } from './KnowledgeGraph.tsx'
import { MetadataForm } from './MetadataForm.tsx'
import { useAuthoring } from './useAuthoring.ts'

type Editor = 'map' | 'new-node' | 'node' | 'edge' | 'delete-map' | 'delete-node' | 'delete-edge' | null

export function MapWorkspace({ id, initial, authoring, revalidate, onOpenMap, onDeleted, onRename }:
  { id: string; initial: KnowledgeMap; authoring: KnowledgeMapAuthoringClient; revalidate: () => Promise<void>; onOpenMap: (id: string) => void;
    onDeleted: () => void; onRename: (title: string) => void }) {
  const [map, setMap] = useState(initial)
  const [editing, setEditing] = useState(false)
  const [editor, setEditor] = useState<Editor>(null)
  const [choices, setChoices] = useState<KnowledgeMapSummaryDto[]>([])
  const [choiceError, setChoiceError] = useState('')
  const [choiceRetry, setChoiceRetry] = useState(0)
  const [source, setSource] = useState('')
  const [target, setTarget] = useState('')
  const persistedPositions = useRef(new Map(initial.nodes.map((node) => [node.id, { ...node.position }])))
  const positionVersions = useRef(new Map<string, number>())
  const mutation = useAuthoring(revalidate)
  const selected = map.nodes.find((node) => node.selected)
  const selectedEdge = map.edges.find((edge) => edge.selected)

  useEffect(() => {
    if (!editing) return
    const controller = new AbortController()
    let current = true
    void listPersistedKnowledgeMaps(controller.signal).then((list) => {
      if (current) { setChoices(list.maps); setChoiceError('') }
    }, async (error: unknown) => {
      if (!current || controller.signal.aborted) return
      if (error instanceof ApiError && error.status === 401) await revalidate()
      if (current) setChoiceError('Não foi possível atualizar os submapas. Tente carregar a lista novamente.')
    })
    return () => { current = false; controller.abort() }
  }, [editing, choiceRetry, revalidate])

  function nodesChanged(changes: NodeChange<KnowledgeNode>[]) {
    setMap((previous) => ({ ...previous,
      nodes: applyNodeChanges(changes.filter((change) => change.type !== 'remove' && change.type !== 'add'
        && !(mutation.isPending() && change.type === 'position')), previous.nodes),
      edges: changes.some((change) => change.type === 'select' && change.selected)
        ? previous.edges.map((edge) => ({ ...edge, selected: false })) : previous.edges }))
  }
  function edgesChanged(changes: EdgeChange[]) {
    const selections = changes.filter((change) => change.type === 'select')
    if (!selections.length) return
    setEditor(null)
    setMap((previous) => ({ ...previous,
      nodes: selections.some((change) => change.selected) ? previous.nodes.map((node) => ({ ...node, selected: false })) : previous.nodes,
      edges: previous.edges.map((edge) => {
        const selection = selections.findLast((change) => change.id === edge.id)
        return selection ? { ...edge, selected: selection.selected } : edge
      }) }))
  }
  function updateVisualNode(nodeId: string, transform: (node: KnowledgeNode) => KnowledgeNode) {
    setMap((previous) => ({ ...previous, nodes: previous.nodes.map((node) => node.id === nodeId ? transform(node) : node) }))
  }
  async function savePosition(node: KnowledgeNode) {
    if (!editing || mutation.isPending()) return
    const version = (positionVersions.current.get(node.id) ?? 0) + 1
    positionVersions.current.set(node.id, version)
    const position = { x: node.position.x, y: node.position.y }
    const saved = await mutation.run((signal) => authoring.updateNode(id, node.id, { position }, signal), () => {
      if (positionVersions.current.get(node.id) === version) {
        persistedPositions.current.set(node.id, position)
        updateVisualNode(node.id, (current) => ({ ...current, position }))
      }
    })
    if (!saved && mutation.isActive() && positionVersions.current.get(node.id) === version) {
      const previous = persistedPositions.current.get(node.id)
      if (previous) updateVisualNode(node.id, (current) => ({ ...current, position: { ...previous } }))
    }
  }
  function toggleEditing() {
    setEditing((value) => !value)
    setEditor(null)
    setMap((previous) => ({ ...previous, nodes: previous.nodes.map((node) => ({ ...node,
      position: { ...(persistedPositions.current.get(node.id) ?? node.position) } })),
      edges: previous.edges.map((edge) => ({ ...edge, selected: false })) }))
  }
  function saveMetadata(fields: MapFields, child: string | null) {
    if (editor === 'map') {
      const patch = metadataPatch(map, fields)
      if (!Object.keys(patch).length) { setEditor(null); return }
      void mutation.run((signal) => authoring.updateMap(id, patch, signal), () => {
        setMap((previous) => ({ ...previous, ...fields })); onRename(fields.title); setEditor(null)
      })
    } else if (editor === 'new-node') {
      void mutation.run((signal) => authoring.createNode(id, { ...fields, position: { x: map.nodes.length * 240, y: 0 } }, signal), (node) => {
        persistedPositions.current.set(node.id, { ...node.position })
        setMap((previous) => ({ ...previous, edges: previous.edges.map((edge) => ({ ...edge, selected: false })),
          nodes: [...previous.nodes.map((n) => ({ ...n, selected: false })),
          { id: node.id, type: 'knowledge', position: { ...node.position }, selected: true,
            data: { title: node.title, description: node.description, childMapId: node.childMapId } }] }))
        setEditor(null)
      })
    } else if (editor === 'node' && selected) {
      const node = selected
      const patch: NodePatch = { ...metadataPatch(node.data, fields),
        ...(child === node.data.childMapId ? {} : child === null ? { clearChildMap: true } : { childMapId: child }) }
      if (!Object.keys(patch).length) { setEditor(null); return }
      void mutation.run((signal) => authoring.updateNode(id, node.id, patch, signal), () => {
        updateVisualNode(node.id, (current) => ({ ...current, data: { ...current.data, ...fields, childMapId: child } })); setEditor(null)
      })
    }
  }
  function confirmDelete() {
    if (editor === 'delete-map') void mutation.run((signal) => authoring.deleteMap(id, signal), onDeleted)
    else if (editor === 'delete-node' && selected) {
      const nodeId = selected.id
      void mutation.run((signal) => authoring.deleteNode(id, nodeId, signal), () => {
        persistedPositions.current.delete(nodeId)
        setMap((previous) => ({ ...previous, nodes: previous.nodes.filter((node) => node.id !== nodeId) })); setEditor(null)
      })
    } else if (editor === 'delete-edge' && selectedEdge) {
      const edgeId = selectedEdge.id
      void mutation.run((signal) => authoring.deleteEdge(id, edgeId, signal), () => {
        setMap((previous) => ({ ...previous, edges: previous.edges.filter((edge) => edge.id !== edgeId) })); setEditor(null)
      })
    }
  }
  const formInitial = editor === 'map' ? map : editor === 'node' && selected ? selected.data : { title: '', description: null }
  const confirmation = editor === 'delete-map' ? `Excluir o mapa “${map.title}” e seu conteúdo?`
    : editor === 'delete-node' && selected ? `Excluir o nó “${selected.data.title}”?`
    : editor === 'delete-edge' && selectedEdge ? 'Excluir a conexão selecionada?' : null

  return <>
    <div className="editor-toolbar">
      <button disabled={mutation.pending} aria-pressed={editing} onClick={toggleEditing}>{editing ? 'Concluir edição' : 'Editar mapa'}</button>
      {editing && <><strong>Modo de edição</strong><button disabled={mutation.pending} onClick={() => setEditor('map')}>Editar dados do mapa</button>
        <button disabled={mutation.pending} onClick={() => setEditor('new-node')}>Adicionar nó</button>
        <button disabled={mutation.pending || map.nodes.length === 0} onClick={() => setEditor('edge')}>Conectar nós</button>
        <button disabled={mutation.pending} onClick={() => setEditor('delete-map')}>Excluir mapa</button></>}
    </div>
    <p id="authoring-feedback" role="status" className={mutation.feedback ? 'mutation-feedback' : 'sr-only'}>{mutation.feedback}</p>
    {editing && map.nodes.length === 0 && <p>Este mapa ainda não tem nós. Use Adicionar nó para começar.</p>}
    <KnowledgeGraph map={map} onOpenMap={onOpenMap} onNodesChange={nodesChanged} onEdgesChange={edgesChanged}
      editing={editing} pending={mutation.pending} onNodeDragStop={(node) => { void savePosition(node) }}
      onSelectionChange={() => setEditor(null)}>
      {editing && <aside className="details-panel" aria-label="Edição do mapa">
        {choiceError && <p className="session-feedback">{choiceError}</p>}
        <button disabled={mutation.pending} onClick={() => setChoiceRetry((value) => value + 1)}>Atualizar lista de submapas</button>
        {selected && <div className="editor-actions"><button disabled={mutation.pending} onClick={() => setEditor('node')}>Editar nó selecionado</button>
          <button disabled={mutation.pending} onClick={() => { void savePosition(selected) }}>Salvar posição do nó</button>
          <button disabled={mutation.pending} onClick={() => setEditor('delete-node')}>Excluir nó selecionado</button></div>}
        {selectedEdge && <button disabled={mutation.pending} onClick={() => setEditor('delete-edge')}>Excluir conexão selecionada</button>}
        {(editor === 'map' || editor === 'new-node' || editor === 'node' && selected) && <MetadataForm key={`${editor}:${editor === 'node' ? selected?.id : ''}`}
          heading={editor === 'map' ? 'Dados do mapa' : editor === 'node' ? 'Dados do nó' : 'Novo nó'} initial={formInitial}
          pending={mutation.pending} onSave={saveMetadata} onCancel={() => setEditor(null)}
          childMaps={editor === 'node' ? choices : undefined} childMapId={selected?.data.childMapId} />}
        {editor === 'edge' && <form className="authoring-form" onSubmit={(event) => {
          event.preventDefault()
          if (!source || !target || mutation.pending) return
          void mutation.run((signal) => authoring.createEdge(id, source, target, signal), (edge) => {
            setMap((previous) => ({ ...previous, edges: [...previous.edges, { id: edge.id, source: edge.sourceNodeId, target: edge.targetNodeId, type: 'smoothstep' }] })); setEditor(null)
          })
        }}><h3>Conectar nós</h3><fieldset disabled={mutation.pending}>
          <label htmlFor="edge-source">Origem</label><select id="edge-source" required value={source} onChange={(e) => setSource(e.target.value)}>
            <option value="">Escolha um nó</option>{map.nodes.map((node) => <option key={node.id} value={node.id}>{node.data.title}</option>)}
          </select>
          <label htmlFor="edge-target">Destino</label><select id="edge-target" required value={target} onChange={(e) => setTarget(e.target.value)}>
            <option value="">Escolha um nó</option>{map.nodes.map((node) => <option key={node.id} value={node.id}>{node.data.title}</option>)}
          </select><div className="editor-actions"><button type="submit">Criar conexão</button><button type="button" onClick={() => setEditor(null)}>Cancelar</button></div>
        </fieldset></form>}
        {editing && <><label htmlFor="edge-selection">Conexão selecionada</label><select id="edge-selection" disabled={mutation.pending} value={selectedEdge?.id ?? ''} onChange={(event) => {
          setEditor(null)
          setMap((previous) => ({ ...previous, nodes: previous.nodes.map((node) => ({ ...node, selected: false })),
            edges: previous.edges.map((edge) => ({ ...edge, selected: edge.id === event.target.value })) }))
        }}><option value="">Nenhuma conexão</option>{map.edges.map((edge, index) => <option key={edge.id} value={edge.id}>
          {index + 1}: {map.nodes.find((node) => node.id === edge.source)?.data.title} → {map.nodes.find((node) => node.id === edge.target)?.data.title}
        </option>)}</select></>}
        {confirmation && <section aria-label="Confirmar exclusão"><h3>{confirmation}</h3><p>A exclusão é permanente. Referências existentes podem impedir a operação.</p>
          <div className="editor-actions"><button disabled={mutation.pending} onClick={confirmDelete}>Confirmar exclusão</button>
            <button disabled={mutation.pending} onClick={() => setEditor(null)}>Cancelar</button></div></section>}
      </aside>}
    </KnowledgeGraph>
  </>
}
