// Node's built-in assertions and native TypeScript stripping; no added test framework.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'
import { ApiError } from '../src/api/http.ts'
import { createSessionClient } from '../src/session/sessionClient.ts'
import { isStudySession, isActiveStudySession, isStudyHistory } from '../src/api/studySessionsApi.ts'
import { createStudySessionState } from '../src/study/studySessionState.ts'
import { createStudyHistoryState } from '../src/study/studyHistoryState.ts'
import { elapsedSeconds, formatStudyDuration } from '../src/study/studyTime.ts'
import { mapUrl, readMapRoute } from '../src/navigation/mapLocation.ts'

let passed = 0
async function check(name, action) {
  try { await action(); passed++ } catch (error) { console.error(`FAIL: ${name}`); throw error }
}
const map = '10000000-0000-0000-0000-000000000001', node = '20000000-0000-0000-0000-000000000001'
const id = '30000000-0000-0000-0000-000000000001', other = '40000000-0000-0000-0000-000000000001'
const start = '2026-10-18T12:00:00+00:00', end = '2026-10-18T12:01:00+00:00'
const session = { id, knowledgeNodeId: node, startedAtUtc: start, endedAtUtc: null, durationSeconds: null }
const completed = { ...session, endedAtUtc: end, durationSeconds: 60 }
const target = { mapId: map, nodeId: node, nodeTitle: 'Study topic' }
const context = { ...target, session }
const envelope = active => ({ active, serverTimeUtc: end })
const history = (sessions = [], page = 1, hasMore = false) => ({ sessions, page, pageSize: 20, hasMore })
const signal = () => new AbortController().signal
function deferred() { let resolve; const promise = new Promise(r => { resolve = r }); return { promise, resolve } }
const settle = () => new Promise(resolve => setImmediate(resolve))

await check('valid active and completed records', () => { assert.ok(isStudySession(session)); assert.ok(isStudySession(completed)) })
await check('zero and fractional tick duration', () => {
  assert.ok(isStudySession({ ...completed, endedAtUtc: start, durationSeconds: 0 }))
  assert.ok(isStudySession({ ...completed, endedAtUtc: '2026-10-18T12:00:00.0000001Z', durationSeconds: 0.0000001 }))
})
for (const value of [null, [], {}, { ...session, id: 'bad' }, { ...session, knowledgeNodeId: 'bad' },
  { ...session, startedAtUtc: 'yesterday' }, { ...session, startedAtUtc: '2026-02-30T12:00:00Z' },
  { ...session, startedAtUtc: '2026-10-18T12:00:00' }, { ...session, endedAtUtc: end },
  { ...session, durationSeconds: 0 }, { ...completed, endedAtUtc: 'bad' }, { ...completed, durationSeconds: -1 },
  { ...completed, durationSeconds: Infinity }, { ...completed, durationSeconds: '60' },
  { ...completed, durationSeconds: 100 }, { ...completed, endedAtUtc: '2026-10-18T11:00:00Z' }])
  await check('malformed session rejected', () => assert.equal(isStudySession(value), false))
await check('active/null envelopes', () => { assert.ok(isActiveStudySession(envelope(context))); assert.ok(isActiveStudySession(envelope(null))) })
for (const value of [{ active: null }, { ...envelope(null), serverTimeUtc: 'bad' }, envelope({ ...context, mapId: 'bad' }),
  envelope({ ...context, nodeId: other }), envelope({ ...context, nodeTitle: '' }), envelope({ ...context, session: completed })])
  await check('malformed active context rejected', () => assert.equal(isActiveStudySession(value), false))
await check('history empty/completed', () => { assert.ok(isStudyHistory(history())); assert.ok(isStudyHistory(history([completed]))) })
for (const value of [{ items: [], page: 1, pageSize: 20, hasMore: false }, history([session]), history([completed, completed]),
  { ...history(), page: 0 }, { ...history(), page: 1.5 }, { ...history(), pageSize: 101 }, { ...history(), pageSize: 0 },
  { ...history(), hasMore: 1 }, { ...history([completed]), pageSize: 0 }])
  await check('malformed pagination/records rejected', () => assert.equal(isStudyHistory(value), false))

const originalFetch = globalThis.fetch
let requests = [], mode = 'ok', responseValue = session
globalThis.fetch = async (path, init) => {
  requests.push({ path, init })
  if (path === '/api/auth/csrf') return Response.json({ token: 'synthetic-only' })
  if (mode === 'network') throw new TypeError('synthetic network')
  if (mode !== 'ok') {
    const status = Number(mode), code = { 400: 'csrf_invalid', 401: 'authentication_required', 404: 'study_session_not_found', 409: 'active_session_exists', 500: 'unexpected_error' }[status]
    return Response.json({ status, code, detail: 'UNTRUSTED' }, { status })
  }
  if (init.method === 'POST' && !path.endsWith('/complete')) return Response.json(responseValue, { status: 201 })
  if (init.method) return new Response(null, { status: 204 })
  return Response.json(responseValue)
}
try {
  const client = createSessionClient().studySessions
  await check('GET active same-origin/no-store without CSRF and own projection', async () => {
    requests = []; responseValue = { ...envelope({ ...context, privateField: 'discard' }), privateField: 'discard' }
    assert.deepEqual(await client.active(signal()), envelope(context))
    assert.equal(requests.length, 1); assert.equal(requests[0].path, '/api/study-sessions/active')
    assert.equal(requests[0].init.credentials, 'same-origin'); assert.equal(requests[0].init.cache, 'no-store')
    assert.equal(requests[0].init.headers, undefined); assert.equal(requests[0].init.redirect, 'error')
  })
  await check('history exact sessions envelope, page and node check', async () => {
    responseValue = history([completed]); assert.deepEqual(await client.history(map, node, 1, signal()), history([completed]))
    assert.equal(requests.at(-1).path, `/api/maps/${map}/nodes/${node}/study-sessions?page=1&pageSize=20`)
    responseValue = history([{ ...completed, knowledgeNodeId: other }]); await assert.rejects(client.history(map, node, 1, signal()), ApiError)
    responseValue = history([], 2); await assert.rejects(client.history(map, node, 1, signal()), ApiError)
  })
  await check('detail validates identity and copies only contract fields', async () => {
    responseValue = { ...completed, ownerId: 'discard' }; assert.deepEqual(await client.detail(map, node, id, signal()), completed)
    responseValue = { ...completed, id: other }; await assert.rejects(client.detail(map, node, id, signal()), ApiError)
  })
  await check('start returns persisted record without client timestamp/body', async () => {
    requests = []; responseValue = session; assert.deepEqual(await client.start(map, node, signal()), session)
    assert.deepEqual(requests.map(r => r.path), ['/api/auth/csrf', `/api/maps/${map}/nodes/${node}/study-sessions`])
    assert.equal(requests[1].init.method, 'POST'); assert.equal(requests[1].init.body, undefined)
    assert.deepEqual(Object.keys(requests[1].init.headers), ['X-CSRF-TOKEN'])
  })
  await check('complete/remove use own routes, native CSRF and no body', async () => {
    for (const op of ['complete', 'remove']) {
      requests = []; await client[op](map, node, id, signal())
      assert.equal(requests.length, 2); assert.equal(requests[0].path, '/api/auth/csrf')
      assert.equal(requests[1].init.method, op === 'complete' ? 'POST' : 'DELETE')
      assert.equal(requests[1].path, `/api/maps/${map}/nodes/${node}/study-sessions/${id}${op === 'complete' ? '/complete' : ''}`)
      assert.equal(requests[1].init.body, undefined); assert.deepEqual(Object.keys(requests[1].init.headers), ['X-CSRF-TOKEN'])
    }
  })
  for (const failure of ['400', '401', '404', '409', '500', 'network']) for (const operation of [
    () => client.start(map, node, signal()), () => client.complete(map, node, id, signal()), () => client.remove(map, node, id, signal())])
    await check('writes never replay, errors sanitized', async () => {
      mode = failure; requests = []; await assert.rejects(operation(), e => e instanceof ApiError && !e.message.includes('UNTRUSTED'))
      assert.equal(requests.length, 2)
    })
  mode = 'ok'
  await check('invalid IDs and pre-cancellation send no requests', async () => {
    requests = []; await assert.rejects(client.history('bad', node, 1, signal()), ApiError)
    await assert.rejects(client.active(AbortSignal.abort())); assert.equal(requests.length, 0)
  })
  await check('malformed successful write is ambiguous, never replayed', async () => {
    responseValue = {}; requests = []; await assert.rejects(client.start(map, node, signal()), ApiError); assert.equal(requests.length, 2)
  })
} finally { globalThis.fetch = originalFetch }

function fixture(overrides = {}) {
  let serverActive = null, now = 1000, authChecks = 0
  const calls = []
  const client = {
    active: async () => { calls.push('active'); return envelope(serverActive) },
    start: async () => { calls.push('start'); serverActive = context; return session },
    complete: async () => { calls.push('complete'); serverActive = null },
    remove: async () => { calls.push('remove'); serverActive = null },
    history: async () => history(), detail: async () => completed, ...overrides,
  }
  const state = createStudySessionState(client, async () => { authChecks++ }, () => now)
  return { state, client, calls, setActive: value => { serverActive = value }, setNow: value => { now = value }, authChecks: () => authChecks }
}
async function mounted(f) { f.state.mount(); await f.state.refresh(); return f }
await check('bootstrap null does not create a session', async () => {
  const f = await mounted(fixture()); assert.equal(f.state.getSnapshot().active, null); assert.deepEqual(f.calls, ['active']); f.state.dispose()
})
await check('bootstrap restores active and server clock anchor', async () => {
  const f = fixture(); f.setActive(context); await mounted(f)
  assert.deepEqual(f.state.getSnapshot().active, context); assert.deepEqual(f.state.getSnapshot().anchor, { serverMs: Date.parse(end), receivedAt: 1000 }); f.state.dispose()
})
await check('focus sees another tab start/complete and reanchors', async () => {
  const f = await mounted(fixture()); f.setActive(context); f.setNow(5000); f.state.focus(); await f.state.refresh()
  assert.equal(f.state.getSnapshot().active.session.id, id); assert.equal(f.state.getSnapshot().anchor.receivedAt, 5000)
  f.setActive(null); f.state.focus(); await f.state.refresh(); assert.equal(f.state.getSnapshot().active, null); f.state.dispose()
})
await check('focus deduplicates an in-progress read', async () => {
  const gate = deferred(); let reads = 0; const f = fixture({ active: () => { reads++; return gate.promise } })
  f.state.mount(); f.state.focus(); f.state.focus(); await settle(); assert.equal(reads, 1)
  gate.resolve(envelope(null)); await f.state.refresh(); f.state.dispose()
})
await check('late bootstrap cannot overwrite a newer successful start', async () => {
  const gate = deferred(); let reads = 0; const f = fixture({ active: async () => ++reads === 1 ? gate.promise : envelope(reads === 2 ? null : context) })
  f.state.mount(); await settle(); await f.state.start(target); gate.resolve(envelope(null)); await settle()
  assert.equal(f.state.getSnapshot().active.session.id, id); f.state.dispose()
})
await check('successful start is reconciled with server state', async () => {
  const f = await mounted(fixture()); await f.state.start(target)
  assert.deepEqual(f.calls, ['active', 'active', 'start', 'active']); assert.deepEqual(f.state.getSnapshot().active, context); f.state.dispose()
})
await check('single synchronous lock blocks double start/complete/discard', async () => {
  const gate = deferred(); let writes = 0
  const f = await mounted(fixture({ start: async () => { writes++; await gate.promise; return session } }))
  const pending = f.state.start(target); await settle()
  await f.state.start(target); await f.state.complete(context); await f.state.discard(context)
  assert.equal(writes, 1); assert.equal(f.state.getSnapshot().busy, 'start'); gate.resolve(); await pending; f.state.dispose()
})
await check('preflight recovers an existing active without a POST', async () => {
  const f = await mounted(fixture()); f.setActive(context); await f.state.start(target)
  assert.equal(f.calls.filter(c => c === 'start').length, 0); assert.equal(f.state.getSnapshot().active.session.id, id); f.state.dispose()
})
for (const [kind, committed] of [['conflict', true], ['network', true], ['network', false], ['500', true], ['malformed', true]])
  await check('start failure reconciles without replay', async () => {
    const f = fixture({ start: async () => {
      f.calls.push('start'); if (committed) f.setActive(context)
      throw kind === 'conflict' ? new ApiError('http', 409, 'active_session_exists') : new ApiError(kind === 'network' ? 'network' : kind === '500' ? 'http' : 'invalid-response', kind === '500' ? 500 : undefined)
    } })
    await mounted(f); await f.state.start(target)
    assert.equal(f.calls.filter(c => c === 'start').length, 1); assert.equal(f.calls.at(-1), 'active')
    assert.equal(f.state.getSnapshot().active?.session.id ?? null, committed ? id : null); assert.equal(f.state.getSnapshot().busy, null); f.state.dispose()
  })
for (const op of ['complete', 'discard']) for (const committed of [false, true])
  await check('ambiguous completion/discard revalidates without replay', async () => {
    const method = op === 'complete' ? 'complete' : 'remove'
    const f = fixture({ [method]: async () => { f.calls.push(method); if (committed) f.setActive(null); throw new ApiError('network') } })
    f.setActive(context); await mounted(f); const revision = f.state.getSnapshot().historyRevision
    await f.state[op](context); assert.equal(f.calls.filter(c => c === method).length, 1)
    assert.equal(f.state.getSnapshot().active?.session.id ?? null, committed ? null : id)
    assert.ok(f.state.getSnapshot().historyRevision > revision); f.state.dispose()
  })
for (const op of ['complete', 'discard']) await check('successful completion/discard clears active and refreshes history', async () => {
  const f = fixture(); f.setActive(context); await mounted(f); const revision = f.state.getSnapshot().historyRevision
  await f.state[op](context); assert.equal(f.state.getSnapshot().active, null); assert.ok(f.state.getSnapshot().historyRevision > revision); f.state.dispose()
})
await check('stale completion cannot target a new session', async () => {
  const f = fixture(); f.setActive({ ...context, session: { ...session, id: other } }); await mounted(f); await f.state.complete(context)
  assert.ok(!f.calls.includes('complete')); assert.equal(f.state.getSnapshot().active.session.id, other); f.state.dispose()
})
await check('completed history delete uses only explicit ID and refreshes', async () => {
  let deleted; const f = await mounted(fixture({ remove: async (...args) => { deleted = args.slice(0, 3) } }))
  await f.state.deleteHistory(target, id); assert.deepEqual(deleted, [map, node, id]); f.state.dispose()
})
await check('401 uses existing session revalidation without replay', async () => {
  const f = fixture({ active: async () => { throw new ApiError('http', 401, 'authentication_required') } })
  await mounted(f); assert.equal(f.authChecks(), 1); assert.equal(f.state.getSnapshot().status, 'error'); f.state.dispose()
})
await check('logout/dispose aborts local read and ignores late data, without backend mutations', async () => {
  const gate = deferred(); let aborted
  const f = fixture({ active: s => { s.addEventListener('abort', () => { aborted = true }); return gate.promise } })
  f.state.mount(); await settle(); f.state.dispose(); gate.resolve(envelope(context)); await settle()
  assert.ok(aborted); assert.equal(f.state.getSnapshot().active, null); assert.ok(!f.calls.includes('complete') && !f.calls.includes('remove'))
})
await check('StrictMode mount/dispose/remount still restores active', async () => {
  const f = fixture(); f.setActive(context); f.state.mount(); f.state.dispose(); await mounted(f)
  assert.equal(f.state.getSnapshot().active.session.id, id); f.state.dispose()
})
await check('write 401 revalidates the existing session once, without replay', async () => {
  let writes = 0
  const f = await mounted(fixture({ start: async () => { writes++; throw new ApiError('http', 401, 'authentication_required') } }))
  await f.state.start(target); assert.equal(writes, 1); assert.equal(f.authChecks(), 1)
  assert.equal(f.state.getSnapshot().status, 'error'); f.state.dispose()
})
await check('failed active preflight does not send a write or treat failure as empty', async () => {
  const f = fixture({ active: async () => { throw new ApiError('network') } })
  await mounted(f); await f.state.start(target)
  assert.ok(!f.calls.includes('start')); assert.equal(f.state.getSnapshot().status, 'error'); f.state.dispose()
})
await check('explicit successful refresh clears an earlier read error', async () => {
  let fail = true
  const f = fixture({ active: async () => { if (fail) throw new ApiError('network'); return envelope(null) } })
  await mounted(f); assert.ok(f.state.getSnapshot().message); fail = false; await f.state.refresh()
  assert.equal(f.state.getSnapshot().status, 'ready'); assert.equal(f.state.getSnapshot().message, ''); f.state.dispose()
})
await check('logout during a sent start ignores late UI completion without discarding the backend session', async () => {
  const gate = deferred(); let aborted = false
  const f = fixture({ start: async (_map, _node, signal) => {
    f.setActive(context); signal.addEventListener('abort', () => { aborted = true }); await gate.promise; return session
  } })
  await mounted(f); const pending = f.state.start(target); await settle(); f.state.dispose(); gate.resolve(); await pending
  assert.ok(aborted); assert.equal(f.state.getSnapshot().active, null)
  await mounted(f); assert.equal(f.state.getSnapshot().active.session.id, id); assert.ok(!f.calls.includes('remove')); f.state.dispose()
})

for (const [seconds, formatted] of [[0, '0s'], [0.5, '< 1s'], [45.9, '45s'], [192, '3m 12s'], [4080, '1h 08m 00s']])
  await check('duration format floors without rounding up', () => assert.equal(formatStudyDuration(seconds), formatted))
await check('clock anchor corrects local clock skew', () => assert.equal(elapsedSeconds(start, { serverMs: Date.parse(end), receivedAt: 500 }, 500), 60))
await check('elapsed visual tick and missed ticks/suspension derive time, not counts', () => {
  const anchor = { serverMs: Date.parse(start), receivedAt: 100 }
  assert.equal(elapsedSeconds(start, anchor, 1100), 1); assert.equal(elapsedSeconds(start, anchor, 1800100), 1800)
})
await check('focus server reanchor and negative visual clamp', () => {
  assert.equal(elapsedSeconds(start, { serverMs: Date.parse(end), receivedAt: 10000 }, 11000), 61)
  assert.equal(elapsedSeconds(end, { serverMs: Date.parse(start), receivedAt: 10000 }, 11000), 0)
})

await check('history loading, empty and page one', async () => {
  const h = createStudyHistoryState({ history: async () => history() }, map, node, async () => {})
  assert.equal(h.getSnapshot().status, 'loading'); h.mount(); await h.reload()
  assert.equal(h.getSnapshot().status, 'ready'); assert.equal(h.getSnapshot().page, 1); assert.deepEqual(h.getSnapshot().sessions, []); h.dispose()
})
await check('load more appends in server order and deduplicates offset shifts', async () => {
  const pages = []; const second = { ...completed, id: other }
  const h = createStudyHistoryState({ history: async (_m, _n, page) => { pages.push(page); return page === 1 ? history([completed], 1, true) : history([completed, second], 2) } }, map, node, async () => {})
  h.mount(); await h.reload(); await h.more(); await h.more()
  assert.deepEqual(pages, [1, 2]); assert.deepEqual(h.getSnapshot().sessions.map(s => s.id), [id, other]); assert.equal(h.getSnapshot().hasMore, false); h.dispose()
})
await check('history retry resets to page one and refresh after completion replaces old list', async () => {
  let reads = 0; const h = createStudyHistoryState({ history: async () => { if (++reads === 1) throw new ApiError('network'); return history(reads === 2 ? [] : [completed]) } }, map, node, async () => {})
  h.mount(); await h.reload(); assert.equal(h.getSnapshot().status, 'error'); await h.reload(); assert.deepEqual(h.getSnapshot().sessions, [])
  await h.reload(); assert.deepEqual(h.getSnapshot().sessions, [completed]); h.dispose()
})
await check('late page cannot overwrite a newer node reload', async () => {
  const gate = deferred(); let reads = 0
  const h = createStudyHistoryState({ history: async () => ++reads === 1 ? gate.promise : history() }, map, node, async () => {})
  h.mount(); const old = h.reload(); await h.reload(); gate.resolve(history([completed])); await old
  assert.deepEqual(h.getSnapshot().sessions, []); h.dispose()
})
await check('unmounted node history cannot update another node or disclose old records', async () => {
  const gate = deferred(); const old = createStudyHistoryState({ history: () => gate.promise }, map, node, async () => {})
  old.mount(); const pending = old.reload(); old.dispose()
  const next = createStudyHistoryState({ history: async () => history() }, map, other, async () => {})
  next.mount(); await next.reload(); gate.resolve(history([completed])); await pending
  assert.deepEqual(next.getSnapshot().sessions, []); assert.equal(old.getSnapshot().page, 0); next.dispose()
})
await check('load-more error preserves rows, explicit retry uses same page and deduplicates concurrent clicks', async () => {
  let attempts = 0; const gate = deferred()
  const h = createStudyHistoryState({ history: async (_map, _node, page) => {
    if (page === 1) return history([completed], 1, true)
    if (++attempts === 1) throw new ApiError('network')
    await gate.promise; return history([{ ...completed, id: other }], 2)
  } }, map, node, async () => {})
  h.mount(); await h.reload(); await h.more(); assert.deepEqual(h.getSnapshot().sessions, [completed]); assert.equal(h.getSnapshot().page, 1)
  const pending = h.more(); await h.more(); assert.equal(attempts, 2); gate.resolve(); await pending
  assert.equal(h.getSnapshot().page, 2); assert.equal(h.getSnapshot().sessions.length, 2); h.dispose()
})
await check('history 401 uses session revalidation without automatic reload', async () => {
  let reads = 0, auth = 0
  const h = createStudyHistoryState({ history: async () => { reads++; throw new ApiError('http', 401, 'authentication_required') } }, map, node, async () => { auth++ })
  h.mount(); await h.reload(); assert.equal(reads, 1); assert.equal(auth, 1); assert.equal(h.getSnapshot().status, 'error'); h.dispose()
})
const source = name => readFileSync(new URL(`../src/${name}`, import.meta.url), 'utf8')
const require = createRequire(import.meta.url)
function compile(file, modules, globals = {}) {
  const exports = {}
  const js = ts.transpileModule(source(file), { compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX } }).outputText
  runInNewContext(js, { exports, ...globals, require: name => {
    assert.ok(Object.hasOwn(modules, name), `Unexpected import ${name}`); return modules[name]
  } })
  return exports
}
await check('real provider effect registers/cleans focus and never mutates on cleanup', async () => {
  const f = fixture(), window = new EventTarget(), cleanups = []
  const module = compile('study/StudySessionProvider.tsx', {
    react: { useState: initial => [initial()], useSyncExternalStore: (_s, get) => get(), useEffect: action => cleanups.push(action()) },
    'react/jsx-runtime': require('react/jsx-runtime'), './studySessionState.ts': { createStudySessionState: () => f.state },
    './studySessionContext.ts': { StudySessionContext: () => null },
  }, { window })
  module.StudySessionProvider({ client: f.client, revalidate: async () => {}, children: null })
  await f.state.refresh(); f.setActive(context); window.dispatchEvent(new Event('focus')); await f.state.refresh()
  assert.equal(f.state.getSnapshot().active.session.id, id)
  const readCount = f.calls.length; cleanups.forEach(cleanup => cleanup())
  window.dispatchEvent(new Event('focus')); await settle(); assert.equal(f.calls.length, readCount)
  assert.equal(f.state.getSnapshot().active, null); assert.ok(!f.calls.includes('complete') && !f.calls.includes('remove'))
})
function navigationHarness() {
  const location = new URL(`https://localhost/?map=${map}`), window = new EventTarget(), states = []
  let cursor = 0, initialized = false, effect
  const entries = [{ url: location.href, state: null }]; let position = 0
  const apply = url => { location.href = new URL(url, location).href }
  const history = {
    state: null,
    replaceState(state, _unused, url) { this.state = state; entries[position] = { url, state }; apply(url) },
    pushState(state, _unused, url) { this.state = state; entries.splice(++position, entries.length, { url, state }); apply(url) },
    back() { if (position > 0) { const entry = entries[--position]; this.state = entry.state; apply(entry.url); window.dispatchEvent(new Event('popstate')) } },
  }
  const module = compile('navigation/usePersistedMapNavigation.ts', {
    react: { useState: initial => {
      const index = cursor++
      if (!initialized) states[index] = typeof initial === 'function' ? initial() : initial
      return [states[index], next => { states[index] = typeof next === 'function' ? next(states[index]) : next }]
    }, useEffect: action => { if (!initialized) effect = action } },
    './mapLocation.ts': { mapUrl, readMapRoute },
  }, { location, history, crypto: globalThis.crypto,
    addEventListener: window.addEventListener.bind(window), removeEventListener: window.removeEventListener.bind(window) })
  const render = () => { cursor = 0; const result = module.usePersistedMapNavigation(); if (!initialized) { initialized = true; effect() } return result }
  return { render, entries }
}
await check('return to current map selects node without reload or extra history entry', () => {
  const h = navigationHarness(), visit = { ...target, requestId: 1 }
  h.render().openMap(map, visit); const next = h.render()
  assert.equal(next.nodeSelection, visit); assert.equal(next.revision, 0); assert.equal(h.entries.length, 1)
})
await check('map navigation and Back clear obsolete return-node requests', () => {
  const h = navigationHarness(), visit = { ...target, requestId: 1 }
  h.render().openMap(map, visit); h.render().openMap(other)
  assert.equal(h.render().nodeSelection, null); h.render().back()
  assert.equal(h.render().route.id, map); assert.equal(h.render().nodeSelection, null)
})
await check('return from another map preserves browser visit history and target', () => {
  const h = navigationHarness(), visit = { ...target, requestId: 2 }
  h.render().openMap(other); h.render().openMap(map, visit)
  assert.equal(h.render().nodeSelection, visit); assert.equal(h.entries.length, 3); assert.ok(h.render().canGoBack)
})
await check('provider lives above maps, mounted only by authenticated shell', () => {
  assert.match(source('App.tsx'), /session.status === 'anonymous'/)
  const s = source('study/AuthenticatedStudyWorkspace.tsx'); assert.ok(s.indexOf('<StudySessionProvider') < s.indexOf('<PersistedMaps'))
})
await check('provider focus listener cleans up without polling', () => {
  const s = source('study/StudySessionProvider.tsx'); assert.match(s, /addEventListener\('focus'/); assert.match(s, /removeEventListener\('focus'/); assert.doesNotMatch(s, /setInterval/)
})
await check('timer is not a live announcement or persistence loop', () => {
  const s = source('study/ActiveStudySessionIndicator.tsx'); assert.match(s, /role="timer" aria-live="off"/); assert.match(s, /clearInterval/); assert.doesNotMatch(s, /fetch\(|localStorage|sessionStorage/)
})
await check('discard and history deletion expose explicit confirm/cancel actions', () => {
  const indicator = source('study/ActiveStudySessionIndicator.tsx'), section = source('study/NodeStudySection.tsx')
  for (const label of ['Confirmar descarte', 'Cancelar descarte']) assert.ok(indicator.includes(label))
  for (const label of ['Confirmar exclusão da sessão', 'Cancelar exclusão da sessão']) assert.ok(section.includes(label))
})
await check('return-to-node uses existing navigation and selects/focuses correct node', () => {
  assert.match(source('components/PersistedMaps.tsx'), /navigation.openMap\(returnTo.mapId, returnTo\)/)
  assert.match(source('components/MapWorkspace.tsx'), /selected: node.id === returnTo.nodeId/)
  assert.match(source('components/MapWorkspace.tsx'), /getElementById\('details-title'\)\?\.focus/)
})
await check('Study Session requests remain encapsulated away from components', () => {
  for (const file of ['study/ActiveStudySessionIndicator.tsx', 'study/NodeStudySection.tsx', 'components/MapWorkspace.tsx'])
    assert.doesNotMatch(source(file), /X-CSRF-TOKEN|csrfToken|localStorage|sessionStorage/)
})
console.log(`${passed} Study Session checks PASS (API/state/time/history; UI wiring checks are not browser lifecycle evidence)`)
