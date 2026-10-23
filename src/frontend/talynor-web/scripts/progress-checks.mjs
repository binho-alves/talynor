// Existing Node, TypeScript and React only; controlled hooks are not a real-browser lifecycle audit.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'
import { renderToStaticMarkup } from 'react-dom/server'
import { getNodeProgress, isNodeProgress } from '../src/api/nodeProgressApi.ts'
import { ApiError } from '../src/api/http.ts'
import { createNodeProgressState } from '../src/progress/nodeProgressState.ts'

let passed = 0
async function check(name, run) { try { await run(); passed++ } catch (error) { console.error(`FAIL: ${name}`); throw error } }
const map = '10000000-0000-0000-0000-000000000001', node = '20000000-0000-0000-0000-000000000001'
const other = '30000000-0000-0000-0000-000000000001', account = 'account-a'
const now = '2026-10-23T12:00:00.1234567Z'
function summary(days = 30, empty = false) {
  return { calculatedAtUtc: now, period: { days, fromUtc: new Date(Date.parse(now) - days * 86400000).toISOString().replace('.123Z', '.1234567Z'), toUtc: now },
    study: { completedSessionCount: empty ? 0 : 4, totalDurationSeconds: empty ? 0 : 7201 },
    flashcards: { reviewCount: empty ? 0 : 3, correctCount: empty ? 0 : 2, correctPercentage: empty ? null : 66.67 },
    quizzes: { attemptCount: empty ? 0 : 2, answeredQuestionCount: empty ? 0 : 11, correctAnswerCount: empty ? 0 : 6, accuracyPercentage: empty ? null : 54.55 } }
}
const settle = () => new Promise(resolve => setImmediate(resolve))
function deferred() { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b }); return { promise, resolve, reject } }
const originalFetch = globalThis.fetch
await check('populated and empty scalar contracts accepted', () => { assert.ok(isNodeProgress(summary())); assert.ok(isNodeProgress(summary(30, true))) })
for (const [name, mutate] of [
  ['root extra', v => v.private = 'secret'], ['nested extra', v => v.flashcards.front = 'secret'],
  ['missing field', v => delete v.quizzes.answeredQuestionCount], ['unsafe count', v => v.study.totalDurationSeconds = Number.MAX_SAFE_INTEGER + 1],
  ['negative', v => v.flashcards.reviewCount = -1], ['fractional count', v => v.quizzes.attemptCount = 1.5],
  ['NaN', v => v.flashcards.correctPercentage = NaN], ['infinite', v => v.quizzes.accuracyPercentage = Infinity],
  ['ratio bounds', v => v.quizzes.accuracyPercentage = 101], ['ratio precision', v => v.flashcards.correctPercentage = 66.667],
  ['correct > reviews', v => v.flashcards.correctCount = 4], ['correct > questions', v => v.quizzes.correctAnswerCount = 12],
  ['attempts > questions', v => v.quizzes.attemptCount = 12], ['zero attempts nonzero questions', v => v.quizzes.attemptCount = 0],
  ['zero sessions nonzero duration', v => v.study.completedSessionCount = 0], ['null with denominator', v => v.flashcards.correctPercentage = null],
  ['invalid days', v => v.period.days = 31], ['non UTC', v => v.period.toUtc = now.replace('Z', '-03:00')],
  ['wrong interval', v => v.period.fromUtc = now], ['wrong calculatedAt', v => v.calculatedAtUtc = '2026-10-23T12:00:01Z'],
  ['submillisecond drift', v => v.calculatedAtUtc = now.replace('4567', '4568')],
  ['invalid calendar', v => v.period.fromUtc = '2026-02-30T12:00:00Z'],
]) await check(`runtime rejects ${name}`, () => { const v = summary(); mutate(v); assert.equal(isNodeProgress(v), false) })
await check('null differs from measured zero and zero duration with session is valid', () => {
  const v = summary(30, true); v.flashcards.correctPercentage = 0; assert.equal(isNodeProgress(v), false)
  v.flashcards.reviewCount = 1; assert.ok(isNodeProgress(v)); v.study.completedSessionCount = 1; assert.ok(isNodeProgress(v))
})
await check('JS safe maximum preserves exact integer without clamping or local recomputation', () => {
  const v = summary(); v.study.totalDurationSeconds = Number.MAX_SAFE_INTEGER; v.quizzes.accuracyPercentage = 3.13; assert.ok(isNodeProgress(v))
})
try {
  for (const days of [7, 30, 90]) await check(`GET ${days}: same origin no-store, only days, no CSRF or Bearer`, async () => {
    const calls = []; globalThis.fetch = async (path, init) => { calls.push({ path, init }); return Response.json(summary(days)) }
    assert.deepEqual(await getNodeProgress(map, node, days, new AbortController().signal), summary(days))
    assert.equal(calls.length, 1); assert.equal(calls[0].path, `/api/maps/${map}/nodes/${node}/progress?days=${days}`)
    assert.equal(calls[0].init.credentials, 'same-origin'); assert.equal(calls[0].init.cache, 'no-store'); assert.equal(calls[0].init.headers, undefined)
    assert.equal(calls[0].init.body, undefined)
  })
  await check('wrong response preset and invalid JSON never become zero activity', async () => {
    globalThis.fetch = async () => Response.json(summary(7)); await assert.rejects(getNodeProgress(map, node, 30, new AbortController().signal), ApiError)
    globalThis.fetch = async () => new Response('{broken'); await assert.rejects(getNodeProgress(map, node, 30, new AbortController().signal), ApiError)
  })
  await check('invalid IDs and cancelled request send nothing', async () => {
    globalThis.fetch = async () => { assert.fail('must not fetch') }
    await assert.rejects(getNodeProgress('bad', node, 30, new AbortController().signal))
    await assert.rejects(getNodeProgress(map, node, 30, AbortSignal.abort()))
  })
  for (const [status, code] of [[401, 'authentication_required'], [403, 'access_denied'], [404, 'progress_not_found'], [500, 'unexpected_error']])
    await check(`HTTP ${status} stays error without exposing server detail`, async () => {
      let calls = 0; globalThis.fetch = async () => { calls++; return Response.json({ status, code, detail: 'private' }, { status }) }
      await assert.rejects(getNodeProgress(map, node, 30, new AbortController().signal), e => e instanceof ApiError && e.status === status && !e.message.includes('private'))
      assert.equal(calls, 1)
    })
} finally { globalThis.fetch = originalFetch }

function fixture(a = account, m = map, n = node) {
  const calls = []; let auth = 0
  const state = createNodeProgressState(a, m, n, async () => { auth++ }, (...args) => { const d = deferred(); calls.push({ args, ...d }); return d.promise })
  state.mount(); return { state, calls, auth: () => auth }
}
await check('lazy mount, default 30, explicit refresh and reopen always refetch', async () => {
  const f = fixture(); assert.equal(f.calls.length, 0); assert.equal(f.state.getSnapshot().status, 'closed'); assert.equal(f.state.getSnapshot().days, 30)
  const opening = f.state.open(); assert.equal(f.calls.length, 1); assert.equal(f.calls[0].args[2], 30)
  f.calls[0].resolve(summary()); await opening; assert.equal(f.state.getSnapshot().status, 'ready')
  await f.state.open(); assert.equal(f.calls.length, 1)
  const refresh = f.state.refresh(); assert.equal(f.state.getSnapshot().status, 'loading'); assert.ok(!('data' in f.state.getSnapshot()))
  f.calls[1].resolve(summary()); await refresh
  f.state.close(); assert.ok(!('data' in f.state.getSnapshot())); const reopen = f.state.open(); f.calls[2].resolve(summary()); await reopen
  await settle(); assert.equal(f.calls.length, 3); f.state.dispose()
})
await check('switch 7/30/90 aborts and ignores every obsolete request even if transport ignores abort', async () => {
  const f = fixture(); const first = f.state.open(), seven = f.state.selectDays(7), thirty = f.state.selectDays(30), ninety = f.state.selectDays(90)
  assert.deepEqual(f.calls.map(c => c.args[2]), [30, 7, 30, 90]); assert.ok(f.calls.slice(0, 3).every(c => c.args[3].aborted))
  f.calls[3].resolve(summary(90)); await ninety
  for (let i = 0; i < 3; i++) f.calls[i].resolve(summary(f.calls[i].args[2]))
  await Promise.all([first, seven, thirty]); assert.equal(f.state.getSnapshot().data.period.days, 90); f.state.dispose()
})
await check('same-preset refresh generation prevents an older response overwriting the new result', async () => {
  const f = fixture(); const old = f.state.open(), fresh = f.state.refresh(); f.calls[1].resolve(summary()); await fresh
  const prior = summary(30, true); f.calls[0].resolve(prior); await old; assert.equal(f.state.getSnapshot().data.study.completedSessionCount, 4); f.state.dispose()
})
for (const [name, a, m, n] of [['account', 'account-b', map, node], ['map', account, other, node], ['node', account, map, other]])
  await check(`${name} visit: old calls cannot update new visit, including an A-B-A return`, async () => {
    const old = fixture(), pending = old.state.open(); old.state.dispose()
    const next = fixture(a, m, n), read = next.state.open(); next.calls[0].resolve(summary()); await read
    const back = fixture(), backRead = back.state.open(); back.calls[0].resolve(summary(30, true)); await backRead
    old.calls[0].resolve(summary()); await pending
    assert.equal(next.state.getSnapshot().data.study.completedSessionCount, 4); assert.equal(back.state.getSnapshot().data.study.completedSessionCount, 0)
    assert.equal(old.state.getSnapshot().status, 'closed'); next.state.dispose(); back.state.dispose()
  })
for (const [name, error, status] of [['network', new ApiError('network'), 'error'], ['protocol', new ApiError('invalid-response'), 'error'],
  ['500', new ApiError('http', 500), 'error'], ['404', new ApiError('http', 404, 'progress_not_found'), 'not-found'], ['401', new ApiError('http', 401), 'error']])
  await check(`${name} distinct failure with explicit retry and no automatic replay`, async () => {
    const f = fixture(), read = f.state.open(); f.calls[0].reject(error); await read; await settle()
    assert.equal(f.state.getSnapshot().status, status); assert.ok(!('data' in f.state.getSnapshot())); assert.equal(f.calls.length, 1)
    assert.equal(f.auth(), name === '401' ? 1 : 0)
    const retry = f.state.refresh(); f.calls[1].resolve(summary()); await retry; assert.equal(f.state.getSnapshot().status, 'ready'); f.state.dispose()
  })
await check('stale 401 does not revalidate current identity and close aborts without publication', async () => {
  const f = fixture(), read = f.state.open(); f.state.close(); f.calls[0].reject(new ApiError('http', 401)); await read
  assert.equal(f.auth(), 0); assert.equal(f.state.getSnapshot().status, 'closed'); f.state.dispose()
})
await check('401 revalidation rejection remains controlled and never replays', async () => {
  let calls = 0; const state = createNodeProgressState(account, map, node, async () => { throw Error('private') }, async () => { calls++; throw new ApiError('http', 401) })
  state.mount(); await state.open(); assert.equal(state.getSnapshot().status, 'error'); assert.equal(calls, 1); state.dispose()
})

const require = createRequire(import.meta.url), runtime = require('react/jsx-runtime')
const source = name => readFileSync(new URL('../src/' + name, import.meta.url), 'utf8')
function compile(name, modules) {
  const exports = {}, js = ts.transpileModule(source(name), { compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX } }).outputText
  runInNewContext(js, { exports, Intl, Date, require: name => { assert.ok(Object.hasOwn(modules, name), name); return modules[name] } }); return exports
}
function elements(tree, predicate) {
  if (Array.isArray(tree)) return tree.flatMap(t => elements(t, predicate))
  return tree && typeof tree === 'object' ? [...(predicate(tree) ? [tree] : []), ...elements(tree.props?.children, predicate)] : []
}
const text = value => Array.isArray(value) ? value.map(text).join('') : typeof value === 'string' || typeof value === 'number' ? String(value) : ''
await check('real section markup: accessible disclosure, default radios, statuses, null vs zero, server values and timezone', async () => {
  const f = fixture(); let cleanup
  const react = { useState: () => [f.state], useEffect: fn => { cleanup = fn() }, useId: () => 'progress', useSyncExternalStore: (_, snapshot) => snapshot() }
  const { NodeProgressSection } = compile('progress/NodeProgressSection.tsx', { react, 'react/jsx-runtime': runtime, './nodeProgressState.ts': { createNodeProgressState } })
  const props = { accountId: account, mapId: map, nodeId: node, revalidate: async () => {} }
  const inner = NodeProgressSection(props); assert.ok(inner.key.includes(account)); assert.ok(inner.key.includes(node))
  const render = () => inner.type(inner.props)
  let tree = render(); assert.equal(f.calls.length, 0)
  const button = label => elements(tree, e => e.type === 'button' && text(e.props.children) === label)[0]
  assert.equal(button('Ver atividade e resultados').props['aria-expanded'], false); button('Ver atividade e resultados').props.onClick()
  tree = render(); assert.match(renderToStaticMarkup(tree), /Carregando atividade/)
  const radios = elements(tree, e => e.type === 'input'); assert.deepEqual(radios.map(r => r.props.value), [7, 30, 90]); assert.equal(radios[1].props.checked, true)
  f.calls[0].resolve(summary(30, true)); await settle(); tree = render()
  // Render real Metrics with React SSR; hooks are controlled only in the outer component.
  let html = renderToStaticMarkup(tree); assert.match(html, /Nenhuma atividade/); assert.match(html, /Sem respostas registradas/); assert.ok(!html.includes('0%'))
  assert.match(html, /Fuso:/); assert.match(html, /Calculado em:/); assert.match(html, /Conclusão de conteúdo não é acompanhada/)
  button('Atualizar atividade e resultados').props.onClick(); f.calls[1].resolve(summary()); await settle(); tree = render(); html = renderToStaticMarkup(tree)
  assert.match(html, /66,67%/); assert.match(html, /54,55%/); assert.match(html, /7.201 segundos/)
  const v = summary(); v.flashcards.correctCount = 0; v.flashcards.correctPercentage = 0
  button('Atualizar atividade e resultados').props.onClick(); f.calls[2].resolve(v); await settle(); tree = render(); assert.match(renderToStaticMarkup(tree), />0%/)
  button('Fechar atividade e resultados').props.onClick(); tree = render(); assert.equal(elements(tree, e => e.props?.hidden === true).length, 1); cleanup()
})
for (const outcome of ['error', 'not-found', 'ready']) await check(`retry focus control remains mounted/focusable through loading and ${outcome}`, async () => {
  const f = fixture(); let cleanup
  const react = { useState: () => [f.state], useEffect: fn => { cleanup = fn() }, useId: () => 'progress-focus', useSyncExternalStore: (_, snapshot) => snapshot() }
  const { NodeProgressSection } = compile('progress/NodeProgressSection.tsx', { react, 'react/jsx-runtime': runtime, './nodeProgressState.ts': { createNodeProgressState } })
  const visit = NodeProgressSection({ accountId: account, mapId: map, nodeId: node, revalidate: async () => {} })
  const render = () => visit.type(visit.props)
  const action = () => elements(render(), e => e.key === 'read-action')[0]
  render(); const initial = f.state.open(); f.calls[0].reject(new ApiError('http', 500)); await initial
  const retry = action(); assert.equal(text(retry.props.children), 'Tentar novamente')
  retry.props.onClick(); const loading = action()
  assert.equal(loading.type, retry.type); assert.equal(loading.key, retry.key)
  assert.equal(loading.props.disabled, undefined); assert.equal(loading.props['aria-disabled'], true)
  assert.equal(loading.props.autoFocus, undefined)
  loading.props.onClick(); assert.equal(f.calls.length, 2) // aria-disabled still needs a synchronous activation guard.
  if (outcome === 'ready') f.calls[1].resolve(summary())
  else f.calls[1].reject(new ApiError('http', outcome === 'error' ? 500 : 404))
  await settle(); const result = action()
  assert.equal(result.key, retry.key); assert.equal(result.type, retry.type)
  assert.equal(result.props.disabled, undefined); assert.equal(result.props['aria-disabled'], false)
  assert.equal(text(result.props.children), outcome === 'ready' ? 'Atualizar atividade e resultados' : 'Tentar novamente')
  assert.equal(f.state.getSnapshot().status, outcome); cleanup()
})
await check('real MapWorkspace places read-only section outside forms in View and Edit without changing graph navigation', () => {
  let edit = false, slots, cursor
  const react = { lazy: () => () => null, Suspense: 'div', useEffect() {}, useRef: value => ({ current: value }),
    useState(initial) { const i = cursor++; if (!(i in slots)) slots[i] = typeof initial === 'function' ? initial() : initial; return [i === 1 ? edit : slots[i], () => {}] } }
  const stub = () => null, progress = () => null, modules = { react, 'react/jsx-runtime': runtime,
    '@xyflow/react': { applyNodeChanges: () => {} }, '../api/knowledgeMapAuthoringApi.ts': { metadataPatch: () => ({}) }, '../api/http.ts': { ApiError },
    '../api/knowledgeMapsApi.ts': { listPersistedKnowledgeMaps: async () => ({ maps: [] }) }, './KnowledgeGraph.tsx': { KnowledgeGraph: stub },
    './MetadataForm.tsx': { MetadataForm: stub }, './useAuthoring.ts': { useAuthoring: () => ({ pending: false, feedback: '' }) }, './NodeDetails.tsx': { NodeDetails: stub },
    './ContentSection.tsx': { ContentSection: stub }, '../study/NodeStudySection.tsx': { NodeStudySection: stub },
    '../flashcards/FlashcardAuthoring.tsx': { FlashcardAuthoring: stub }, '../flashcards/FlashcardStudy.tsx': { FlashcardStudy: stub },
    '../progress/NodeProgressSection.tsx': { NodeProgressSection: progress } }
  const { MapWorkspace } = compile('components/MapWorkspace.tsx', modules)
  for (edit of [false, true]) {
    slots = []; cursor = 0
    const tree = MapWorkspace({ accountId: account, id: map, initial: { title: 'Map', nodes: [{ id: node, selected: true, position: { x: 0, y: 0 }, data: { title: 'Concept' } }], edges: [] },
      returnTo: null, revalidate: async () => {}, onOpenMap() {} })
    const panels = elements(tree, e => e.type === progress); assert.equal(panels.length, 1); assert.equal(panels[0].props.accountId, account); assert.equal(panels[0].props.nodeId, node)
    for (const form of elements(tree, e => e.type === 'form')) assert.equal(elements(form, e => e.type === progress).length, 0)
  }
})
console.log(`Progress checks: ${passed} PASS`)
