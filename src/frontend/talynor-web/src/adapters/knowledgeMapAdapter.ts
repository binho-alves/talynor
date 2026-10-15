import type { KnowledgeMap } from '../types/knowledge.ts'
import type { PersistedKnowledgeMapDto } from '../types/persistedKnowledgeMap.ts'

export function adaptKnowledgeMap(map: PersistedKnowledgeMapDto): KnowledgeMap {
  return {
    title: map.title,
    description: map.description,
    nodes: map.nodes.map((node) => ({
      id: node.id, type: 'knowledge', position: { ...node.position },
      data: { title: node.title, description: node.description, childMapId: node.childMapId },
    })),
    edges: map.edges.map((edge) => ({
      id: edge.id, source: edge.sourceNodeId, target: edge.targetNodeId, type: 'smoothstep',
    })),
  }
}
