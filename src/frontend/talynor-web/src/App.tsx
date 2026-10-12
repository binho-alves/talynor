import { useCallback, useEffect, useState } from 'react'
import {
  applyNodeChanges,
  Background,
  Controls,
  MiniMap,
  ReactFlow,
  type NodeChange,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { KnowledgeNodeCard } from './components/KnowledgeNodeCard'
import { NodeDetails } from './components/NodeDetails'
import { loadKnowledgeMap } from './api/knowledgeMaps'
import { adaptKnowledgeMap } from './adapters/knowledgeMapAdapter'
import type { KnowledgeMap, KnowledgeNode, MapId } from './types/knowledge'
import './App.css'

// Stable references prevent React Flow from recreating the custom node types.
const nodeTypes = { knowledge: KnowledgeNodeCard }
const fitViewOptions = { padding: 0.18 }

type LoadState = { mapId: string; requestId: number } & (
  | { status: 'loading' | 'not-found' }
  | { status: 'error'; message: string }
  | { status: 'ready'; map: KnowledgeMap }
)

function App() {
  const [activeMapId, setActiveMapId] = useState<MapId>('software')
  const [requestId, setRequestId] = useState(0)
  const [loadState, setLoadState] = useState<LoadState>({ mapId: 'software', requestId: 0, status: 'loading' })
  const [nodesByMap, setNodesByMap] = useState<Record<string, KnowledgeNode[]>>({})

  useEffect(() => {
    const controller = new AbortController()
    let current = true

    async function load() {
      try {
        const response = await loadKnowledgeMap(activeMapId, controller.signal)
        if (!current) return
        if (!response) {
          setLoadState({ mapId: activeMapId, requestId, status: 'not-found' })
          return
        }
        const map = adaptKnowledgeMap(response)
        setNodesByMap((previous) => ({
          ...previous,
          [activeMapId]: map.nodes.map((node) => {
            const old = previous[activeMapId]?.find((item) => item.id === node.id)
            return old ? { ...node, position: old.position, selected: old.selected } : node
          }),
        }))
        setLoadState({ mapId: activeMapId, requestId, status: 'ready', map })
      } catch (error) {
        if (!current || controller.signal.aborted) return
        setLoadState({ mapId: activeMapId, requestId, status: 'error',
          message: error instanceof Error ? error.message : 'Não foi possível carregar o mapa. Tente novamente.' })
      }
    }
    void load()
    return () => { current = false; controller.abort() }
  }, [activeMapId, requestId])

  const state: LoadState = loadState.mapId === activeMapId && loadState.requestId === requestId
    ? loadState : { mapId: activeMapId, requestId, status: 'loading' }
  const activeMap = state.status === 'ready' ? state.map : undefined
  const nodes = nodesByMap[activeMapId] ?? []
  // Derive selection instead of duplicating it in another state variable.
  const selectedNode = nodes.find((node) => node.selected)

  const onNodesChange = useCallback(
    (changes: NodeChange<KnowledgeNode>[]) => {
      setNodesByMap((previous) => ({
        ...previous,
        [activeMapId]: applyNodeChanges(changes, previous[activeMapId] ?? []),
      }))
    },
    [activeMapId],
  )

  function navigateToMap(mapId: MapId) {
    if (mapId === activeMapId) return
    setActiveMapId(mapId)
    // Every visit gets a new identity, including a quick return to the same map.
    setRequestId((value) => value + 1)
  }

  function openSubmap(node: KnowledgeNode) {
    if (node.data.childMapId) navigateToMap(node.data.childMapId)
  }

  return (
    <div className="app-shell">
      <header className="app-header">
        <div>
          <p className="brand">Talynor</p>
          <h1>Seu conhecimento, conectado</h1>
          <p className="intro">Explore conceitos e descubra novas conexões.</p>
        </div>
        <span className="demo-label">Dados de demonstração</span>
      </header>

      <main>
        <nav className="map-navigation" aria-label="Navegação entre mapas">
          {activeMapId !== 'software' && (
            <button className="back-button" onClick={() => navigateToMap('software')}>
              ← Voltar ao mapa anterior
            </button>
          )}
          <span>Software Development</span>
          {activeMapId !== 'software' && <span aria-current="page"> / {activeMap?.title ?? 'Submapa'}</span>}
        </nav>

        <div role="status" aria-live="polite" className="map-status">
          {state.status === 'loading' && <p>Carregando mapa…</p>}
          {state.status === 'not-found' && <p>Mapa não encontrado</p>}
          {state.status === 'error' && <p>{state.message}</p>}
          {(state.status === 'error' || state.status === 'not-found') && (
            <button className="back-button" onClick={() => setRequestId((value) => value + 1)}>Tentar novamente</button>
          )}
        </div>
        {activeMap && <div className="workspace">
          <section className="graph-section" aria-labelledby="map-title">
            <div className="map-heading">
              <h2 id="map-title">{activeMap.title}</h2>
              <span>{nodes.length} conceitos</span>
            </div>
            <p className="map-help">{activeMap.description}</p>

            <div className="graph-canvas">
              <ReactFlow<KnowledgeNode>
                key={activeMapId}
                nodes={nodes}
                edges={activeMap.edges}
                nodeTypes={nodeTypes}
                onNodesChange={onNodesChange}
                onNodeDoubleClick={(_event, node) => openSubmap(node)}
                fitView
                fitViewOptions={fitViewOptions}
                minZoom={0.25}
                maxZoom={2}
                nodesConnectable={false}
                edgesFocusable={false}
                edgesReconnectable={false}
                deleteKeyCode={null}
                multiSelectionKeyCode={null}
                selectionKeyCode={null}
                selectionOnDrag={false}
                ariaLabelConfig={{
                  'node.a11yDescription.default':
                    'Pressione Enter ou Espaço para selecionar um nó. Use as setas para mover o nó selecionado e Escape para cancelar a seleção.',
                  'node.a11yDescription.keyboardDisabled':
                    'Pressione Enter ou Espaço para selecionar um nó e Escape para cancelar a seleção.',
                  'node.a11yDescription.ariaLiveMessage': ({ x, y }) =>
                    `Nó selecionado movido. Nova posição: x ${x}, y ${y}.`,
                  'edge.a11yDescription.default':
                    'Conexão entre conceitos. As conexões não podem ser editadas neste spike.',
                  'controls.ariaLabel': 'Controles do mapa',
                  'controls.zoomIn.ariaLabel': 'Aproximar mapa',
                  'controls.zoomOut.ariaLabel': 'Afastar mapa',
                  'controls.fitView.ariaLabel': 'Enquadrar mapa',
                  'controls.interactive.ariaLabel': 'Alternar interação com o mapa',
                  'minimap.ariaLabel': 'Visão geral do mapa',
                  'handle.ariaLabel': 'Ponto de conexão',
                }}
              >
                <Background gap={22} size={1} color="#cbd5e1" />
                <Controls showInteractive={false} />
                <MiniMap
                  pannable
                  zoomable
                  nodeColor={(node) => (node.selected ? '#2563eb' : '#94a3b8')}
                />
              </ReactFlow>
            </div>
            <p className="interaction-help">
              Selecione um nó para ver detalhes. Arraste os nós para reorganizar;
              arraste o fundo para mover o mapa. Use os controles para ajustar o zoom.
            </p>
          </section>

          <NodeDetails node={selectedNode} onOpenSubmap={openSubmap} />
        </div>}
      </main>
    </div>
  )
}

export default App
