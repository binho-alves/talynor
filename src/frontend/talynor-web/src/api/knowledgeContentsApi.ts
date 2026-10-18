import { ApiError, isRecord, readJson, request } from './http.ts'
import { isMapId } from './knowledgeMapsApi.ts'
import type { ContentDetail, ContentSummary } from '../types/knowledgeContent.ts'

// This is a conservative navigation check, not a replacement for the backend URL policy.
export function isContentUrl(value: unknown): value is string {
  if (typeof value !== 'string' || value.length > 2048 || value !== value.trim()
    || /[\s\\]/u.test(value) || [...value].some((char) => char.charCodeAt(0) < 32
      || char.charCodeAt(0) >= 127 && char.charCodeAt(0) <= 159) || !/^https?:\/\//i.test(value)) return false
  try {
    const parsed = new URL(value)
    const authority = value.slice(value.indexOf('://') + 3).split(/[/?#]/)[0]
    return !!parsed.hostname && !parsed.username && !parsed.password && !authority.includes('@')
  } catch { return false }
}
const timestamp = (value: unknown) => typeof value === 'string'
  && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value)
  && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 19) === value.slice(0, 19)
function base(value: unknown): value is Record<string, unknown> {
  return isRecord(value) && isMapId(value.id) && (value.title === null
    || typeof value.title === 'string' && value.title === value.title.trim() && value.title.length > 0 && value.title.length <= 200)
    && timestamp(value.createdAtUtc) && timestamp(value.updatedAtUtc)
}
export function isContentSummary(value: unknown): value is ContentSummary {
  return base(value) && !Object.hasOwn(value, 'body') && (value.type === 'note' && !Object.hasOwn(value, 'url')
    || value.type === 'link' && isContentUrl(value.url))
}
export function isContentDetail(value: unknown): value is ContentDetail {
  return base(value) && (value.type === 'note' && typeof value.body === 'string'
    && value.body.trim().length > 0 && value.body.length <= 4000 && !Object.hasOwn(value, 'url')
    || value.type === 'link' && isContentUrl(value.url) && !Object.hasOwn(value, 'body'))
}
export function isContentList(value: unknown): value is { contents: ContentSummary[] } {
  if (!isRecord(value) || !Array.isArray(value.contents)) return false
  const ids = new Set<string>()
  return value.contents.every((item) => {
    if (!isContentSummary(item) || ids.has(item.id.toLowerCase())) return false
    ids.add(item.id.toLowerCase()); return true
  })
}
// Copy only the HTTP contract; never retain arbitrary fields in component state.
export function copyContent<T extends ContentSummary | ContentDetail>(value: T): T {
  return { id: value.id.toLowerCase(), type: value.type, title: value.title,
    createdAtUtc: value.createdAtUtc, updatedAtUtc: value.updatedAtUtc,
    ...(value.type === 'link' ? { url: value.url } : 'body' in value ? { body: value.body } : {}) } as T
}
export function contentPath(map: string, node: string, content?: string): string {
  if (!isMapId(map) || !isMapId(node) || content !== undefined && !isMapId(content)) throw new ApiError('invalid-response')
  return `/api/maps/${map}/nodes/${node}/contents${content === undefined ? '' : `/${content}`}`
}
export async function listContents(map: string, node: string, signal: AbortSignal): Promise<ContentSummary[]> {
  const result = await readJson(await request(contentPath(map, node), signal), signal, isContentList)
  return result.contents.map(copyContent)
}
export async function getContent(map: string, node: string, id: string, signal: AbortSignal): Promise<ContentDetail> {
  const result = await readJson(await request(contentPath(map, node, id), signal), signal, isContentDetail)
  if (result.id.toLowerCase() !== id.toLowerCase()) throw new ApiError('invalid-response')
  return copyContent(result)
}
export const contentLabel = (content: ContentSummary | ContentDetail) => content.title ?? (content.type === 'note' ? 'Nota' : content.url)

export function reconcileSelectedContent(selected: string | null, newContents: readonly ContentSummary[]): string | null {
  return selected !== null && newContents.some((content) => content.id === selected) ? selected : null
}
