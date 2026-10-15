import type { ReactNode } from 'react'
import {
  Background, Controls, MiniMap, ReactFlow, type NodeChange, type EdgeChange,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { KnowledgeNodeCard } from './KnowledgeNodeCard'
import { NodeDetails } from './NodeDetails'
import type { KnowledgeMap, KnowledgeNode } from '../types/knowledge'

// Stable references avoid recreating the custom node types.
const nodeTypes = { knowledge: KnowledgeNodeCard }
const fitViewOptions = { padding: 0.18 }

export function KnowledgeGraph({ map, onOpenMap, onNodesChange, onEdgesChange, onNodeDragStop, onSelectionChange, editing, pending, children }:
  { map: KnowledgeMap; onOpenMap: (id: string) => void; onNodesChange: (changes: NodeChange<KnowledgeNode>[]) => void;
    onEdgesChange: (changes: EdgeChange[]) => void; onNodeDragStop: (node: KnowledgeNode) => void;
    onSelectionChange: () => void; editing: boolean; pending: boolean; children?: ReactNode }) {
  const nodes = map.nodes
  const selectedNode = nodes.find((node) => node.selected)
  function openSubmap(node: KnowledgeNode) {
    if (node.data.childMapId) onOpenMap(node.data.childMapId)
  }
  return (
    <div className="workspace">
      <section className="graph-section" aria-labelledby="map-title">
        <div className="map-heading">
          <h2 id="map-title">{map.title}</h2>
          <span>{nodes.length} conceitos</span>
        </div>
        <p className="map-help">{map.description || 'Sem descrição.'}</p>

        <div className="graph-canvas">
          <ReactFlow<KnowledgeNode>
            nodes={nodes}
            edges={map.edges.map((edge) => ({ ...edge, selectable: editing }))}
            nodeTypes={nodeTypes}
            onNodesChange={(changes) => {
              if (changes.some((change) => change.type === 'select' && change.selected)) onSelectionChange()
              onNodesChange(changes)
            }}
            onEdgesChange={onEdgesChange}
            onNodeDragStop={(_event, node) => onNodeDragStop(node)}
            onNodeDoubleClick={(_event, node) => { if (!editing) openSubmap(node) }}
            fitView
            fitViewOptions={fitViewOptions}
            minZoom={0.25}
            maxZoom={2}
            nodesConnectable={false}
            edgesFocusable={editing}
            nodesDraggable={!pending}
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
                'Conexão entre conceitos. No modo de edição, selecione para acessar a exclusão com confirmação.',
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
          {editing ? 'A posição é salva ao soltar o nó. Após mover por teclado, use Salvar posição do nó.'
            : 'As posições alteradas são locais. Entre em Editar mapa para salvar mudanças.'}
        </p>
      </section>

      {children || <NodeDetails node={selectedNode} onOpenSubmap={openSubmap} />}
    </div>
  )
}
