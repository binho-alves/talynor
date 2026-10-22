// Existing Node + TypeScript + React only. Controlled hook harness complements the browser smoke.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { runInNewContext } from 'node:vm'
import { renderToStaticMarkup } from 'react-dom/server'
import ts from 'typescript'
import { ApiError } from '../src/api/http.ts'
import { createSessionClient } from '../src/session/sessionClient.ts'
import * as api from '../src/api/quizzesApi.ts'
import { quizInput, quizChanged, definitionInput } from '../src/quizzes/quizInput.ts'
import { completeQuizAnswers, createQuizTakingState } from '../src/quizzes/quizTakingState.ts'
import { createQuizListState, createQuizHistoryState } from '../src/quizzes/quizReadState.ts'

let passed = 0
async function check(name, fn) { try { await fn(); passed++ } catch (error) { console.error(`FAIL: ${name}`); throw error } }
const guid = n => `10000000-0000-0000-0000-${String(n).padStart(12, '0')}`
const map = guid(1), node = guid(2), quiz = guid(3), attempt = guid(4), other = guid(5)
const time = '2026-10-21T22:00:00.1234567+00:00'
const definition = { id: quiz, knowledgeNodeId: node, title: 'Quiz histórico', contentRevision: 1, createdAtUtc: time, updatedAtUtc: time,
  questions: [0, 1, 2].map(i => ({ id: guid(10 + i), text: ` pergunta <b>${i}</b>\n`, position: i + 1, correctOptionIndex: i,
    options: [0, 1, 2].map(j => ({ id: guid(100 + i * 3 + j), text: ` alternativa ${j}\n`, position: j + 1 })) })) }
const take = { quizId: quiz, title: definition.title, contentRevision: 1,
  questions: definition.questions.map(({ id, text, position, options }) => ({ id, text, position, options })) }
const summary = { id: quiz, title: definition.title, contentRevision: 1, createdAtUtc: time, updatedAtUtc: time }
const result = { attemptId: attempt, quizId: quiz, quizTitle: definition.title, contentRevision: 1, submittedAtUtc: time,
  questionCount: 3, correctCount: 2, accuracyPercentage: 66.67,
  answers: definition.questions.map((q, i) => ({ questionId: q.id, position: q.position, text: q.text, options: q.options,
    selectedOptionId: q.options[i < 2 ? i : 0].id, correctOptionId: q.options[i].id, isCorrect: i < 2 })) }
const selections = Object.fromEntries(result.answers.map(a => [a.questionId, a.selectedOptionId]))
const pending = { attemptId: attempt, quizId: quiz, contentRevision: 1, answers: result.answers.map(a => ({ questionId: a.questionId, selectedOptionId: a.selectedOptionId })) }
const attemptSummary = Object.fromEntries(Object.entries(result).filter(([key]) => key !== 'answers'))
const page = (items = [summary], p = 1, hasMore = false) => ({ quizzes: items, page: p, pageSize: 20, hasMore })
const history = (items = [attemptSummary], p = 1, hasMore = false) => ({ attempts: items, page: p, pageSize: 20, hasMore })
const sig = () => new AbortController().signal
const settle = () => new Promise(resolve => setImmediate(resolve))
function deferred() { let resolve; const promise = new Promise(r => { resolve = r }); return { promise, resolve } }
const problem = (status, code) => Response.json({ status, code, detail: 'PRIVATE-DIAGNOSTIC-NEVER-SHOWN' }, { status })
const source = name => readFileSync(new URL('../src/' + name, import.meta.url), 'utf8')

for (const [name, v, validate] of [['summary', summary, api.isQuizSummary], ['page', page(), api.isQuizPage], ['definition', definition, api.isQuizDefinition],
  ['take', take, api.isQuizTake], ['attempt', result, api.isQuizAttempt], ['attempt summary', attemptSummary, api.isQuizAttemptSummary], ['history', history(), api.isQuizAttemptPage]])
  await check(`valid ${name}`, () => assert.ok(validate(v)))
for (const bad of [null, {}, { ...definition, id: 'bad' }, { ...definition, contentRevision: 0 }, { ...definition, contentRevision: 2147483648 },
  { ...definition, createdAtUtc: '2026-02-30T12:00:00Z' }, { ...definition, questions: [] }, { ...definition, questions: [...definition.questions, definition.questions[0]] },
  { ...definition, questions: [{ ...definition.questions[0], correctOptionIndex: 9 }] }])
  await check('reject invalid authoring definition', () => assert.equal(api.isQuizDefinition(bad), false))
for (const field of ['correctOptionIndex', 'isCorrect', 'correctOptionId', 'answerKey']) {
  for (const level of ['root', 'question', 'option']) await check(`take rejects ${field} at ${level}`, () => {
    const v = structuredClone(take), target = level === 'root' ? v : level === 'question' ? v.questions[0] : v.questions[0].options[0]
    target[field] = 'KEY-LEAK'; assert.equal(api.isQuizTake(v), false)
  })
}
for (const bad of [{ ...take, questions: [] }, { ...take, questions: [{ ...take.questions[0], position: 2 }] },
  { ...take, questions: [{ ...take.questions[0], options: [take.questions[0].options[0]] }] },
  { ...take, questions: [{ ...take.questions[0], options: [take.questions[0].options[0], take.questions[0].options[0]] }] }])
  await check('reject invalid key-free taking structure', () => assert.equal(api.isQuizTake(bad), false))
for (const bad of [{ ...result, answers: [] }, { ...result, correctCount: 4 }, { ...result, accuracyPercentage: 66.66 },
  { ...result, answers: result.answers.map((a, i) => i === 0 ? { ...a, correctOptionId: other } : a) },
  { ...result, answers: result.answers.map((a, i) => i === 0 ? { ...a, isCorrect: false } : a) },
  { ...result, answers: result.answers.map((a, i) => i === 0 ? { ...a, selectedOptionId: other } : a) }])
  await check('reject inconsistent historical correction', () => assert.equal(api.isQuizAttempt(bad), false))
for (const bad of [{ ...page(), page: 0 }, { ...page(), pageSize: 101 }, page([summary, summary]), { ...history(), hasMore: null }, history([attemptSummary, attemptSummary])])
  await check('reject malformed/duplicate page', () => assert.equal('attempts' in bad ? api.isQuizAttemptPage(bad) : api.isQuizPage(bad), false))
const input = definitionInput(definition)
await check('literal text and normalized title match domain', () => assert.deepEqual(quizInput('  title\u0085', input.questions), { ...input, title: 'title' }))
for (const [title, questions] of [[' ', input.questions], ['\u0085', input.questions], ['x'.repeat(201), input.questions], ['ok', []],
  ['ok', Array.from({ length: 11 }, () => input.questions[0])], ['ok', [{ ...input.questions[0], text: '\u0085' }]],
  ['ok', [{ ...input.questions[0], text: '😀'.repeat(500) + 'x' }]], ['ok', [{ text: 'q', options: ['a'], correctOptionIndex: 0 }]],
  ['ok', [{ text: 'q', options: ['a', 'b', 'c', 'd', 'e'], correctOptionIndex: 0 }]], ['ok', [{ text: 'q', options: [' a', 'a '], correctOptionIndex: 0 }]],
  ['ok', [{ text: 'q', options: ['a', '\u0085a\u0085'], correctOptionIndex: 0 }]], ['ok', [{ text: 'q', options: ['a', 'b'], correctOptionIndex: -1 }]],
  ['ok', [{ text: 'q', options: ['a', 'b'], correctOptionIndex: 2 }]], ['ok', [{ text: 'q', options: ['😀'.repeat(250) + 'x', 'b'], correctOptionIndex: 0 }]]])
  await check('input limits, duplicate semantics and exactly one correct', () => assert.equal(quizInput(title, questions), null))
await check('UTF16 exact boundaries and Unicode edge cases', () => {
  assert.ok(quizInput('😀'.repeat(100), [{ text: '😀'.repeat(500), options: ['😀'.repeat(250), '\uFEFF'], correctOptionIndex: 1 }]))
  assert.ok(quizInput('title', [{ text: '\uFEFF', options: ['a', '\uFEFFa'], correctOptionIndex: 0 }]))
  assert.ok(quizInput('title', [{ text: 'q', options: ['A', 'a', 'é', 'e\u0301'], correctOptionIndex: 3 }]))
  assert.equal(quizChanged(input, input), false); assert.equal(quizChanged(input, { ...input, title: 'changed' }), true)
})
await check('semantic equality ignores order and case of GUIDs, not answers/revision', () => {
  assert.ok(api.matchingAttempt({ ...pending, answers: [...pending.answers].reverse() }, result))
  assert.ok(!api.matchingAttempt({ ...pending, contentRevision: 2 }, result)); assert.ok(!api.matchingAttempt({ ...pending, attemptId: other }, result))
  assert.ok(!api.matchingAttempt({ ...pending, answers: [] }, result))
})
await check('duplicate pending answers cannot confirm and empty GUID responses fail closed', () => {
  assert.ok(!api.matchingAttempt({ ...pending, answers: [pending.answers[0], pending.answers[0], pending.answers[2]] }, result))
  assert.equal(api.isQuizSummary({ ...summary, id: '00000000-0000-0000-0000-000000000000' }), false)
})

const originalFetch = globalThis.fetch
let calls = [], response = page(), status = 200
globalThis.fetch = async (path, init) => {
  calls.push({ path, init }); if (path === '/api/auth/csrf') return Response.json({ token: 'synthetic-only' })
  return status === 204 ? new Response(null, { status }) : Response.json(response, { status })
}
try {
  const client = createSessionClient().quizzes
  await check('list preserves backend order and discards extra fields', async () => {
    response = page([{ ...summary, id: other, extra: 'discard' }, summary]); calls = []
    const v = await client.list(map, node, 1, sig()); assert.deepEqual(v.quizzes.map(q => q.id), [other, quiz]); assert.ok(!JSON.stringify(v).includes('discard'))
    assert.equal(calls.length, 1); assert.equal(calls[0].init.headers, undefined); assert.equal(calls[0].init.credentials, 'same-origin'); assert.equal(calls[0].init.cache, 'no-store')
  })
  await check('list/detail require matching pagination/identity', async () => {
    response = page([], 2); await assert.rejects(client.list(map, node, 1, sig()), ApiError)
    response = { ...definition, knowledgeNodeId: other }; await assert.rejects(client.detail(map, node, quiz, sig()), ApiError)
  })
  await check('create sends no local keys, uses CSRF once, trusts server revision', async () => {
    response = definition; status = 201; calls = []; const v = await client.create(map, node, { ...input, uiKey: 'discard' }, sig())
    assert.equal(v.contentRevision, 1); assert.deepEqual(JSON.parse(calls[1].init.body), input); assert.equal(calls.length, 2)
    assert.equal(calls[0].path, '/api/auth/csrf'); assert.ok(calls[1].init.headers['X-CSRF-TOKEN'])
  })
  await check('full PUT uses expectedRevision; DELETE body absent', async () => {
    status = 204; await client.replace(map, node, quiz, 7, input, sig()); assert.deepEqual(JSON.parse(calls.at(-1).init.body), { expectedRevision: 7, ...input })
    assert.equal(calls.at(-1).init.method, 'PUT'); await client.remove(map, node, quiz, sig()); assert.equal(calls.at(-1).init.method, 'DELETE'); assert.equal(calls.at(-1).init.body, undefined)
  })
  await check('take fetches only take endpoint, never authoring and no key in model', async () => {
    response = take; status = 200; calls = []; assert.deepEqual(await client.take(map, node, quiz, sig()), take)
    assert.equal(calls.length, 1); assert.ok(calls[0].path.endsWith('/take')); assert.equal(calls[0].init.headers, undefined)
    response = { ...take, correctOptionIndex: 0 }; await assert.rejects(client.take(map, node, quiz, sig()), ApiError)
  })
  await check('submit/lookup share AttemptId and exact answer-only payload', async () => {
    response = { ...result, extra: 'discard' }; calls = []; assert.deepEqual(await client.submit(map, node, pending, sig()), result)
    assert.deepEqual(JSON.parse(calls[1].init.body), { contentRevision: 1, answers: pending.answers }); assert.equal(calls[1].init.method, 'PUT')
    await client.lookup(map, node, quiz, attempt, sig()); assert.equal(calls[1].path, calls[2].path); assert.equal(calls[2].init.headers, undefined)
  })
  await check('history validates scope and page, strips unknown data', async () => {
    response = history(); assert.deepEqual(await client.history(map, node, quiz, 1, sig()), history())
    response = history([{ ...attemptSummary, quizId: other }]); await assert.rejects(client.history(map, node, quiz, 1, sig()), ApiError)
  })
  await check('cancelled reads never fetch', async () => { calls = []; await assert.rejects(client.take(map, node, quiz, AbortSignal.abort())); assert.equal(calls.length, 0) })
} finally { globalThis.fetch = originalFetch }

function fixture(overrides = {}) {
  let ids = 0, auth = 0
  const client = { take: async () => take, submit: async () => result, lookup: async () => result, ...overrides }
  const flow = createQuizTakingState(client, map, node, quiz, async () => { auth++ }, () => { ids++; return attempt }); flow.mount()
  return { flow, client, ids: () => ids, auth: () => auth }
}
async function fill(f) { await f.flow.start(); for (const a of pending.answers) f.flow.select(a.questionId, a.selectedOptionId) }
await check('incomplete/extra answers rejected without allocation or PUT', async () => {
  const f = fixture(); await f.flow.start(); await f.flow.submit(); assert.equal(f.ids(), 0)
  assert.equal(completeQuizAnswers(take, {}), false); assert.equal(completeQuizAnswers(take, { ...selections, [other]: other }), false); f.flow.dispose()
})
await check('lock BEFORE id allocation; two synchronous submissions allocate/send once', async () => {
  const gate = deferred(); let puts = 0
  const f = fixture({ submit: async (_m, _n, p) => { puts++; assert.ok(Object.isFrozen(p)); assert.ok(Object.isFrozen(p.answers)); return gate.promise } })
  await fill(f); const first = f.flow.submit(); const second = f.flow.submit(); assert.equal(f.ids(), 1); assert.equal(puts, 1)
  f.flow.select(other, other); gate.resolve(result); await Promise.all([first, second]); assert.equal(f.flow.getSnapshot().result.accuracyPercentage, 66.67); f.flow.dispose()
})
await check('synchronous context disposal during lock/allocation never sends a stale PUT', async () => {
  for (const phase of ['locking', 'submitting']) {
    let puts = 0; const f = fixture({ submit: async () => { puts++; return result } }); await fill(f)
    f.flow.subscribe(() => { if (f.flow.getSnapshot().status === phase) f.flow.dispose() })
    await f.flow.submit(); assert.equal(puts, 0); assert.equal(f.ids(), phase === 'locking' ? 0 : 1)
  }
})
for (const change of [{ ...result, contentRevision: 2 }, { ...result, attemptId: other }, { ...result, answers: result.answers.map(a => ({ ...a, selectedOptionId: other })) }])
  await check('mismatched returned identity is conflict and never accepted', async () => { const f = fixture({ submit: async () => change }); await fill(f); await f.flow.submit(); assert.equal(f.flow.getSnapshot().status, 'conflict'); f.flow.dispose() })

function httpFixture(put, lookup = () => Response.json(result), csrf = () => Response.json({ token: 'synthetic-only' })) {
  const calls = []; let ids = 0, auth = 0
  globalThis.fetch = async (path, init) => {
    calls.push({ path, init }); if (path === '/api/auth/csrf') return csrf()
    if (path.endsWith('/take')) return Response.json(take)
    if (init.method === 'PUT') return put()
    assert.ok(path.endsWith(`/attempts/${attempt}`)); return lookup()
  }
  const flow = createQuizTakingState(createSessionClient().quizzes, map, node, quiz, async () => { auth++ }, () => { ids++; return attempt }); flow.mount()
  return { flow, calls, ids: () => ids, auth: () => auth }
}
try {
  for (const [name, transport, expected, status] of [
    ['400 known', () => problem(400, 'validation_failed'), 'error', 400], ['400 unknown', () => problem(400, 'unknown'), 'error', 400],
    ['401 invalid', () => new Response('{bad', { status: 401 }), 'error', 401], ['403 invalid', () => Response.json({}, { status: 403 }), 'error', 403],
    ['409 changed', () => problem(409, 'quiz_changed'), 'changed', 409], ['409 conflict', () => problem(409, 'attempt_id_conflict'), 'conflict', 409],
    ['409 unknown', () => problem(409, 'unknown'), 'error', 409], ['500 known', () => problem(500, 'unexpected_error'), 'error', 500],
    ['500 invalid', () => new Response('{bad', { status: 500 }), 'error', 500], ['200 invalid JSON', () => new Response('{bad'), 'error', 200],
    ['200 invalid shape', () => Response.json({}), 'error', 200], ['204 invalid success', () => new Response(null, { status: 204 }), 'error', 204],
  ]) await check(`real received HTTP ${name}: no automatic lookup or replay`, async () => {
    const f = httpFixture(transport); await fill(f); await f.flow.submit()
    assert.equal(f.flow.getSnapshot().status, expected); assert.equal(f.ids(), 1); assert.equal(f.auth(), status === 401 ? 1 : 0)
    assert.equal(f.calls.filter(c => c.init.method === 'PUT').length, 1)
    assert.equal(f.calls.filter(c => c.path.includes('/attempts/') && c.init.method !== 'PUT').length, 0)
    assert.ok(!f.flow.getSnapshot().message.includes('PRIVATE')); f.flow.dispose()
  })
  await check('real PUT no-response → matching lookup confirms without replay', async () => {
    const f = httpFixture(() => { throw new TypeError('lost response') }); await fill(f); await f.flow.submit()
    assert.equal(f.flow.getSnapshot().status, 'result'); assert.equal(f.calls.filter(c => c.init.method === 'PUT').length, 1)
    assert.equal(f.calls.at(-1).path, f.calls.at(-2).path); f.flow.dispose()
  })
  await check('real lookup404 explicit retry retains exact ID/payload and re-locks', async () => {
    let writes = 0; const gate = deferred()
    const f = httpFixture(() => { if (++writes === 1) throw new TypeError('lost'); return gate.promise }, () => problem(404, 'quiz_not_found'))
    await fill(f); await f.flow.submit(); assert.equal(f.flow.getSnapshot().canRetry, true); const original = f.flow.getSnapshot().pending
    await settle(); assert.equal(writes, 1); const retry = f.flow.retry(); await f.flow.retry(); await settle(); assert.equal(writes, 2)
    assert.equal(f.ids(), 1); const putCalls = f.calls.filter(c => c.init.method === 'PUT'); assert.equal(putCalls[0].path, putCalls[1].path); assert.equal(putCalls[0].init.body, putCalls[1].init.body)
    assert.equal(f.flow.getSnapshot().pending, original); gate.resolve(Response.json(result)); await retry; assert.equal(f.flow.getSnapshot().status, 'result'); f.flow.dispose()
  })
  await check('lookup error remains recoverable; explicit check confirms same attempt', async () => {
    let reads = 0; const f = httpFixture(() => { throw new TypeError('lost') }, () => { if (++reads === 1) throw new TypeError('offline'); return Response.json(result) })
    await fill(f); await f.flow.submit(); assert.equal(f.flow.getSnapshot().canRetry, false); await f.flow.retry(); assert.equal(reads, 1)
    await f.flow.check(); assert.equal(f.flow.getSnapshot().status, 'result'); assert.equal(f.ids(), 1); f.flow.dispose()
  })
  await check('divergent lookup refuses replay', async () => {
    const changed = structuredClone(result); changed.answers[2].selectedOptionId = changed.answers[2].correctOptionId; changed.answers[2].isCorrect = true; changed.correctCount = 3; changed.accuracyPercentage = 100
    const f = httpFixture(() => { throw new TypeError('lost') }, () => Response.json(changed)); await fill(f); await f.flow.submit()
    assert.equal(f.flow.getSnapshot().status, 'conflict'); await f.flow.retry(); assert.equal(f.calls.filter(c => c.init.method === 'PUT').length, 1); f.flow.dispose()
  })
  await check('CSRF network preparation failure sends no PUT and no automatic lookup', async () => {
    const f = httpFixture(() => Response.json(result), undefined, () => { throw new TypeError('offline') }); await fill(f); await f.flow.submit()
    assert.equal(f.flow.getSnapshot().status, 'error'); assert.equal(f.calls.filter(c => c.path.includes('/attempts/')).length, 0); f.flow.dispose()
  })
} finally { globalThis.fetch = originalFetch }
for (const stage of ['take', 'submit', 'lookup']) await check(`late ${stage} after dispose cannot contaminate another visit`, async () => {
  const gate = deferred(); const f = fixture({ [stage]: () => gate.promise, ...(stage === 'lookup' ? { submit: async () => { throw new ApiError('network') } } : {}) })
  let inflight
  if (stage === 'take') inflight = f.flow.start()
  else { await fill(f); inflight = f.flow.submit() }
  await settle(); f.flow.dispose(); gate.resolve(stage === 'take' ? take : result); await inflight
  assert.deepEqual(f.flow.getSnapshot(), { status: 'idle', message: '' })
})
await check('quiz_changed clears stale answer sheet; explicit reload starts empty revision2', async () => {
  const f = fixture({ submit: async () => { throw new ApiError('http', 409, 'quiz_changed') } }); await fill(f); await f.flow.submit()
  assert.equal(f.flow.getSnapshot().status, 'changed'); assert.ok(!('selections' in f.flow.getSnapshot()))
  f.client.take = async () => ({ ...take, contentRevision: 2 }); await f.flow.start(); assert.deepEqual(f.flow.getSnapshot().selections, {}); f.flow.dispose()
})
for (const kind of ['list', 'history']) {
  const create = load => kind === 'list' ? createQuizListState({ list: load }, map, node, async () => {}) : createQuizHistoryState({ history: (_m, _n, _q, p, s) => load(_m, _n, p, s) }, map, node, quiz, async () => {})
  const envelope = (rows, p, more) => kind === 'list' ? page(rows, p, more) : history(rows, p, more)
  const row = kind === 'list' ? summary : attemptSummary, second = kind === 'list' ? { ...summary, id: other } : { ...attemptSummary, attemptId: other }
  await check(`${kind} pagination appends server order and locks rapid more`, async () => {
    let calls = 0; const gate = deferred(); const state = create(async (_m, _n, p) => { calls++; return p === 1 ? envelope([second], 1, true) : gate.promise })
    state.mount(); await state.reload(); const more = state.more(); await state.more(); assert.equal(calls, 2); gate.resolve(envelope([row], 2, false)); await more
    assert.deepEqual(state.getSnapshot().items, [second, row]); state.dispose()
  })
  await check(`${kind} overlap explicitly requests refresh, no silent contradictory merge`, async () => {
    const state = create(async (_m, _n, p) => envelope([row], p, p === 1)); state.mount(); await state.reload(); await state.more()
    assert.equal(state.getSnapshot().items.length, 1); assert.match(state.getSnapshot().message, /Atualize/); assert.equal(state.getSnapshot().hasMore, false); state.dispose()
  })
  await check(`${kind} old response after reload or unmount ignored`, async () => {
    let calls = 0; const gate = deferred(); const state = create(async () => ++calls === 1 ? gate.promise : envelope([], 1, false))
    state.mount(); const old = state.reload(); await state.reload(); gate.resolve(envelope([row], 1, false)); await old; assert.deepEqual(state.getSnapshot().items, []); state.dispose()
  })
  await check(`${kind} operational failure is error, never empty success`, async () => {
    const state = create(async () => { throw new ApiError('http', 500, 'unexpected_error') }); state.mount(); await state.reload(); assert.equal(state.getSnapshot().status, 'error'); state.dispose()
  })
}

const require = createRequire(import.meta.url), runtime = require('react/jsx-runtime')
function compile(name, modules) {
  const exports = {}, js = ts.transpileModule(source(name), { compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX } }).outputText
  runInNewContext(js, { exports, AbortController, require: name => { assert.ok(Object.hasOwn(modules, name), name); return modules[name] } }); return exports
}
function hooks() {
  const components = new Map(); let slots, cursor
  return { api: {
    useState(initial) { const i = cursor++; if (!(i in slots)) slots[i] = typeof initial === 'function' ? initial() : initial; const target = slots
      return [slots[i], v => { target[i] = typeof v === 'function' ? v(target[i]) : v }] },
    useRef(initial) { const i = cursor++; return slots[i] ??= { current: initial } },
    useId() { const i = cursor++; return slots[i] ??= `test-${i}` },
    useEffect(fn, deps = []) { const i = cursor++; const prior = slots[i]; if (!prior || deps.some((v, n) => !Object.is(v, prior.deps[n]))) { prior?.cleanup?.(); slots[i] = { deps, cleanup: fn() } } },
    useSyncExternalStore(_sub, get) { return get() },
  }, render(id, fn) { slots = components.get(id) ?? []; components.set(id, slots); cursor = 0; return fn() },
  dispose() { for (const slots of components.values()) for (const slot of slots) slot?.cleanup?.() } }
}
function elements(tree, predicate) {
  if (Array.isArray(tree)) return tree.flatMap(v => elements(v, predicate))
  return tree && typeof tree === 'object' ? [...(predicate(tree) ? [tree] : []), ...elements(tree.props?.children, predicate)] : []
}
const label = value => Array.isArray(value) ? value.map(label).join('') : typeof value === 'string' || typeof value === 'number' ? String(value) : ''
const QuizResult = compile('quizzes/QuizResult.tsx', { react: require('react'), 'react/jsx-runtime': runtime }).QuizResult
await check('REAL result renders historical snapshots/counts, no current definition', () => {
  const html = renderToStaticMarkup(runtime.jsx(QuizResult, { result })); assert.match(html, /66,67%/); assert.match(html, /Quiz histórico/)
  assert.match(html, /sua escolha/); assert.match(html, /resposta correta/); assert.match(html, /&lt;b&gt;/); assert.ok(!html.includes('dangerouslySetInnerHTML'))
})
await check('REAL taking markup/state has native radios and no answer key before result', async () => {
  const h = hooks(), f = fixture()
  const { QuizTaking } = compile('quizzes/QuizTaking.tsx', { react: h.api, 'react/jsx-runtime': runtime,
    './quizTakingState.ts': { completeQuizAnswers, createQuizTakingState: () => f.flow }, './QuizResult.tsx': { QuizResult } })
  const render = () => h.render('taking', () => QuizTaking({ mapId: map, nodeId: node, quizId: quiz, client: f.client, revalidate: async () => {} }))
  render(); await settle(); const tree = render(), html = renderToStaticMarkup(tree)
  assert.equal(elements(tree, e => e.type === 'input' && e.props.type === 'radio').length, 9)
  assert.equal(elements(tree, e => e.type === 'fieldset').length, 3)
  for (const key of ['correctOptionIndex', 'correctOptionId', 'isCorrect', 'answerKey']) { assert.ok(!html.includes(key)); assert.ok(!JSON.stringify(f.flow.getSnapshot()).includes(key)) }
  assert.equal(elements(tree, e => e.type === 'button' && e.props.type === 'submit')[0].props.disabled, true); h.dispose()
})
await check('REAL controlled form no-op, full inputs, stable keys and limits', () => {
  const h = hooks(); let saved = 0
  const { QuizForm } = compile('quizzes/QuizForm.tsx', { react: h.api, 'react/jsx-runtime': runtime, './quizInput.ts': { quizInput, quizChanged } })
  const render = () => h.render('form', () => QuizForm({ initial: input, pending: false, onSave: () => saved++, onCancel() {} }))
  let tree = render(); tree.props.onSubmit({ preventDefault() {} }); assert.equal(saved, 0)
  const title = elements(tree, e => e.type === 'input' && e.props.maxLength === 200)[0]; title.props.onChange({ target: { value: 'edited' } })
  tree = render(); tree.props.onSubmit({ preventDefault() {} }); assert.equal(saved, 1)
  assert.ok(elements(tree, e => e.type === 'textarea').every(e => [500, 1000].includes(e.props.maxLength)))
  assert.equal(elements(tree, e => e.type === 'button' && label(e.props.children) === 'Remover pergunta 1')[0].props.disabled, false); h.dispose()
})
await check('REAL authoring create/delete share synchronous mutation lock and warning', async () => {
  const h = hooks(), writes = [], gate = deferred(); let rows = [summary]
  const client = { list: async () => page(rows), create: async (_m, _n, v) => { writes.push(['create', v]); await gate.promise; return definition }, remove: async () => { writes.push(['delete']); rows = [] } }
  const { useAuthoring } = compile('components/useAuthoring.ts', { react: h.api, '../api/http.ts': { ApiError }, '../api/knowledgeMapAuthoringApi.ts': { authoringErrorMessage: api.quizError } })
  const mutation = h.render('mutation', () => useAuthoring(async () => {}))
  const { QuizAuthoring } = compile('quizzes/QuizAuthoring.tsx', { react: h.api, 'react/jsx-runtime': runtime, '../api/http.ts': { ApiError },
    '../api/quizzesApi.ts': api, './quizReadState.ts': { createQuizListState }, './quizInput.ts': { definitionInput }, './QuizForm.tsx': { QuizForm: () => null } })
  const render = () => h.render('authoring', () => QuizAuthoring({ mapId: map, nodeId: node, client, mutation, revalidate: async () => {} }))
  const button = text => elements(render(), e => e.type === 'button' && label(e.props.children) === text)[0]
  render(); await settle(); button('Adicionar quiz').props.onClick()
  const form = elements(render(), e => typeof e.type === 'function' && e.props.onSave)[0]; form.props.onSave(input); form.props.onSave(input)
  assert.equal(writes.length, 1); gate.resolve(); await settle(); await settle()
  const remove = button('Excluir quiz ' + summary.title)
  remove.props.onClick(); const confirm = button('Confirmar exclusão do quiz')
  assert.ok(elements(render(), e => e.type === 'p' && String(e.props.children).includes('histórico de tentativas')).length)
  assert.equal(writes.length, 1); confirm.props.onClick(); await settle(); await settle(); assert.equal(writes.length, 2); h.dispose()
})
await check('REAL edit uses loaded revision; stale form unmounts, explicit reload uses fresh authority', async () => {
  const h = hooks(), writes = []; let revision = 7, fail = true, finished = 0
  const client = { list: async () => page(), detail: async () => ({ ...definition, contentRevision: revision }),
    replace: async (_m, _n, _q, expected, value) => { writes.push({ expected, value }); if (fail) throw new ApiError('http', 409, 'quiz_changed') } }
  const { useAuthoring } = compile('components/useAuthoring.ts', { react: h.api, '../api/http.ts': { ApiError }, '../api/knowledgeMapAuthoringApi.ts': { authoringErrorMessage: api.quizError } })
  const mutation = h.render('mutation', () => useAuthoring(async () => {}))
  const { QuizAuthoring } = compile('quizzes/QuizAuthoring.tsx', { react: h.api, 'react/jsx-runtime': runtime, '../api/http.ts': { ApiError },
    '../api/quizzesApi.ts': api, './quizReadState.ts': { createQuizListState }, './quizInput.ts': { definitionInput }, './QuizForm.tsx': { QuizForm: () => null } })
  const render = () => h.render('authoring', () => QuizAuthoring({ mapId: map, nodeId: node, client, mutation, revalidate: async () => {} }))
  render(); await settle()
  elements(render(), e => e.type === 'button' && label(e.props.children) === 'Editar quiz '+summary.title)[0].props.onClick()
  const editor = elements(render(), e => typeof e.type === 'function' && e.props.quiz)[0]
  const props = { ...editor.props, onDone: () => { finished++ } }
  const edit = () => h.render('editor', () => editor.type(props))
  edit(); await settle(); let tree = edit()
  elements(tree, e => typeof e.type === 'function' && e.props.onSave)[0].props.onSave({ ...input, title: 'changed' })
  await settle(); tree = edit(); assert.equal(writes[0].expected, 7); assert.equal(elements(tree, e => typeof e.type === 'function' && e.props.onSave).length, 0); assert.equal(finished, 0)
  revision = 9; fail = false; elements(tree, e => e.type === 'button' && e.props.children === 'Carregar definição atual')[0].props.onClick()
  edit(); await settle(); tree = edit(); elements(tree, e => typeof e.type === 'function' && e.props.onSave)[0].props.onSave({ ...input, title: 'fresh' })
  await settle(); assert.equal(writes[1].expected, 9); assert.equal(finished, 1); h.dispose()
})
await check('authoring/taking import and mode boundaries, exact revision and delete semantics', () => {
  const s = source('components/MapWorkspace.tsx'); assert.match(s, /key=\{`\$\{selected.id\}:quiz-view`\}/); assert.match(s, /key=\{`\$\{selected.id\}:quiz-edit`\}/)
  assert.ok(s.indexOf('<QuizView') < s.indexOf('{editing && <aside')); assert.ok(s.indexOf('<QuizAuthoring') > s.indexOf('{editing && <aside'))
  assert.match(source('quizzes/QuizAuthoring.tsx'), /client.replace\(mapId, nodeId, quiz.id, loaded.contentRevision/)
  for (const name of ['QuizTaking.tsx', 'quizTakingState.ts', 'QuizResult.tsx']) assert.doesNotMatch(source('quizzes/' + name), /QuizDefinition|correctOptionIndex/)
})
await check('no forbidden persistence, timer, replay scheduler or study coupling', () => {
  for (const name of ['QuizTaking.tsx', 'quizTakingState.ts', 'QuizAuthoring.tsx', 'QuizView.tsx', 'QuizHistory.tsx', 'QuizForm.tsx'])
    assert.doesNotMatch(source('quizzes/' + name), /localStorage|sessionStorage|indexedDB|beforeunload|setInterval|WebSocket|StudySession|dangerouslySetInnerHTML/)
})
console.log(`${passed} quiz frontend checks PASS`)
