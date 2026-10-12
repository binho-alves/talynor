import type { Edge, Node } from '@xyflow/react'

// Spike identifiers and view data; these are not persistence/domain entities.
export type MapId = 'software' | 'csharp'

export type KnowledgeNodeData = {
  title: string
  description: string
  progress: number
  studyMinutes: number
  submapId?: MapId
}

export type KnowledgeNode = Node<KnowledgeNodeData, 'knowledge'>

export type KnowledgeMap = {
  title: string
  description: string
  nodes: KnowledgeNode[]
  edges: Edge[]
}
