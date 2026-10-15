import { useEffect, useState } from 'react'
import { mapUrl, readMapRoute } from './mapLocation.ts'

// History is a sequence of visits, not a parent tree. No map content or auth is stored here.
export function usePersistedMapNavigation() {
  const [scope] = useState(() => crypto.randomUUID())
  const [visit, setVisit] = useState(() => ({ search: location.search, revision: 0, canGoBack: false }))

  useEffect(() => {
    history.replaceState({ ...history.state, talynorMapVisit: { scope, index: 0 } }, '', location.href)
    const onPopState = () => {
      const marker = history.state?.talynorMapVisit
      setVisit((previous) => ({ search: location.search, revision: previous.revision + 1,
        canGoBack: marker?.scope === scope && Number.isSafeInteger(marker.index) && marker.index > 0 }))
    }
    addEventListener('popstate', onPopState)
    return () => removeEventListener('popstate', onPopState)
  }, [scope])

  function navigate(id: string | null) {
    const url = mapUrl(location.href, id)
    if (url === location.pathname + location.search + location.hash) return
    const marker = history.state?.talynorMapVisit
    const index = marker?.scope === scope && Number.isSafeInteger(marker.index) ? marker.index : 0
    history.pushState({ talynorMapVisit: { scope, index: index + 1 } }, '', url)
    setVisit((previous) => ({ search: location.search, revision: previous.revision + 1, canGoBack: true }))
  }

  return { route: readMapRoute(visit.search), revision: visit.revision, canGoBack: visit.canGoBack,
    openMap: (id: string) => navigate(id), showPicker: () => navigate(null),
    back: () => { if (visit.canGoBack) history.back(); else navigate(null) } }
}
