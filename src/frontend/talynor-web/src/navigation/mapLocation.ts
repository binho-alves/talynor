import { isMapId } from '../api/knowledgeMapsApi.ts'

export type MapRoute = { kind: 'picker' } | { kind: 'invalid' } | { kind: 'map'; id: string }

export function readMapRoute(search: string): MapRoute {
  const values = new URLSearchParams(search).getAll('map')
  if (values.length === 0) return { kind: 'picker' }
  if (values.length !== 1 || !isMapId(values[0])) return { kind: 'invalid' }
  return { kind: 'map', id: values[0].toLowerCase() }
}

export function mapUrl(href: string, id: string | null): string {
  const url = new URL(href)
  if (id === null) url.searchParams.delete('map')
  else {
    if (!isMapId(id)) throw new Error('Identificador de mapa inválido.')
    url.searchParams.set('map', id.toLowerCase())
  }
  return url.pathname + url.search + url.hash
}
