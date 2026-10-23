import { useEffect, useId, useState, useSyncExternalStore } from 'react'
import type { NodeProgress, ProgressDays } from '../types/nodeProgress.ts'
import { createNodeProgressState } from './nodeProgressState.ts'

type Props = { accountId: string; mapId: string; nodeId: string; revalidate: () => Promise<void> }
const number = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 0 })
const percent = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 })
const date = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'medium' })
const ratio = (value: number | null) => value === null ? 'Sem respostas registradas' : `${percent.format(value)}%`

function Metrics({ data }: { data: NodeProgress }) {
  const { study, flashcards, quizzes, period } = data
  const empty = study.completedSessionCount === 0 && flashcards.reviewCount === 0 && quizzes.attemptCount === 0
  return <>
    {empty && <p role="status">Nenhuma atividade registrada neste período.</p>}
    <p>Período: {date.format(new Date(period.fromUtc))} (incluído) até {date.format(new Date(period.toUtc))} (não incluído).
      {' '}Fuso: {date.resolvedOptions().timeZone}. São {period.days} dias de 24 horas.</p>
    <p>Calculado em: {date.format(new Date(data.calculatedAtUtc))}.</p>
    <h4>Estudo</h4><dl className="activity-metrics">
      <dt>Sessões concluídas</dt><dd>{number.format(study.completedSessionCount)}</dd>
      <dt>Tempo total</dt><dd>{number.format(study.totalDurationSeconds)} segundos</dd>
    </dl>
    <p>Duração integral dos períodos encerrados no intervalo, mesmo quando começaram antes. Estudos ativos não entram neste total.</p>
    <h4>Flashcards</h4><dl className="activity-metrics">
      <dt>Revisões</dt><dd>{number.format(flashcards.reviewCount)}</dd>
      <dt>Acertos</dt><dd>{number.format(flashcards.correctCount)}</dd>
      <dt>Percentual de acertos autorrelatados</dt><dd>{ratio(flashcards.correctPercentage)}</dd>
    </dl><p>Inclui respostas repetidas e revisões anteriores dos cartões; não mede retenção ao longo do tempo.</p>
    <h4>Quizzes</h4><dl className="activity-metrics">
      <dt>Tentativas</dt><dd>{number.format(quizzes.attemptCount)}</dd>
      <dt>Respostas corretas</dt><dd>{number.format(quizzes.correctAnswerCount)}</dd>
      <dt>Questões respondidas</dt><dd>{number.format(quizzes.answeredQuestionCount)}</dd>
      <dt>Percentual de acertos por questão</dt><dd>{ratio(quizzes.accuracyPercentage)}</dd>
    </dl><p>Usa as questões respondidas nas tentativas históricas, sem recalcular respostas após edições.</p>
    <p>Conclusão de conteúdo não é acompanhada. Estes dados descrevem atividade e resultados, sem medir domínio do conceito.</p>
    <p>Alterações em outros painéis ou abas aparecem ao atualizar ou reabrir esta seção.</p>
  </>
}

function useNodeProgress({ accountId, mapId, nodeId, revalidate }: Props) {
  const [read] = useState(() => createNodeProgressState(accountId, mapId, nodeId, revalidate))
  const state = useSyncExternalStore(read.subscribe, read.getSnapshot)
  useEffect(() => { read.mount(); return () => read.dispose() }, [read])
  return { read, state }
}

function ProgressVisit(props: Props) {
  const { read, state } = useNodeProgress(props)
  const id = useId()
  const open = state.status !== 'closed', busy = state.status === 'loading' || state.status === 'session-check'
  const failed = state.status === 'error' || state.status === 'not-found'
  return <section className="activity-section" aria-labelledby={`${id}-title`}>
    <h3 id={`${id}-title`}>Atividade e resultados</h3>
    <button type="button" aria-expanded={open} aria-controls={`${id}-body`} onClick={() => {
      if (open) read.close(); else void read.open()
    }}>{open ? 'Fechar atividade e resultados' : 'Ver atividade e resultados'}</button>
    <p role="status" className={busy ? '' : 'sr-only'}>{state.status === 'loading' ? 'Carregando atividade e resultados…'
      : state.status === 'session-check' ? 'Reavaliando a sessão…' : state.status === 'ready' ? 'Atividade e resultados carregados.' : ''}</p>
    <div id={`${id}-body`} hidden={!open} aria-busy={busy}>
      {open && <><fieldset className="activity-presets"><legend>Período em dias</legend>
        {([7, 30, 90] as ProgressDays[]).map(days => <label key={days}><input type="radio" name={`${id}-days`}
          value={days} checked={state.days === days} onChange={() => { void read.selectDays(days) }} />{days} dias</label>)}
      </fieldset>
      {/* Keep the same focusable control through retry/loading/result, without stealing focus if the user moves away. */}
      <button key="read-action" type="button" aria-disabled={busy} onClick={() => { if (!busy) void read.refresh() }}>
        {busy ? 'Carregando atividade e resultados…' : failed ? 'Tentar novamente' : 'Atualizar atividade e resultados'}
      </button>
      {failed && <p role="alert">{state.message}</p>}
      {state.status === 'ready' && <Metrics data={state.data} />}</>}
    </div>
  </section>
}

// The key hides the previous account/node synchronously, before effect cleanup runs.
export function NodeProgressSection(props: Props) {
  return <ProgressVisit key={JSON.stringify([props.accountId, props.mapId, props.nodeId])} {...props} />
}
