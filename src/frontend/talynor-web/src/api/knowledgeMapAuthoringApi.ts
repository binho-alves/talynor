import { ApiError, isRecord, isText, request, requestNoContent } from './http.ts'
import { isMapId, isPersistedKnowledgeMap } from './knowledgeMapsApi.ts'
import type { PersistedKnowledgeNodeDto, PersistedKnowledgeEdgeDto } from '../types/persistedKnowledgeMap.ts'
import type { WithCsrf } from '../session/sessionClient.ts'

export type MapFields = { title: string; description: string | null }
export type MapPatch = { title?: string; description?: string; clearDescription?: boolean }
export type NodePatch = MapPatch & { position?: { x: number; y: number }; childMapId?: string; clearChildMap?: boolean }
export type CreatedMap = MapFields & { id: string; createdAtUtc: string; updatedAtUtc: string }
export type CreatedNode = PersistedKnowledgeNodeDto & { createdAtUtc: string; updatedAtUtc: string }
export type CreatedEdge = PersistedKnowledgeEdgeDto & { createdAtUtc: string }

const timestamp = (value: unknown): value is string => typeof value === 'string'
  && /^\d{4}-\d\d-\d\dT/.test(value) && Number.isFinite(Date.parse(value))
export function isCreatedMap(value: unknown): value is CreatedMap {
  return isRecord(value) && isMapId(value.id) && isText(value.title)
    && (value.description === null || typeof value.description === 'string')
    && timestamp(value.createdAtUtc) && timestamp(value.updatedAtUtc)
}
export function isCreatedNode(value: unknown): value is CreatedNode {
  return isRecord(value) && timestamp(value.createdAtUtc) && timestamp(value.updatedAtUtc)
    && isPersistedKnowledgeMap({ id: value.id, title: 'node', description: null, nodes: [value], edges: [] })
}
export function isCreatedEdge(value: unknown): value is CreatedEdge {
  return isRecord(value) && isMapId(value.id) && isMapId(value.sourceNodeId)
    && isMapId(value.targetNodeId) && timestamp(value.createdAtUtc)
}

function mapPath(id: string) {
  if (!isMapId(id)) throw new ApiError('invalid-response')
  return `/api/maps/${id}`
}
function resourcePath(mapId: string, kind: 'nodes' | 'edges', id: string) {
  if (!isMapId(id)) throw new ApiError('invalid-response')
  return `${mapPath(mapId)}/${kind}/${id}`
}
function init(method: string, token: string, body?: unknown): RequestInit {
  return { method, headers: { 'X-CSRF-TOKEN': token, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }) }
}
async function created<T>(path: string, body: unknown, token: string, signal: AbortSignal,
  validate: (value: unknown) => value is T): Promise<T> {
  const response = await request(path, signal, init('POST', token, body))
  if (response.status !== 201) throw new ApiError('invalid-response', response.status)
  let value: unknown
  try { value = await response.json() } catch { throw new ApiError('invalid-response') }
  signal.throwIfAborted()
  if (!validate(value)) throw new ApiError('invalid-response')
  return value
}

async function createMap(fields: MapFields, token: string, signal: AbortSignal): Promise<CreatedMap> {
  const v = await created('/api/maps', { title: fields.title, description: fields.description }, token, signal, isCreatedMap)
  return { id: v.id.toLowerCase(), title: v.title, description: v.description, createdAtUtc: v.createdAtUtc, updatedAtUtc: v.updatedAtUtc }
}
function updateMap(id: string, patch: MapPatch, token: string, signal: AbortSignal) {
  return requestNoContent(mapPath(id), signal, init('PATCH', token, patch))
}
function deleteMap(id: string, token: string, signal: AbortSignal) {
  return requestNoContent(mapPath(id), signal, init('DELETE', token))
}
async function createNode(mapId: string, fields: MapFields & { position: { x: number; y: number } }, token: string, signal: AbortSignal): Promise<CreatedNode> {
  const v = await created(`${mapPath(mapId)}/nodes`, { title: fields.title, description: fields.description, position: fields.position }, token, signal, isCreatedNode)
  return { id: v.id.toLowerCase(), title: v.title, description: v.description, position: { x: v.position.x, y: v.position.y }, childMapId: v.childMapId?.toLowerCase() ?? null,
    createdAtUtc: v.createdAtUtc, updatedAtUtc: v.updatedAtUtc }
}
function updateNode(mapId: string, id: string, patch: NodePatch, token: string, signal: AbortSignal) {
  return requestNoContent(resourcePath(mapId, 'nodes', id), signal, init('PATCH', token, patch))
}
function deleteNode(mapId: string, id: string, token: string, signal: AbortSignal) {
  return requestNoContent(resourcePath(mapId, 'nodes', id), signal, init('DELETE', token))
}
async function createEdge(mapId: string, sourceNodeId: string, targetNodeId: string, token: string, signal: AbortSignal): Promise<CreatedEdge> {
  const v = await created(`${mapPath(mapId)}/edges`, { sourceNodeId, targetNodeId }, token, signal, isCreatedEdge)
  if (v.sourceNodeId.toLowerCase() !== sourceNodeId.toLowerCase() || v.targetNodeId.toLowerCase() !== targetNodeId.toLowerCase()) throw new ApiError('invalid-response')
  return { id: v.id.toLowerCase(), sourceNodeId: v.sourceNodeId.toLowerCase(), targetNodeId: v.targetNodeId.toLowerCase(), createdAtUtc: v.createdAtUtc }
}
function deleteEdge(mapId: string, id: string, token: string, signal: AbortSignal) {
  return requestNoContent(resourcePath(mapId, 'edges', id), signal, init('DELETE', token))
}

// Bind once per session client. Only this API layer receives the raw request token.
export function createKnowledgeMapAuthoringClient(withCsrf: WithCsrf) {
  return {
    createMap: (fields: MapFields, signal: AbortSignal) =>
      withCsrf((token, signal) => createMap(fields, token, signal), signal),
    updateMap: (id: string, patch: MapPatch, signal: AbortSignal) =>
      withCsrf((token, signal) => updateMap(id, patch, token, signal), signal),
    deleteMap: (id: string, signal: AbortSignal) =>
      withCsrf((token, signal) => deleteMap(id, token, signal), signal),
    createNode: (mapId: string, fields: MapFields & { position: { x: number; y: number } }, signal: AbortSignal) =>
      withCsrf((token, signal) => createNode(mapId, fields, token, signal), signal),
    updateNode: (mapId: string, id: string, patch: NodePatch, signal: AbortSignal) =>
      withCsrf((token, signal) => updateNode(mapId, id, patch, token, signal), signal),
    deleteNode: (mapId: string, id: string, signal: AbortSignal) =>
      withCsrf((token, signal) => deleteNode(mapId, id, token, signal), signal),
    createEdge: (mapId: string, sourceNodeId: string, targetNodeId: string, signal: AbortSignal) =>
      withCsrf((token, signal) => createEdge(mapId, sourceNodeId, targetNodeId, token, signal), signal),
    deleteEdge: (mapId: string, id: string, signal: AbortSignal) =>
      withCsrf((token, signal) => deleteEdge(mapId, id, token, signal), signal),
  }
}
export type KnowledgeMapAuthoringClient = ReturnType<typeof createKnowledgeMapAuthoringClient>

// Only changed fields are sent. Clearing a nullable field is explicit, never JSON null.
export function metadataPatch(previous: MapFields, next: MapFields): MapPatch {
  return { ...(previous.title === next.title.trim() ? {} : { title: next.title.trim() }),
    ...(previous.description === next.description ? {} : next.description === null ? { clearDescription: true } : { description: next.description }) }
}
export function authoringErrorMessage(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Não foi possível salvar. Confira o estado do mapa antes de tentar novamente.'
  switch (error.code) {
    case 'csrf_invalid': return 'A proteção da sessão precisa ser renovada. Tente novamente para obter um novo token; a operação não foi repetida.'
    case 'validation_failed': return 'Confira os campos: título obrigatório (até 200 caracteres) e descrição até 4000 caracteres.'
    case 'map_in_use': return 'Remova os vínculos de submapa dos nós que referenciam este mapa antes de excluí-lo.'
    case 'node_in_use': return 'Remova as conexões deste nó antes de excluí-lo.'
    case 'invalid_child_map': return 'O submapa informado não está disponível. Atualize a lista e escolha novamente.'
    case 'invalid_edge_nodes': return 'Os nós informados não estão disponíveis neste mapa.'
  }
  if (error.status === 401) return 'A sessão foi reavaliada. Se você continua conectado, tente novamente explicitamente.'
  if (error.status === 403) return 'Não foi possível autorizar a operação. Verifique a sessão e tente novamente.'
  if (error.status === 404) return 'Esse recurso não está mais disponível.'
  if (error.status === 413) return 'Os dados excedem o tamanho permitido.'
  return 'Não foi possível confirmar a gravação. Recarregue o mapa antes de tentar novamente para evitar duplicações.'
}
