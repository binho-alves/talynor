export type ProblemCode = 'csrf_invalid' | 'validation_failed' | 'invalid_credentials'
  | 'authentication_required' | 'already_authenticated' | 'rate_limited' | 'unexpected_error'
  | 'invalid_child_map' | 'invalid_edge_nodes' | 'map_in_use' | 'node_in_use'

// Only known status/code pairs are retained. Server titles, details and field values are never rendered.
type ProblemDetails = { status?: number; code: ProblemCode }
const codeStatuses: Record<ProblemCode, number> = {
  csrf_invalid: 400, validation_failed: 400, invalid_credentials: 401,
  authentication_required: 401, already_authenticated: 409, rate_limited: 429, unexpected_error: 500,
  invalid_child_map: 400, invalid_edge_nodes: 400, map_in_use: 409, node_in_use: 409,
}

export class ApiError extends Error {
  readonly kind: 'http' | 'network' | 'invalid-response'
  readonly status?: number
  readonly code?: ProblemCode

  constructor(kind: ApiError['kind'], status?: number, code?: ProblemCode) {
    super(kind === 'network' ? 'Não foi possível conectar à API. Tente novamente.'
      : kind === 'invalid-response' ? 'A API retornou uma resposta inesperada. Tente novamente.'
        : 'Não foi possível concluir a operação. Tente novamente.')
    this.kind = kind
    this.status = status
    this.code = code
  }
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function isText(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isProblem(value: unknown, status: number): value is ProblemDetails {
  return isRecord(value) && typeof value.code === 'string'
    && Object.hasOwn(codeStatuses, value.code)
    && codeStatuses[value.code as ProblemCode] === status
    && (value.status === undefined || value.status === status)
}

export async function request(path: string, signal: AbortSignal, init: RequestInit = {}): Promise<Response> {
  signal.throwIfAborted()
  let response: Response
  try {
    response = await fetch(path, { ...init, signal, credentials: 'same-origin', cache: 'no-store', redirect: 'error' })
  } catch {
    signal.throwIfAborted()
    throw new ApiError('network')
  }
  signal.throwIfAborted()
  if (!response.ok) {
    let value: unknown
    try { value = await response.json() } catch { signal.throwIfAborted() }
    signal.throwIfAborted()
    throw new ApiError(isProblem(value, response.status) ? 'http' : 'invalid-response',
      response.status, isProblem(value, response.status) ? value.code : undefined)
  }
  return response
}

export async function readJson<T>(response: Response, signal: AbortSignal,
  validate: (value: unknown) => value is T): Promise<T> {
  if (response.status !== 200) throw new ApiError('invalid-response', response.status)
  let value: unknown
  try { value = await response.json() } catch {
    signal.throwIfAborted()
    throw new ApiError('invalid-response', response.status)
  }
  signal.throwIfAborted()
  if (!validate(value)) throw new ApiError('invalid-response', response.status)
  return value
}

export async function requestNoContent(path: string, signal: AbortSignal, init: RequestInit): Promise<void> {
  const response = await request(path, signal, init)
  if (response.status !== 204) throw new ApiError('invalid-response', response.status)
}
