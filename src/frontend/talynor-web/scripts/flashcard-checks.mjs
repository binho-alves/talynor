// Node 24 + existing TypeScript transpiler/React renderer. No new framework or package.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { runInNewContext } from 'node:vm'
import { renderToStaticMarkup } from 'react-dom/server'
import ts from 'typescript'
import { ApiError } from '../src/api/http.ts'
import { createSessionClient } from '../src/session/sessionClient.ts'
import { flashcardInput, flashcardPatch, isFlashcard, isFlashcardPage, isStudyNext, isReviewEnvelope, isReveal, isReviewState, matchingReview, flashcardError } from '../src/api/flashcardsApi.ts'
import { createFlashcardStudyState } from '../src/flashcards/flashcardStudyState.ts'
import { createFlashcardListState } from '../src/flashcards/flashcardListState.ts'

let passed = 0
async function check(name, action) { try { await action(); passed++ } catch (error) { console.error(`FAIL: ${name}`); throw error } }
const map = '10000000-0000-0000-0000-000000000001', node = '20000000-0000-0000-0000-000000000001'
const id = '30000000-0000-0000-0000-000000000001', other = '40000000-0000-0000-0000-000000000001', rid = '50000000-0000-0000-0000-000000000001'
const time = '2026-10-21T12:00:00.1234567+00:00', secret = 'ANSWER-ONLY-AFTER-EXPLICIT-REVEAL'
const counts = { contentRevision: 1, reviewCount: 0, correctCount: 0, lastReviewedAtUtc: null }
const card = { id, knowledgeNodeId: node, front: '  literal <b>question</b>\n', back: secret, contentRevision: 1, createdAtUtc: time, updatedAtUtc: time }
const prompt = { id, front: card.front, contentRevision: 1, reviewState: counts }
const pending = { reviewId: rid, flashcardId: id, contentRevision: 1, result: 'correct' }
const event = { review: { ...pending, reviewedAtUtc: time }, currentReviewState: { ...counts, reviewCount: 1, correctCount: 1, lastReviewedAtUtc: time } }
const page = (cards = [], p = 1, hasMore = false) => ({ flashcards: cards, page: p, pageSize: 20, hasMore })
const signal = () => new AbortController().signal
const settle = () => new Promise(resolve => setImmediate(resolve))
function deferred() { let resolve; const promise = new Promise(r => { resolve = r }); return { promise, resolve } }
for (const [name, value, validate] of [['card', card, isFlashcard], ['page', page([card]), isFlashcardPage], ['next', { item: prompt }, isStudyNext],
  ['empty next', { item: null }, isStudyNext], ['reveal', { flashcardId: id, contentRevision: 1, back: secret }, isReveal], ['event', event, isReviewEnvelope]])
  await check(`valid ${name}`, () => assert.ok(validate(value)))
for (const bad of [null, {}, { ...card, contentRevision: 0 }, { ...card, contentRevision: 2147483648 }, { ...card, id: 'bad' },
  { ...card, front: ' ' }, { ...card, back: 'x'.repeat(4001) }, { ...card, createdAtUtc: '2026-02-30T12:00:00Z' }])
  await check('malformed authoring record', () => assert.equal(isFlashcard(bad), false))
for (const bad of [{ item: { ...prompt, back: secret } }, { item: { ...prompt, reviewState: { ...counts, back: secret } } },
  { item: { ...prompt, reviewState: { ...counts, contentRevision: 2 } } }, { item: undefined }])
  await check('study does not accept Back or mismatched revision', () => assert.equal(isStudyNext(bad), false))
for (const bad of [{ ...counts, reviewCount: -1 }, { ...counts, correctCount: 1 }, { ...counts, reviewCount: 1 }, { ...counts, lastReviewedAtUtc: time },
  { ...counts, reviewCount: 1.5 }, { ...counts, contentRevision: '1' }])
  await check('invalid current progress', () => assert.equal(isReviewState(bad), false))
for (const bad of [page([card, card]), { ...page(), page: 0 }, { ...page(), pageSize: 101 }, { ...page(), hasMore: 1 }])
  await check('invalid authoring page', () => assert.equal(isFlashcardPage(bad), false))
await check('UTF16 preserves whitespace, emoji boundaries and newlines', () => {
  assert.deepEqual(flashcardInput(card.front, '  back\n'), { front: card.front, back: '  back\n' })
  assert.ok(flashcardInput('😀'.repeat(1000), '😀'.repeat(2000)))
  assert.equal(flashcardInput('😀'.repeat(1000) + 'x', 'back'), null)
  assert.equal(flashcardInput('front', '😀'.repeat(2000) + 'x'), null)
  assert.equal(flashcardInput(' \n', 'back'), null)
})
await check('PATCH omits unchanged fields and forbids intended noop', () => {
  assert.deepEqual(flashcardPatch(card, card), {})
  assert.deepEqual(flashcardPatch(card, { ...card, back: 'new' }), { back: 'new' })
  assert.deepEqual(flashcardPatch(card, { ...card, front: card.front.trim() }), { front: card.front.trim() })
})
const originalFetch = globalThis.fetch
let calls = [], response = { item: prompt }, mode = 'ok'
globalThis.fetch = async (path, init) => {
  calls.push({ path, init })
  if (path === '/api/auth/csrf') return Response.json({ token: 'synthetic-test-only' })
  if (mode === 'network') throw new TypeError('synthetic')
  if (mode !== 'ok') {
    const [status, code] = mode
    return Response.json({ status, code, detail: secret }, { status })
  }
  if (init.method === 'POST') return Response.json(response, { status: 201 })
  if (init.method === 'PATCH' || init.method === 'DELETE') return new Response(null, { status: 204 })
  return Response.json(response)
}
try {
  const client = createSessionClient().flashcards
  await check('next copies only prompt shape without retaining injected extras', async () => {
    response = { item: { ...prompt, extra: { back: secret }, reviewState: { ...counts, extra: secret } } }
    const item = await client.next(map, node, signal())
    assert.deepEqual(item, prompt); assert.ok(!JSON.stringify(item).includes(secret))
    assert.equal(calls.length, 1); assert.ok(calls[0].path.endsWith('/flashcards/study/next'))
    assert.equal(calls[0].init.credentials, 'same-origin'); assert.equal(calls[0].init.cache, 'no-store'); assert.equal(calls[0].init.headers, undefined)
  })
  await check('Back returned early is a controlled protocol error', async () => {
    response = { item: { ...prompt, back: secret } }; await assert.rejects(client.next(map, node, signal()), ApiError)
  })
  await check('list pagination follows response, filters extra fields', async () => {
    response = page([{ ...card, unknown: secret }], 2); assert.deepEqual(await client.list(map, node, 2, signal()), page([card], 2))
    assert.ok(calls.at(-1).path.endsWith('?page=2&pageSize=20'))
    response = page([card], 1); await assert.rejects(client.list(map, node, 2, signal()), ApiError)
  })
  await check('detail and list reject another node or card identity', async () => {
    response = { ...card, id: other }; await assert.rejects(client.detail(map, node, id, signal()), ApiError)
    response = page([{ ...card, knowledgeNodeId: other }]); await assert.rejects(client.list(map, node, 1, signal()), ApiError)
  })
  await check('create uses same private CSRF, only Front/Back, once', async () => {
    response = card; calls = []; assert.deepEqual(await client.create(map, node, card, signal()), card)
    assert.equal(calls.length, 2); assert.equal(calls[0].path, '/api/auth/csrf')
    assert.deepEqual(JSON.parse(calls[1].init.body), { front: card.front, back: card.back }); assert.ok(calls[1].init.headers['X-CSRF-TOKEN'])
  })
  await check('PATCH and DELETE exact methods, payload and CSRF', async () => {
    await client.update(map, node, id, { back: 'new' }, signal()); assert.deepEqual(JSON.parse(calls.at(-1).init.body), { back: 'new' })
    assert.equal(calls.at(-1).init.method, 'PATCH'); await client.remove(map, node, id, signal())
    assert.equal(calls.at(-1).init.method, 'DELETE'); assert.equal(calls.at(-1).init.body, undefined)
  })
  await check('reveal only explicit revision matched GET with no CSRF', async () => {
    response = { flashcardId: id, contentRevision: 1, back: secret }; assert.equal((await client.reveal(map, node, id, 1, signal())).back, secret)
    assert.ok(calls.at(-1).path.endsWith('/study/reveal?contentRevision=1')); assert.equal(calls.at(-1).init.headers, undefined)
    response = { ...response, contentRevision: 2 }; await assert.rejects(client.reveal(map, node, id, 1, signal()), ApiError)
  })
  await check('PUT and lookup share ReviewId path and no client time/count/body extras', async () => {
    response = event; calls = []; assert.deepEqual(await client.review(map, node, pending, signal()), event)
    assert.deepEqual(JSON.parse(calls[1].init.body), { contentRevision: 1, result: 'correct' }); assert.equal(calls[1].init.method, 'PUT')
    await client.lookup(map, node, pending, signal()); assert.equal(calls.at(-1).path, calls[1].path); assert.equal(calls.at(-1).init.headers, undefined)
  })
  for (const failure of ['network', [400, 'csrf_invalid'], [401, 'authentication_required'], [409, 'flashcard_changed'], [409, 'review_id_conflict'], [500, 'unexpected_error']])
    await check('client never replays writes or renders server details', async () => {
      mode = failure; calls = []; await assert.rejects(client.review(map, node, pending, signal()), err => {
        assert.ok(!flashcardError(err).includes(secret)); return err instanceof ApiError
      }); assert.equal(calls.length, 2)
    })
  mode = 'ok'
  await check('cancelled reads send nothing', async () => { calls = []; await assert.rejects(client.next(map, node, AbortSignal.abort())); assert.equal(calls.length, 0) })
} finally { globalThis.fetch = originalFetch }

// Real HTTP/session/Flashcards clients and coordinator; only the transport is replaced.
const directedStart = passed
function httpFlow(put, lookup = () => Response.json(event)) {
  const calls = []; let auth = 0, ids = 0
  globalThis.fetch = async (path, init) => {
    calls.push({ path, init })
    if (path === '/api/auth/csrf') return Response.json({ token: 'synthetic-test-only' })
    if (init.method === 'PUT') return put()
    if (path.includes('/reviews/')) return lookup()
    if (path.includes('/study/reveal')) return Response.json({ flashcardId: id, contentRevision: 1, back: secret })
    assert.ok(path.endsWith('/study/next'))
    return Response.json({ item: prompt })
  }
  const flow = createFlashcardStudyState(createSessionClient().flashcards, map, node,
    async () => { auth++ }, () => { ids++; return rid })
  flow.mount()
  return { flow, calls, auth: () => auth, ids: () => ids }
}
const problem = (status, code) => Response.json({ status, code }, { status })
const invalid = status => new Response('{invalid JSON', { status })
try {
  for (const [name, response, status, expected = 'error'] of [
    ['403 access_denied', () => problem(403, 'access_denied'), 403],
    ['403 unknown shape', () => Response.json({ unknown: true }, { status: 403 }), 403],
    ['400 invalid JSON', () => invalid(400), 400],
    ['400 unknown shape', () => Response.json({}, { status: 400 }), 400],
    ['400 csrf_invalid', () => problem(400, 'csrf_invalid'), 400],
    ['401 invalid JSON', () => invalid(401), 401],
    ['401 unknown shape', () => Response.json({}, { status: 401 }), 401],
    ['401 authentication_required', () => problem(401, 'authentication_required'), 401],
    ['409 unknown shape', () => Response.json({}, { status: 409 }), 409],
    ['409 flashcard_changed', () => problem(409, 'flashcard_changed'), 409, 'prompt'],
    ['409 review_id_conflict', () => problem(409, 'review_id_conflict'), 409, 'conflict'],
    ['500 unexpected_error', () => problem(500, 'unexpected_error'), 500],
    ['200 invalid JSON', () => invalid(200), 200],
    ['200 invalid envelope', () => Response.json({}), 200],
  ]) await check(`received HTTP ${name} never reconciles or replays`, async () => {
    const f = httpFlow(response)
    try {
      await revealed(f); await f.flow.answer('correct'); await f.flow.check(); await f.flow.retry()
      assert.equal(f.flow.getSnapshot().status, expected)
      assert.ok(!Object.hasOwn(f.flow.getSnapshot(), 'pending'))
      assert.ok(f.flow.getSnapshot().message.length > 0)
      assert.equal(f.calls.filter(c => c.init.method === 'PUT').length, 1)
      assert.equal(f.calls.filter(c => c.path.includes('/reviews/') && c.init.method !== 'PUT').length, 0)
      assert.equal(f.auth(), status === 401 ? 1 : 0); assert.equal(f.ids(), 1)
    } finally { f.flow.dispose() }
  })
  await check('network failure reconciles original ID through real clients without PUT replay', async () => {
    const f = httpFlow(() => { throw new TypeError('synthetic connection loss') })
    try {
      await revealed(f); await f.flow.answer('correct')
      const reviews = f.calls.filter(c => c.path.includes('/reviews/'))
      assert.equal(reviews.length, 2); assert.equal(reviews[0].init.method, 'PUT')
      assert.equal(reviews[1].init.method, undefined); assert.equal(reviews[0].path, reviews[1].path)
      assert.ok(reviews[0].path.endsWith(rid)); assert.equal(f.flow.getSnapshot().status, 'prompt')
      assert.equal(f.ids(), 1)
    } finally { f.flow.dispose() }
  })
  await check('network then lookup 404 waits for explicit same-ID retry through real clients', async () => {
    let puts = 0
    const f = httpFlow(() => { if (++puts === 1) throw new TypeError('synthetic'); return Response.json(event) },
      () => problem(404, 'flashcard_not_found'))
    try {
      await revealed(f); await f.flow.answer('correct'); await settle()
      assert.equal(f.flow.getSnapshot().status, 'uncertain'); assert.equal(f.flow.getSnapshot().canRetry, true)
      assert.equal(puts, 1); assert.deepEqual(f.flow.getSnapshot().pending, pending)
      await f.flow.retry(); assert.equal(puts, 2); assert.equal(f.ids(), 1)
      const writes = f.calls.filter(c => c.init.method === 'PUT')
      assert.equal(writes[0].path, writes[1].path); assert.equal(writes[0].init.body, writes[1].init.body)
      assert.equal(f.flow.getSnapshot().status, 'prompt')
    } finally { f.flow.dispose() }
  })
  for (const leave of ['stop', 'dispose']) await check(`${leave} ignores late network failure without lookup`, async () => {
    const gate = deferred()
    const f = httpFlow(async () => { await gate.promise; throw new TypeError('synthetic') })
    try {
      await revealed(f); const sending = f.flow.answer('correct'); await settle()
      assert.equal(f.calls.filter(c => c.init.method === 'PUT').length, 1)
      f.flow[leave](); gate.resolve(); await sending
      assert.equal(f.flow.getSnapshot().status, 'idle')
      assert.equal(f.calls.filter(c => c.path.includes('/reviews/')).length, 1)
    } finally { gate.resolve(); f.flow.dispose() }
  })
  await check('navigation aborts real pending read without auth or reconciliation', async () => {
    const f = httpFlow(() => assert.fail('unexpected PUT'))
    let readSignal
    globalThis.fetch = async (_path, init) => {
      readSignal = init.signal
      return new Promise((_resolve, reject) => init.signal.addEventListener('abort',
        () => reject(new DOMException('Aborted', 'AbortError')), { once: true }))
    }
    try {
      const reading = f.flow.start(); await settle(); f.flow.stop(); await reading
      assert.equal(readSignal.aborted, true); assert.equal(f.flow.getSnapshot().status, 'idle')
      assert.equal(f.auth(), 0); assert.equal(f.ids(), 0)
    } finally { f.flow.dispose() }
  })
} finally { globalThis.fetch = originalFetch }
console.log(`${passed - directedStart} directed HTTP/network/cancellation checks PASS`)

function fixture(overrides = {}) {
  const calls = []; let ids = 0, auth = 0
  const client = {
    next: async () => { calls.push('next'); return prompt },
    reveal: async () => { calls.push('reveal'); return { flashcardId: id, contentRevision: 1, back: secret } },
    review: async (_m, _n, p) => { calls.push(['PUT', { ...p }]); return { ...event, review: { ...p, reviewedAtUtc: time } } },
    lookup: async (_m, _n, p) => { calls.push(['GET', { ...p }]); return { ...event, review: { ...p, reviewedAtUtc: time } } }, ...overrides,
  }
  const flow = createFlashcardStudyState(client, map, node, async () => { auth++ }, () => { ids++; return rid })
  flow.mount()
  return { flow, calls, client, ids: () => ids, auth: () => auth }
}
async function revealed(f) { await f.flow.start(); await f.flow.reveal(); assert.equal(f.flow.getSnapshot().status, 'revealed') }
await check('idle has no HTTP, empty is not deck-complete', async () => {
  const f = fixture({ next: async () => null }); assert.deepEqual(f.calls, []); await f.flow.start(); assert.equal(f.flow.getSnapshot().status, 'empty'); f.flow.dispose()
})
await check('prompt model has no answer, no queue and no reveal prefetch', async () => {
  const f = fixture(); await f.flow.start(); assert.deepEqual(f.calls, ['next']); assert.deepEqual(f.flow.getSnapshot().item, prompt)
  assert.ok(!JSON.stringify(f.flow.getSnapshot()).includes(secret)); await f.flow.reveal(); assert.equal(f.flow.getSnapshot().back, secret); f.flow.dispose()
})
for (const result of ['correct', 'incorrect']) await check(`${result} review uses server current state then asks backend next`, async () => {
  const f = fixture(); await revealed(f); await f.flow.answer(result)
  assert.equal(f.ids(), 1); assert.equal(f.calls[2][1].result, result); assert.equal(f.calls.at(-1), 'next')
  assert.equal(f.flow.getSnapshot().status, 'prompt'); assert.ok(!Object.hasOwn(f.flow.getSnapshot(), 'back')); f.flow.dispose()
})
await check('synchronous Correct then Incorrect/double click allocates one ID and PUT', async () => {
  const gate = deferred(); let puts = 0
  const f = fixture({ review: async () => { puts++; await gate.promise; return event } }); await revealed(f)
  const first = f.flow.answer('correct'); await f.flow.answer('incorrect'); await f.flow.answer('correct')
  assert.equal(f.ids(), 1); assert.equal(puts, 1); assert.equal(f.flow.getSnapshot().status, 'submitting')
  assert.ok(!JSON.stringify(f.flow.getSnapshot()).includes(secret)); gate.resolve(); await first; f.flow.dispose()
})
for (const phase of ['reveal', 'review']) await check(`${phase} flashcard_changed discards prompt/back, fetches next`, async () => {
  const f = fixture({ [phase]: async () => { throw new ApiError('http', 409, 'flashcard_changed') } })
  await f.flow.start(); if (phase === 'review') { await f.flow.reveal(); await f.flow.answer('correct') } else await f.flow.reveal()
  assert.equal(f.flow.getSnapshot().status, 'prompt'); assert.ok(!Object.hasOwn(f.flow.getSnapshot(), 'back')); assert.equal(f.ids(), phase === 'review' ? 1 : 0); f.flow.dispose()
})
await check('ambiguous PUT looks up exact same ID/payload; match confirms without replay', async () => {
  let attempted; const f = fixture({ review: async (_m, _n, p) => { attempted = p; throw new ApiError('network') } })
  await revealed(f); await f.flow.answer('correct'); assert.equal(f.calls[2][0], 'GET'); assert.deepEqual(f.calls[2][1], attempted)
  assert.equal(f.ids(), 1); assert.equal(f.flow.getSnapshot().status, 'prompt'); f.flow.dispose()
})
for (const field of ['reviewId', 'flashcardId', 'contentRevision', 'result']) await check(`divergent lookup ${field} conflicts without replay`, async () => {
  const value = field === 'contentRevision' ? 2 : field === 'result' ? 'incorrect' : other
  const f = fixture({ review: async () => { throw new ApiError('network') }, lookup: async () => ({ ...event, review: { ...event.review, [field]: value } }) })
  await revealed(f); await f.flow.answer('correct'); assert.equal(f.flow.getSnapshot().status, 'conflict'); assert.equal(f.ids(), 1); f.flow.dispose()
})
await check('404 lookup offers only deliberate same-ID/payload retry, no auto replay', async () => {
  let puts = 0, lookedUp
  const f = fixture({ review: async (_m, _n, p) => { puts++; if (puts === 1) throw new ApiError('network'); assert.deepEqual(p, lookedUp); return event },
    lookup: async (_m, _n, p) => { lookedUp = p; throw new ApiError('http', 404, 'flashcard_not_found') } })
  await revealed(f); await f.flow.answer('correct'); assert.equal(puts, 1); assert.equal(f.flow.getSnapshot().canRetry, true)
  assert.deepEqual(f.flow.getSnapshot().pending, pending); await settle(); assert.equal(puts, 1)
  await f.flow.retry(); assert.equal(puts, 2); assert.equal(f.ids(), 1); f.flow.dispose()
})
await check('failed lookup stays recoverable without PUT, matching later lookup confirms', async () => {
  let reads = 0, puts = 0
  const f = fixture({ review: async () => { puts++; throw new ApiError('network') }, lookup: async () => { if (++reads === 1) throw new ApiError('network'); return event } })
  await revealed(f); await f.flow.answer('correct'); assert.equal(f.flow.getSnapshot().canRetry, false); await f.flow.retry(); assert.equal(puts, 1)
  await f.flow.check(); assert.equal(f.flow.getSnapshot().status, 'prompt'); assert.equal(puts, 1); f.flow.dispose()
})
await check('review_id_conflict is terminal until deliberate restart', async () => {
  const f = fixture({ review: async () => { throw new ApiError('http', 409, 'review_id_conflict') } }); await revealed(f); await f.flow.answer('correct')
  assert.equal(f.flow.getSnapshot().status, 'conflict'); assert.ok(!f.calls.some(c => Array.isArray(c) && c[0] === 'GET')); f.flow.dispose()
})
await check('old revision event confirms using explicitly CURRENT progress', async () => {
  const f = fixture({ review: async () => ({ ...event, currentReviewState: { ...counts, contentRevision: 2 } }) })
  await revealed(f); await f.flow.answer('correct'); assert.match(f.flow.getSnapshot().message, /Revisão atual 2: 0 respostas/); f.flow.dispose()
})
for (const method of ['next', 'reveal', 'review', 'lookup']) await check(`node switch/stop invalidates late ${method} and clears Back`, async () => {
  const gate = deferred(); const f = fixture({ [method]: () => gate.promise,
    ...(method === 'lookup' ? { review: async () => { throw new ApiError('network') } } : {}) })
  let inflight
  if (method === 'next') inflight = f.flow.start()
  else { await f.flow.start(); if (method === 'reveal') inflight = f.flow.reveal()
    else { await f.flow.reveal(); inflight = f.flow.answer('correct') } }
  await settle(); f.flow.dispose(); gate.resolve(method === 'next' ? prompt : method === 'reveal' ? { back: secret } : event); await inflight
  assert.deepEqual(f.flow.getSnapshot(), { status: 'idle', message: '' })
})
await check('newer next request wins even when cancelled transport ignores abort', async () => {
  const gate = deferred(); let n = 0
  const f = fixture({ next: async () => ++n === 1 ? gate.promise : null }); const old = f.flow.start(); f.flow.stop(); await f.flow.start()
  gate.resolve(prompt); await old; assert.equal(f.flow.getSnapshot().status, 'empty'); f.flow.dispose()
})
await check('401 revalidates session without automatic read/write replay', async () => {
  let reads = 0
  const f = fixture({ next: async () => { reads++; throw new ApiError('http', 401, 'authentication_required') } }); await f.flow.start()
  assert.equal(reads, 1); assert.equal(f.auth(), 1); assert.equal(f.flow.getSnapshot().status, 'error'); f.flow.dispose()
})
await check('one card continuously reappears from backend without local exhaustion', async () => {
  const f = fixture(); await revealed(f); await f.flow.answer('correct'); assert.equal(f.flow.getSnapshot().item.id, id)
  await f.flow.reveal(); await f.flow.answer('incorrect'); assert.equal(f.flow.getSnapshot().item.id, id); assert.equal(f.ids(), 2); f.flow.dispose()
})

await check('authoring pagination preserves server order/deduplicates offset overlap', async () => {
  const pages = []; const h = createFlashcardListState({ list: async (_m, _n, p) => { pages.push(p); return p === 1 ? page([card], 1, true) : page([card, { ...card, id: other }], 2) } }, map, node, async () => {})
  h.mount(); await h.reload(); await h.more(); await h.more(); assert.deepEqual(pages, [1, 2]); assert.deepEqual(h.getSnapshot().cards.map(c => c.id), [id, other]); h.dispose()
})
await check('authoring load more lock, failure preserves page then explicit retry', async () => {
  let attempts = 0; const gate = deferred()
  const h = createFlashcardListState({ list: async (_m, _n, p) => { if (p === 1) return page([card], 1, true); if (++attempts === 1) throw new ApiError('network'); await gate.promise; return page([], 2) } }, map, node, async () => {})
  h.mount(); await h.reload(); await h.more(); assert.equal(h.getSnapshot().page, 1)
  const load = h.more(); await h.more(); assert.equal(attempts, 2); gate.resolve(); await load; assert.equal(h.getSnapshot().page, 2); h.dispose()
})
await check('authoring stale load after reload/unmount cannot overwrite current cards', async () => {
  const gate = deferred(); let n = 0
  const h = createFlashcardListState({ list: async () => ++n === 1 ? gate.promise : page() }, map, node, async () => {})
  h.mount(); const old = h.reload(); await h.reload(); gate.resolve(page([card])); await old; assert.deepEqual(h.getSnapshot().cards, []); h.dispose()
})

const require = createRequire(import.meta.url), runtime = require('react/jsx-runtime')
const source = name => readFileSync(new URL('../src/' + name, import.meta.url), 'utf8')
function compile(name, modules) {
  const exports = {}, js = ts.transpileModule(source(name), { compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX } }).outputText
  runInNewContext(js, { exports, AbortController, require: name => { assert.ok(Object.hasOwn(modules, name), name); return modules[name] } }); return exports
}
function hooks() {
  const slots = []; let cursor = 0; const cleanups = []
  return { api: { useState: initial => { const i = cursor++; if (!(i in slots)) slots[i] = typeof initial === 'function' ? initial() : initial;
    return [slots[i], value => { slots[i] = typeof value === 'function' ? value(slots[i]) : value }] },
    useRef: initial => { const i = cursor++; return slots[i] ??= { current: initial } },
    useEffect: fn => { const i = cursor++; if (!(i in slots)) { slots[i] = true; cleanups.push(fn()) } },
    useSyncExternalStore: (_s, get) => get(),
  }, render: action => { cursor = 0; return action() }, dispose: () => cleanups.forEach(fn => fn?.()) }
}
function elements(tree, predicate) {
  if (Array.isArray(tree)) return tree.flatMap(v => elements(v, predicate))
  return tree && typeof tree === 'object' ? [...(predicate(tree) ? [tree] : []), ...elements(tree.props?.children, predicate)] : []
}
await check('REAL study component DOM/model has no Back before explicit Reveal', async () => {
  const f = fixture(), h = hooks()
  const component = compile('flashcards/FlashcardStudy.tsx', { react: h.api, 'react/jsx-runtime': runtime, './flashcardStudyState.ts': { createFlashcardStudyState: () => f.flow } })
  const render = () => h.render(() => component.FlashcardStudy({ mapId: map, nodeId: node, client: f.client, revalidate: async () => {} }))
  render(); await f.flow.start(); const tree = render(), html = renderToStaticMarkup(tree)
  assert.ok(!html.includes(secret)); assert.match(html, /&lt;b&gt;question&lt;\/b&gt;/); assert.ok(!JSON.stringify(f.flow.getSnapshot()).includes(secret))
  const reveal = elements(tree, e => e.type === 'button' && e.props.children === 'Revelar resposta')[0]
  reveal.props.onClick(); await settle(); assert.ok(renderToStaticMarkup(render()).includes(secret)); h.dispose()
})
await check('REAL authoring component list/create/edit/no-op/delete invoke approved client and reload page 1', async () => {
  const h = hooks(), mh = hooks(), writes = []; let cards = [card], lockedGate
  const client = { list: async () => page(cards), create: async (_m, _n, input) => { writes.push(['create', input]); if (lockedGate) await lockedGate.promise; cards = [{ ...card, ...input }]; return cards[0] },
    update: async (_m, _n, _id, patch) => { writes.push(['edit', patch]); cards = [{ ...card, ...patch, contentRevision: 2 }] },
    remove: async () => { writes.push(['delete']); cards = [] } }
  const mutationModule = compile('components/useAuthoring.ts', { react: mh.api, '../api/http.ts': { ApiError }, '../api/knowledgeMapAuthoringApi.ts': { authoringErrorMessage: flashcardError } })
  const mutation = mh.render(() => mutationModule.useAuthoring(async () => {}))
  const { FlashcardAuthoring, FlashcardForm } = compile('flashcards/FlashcardAuthoring.tsx', { react: h.api, 'react/jsx-runtime': runtime,
    '../api/flashcardsApi.ts': { flashcardError, flashcardInput, flashcardPatch }, './flashcardListState.ts': { createFlashcardListState } })
  const render = () => h.render(() => FlashcardAuthoring({ mapId: map, nodeId: node, client, mutation, revalidate: async () => {} }))
  let tree = render(); await settle(); tree = render()
  assert.ok(elements(tree, e => e.type === 'p' && e.props.children === secret).length)
  const button = label => elements(render(), e => e.type === 'button' && e.props.children === label)[0]
  button('Adicionar flashcard').props.onClick(); let form = elements(render(), e => e.type === FlashcardForm)[0]
  lockedGate = deferred(); form.props.onSave({ front: 'new', back: 'answer' }); form.props.onSave({ front: 'duplicate', back: 'answer' })
  assert.equal(writes.length, 1); lockedGate.resolve(); await settle(); await settle()
  button('Editar flashcard').props.onClick(); form = elements(render(), e => e.type === FlashcardForm)[0]
  form.props.onSave(cards[0]); assert.equal(writes.length, 1)
  form.props.onSave({ ...cards[0], back: 'edited' }); await settle(); await settle(); assert.deepEqual(writes[1], ['edit', { back: 'edited' }])
  assert.ok(elements(render(), e => e.type === 'p' && e.props.children === 'edited').length)
  button('Excluir flashcard').props.onClick(); assert.equal(writes.length, 2)
  button('Confirmar exclusão do flashcard').props.onClick(); await settle(); await settle(); assert.equal(writes.length, 3)
  assert.ok(elements(render(), e => e.type === 'p' && String(e.props.children).includes('Ainda não há flashcards')).length); h.dispose(); mh.dispose()
})
await check('REAL form no-op and UTF16 input validation with native labels/limits', () => {
  const h = hooks(); let saved = 0
  const { FlashcardForm } = compile('flashcards/FlashcardAuthoring.tsx', { react: h.api, 'react/jsx-runtime': runtime,
    '../api/flashcardsApi.ts': { flashcardError, flashcardInput, flashcardPatch }, './flashcardListState.ts': { createFlashcardListState } })
  const render = () => h.render(() => FlashcardForm({ initial: card, pending: false, onSave: () => saved++, onCancel: () => {} }))
  let tree = render(); tree.props.onSubmit({ preventDefault() {} }); assert.equal(saved, 0)
  const fields = elements(tree, e => e.type === 'textarea'); assert.deepEqual(fields.map(e => e.props.maxLength), [2000, 4000])
  fields[0].props.onChange({ target: { value: '😀'.repeat(1000) + 'x' } }); render().props.onSubmit({ preventDefault() {} }); assert.equal(saved, 0)
  fields[0].props.onChange({ target: { value: 'new' } }); tree = render(); tree.props.onSubmit({ preventDefault() {} }); assert.equal(saved, 1)
})
await check('mode/node keys isolate authoring answers from study component', () => {
  const s = source('components/MapWorkspace.tsx'); assert.match(s, /key=\{`\$\{selected.id\}:flashcard-study`\}/)
  assert.match(s, /key=\{`\$\{selected.id\}:flashcard-edit`\}/)
  assert.ok(s.indexOf('<FlashcardStudy') < s.indexOf('{editing && <aside')); assert.ok(s.indexOf('<FlashcardAuthoring') > s.indexOf('{editing && <aside'))
})
await check('no content/state storage, queue, scheduler or active-study integration', () => {
  for (const name of ['flashcardStudyState.ts', 'FlashcardStudy.tsx', 'FlashcardAuthoring.tsx']) {
    const s = source('flashcards/' + name); assert.doesNotMatch(s, /localStorage|sessionStorage|setInterval|WebSocket|StudySession|DueAtUtc|dangerouslySetInnerHTML/)
  }
  assert.ok(matchingReview(pending, event)); assert.ok(!matchingReview({ ...pending, result: 'incorrect' }, event))
})
console.log(`${passed} flashcard frontend checks PASS`)
