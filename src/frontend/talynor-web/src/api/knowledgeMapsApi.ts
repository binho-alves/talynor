import { ApiError, isRecord, isText, readJson, request } from './http.ts'
import type { KnowledgeMapListDto, PersistedKnowledgeMapDto } from '../types/persistedKnowledgeMap.ts'

export function isMapId(value: unknown): value is string {
  return typeof value === 'string' && /^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i.test(value)
}

const nullableText = (value: unknown) => value === null || typeof value === 'string'

export function isKnowledgeMapList(value: unknown): value is KnowledgeMapListDto {
  if (!isRecord(value) || !Array.isArray(value.maps)) return false
  const ids = new Set<string>()
  return value.maps.every((map) => {
    if (!isRecord(map) || !isMapId(map.id) || !isText(map.title) || ids.has(map.id.toLowerCase())) return false
    ids.add(map.id.toLowerCase())
    return true
  })
}

export function isPersistedKnowledgeMap(value: unknown): value is PersistedKnowledgeMapDto {
  if (!isRecord(value) || !isMapId(value.id) || !isText(value.title) || !nullableText(value.description)
    || !Array.isArray(value.nodes) || !Array.isArray(value.edges)) return false
  const nodeIds = new Set<string>()
  for (const node of value.nodes) {
    if (!isRecord(node) || !isMapId(node.id) || nodeIds.has(node.id.toLowerCase()) || !isText(node.title)
      || !nullableText(node.description) || !(node.childMapId === null || isMapId(node.childMapId))
      || !isRecord(node.position) || typeof node.position.x !== 'number' || !Number.isFinite(node.position.x)
      || typeof node.position.y !== 'number' || !Number.isFinite(node.position.y)) return false
    nodeIds.add(node.id.toLowerCase())
  }
  const edgeIds = new Set<string>()
  for (const edge of value.edges) {
    if (!isRecord(edge) || !isMapId(edge.id) || edgeIds.has(edge.id.toLowerCase())
      || !isMapId(edge.sourceNodeId) || !isMapId(edge.targetNodeId)
      || !nodeIds.has(edge.sourceNodeId.toLowerCase()) || !nodeIds.has(edge.targetNodeId.toLowerCase())) return false
    edgeIds.add(edge.id.toLowerCase())
  }
  return true
}

export async function listPersistedKnowledgeMaps(signal: AbortSignal): Promise<KnowledgeMapListDto> {
  const value = await readJson(await request('/api/maps', signal), signal, isKnowledgeMapList)
  return { maps: value.maps.map(({ id, title }) => ({ id: id.toLowerCase(), title })) }
}

export async function getPersistedKnowledgeMap(id: string, signal: AbortSignal): Promise<PersistedKnowledgeMapDto> {
  if (!isMapId(id)) throw new ApiError('invalid-response')
  const value = await readJson(await request(`/api/maps/${encodeURIComponent(id)}`, signal), signal, isPersistedKnowledgeMap)
  if (value.id.toLowerCase() !== id.toLowerCase()) throw new ApiError('invalid-response')
  // Retain only contract fields, with consistent GUID casing for visual references.
  return {
    id: value.id.toLowerCase(), title: value.title, description: value.description,
    nodes: value.nodes.map((node) => ({
      id: node.id.toLowerCase(), title: node.title, description: node.description,
      position: { x: node.position.x, y: node.position.y }, childMapId: node.childMapId?.toLowerCase() ?? null,
    })),
    edges: value.edges.map((edge) => ({ id: edge.id.toLowerCase(),
      sourceNodeId: edge.sourceNodeId.toLowerCase(), targetNodeId: edge.targetNodeId.toLowerCase() })),
  }
}
