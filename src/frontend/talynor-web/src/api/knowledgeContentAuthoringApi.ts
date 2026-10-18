import { ApiError, request, requestNoContent } from './http.ts'
import { contentPath, copyContent, isContentDetail, isContentUrl } from './knowledgeContentsApi.ts'
import type { WithCsrf } from '../session/sessionClient.ts'
import type { ContentDetail, ContentInput, ContentPatch } from '../types/knowledgeContent.ts'

function init(method: string, token: string, body?: unknown): RequestInit {
  return { method, headers: { 'X-CSRF-TOKEN': token, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }) }
}
export function createKnowledgeContentAuthoringClient(withCsrf: WithCsrf) {
  return {
    createContent: (map: string, node: string, input: ContentInput, signal: AbortSignal): Promise<ContentDetail> => withCsrf(async (token, signal) => {
      const payload = { type: input.type, title: input.title, ...(input.type === 'note' ? { body: input.body } : { url: input.url }) }
      const response = await request(contentPath(map, node), signal, init('POST', token, payload))
      if (response.status !== 201) throw new ApiError('invalid-response', response.status)
      let result: unknown
      try { result = await response.json() } catch { signal.throwIfAborted(); throw new ApiError('invalid-response') }
      signal.throwIfAborted()
      if (!isContentDetail(result) || result.type !== input.type) throw new ApiError('invalid-response')
      return copyContent(result)
    }, signal),
    updateContent: (map: string, node: string, id: string, patch: ContentPatch, signal: AbortSignal) =>
      withCsrf((token, signal) => requestNoContent(contentPath(map, node, id), signal, init('PATCH', token, patch)), signal),
    deleteContent: (map: string, node: string, id: string, signal: AbortSignal) =>
      withCsrf((token, signal) => requestNoContent(contentPath(map, node, id), signal, init('DELETE', token)), signal),
  }
}
export type KnowledgeContentAuthoringClient = ReturnType<typeof createKnowledgeContentAuthoringClient>

export function contentInput(type: 'note' | 'link', title: string, value: string): ContentInput | null {
  const normalizedTitle = title.trim() || null
  if (normalizedTitle !== null && normalizedTitle.length > 200) return null
  if (type === 'note') return value.trim() && value.length <= 4000 ? { type, title: normalizedTitle, body: value } : null
  return isContentUrl(value.trim()) ? { type, title: normalizedTitle, url: value.trim() } : null
}

export function contentPatch(previous: ContentDetail, next: ContentInput): ContentPatch {
  if (previous.type !== next.type) throw new Error('Content type cannot change.')
  const title = next.title?.trim() || null
  const patch: ContentPatch = previous.title === title ? {} : title === null ? { clearTitle: true } : { title }
  if (previous.type === 'note' && next.type === 'note' && previous.body !== next.body) patch.body = next.body
  if (previous.type === 'link' && next.type === 'link' && previous.url !== next.url.trim()) patch.url = next.url.trim()
  return patch
}
export function contentWriteError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code === 'validation_failed') return 'Confira os campos e limites do conteúdo. Nenhuma alteração válida foi aplicada.'
    if (error.code === 'csrf_invalid') return 'A proteção da sessão precisa ser renovada. Envie novamente por uma ação explícita; a operação não foi repetida.'
    if (error.status === 404) return 'Conteúdo ou conceito indisponível. Atualize a lista para conferir o estado atual.'
    if (error.status === 401) return 'A sessão foi reavaliada. Confira o estado do conteúdo antes de tentar novamente; a operação não foi repetida.'
    if (error.status === 413) return 'Os dados excedem o tamanho permitido.'
  }
  return 'Não foi possível confirmar a gravação. Atualize os conteúdos para conferir o estado antes de tentar novamente e evitar duplicações.'
}
