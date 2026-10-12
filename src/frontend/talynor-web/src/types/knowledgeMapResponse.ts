export type KnowledgeNodeResponse = {
  id: string
  title: string
  description: string
  progress: number
  studyMinutes: number
  childMapId: string | null
}

export type KnowledgeEdgeResponse = {
  id: string
  sourceNodeId: string
  targetNodeId: string
}

export type KnowledgeMapResponse = {
  id: string
  title: string
  description: string
  nodes: KnowledgeNodeResponse[]
  edges: KnowledgeEdgeResponse[]
}
