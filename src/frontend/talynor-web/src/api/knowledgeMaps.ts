import type { KnowledgeMapResponse } from '../types/knowledgeMapResponse'

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isKnowledgeMap(value: unknown): value is KnowledgeMapResponse {
  if (!isRecord(value) || !isText(value.id) || !isText(value.title) ||
    !isText(value.description) || !Array.isArray(value.nodes) || !Array.isArray(value.edges)) return false

  const nodeIds = new Set<string>()
  for (const node of value.nodes) {
    if (!isRecord(node) || !isText(node.id) || nodeIds.has(node.id) ||
      !isText(node.title) || !isText(node.description) ||
      typeof node.progress !== 'number' || !Number.isFinite(node.progress) ||
      node.progress < 0 || node.progress > 100 ||
      typeof node.studyMinutes !== 'number' || !Number.isSafeInteger(node.studyMinutes) ||
      node.studyMinutes < 0 || !(node.childMapId === null || isText(node.childMapId))) return false
    nodeIds.add(node.id)
  }

  const edgeIds = new Set<string>()
  for (const edge of value.edges) {
    if (!isRecord(edge) || !isText(edge.id) || edgeIds.has(edge.id) ||
      !isText(edge.sourceNodeId) || !isText(edge.targetNodeId) ||
      !nodeIds.has(edge.sourceNodeId) || !nodeIds.has(edge.targetNodeId)) return false
    edgeIds.add(edge.id)
  }
  return true
}

// null means 404; cancellation remains an AbortError for the caller to ignore.
export async function loadKnowledgeMap(mapId: string, signal: AbortSignal): Promise<KnowledgeMapResponse | null> {
  let response: Response
  try {
    response = await fetch(`/api/knowledge-maps/${encodeURIComponent(mapId)}`, {
      signal,
      cache: 'no-store',
    })
  } catch (error) {
    if (signal.aborted) throw error
    throw new Error('Não foi possível conectar à API. Tente novamente.', { cause: error })
  }
  if (response.status === 404) return null
  if (!response.ok) throw new Error('Não foi possível carregar o mapa. Tente novamente.')

  let value: unknown
  try {
    value = await response.json()
  } catch (error) {
    if (signal.aborted) throw error
    throw new Error('A API retornou uma resposta inválida.', { cause: error })
  }
  if (!isKnowledgeMap(value) || value.id !== mapId) {
    throw new Error('A API retornou um mapa inválido.')
  }
  return value
}
