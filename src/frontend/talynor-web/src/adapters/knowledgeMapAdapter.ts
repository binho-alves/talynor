import { knowledgeMapPositions } from '../data/knowledgeMapPositions'
import type { KnowledgeMap } from '../types/knowledge'
import type { KnowledgeMapResponse } from '../types/knowledgeMapResponse'

export function adaptKnowledgeMap(map: KnowledgeMapResponse): KnowledgeMap {
  const positions = Object.hasOwn(knowledgeMapPositions, map.id) ? knowledgeMapPositions[map.id] : undefined
  return {
    title: map.title,
    description: map.description,
    nodes: map.nodes.map((node) => {
      const position = positions && Object.hasOwn(positions, node.id) ? positions[node.id] : undefined
      if (!position) throw new Error('O mapa recebido contém um conceito sem posição demonstrativa conhecida.')
      return {
        id: node.id,
        type: 'knowledge',
        position: { ...position },
        data: {
          title: node.title,
          description: node.description,
          progress: node.progress,
          studyMinutes: node.studyMinutes,
          childMapId: node.childMapId,
        },
      }
    }),
    edges: map.edges.map((edge) => ({
      id: edge.id,
      source: edge.sourceNodeId,
      target: edge.targetNodeId,
      type: 'smoothstep',
    })),
  }
}
