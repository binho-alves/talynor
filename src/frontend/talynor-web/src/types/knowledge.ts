import type { Edge, Node } from '@xyflow/react'

// Visualization data, independent of HTTP contracts and persistence entities.
export type KnowledgeNodeData = {
  title: string
  description: string | null
  childMapId: string | null
}

export type KnowledgeNode = Node<KnowledgeNodeData, 'knowledge'>

export type KnowledgeMap = {
  title: string
  description: string | null
  nodes: KnowledgeNode[]
  edges: Edge[]
}
