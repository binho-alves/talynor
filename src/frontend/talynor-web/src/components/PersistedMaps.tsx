import { useEffect, useRef, useState } from 'react'
import { ApiError } from '../api/http.ts'
import { getPersistedKnowledgeMap, listPersistedKnowledgeMaps } from '../api/knowledgeMapsApi.ts'
import { adaptKnowledgeMap } from '../adapters/knowledgeMapAdapter.ts'
import { usePersistedMapNavigation } from '../navigation/usePersistedMapNavigation.ts'
import type { KnowledgeMap } from '../types/knowledge.ts'
import type { KnowledgeMapSummaryDto } from '../types/persistedKnowledgeMap.ts'
import { MapWorkspace } from './MapWorkspace.tsx'
import { CreateMap } from './CreateMap.tsx'
import type { KnowledgeMapAuthoringClient } from '../api/knowledgeMapAuthoringApi.ts'
import type { KnowledgeContentAuthoringClient } from '../api/knowledgeContentAuthoringApi.ts'
import type { StudySessionsClient } from '../api/studySessionsApi.ts'
import type { StudyNodeVisit } from '../types/studySession.ts'

type LoadState = { identity: string } & (
  | { status: 'loading' | 'not-found' | 'session-check' }
  | { status: 'error'; message: string }
  | { status: 'list'; maps: KnowledgeMapSummaryDto[] }
  | { status: 'map'; map: KnowledgeMap }
)

export function PersistedMaps({ onAuthenticationRequired, authoring, contentAuthoring, studySessions, returnTo }:
  { onAuthenticationRequired: () => Promise<void>; authoring: KnowledgeMapAuthoringClient; contentAuthoring: KnowledgeContentAuthoringClient;
    studySessions: StudySessionsClient; returnTo: StudyNodeVisit | null }) {
  const navigation = usePersistedMapNavigation()
  const { route, revision } = navigation
  const mapId = route.kind === 'map' ? route.id : null
  const [retry, setRetry] = useState(0)
  const identity = `${route.kind}:${mapId}:${revision}:${retry}`
  const [loaded, setLoaded] = useState<LoadState>({ identity: '', status: 'loading' })
  const handledReturn = useRef<StudyNodeVisit | null>(null)
  useEffect(() => {
    if (returnTo && handledReturn.current !== returnTo) {
      handledReturn.current = returnTo
      navigation.openMap(returnTo.mapId, returnTo)
    }
  }, [returnTo, navigation])

  useEffect(() => {
    if (route.kind === 'invalid') return
    const controller = new AbortController()
    let current = true
    async function load() {
      try {
        const result = mapId === null
          ? { status: 'list' as const, maps: (await listPersistedKnowledgeMaps(controller.signal)).maps }
          : { status: 'map' as const, map: adaptKnowledgeMap(await getPersistedKnowledgeMap(mapId, controller.signal)) }
        if (current && !controller.signal.aborted) setLoaded({ identity, ...result })
      } catch (error) {
        if (!current || controller.signal.aborted) return
        if (error instanceof ApiError && error.status === 401) {
          setLoaded({ identity, status: 'session-check' })
          await onAuthenticationRequired()
          if (current && !controller.signal.aborted) {
            setLoaded({ identity, status: 'error', message: 'Não foi possível carregar os mapas. Tente novamente.' })
          }
        } else if (mapId !== null && error instanceof ApiError && error.status === 404) {
          setLoaded({ identity, status: 'not-found' })
        } else {
          setLoaded({ identity, status: 'error', message: error instanceof ApiError ? error.message
            : 'Não foi possível carregar os mapas. Tente novamente.' })
        }
      }
    }
    void load()
    return () => { current = false; controller.abort() }
  }, [identity, mapId, route.kind, onAuthenticationRequired])

  const state: LoadState = loaded.identity === identity ? loaded : { identity, status: 'loading' }
  const busy = route.kind !== 'invalid' && (state.status === 'loading' || state.status === 'session-check')
  const statusMessage = route.kind === 'invalid' ? 'Endereço de mapa inválido.'
    : state.status === 'loading' ? (mapId ? 'Carregando mapa…' : 'Carregando seus mapas…')
    : state.status === 'session-check' ? 'Reavaliando a sessão…'
    : state.status === 'error' ? state.message
    : state.status === 'not-found' ? 'Mapa indisponível ou não encontrado.'
    : state.status === 'list' ? (state.maps.length === 0 ? 'Você ainda não tem mapas de conhecimento.' : 'Mapas carregados. Escolha um mapa para explorar.')
    : 'Mapa carregado.'
  return <main>
    <nav className="map-navigation" aria-label="Navegação entre mapas">
      {navigation.canGoBack && <button className="back-button" onClick={navigation.back}>← Voltar à visita anterior</button>}
      {route.kind !== 'picker' && <button className="back-button" onClick={navigation.showPicker}>Todos os mapas</button>}
    </nav>
    <p className={busy ? 'map-message' : 'sr-only'} role="status">{statusMessage}</p>
    <div aria-busy={busy}>
    {route.kind === 'invalid' ? <section className="map-message" aria-labelledby="invalid-map-title">
      <h2 id="invalid-map-title">Endereço de mapa inválido</h2>
      <p>Escolha um mapa em Todos os mapas.</p>
    </section> : <>
      {state.status === 'not-found' && <section className="map-message">
        <h2>Mapa indisponível ou não encontrado</h2><p>Volte a Todos os mapas para escolher outro.</p>
      </section>}
      {state.status === 'error' && <section className="map-message">
        <h2>{mapId ? 'Não foi possível abrir o mapa' : 'Não foi possível carregar seus mapas'}</h2>
        <p>{state.message}</p>
        <button className="back-button" onClick={() => setRetry((value) => value + 1)}>Tentar novamente</button>
      </section>}
      {state.status === 'list' && <section className="map-message" aria-labelledby="picker-title">
        <h2 id="picker-title">Seus mapas de conhecimento</h2>
        {state.maps.length === 0 ? <p>Você ainda não tem mapas de conhecimento. Crie seu primeiro mapa para começar.</p>
          : <><p>Escolha um mapa para explorar.</p><ul className="map-picker">
            {state.maps.map((map) => <li key={map.id}><button className="map-choice" onClick={() => navigation.openMap(map.id)}>
              {map.title}
            </button></li>)}
          </ul></>}
        <CreateMap key={identity} authoring={authoring} revalidate={onAuthenticationRequired} onCreated={(map) => {
          setLoaded((previous) => previous.status === 'list' ? { ...previous, maps: [...previous.maps, { id: map.id, title: map.title }] } : previous)
          navigation.openMap(map.id)
        }} />
      </section>}
      {state.status === 'map' && mapId && <MapWorkspace key={identity} id={mapId} initial={state.map} authoring={authoring} contentAuthoring={contentAuthoring}
        studySessions={studySessions} returnTo={navigation.nodeSelection}
        revalidate={onAuthenticationRequired} onOpenMap={navigation.openMap} onDeleted={navigation.showPicker}
        onRename={(title) => setLoaded((previous) => previous.status === 'map' ? { ...previous, map: { ...previous.map, title } } : previous)} />}
    </>}
    </div>
  </main>
}
