import { useId, useRef, useState } from 'react'
import type { QuizInput } from '../types/quiz.ts'
import { quizChanged, quizInput } from './quizInput.ts'

export function QuizForm({ initial, pending, onSave, onCancel }:
  { initial?: QuizInput; pending: boolean; onSave: (input: QuizInput) => void; onCancel: () => void }) {
  const prefix = useId(), counter = useRef(100)
  const [title, setTitle] = useState(initial?.title ?? '')
  const [questions, setQuestions] = useState(() => (initial?.questions ?? [{ text: '', options: ['', ''], correctOptionIndex: -1 }])
    .map((q, i) => ({ key: `q${i}`, text: q.text, options: q.options.map((text, j) => ({ key: `q${i}o${j}`, text })),
      correct: q.correctOptionIndex < 0 ? '' : `q${i}o${q.correctOptionIndex}` })))
  const input = quizInput(title, questions.map(q => ({ text: q.text, options: q.options.map(o => o.text), correctOptionIndex: q.options.findIndex(o => o.key === q.correct) })))
  const changed = !!input && (!initial || quizChanged(initial, input))
  const field = (key: string) => `${prefix}-${key}`
  return <form className="authoring-form quiz-form" onSubmit={e => { e.preventDefault(); if (input && changed && !pending) onSave(input) }}>
    <h4>{initial ? 'Editar quiz completo' : 'Novo quiz'}</h4>
    <p id={field('limits')}>Título até 200, pergunta até 1000 e alternativa até 500 unidades UTF-16. De 1 a 10 perguntas, 2 a 4 alternativas e exatamente uma correta por pergunta. Alternativas iguais após remover espaços das pontas não são válidas.</p>
    <fieldset disabled={pending} aria-describedby={field('limits')}>
      <label htmlFor={field('title')}>Título do quiz</label><input id={field('title')} required maxLength={200} value={title} onChange={e => setTitle(e.target.value)} autoFocus />
      {questions.map((q, i) => <fieldset key={q.key} className="quiz-question">
        <legend>Pergunta {i + 1}</legend><label htmlFor={field(q.key)}>Texto da pergunta {i + 1}</label>
        <textarea id={field(q.key)} required maxLength={1000} value={q.text} onChange={e => setQuestions(rows => rows.map(row => row.key === q.key ? { ...row, text: e.target.value } : row))} />
        {q.options.map((option, j) => <div key={option.key} className="quiz-option-editor">
          <label htmlFor={field(option.key)}>Alternativa {j + 1} da pergunta {i + 1}</label>
          <textarea id={field(option.key)} required maxLength={500} value={option.text} onChange={e => setQuestions(rows => rows.map(row => row.key === q.key
            ? { ...row, options: row.options.map(o => o.key === option.key ? { ...o, text: e.target.value } : o) } : row))} />
          <label className="quiz-radio"><input type="radio" required name={field(`${q.key}-correct`)} checked={q.correct === option.key}
            onChange={() => setQuestions(rows => rows.map(row => row.key === q.key ? { ...row, correct: option.key } : row))} />Correta: alternativa {j + 1}</label>
          <button type="button" disabled={q.options.length <= 2} onClick={() => setQuestions(rows => rows.map(row => row.key === q.key
            ? { ...row, correct: row.correct === option.key ? '' : row.correct, options: row.options.filter(o => o.key !== option.key) } : row))}>Remover alternativa {j + 1}</button>
        </div>)}
        <div className="editor-actions"><button type="button" disabled={q.options.length >= 4} onClick={() => {
          const key = `o${counter.current++}`; setQuestions(rows => rows.map(row => row.key === q.key ? { ...row, options: [...row.options, { key, text: '' }] } : row))
        }}>Adicionar alternativa</button><button type="button" disabled={questions.length <= 1} onClick={() => setQuestions(rows => rows.filter(row => row.key !== q.key))}>Remover pergunta {i + 1}</button></div>
      </fieldset>)}
      <button type="button" disabled={questions.length >= 10} onClick={() => {
        const key = `q${counter.current++}`; setQuestions(rows => [...rows, { key, text: '', options: [{ key: `${key}a`, text: '' }, { key: `${key}b`, text: '' }], correct: '' }])
      }}>Adicionar pergunta</button>
      {!input && <p>Preencha todos os textos, escolha uma correta por pergunta e confira os limites.</p>}
      {input && !changed && <p>Não há alteração material para salvar.</p>}
      <div className="editor-actions"><button type="submit" disabled={!changed}>Salvar quiz</button><button type="button" onClick={onCancel}>Cancelar quiz</button></div>
    </fieldset>
  </form>
}
