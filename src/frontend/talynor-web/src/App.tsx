import { useCallback, useState } from 'react'
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
import { knowledgeMaps } from './data/knowledgeMaps'
import type { KnowledgeNode, MapId } from './types/knowledge'
import './App.css'

// Stable references prevent React Flow from recreating the custom node types.
const nodeTypes = { knowledge: KnowledgeNodeCard }
const fitViewOptions = { padding: 0.18 }

function App() {
  const [activeMapId, setActiveMapId] = useState<MapId>('software')
  // Local UI state only. Copy mocks so dragging never modifies source data.
  const [nodesByMap, setNodesByMap] = useState(() => ({
    software: structuredClone(knowledgeMaps.software.nodes),
    csharp: structuredClone(knowledgeMaps.csharp.nodes),
  }))

  const activeMap = knowledgeMaps[activeMapId]
  const nodes = nodesByMap[activeMapId]
  // Derive selection instead of duplicating it in another state variable.
  const selectedNode = nodes.find((node) => node.selected)

  const onNodesChange = useCallback(
    (changes: NodeChange<KnowledgeNode>[]) => {
      setNodesByMap((previous) => ({
        ...previous,
        [activeMapId]: applyNodeChanges(changes, previous[activeMapId]),
      }))
    },
    [activeMapId],
  )

  function openSubmap(node: KnowledgeNode) {
    if (node.data.submapId) setActiveMapId(node.data.submapId)
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
          {activeMapId === 'csharp' && (
            <button className="back-button" onClick={() => setActiveMapId('software')}>
              ← Voltar ao mapa anterior
            </button>
          )}
          <span>Software Development</span>
          {activeMapId === 'csharp' && <span aria-current="page"> / C#</span>}
        </nav>

        <div className="workspace">
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
        </div>
      </main>
    </div>
  )
}

export default App
