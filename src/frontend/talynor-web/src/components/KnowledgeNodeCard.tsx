import { Handle, Position, type NodeProps } from '@xyflow/react'
import type { KnowledgeNode } from '../types/knowledge'

export function KnowledgeNodeCard({ data, selected }: NodeProps<KnowledgeNode>) {
  return (
    <div className={'knowledge-node' + (selected ? ' is-selected' : '')}>
      <Handle type="target" position={Position.Left} />
      <span className="node-caption">Conceito</span>
      <strong>{data.title}</strong>
      <div className="node-progress">
        <span>Progresso</span>
        <span>{data.progress}%</span>
      </div>
      {data.childMapId && <span className="submap-label">Submapa disponível →</span>}
      <Handle type="source" position={Position.Right} />
    </div>
  )
}
