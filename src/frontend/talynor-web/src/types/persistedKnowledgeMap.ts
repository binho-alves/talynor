// HTTP contracts are independent of XYFlow's presentation types.
export type KnowledgeMapSummaryDto = { id: string; title: string }
export type KnowledgeMapListDto = { maps: KnowledgeMapSummaryDto[] }
export type PersistedKnowledgeNodeDto = {
  id: string
  title: string
  description: string | null
  position: { x: number; y: number }
  childMapId: string | null
}
export type PersistedKnowledgeEdgeDto = { id: string; sourceNodeId: string; targetNodeId: string }
export type PersistedKnowledgeMapDto = {
  id: string
  title: string
  description: string | null
  nodes: PersistedKnowledgeNodeDto[]
  edges: PersistedKnowledgeEdgeDto[]
}
