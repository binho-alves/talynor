// Node 24, native TypeScript stripping, built-in assert: no test framework or dependency.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'
import { isContentDetail, isContentList, isContentSummary, isContentUrl, listContents, getContent, contentPath, contentLabel, reconcileSelectedContent } from '../src/api/knowledgeContentsApi.ts'
import { contentInput, contentPatch, contentWriteError } from '../src/api/knowledgeContentAuthoringApi.ts'
import { ApiError } from '../src/api/http.ts'
import { createSessionClient } from '../src/session/sessionClient.ts'
import { isKnowledgeMapList, isPersistedKnowledgeMap } from '../src/api/knowledgeMapsApi.ts'
import { readMapRoute, mapUrl } from '../src/navigation/mapLocation.ts'
import { adaptKnowledgeMap } from '../src/adapters/knowledgeMapAdapter.ts'
import { metadataPatch } from '../src/api/knowledgeMapAuthoringApi.ts'

let passed = 0
async function check(name, action) { try { await action(); passed++ } catch (error) { console.error(`FAIL: ${name}`); throw error } }
const map = '10000000-0000-0000-0000-000000000001', node = '20000000-0000-0000-0000-000000000001'
const id = '30000000-0000-0000-0000-000000000001', time = '2026-10-17T12:00:00.1234567+00:00'
const note = { id, type: 'note', title: null, body: 'literal\n<b>text</b>', createdAtUtc: time, updatedAtUtc: time }
const link = { ...note, type: 'link', url: 'https://localhost/docs', body: undefined }; delete link.body
const summary = { ...note }; delete summary.body
await check('note/detail discriminator', () => assert.ok(isContentDetail(note)))
await check('link/detail discriminator', () => assert.ok(isContentDetail(link)))
await check('summary excludes body', () => assert.ok(isContentSummary(summary) && !isContentSummary(note)))
await check('empty list', () => assert.ok(isContentList({ contents: [] })))
await check('duplicate ids including casing', () => assert.ok(!isContentList({ contents: [summary, { ...summary, id: id.toUpperCase() }] })))
for (const bad of [null, [], {}, { ...note, id: 'bad' }, { ...note, type: 'video' }, { ...note, title: 1 }, { ...note, title: ' ' },
  { ...note, body: ' ' }, { ...note, body: 'x'.repeat(4001) }, { ...note, url: null }, { ...link, body: null },
  { ...note, createdAtUtc: '2026-02-30T12:00:00Z' }, { ...note, updatedAtUtc: 'yesterday' }])
  await check('invalid detail', () => assert.equal(isContentDetail(bad), false))
for (const url of ['https://intranet/docs?q=1#part', 'http://127.0.0.1', 'http://[::1]/', 'HTTPS://Example.Invalid/Case'])
  await check('safe syntax accepted', () => assert.ok(isContentUrl(url)))
for (const url of ['javascript:alert(1)', 'data:text/html,x', '//host', 'https://@host', 'https://user:pass@host', 'https://host/\n', 'https://host\\evil', 'https://host/a b', 'https://host/' + 'x'.repeat(2048)])
  await check('unsafe syntax rejected', () => assert.equal(isContentUrl(url), false))
await check('title trim and literal body', () => assert.deepEqual(contentInput('note', ' Title ', '  body\n'), { type: 'note', title: 'Title', body: '  body\n' }))
await check('optional whitespace title', () => assert.equal(contentInput('note', ' ', 'body').title, null))
await check('unicode UTF16 limit', () => assert.equal(contentInput('note', '', '😀'.repeat(2000) + 'x'), null))
await check('title limit', () => assert.equal(contentInput('note', 'x'.repeat(201), 'body'), null))
await check('URL trim', () => assert.equal(contentInput('link', '', ' https://localhost/docs ').url, link.url))
await check('note noop', () => assert.deepEqual(contentPatch(note, { type: 'note', title: null, body: note.body }), {}))
await check('normalized title noop', () => assert.deepEqual(contentPatch({ ...note, title: 'T' }, { type: 'note', title: ' T ', body: note.body }), {}))
await check('clearTitle only', () => assert.deepEqual(contentPatch({ ...note, title: 'T' }, { type: 'note', title: null, body: note.body }), { clearTitle: true }))
await check('note patch excludes type', () => assert.deepEqual(contentPatch(note, { type: 'note', title: 'T', body: 'new' }), { title: 'T', body: 'new' }))
await check('link patch', () => assert.deepEqual(contentPatch(link, { type: 'link', title: null, url: ' https://localhost/new ' }), { url: 'https://localhost/new' }))
await check('type immutable', () => assert.throws(() => contentPatch(note, { type: 'link', title: null, url: link.url })))
await check('fallbacks', () => assert.equal(contentLabel(note), 'Nota'))
await check('link fallback', () => assert.equal(contentLabel(link), link.url))
await check('nested paths', () => assert.equal(contentPath(map, node, id), `/api/maps/${map}/nodes/${node}/contents/${id}`))
await check('invalid path not sent', () => assert.throws(() => contentPath('bad', node)))
await check('new list removes missing selection', () => assert.equal(reconcileSelectedContent(id, []), null))
await check('new list preserves created content', () => assert.equal(reconcileSelectedContent(id, [summary]), id))
await check('empty selection remains empty', () => assert.equal(reconcileSelectedContent(null, [summary]), null))

// Execute the real component with controlled hook results. This verifies reconciliation/render
// wiring, not React's effect scheduling or browser lifecycle (those remain separate evidence).
const require = createRequire(import.meta.url)
const compiledSection = ts.transpileModule(readFileSync(new URL('../src/components/ContentSection.tsx', import.meta.url), 'utf8'),
  { compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX } }).outputText
function renderSection(listState, previousList, detailState = { status: 'ready', data: note }, editor = 'edit') {
  const states = [id, editor, 0, previousList], detailKeys = []
  let cursor, reads, changed
  const exports = {}
  const react = { useState: () => { const index = cursor++; return [states[index], (value) => {
    states[index] = typeof value === 'function' ? value(states[index]) : value; changed = true
  }] }, useRef: (value) => ({ current: value }), useEffect: () => {}, useCallback: (fn) => fn }
  const modules = {
    react, 'react/jsx-runtime': require('react/jsx-runtime'), '../api/http.ts': { ApiError },
    '../api/knowledgeContentsApi.ts': { contentLabel, reconcileSelectedContent },
    '../api/knowledgeContentAuthoringApi.ts': { contentPatch, contentWriteError },
    './ContentForm.tsx': { ContentForm: () => null }, './useContentRead.ts': { useContentRead: (key) => {
      if (reads++ === 0) return { state: listState, reload: () => {} }
      detailKeys.push(key)
      return { state: key === null ? { status: 'loading' } : detailState, reload: () => {} }
    } },
  }
  runInNewContext(compiledSection, { exports, require: (name) => {
    assert.ok(Object.hasOwn(modules, name), `Unexpected import: ${name}`); return modules[name]
  } })
  let tree
  for (let attempt = 0; attempt < 5; attempt++) {
    cursor = 0; reads = 0; changed = false
    tree = exports.ContentSection({ mapId: map, nodeId: node, nodeTitle: 'Node', editing: true, client: {},
      mutation: { pending: false, isPending: () => false }, revalidate: async () => {} })
    if (!changed) return { states, detailKeys, tree }
  }
  assert.fail('Reconciliation did not settle')
}
function treeContains(tree, predicate) {
  if (Array.isArray(tree)) return tree.some((item) => treeContains(item, predicate))
  return !!tree && typeof tree === 'object' && (predicate(tree) || treeContains(tree.props?.children, predicate))
}
await check('external disappearance closes selected detail and editor', () => {
  const result = renderSection({ status: 'ready', data: [] }, [summary])
  assert.equal(result.states[0], null); assert.equal(result.states[1], null)
  assert.equal(result.detailKeys.at(-1), null)
  assert.equal(treeContains(result.tree, (element) => element.type === 'article'), false)
})
await check('external disappearance removes obsolete detail retry', () => {
  const result = renderSection({ status: 'ready', data: [] }, [summary], { status: 'error', message: 'unavailable' })
  assert.equal(treeContains(result.tree, (element) => element.props?.children === 'Tentar carregar detalhe novamente'), false)
})
await check('create does not reconcile against unchanged old list', () => {
  const oldList = []
  assert.equal(renderSection({ status: 'ready', data: oldList }, oldList).states[0], id)
})
await check('new create list preserves selected content', () => {
  const result = renderSection({ status: 'ready', data: [summary] }, [])
  assert.equal(result.states[0], id); assert.equal(result.states[1], 'edit')
})
for (const status of ['loading', 'error']) await check('non-ready/stale list cannot reconcile selection', () => {
  const result = renderSection({ status, message: 'unavailable' }, [summary])
  assert.equal(result.states[0], id); assert.equal(result.states[1], 'edit')
})
function readStateForContext(key, loaded) {
  let index = 0
  const exports = {}, initial = [0, loaded]
  const compiled = ts.transpileModule(readFileSync(new URL('../src/components/useContentRead.ts', import.meta.url), 'utf8'),
    { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText
  runInNewContext(compiled, { exports, AbortController, require: (name) => {
    if (name === '../api/http.ts') return { ApiError }
    assert.equal(name, 'react')
    return { useState: () => [initial[index++], () => {}], useEffect: () => {} }
  } })
  return exports.useContentRead(key, async () => [], async () => {}).state
}
await check('old node list identity cannot clear current selection', () => {
  const state = readStateForContext('node-b', { identity: 'node-a:0', status: 'ready', data: [] })
  assert.equal(state.status, 'loading')
  assert.equal(renderSection(state, [summary]).states[0], id)
})
await check('cleared detail identity hides late old data and errors', () => {
  for (const status of ['ready', 'error']) {
    const state = readStateForContext(null, { identity: `${id}:0`, status, data: note, message: 'old error' })
    assert.equal(state.status, 'loading'); assert.equal(Object.hasOwn(state, 'data'), false)
    assert.equal(Object.hasOwn(state, 'message'), false)
  }
})

const originalFetch = globalThis.fetch
const calls = []
let mode = 'ok', posted = note
globalThis.fetch = async (path, init) => {
  calls.push({ path, init })
  if (path === '/api/auth/csrf') return Response.json({ token: 'synthetic-test-only' })
  if (mode === 'network') throw new TypeError('synthetic network failure')
  if (mode !== 'ok') return Response.json({ status: Number(mode), code: ({ 400: 'csrf_invalid', 401: 'authentication_required', 404: 'content_not_found', 500: 'unexpected_error' })[mode], detail: 'UNTRUSTED' }, { status: Number(mode) })
  if (path === '/api/auth/me') return Response.json({ id: 'test-user', email: 'synthetic@example.invalid' })
  if (path === '/api/workspace') return Response.json({ id: map, createdAtUtc: time })
  if (path === '/api/auth/login' || path === '/api/auth/logout') return new Response(null, { status: 204 })
  if (init.method === 'POST') return Response.json(posted, { status: 201 })
  if (init.method === 'PATCH' || init.method === 'DELETE') return new Response(null, { status: 204 })
  return Response.json(path.endsWith('/' + id) ? note : { contents: [summary] })
}
try {
  const signal = new AbortController().signal, client = createSessionClient().contentAuthoring
  await check('list uses same-origin/no-store/no CSRF and own projection', async () => {
    const items = await listContents(map, node, signal), call = calls.at(-1)
    assert.deepEqual(items, [summary]); assert.equal(call.init.credentials, 'same-origin'); assert.equal(call.init.cache, 'no-store')
    assert.equal(call.init.headers, undefined); assert.equal(call.init.redirect, 'error')
  })
  await check('detail exact path', async () => assert.deepEqual(await getContent(map, node, id, signal), note))
  await check('create renews CSRF once, sends one POST and validates body', async () => {
    calls.length = 0; await client.createContent(map, node, { type: 'note', title: null, body: 'body' }, signal)
    assert.deepEqual(calls.map(c => c.path), ['/api/auth/csrf', contentPath(map, node)])
    assert.deepEqual(JSON.parse(calls[1].init.body), { type: 'note', title: null, body: 'body' })
    assert.ok(calls[1].init.headers['X-CSRF-TOKEN'])
  })
  await check('link create own payload', async () => {
    posted = link; await client.createContent(map, node, { type: 'link', title: null, url: link.url }, signal)
    assert.deepEqual(JSON.parse(calls.at(-1).init.body), { type: 'link', title: null, url: link.url }); posted = note
  })
  await check('create rejects wrong discriminator', async () => {
    posted = link; await assert.rejects(client.createContent(map, node, { type: 'note', title: null, body: 'body' }, signal), ApiError); posted = note
  })
  await check('patch exact path and clear flag', async () => {
    await client.updateContent(map, node, id, { clearTitle: true }, signal)
    assert.equal(calls.at(-1).path, contentPath(map, node, id)); assert.equal(calls.at(-1).init.method, 'PATCH')
    assert.deepEqual(JSON.parse(calls.at(-1).init.body), { clearTitle: true })
  })
  await check('delete has no payload', async () => {
    await client.deleteContent(map, node, id, signal); assert.equal(calls.at(-1).init.method, 'DELETE'); assert.equal(calls.at(-1).init.body, undefined)
  })
  for (const failure of ['400', '401', '404', '500', 'network']) for (const operation of [
    () => client.createContent(map, node, { type: 'note', title: null, body: 'body' }, signal),
    () => client.updateContent(map, node, id, { title: 'T' }, signal), () => client.deleteContent(map, node, id, signal)])
    await check('no replay on failure', async () => {
      mode = failure; calls.length = 0
      await assert.rejects(operation(), error => { assert.ok(error instanceof ApiError); assert.ok(!contentWriteError(error).includes('UNTRUSTED')); return true })
      assert.equal(calls.length, 2); assert.equal(calls[0].path, '/api/auth/csrf')
    })
  mode = 'ok'
  await check('pre-cancelled reads send nothing', async () => { calls.length = 0; await assert.rejects(listContents(map, node, AbortSignal.abort())); assert.equal(calls.length, 0) })
  await check('existing session bootstrap sequence', async () => {
    calls.length = 0; const result = await createSessionClient().bootstrap(signal)
    assert.equal(result.status, 'authenticated'); assert.deepEqual(calls.map(c => c.path), ['/api/auth/me', '/api/workspace'])
  })
  await check('existing login sequence', async () => {
    calls.length = 0; await createSessionClient().login('synthetic@example.invalid', 'synthetic-test-only', signal)
    assert.deepEqual(calls.map(c => c.path), ['/api/auth/csrf', '/api/auth/login', '/api/auth/csrf', '/api/auth/me', '/api/workspace'])
  })
  await check('existing logout sequence', async () => {
    calls.length = 0; assert.equal((await createSessionClient().logout(signal)).status, 'anonymous')
    assert.deepEqual(calls.map(c => c.path), ['/api/auth/csrf', '/api/auth/logout'])
  })
  await check('existing login never replays credentials on CSRF failure', async () => {
    mode = '400'; calls.length = 0; await assert.rejects(createSessionClient().login('synthetic@example.invalid', 'synthetic-test-only', signal))
    assert.deepEqual(calls.map(c => c.path), ['/api/auth/csrf', '/api/auth/login', '/api/auth/csrf']); mode = 'ok'
  })
  await check('existing graph create client remains usable', async () => {
    posted = { id: map, title: 'Map', description: null, createdAtUtc: time, updatedAtUtc: time }
    const result = await createSessionClient().authoring.createMap({ title: 'Map', description: null }, signal)
    assert.equal(result.id, map); assert.equal(calls.at(-1).path, '/api/maps'); posted = note
  })
  for (const operation of [
    authoring => authoring.updateMap(map, { title: 'Changed' }, signal), authoring => authoring.deleteMap(map, signal),
    authoring => authoring.updateNode(map, node, { position: { x: 2, y: 3 } }, signal), authoring => authoring.deleteNode(map, node, signal),
    authoring => authoring.deleteEdge(map, id, signal)])
    await check('existing graph write renews CSRF once', async () => {
      calls.length = 0; await operation(createSessionClient().authoring); assert.equal(calls.length, 2)
      assert.equal(calls[0].path, '/api/auth/csrf'); assert.ok(calls[1].init.headers['X-CSRF-TOKEN'])
    })
} finally { globalThis.fetch = originalFetch }

const graph = { id: map, title: 'Map', description: null, nodes: [{ id: node, title: 'N', description: null, childMapId: null, position: { x: 1, y: 2 } }], edges: [] }
await check('existing map DTO remains independent', () => assert.ok(isPersistedKnowledgeMap(graph)))
await check('existing adapter coordinates', () => assert.deepEqual(adaptKnowledgeMap(graph).nodes[0].position, { x: 1, y: 2 }))
await check('existing discovery', () => assert.ok(isKnowledgeMapList({ maps: [{ id: map, title: 'M' }] })))
await check('existing map route', () => assert.deepEqual(readMapRoute(`?map=${map}`), { kind: 'map', id: map }))
await check('duplicate map query invalid', () => assert.deepEqual(readMapRoute(`?map=${map}&map=${map}`), { kind: 'invalid' }))
await check('content does not change map URL builder', () => assert.equal(mapUrl('https://localhost/?map=' + map, null), '/'))
await check('existing metadata clear semantics', () => assert.deepEqual(metadataPatch({ title: 'M', description: 'old' }, { title: 'M', description: null }), { clearDescription: true }))
const source = file => readFileSync(new URL('../src/' + file, import.meta.url), 'utf8')
await check('safe link markup', () => assert.match(source('components/ContentSection.tsx'), /target="_blank" rel="noopener noreferrer"/))
await check('literal note and no active HTML', () => { assert.match(source('App.css'), /white-space: pre-wrap/); assert.doesNotMatch(source('components/ContentSection.tsx'), /dangerouslySetInnerHTML|<iframe/) })
await check('graph stays presentation-only', () => assert.doesNotMatch(source('components/KnowledgeGraph.tsx'), /fetch\(|useContentRead|X-CSRF-TOKEN/))
await check('shared mutation coordinator', () => assert.match(source('components/MapWorkspace.tsx'), /client=\{contentAuthoring\} mutation=\{mutation\}/))
await check('read cleanup and identity guards', () => { const s = source('components/useContentRead.ts'); assert.match(s, /current = false; controller.abort\(\)/); assert.match(s, /loaded.identity === identity/) })
await check('node/mode remount clears local context', () => assert.match(source('components/MapWorkspace.tsx'), /key=\{`\$\{selected.id\}:edit`\}/))
console.log(`${passed} frontend content/baseline checks PASS`)
