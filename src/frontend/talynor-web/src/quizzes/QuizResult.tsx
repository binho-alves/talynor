import { useEffect, useRef } from 'react'
import type { QuizAttempt } from '../types/quizAttempt.ts'

export function QuizResult({ result }: { result: QuizAttempt }) {
  const heading = useRef<HTMLHeadingElement>(null)
  useEffect(() => { heading.current?.focus() }, [result])
  return <article className="quiz-result" aria-label="Resultado histórico do quiz">
    <h4 ref={heading} tabIndex={-1}>Resultado: {result.quizTitle}</h4>
    <p><strong>{result.correctCount}/{result.questionCount} corretas — {result.accuracyPercentage.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%</strong></p>
    <p>Revisão {result.contentRevision} · <time dateTime={result.submittedAtUtc}>{new Date(result.submittedAtUtc).toLocaleString()}</time></p>
    <p>Este resultado descreve somente esta tentativa.</p>
    <ol>{result.answers.map(a => <li key={a.questionId}>
      <p className="quiz-text">{a.text}</p><strong>{a.isCorrect ? 'Acertou' : 'Errou'}</strong>
      <ul>{a.options.map(o => <li key={o.id} className="quiz-text">{o.text}{o.id === a.selectedOptionId && <strong> — sua escolha</strong>}{o.id === a.correctOptionId && <strong> — resposta correta</strong>}</li>)}</ul>
    </li>)}</ol>
  </article>
}
