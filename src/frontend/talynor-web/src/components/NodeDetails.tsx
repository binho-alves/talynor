import type { KnowledgeNode } from '../types/knowledge'

type NodeDetailsProps = {
  node?: KnowledgeNode
  onOpenSubmap: (node: KnowledgeNode) => void
}

export function NodeDetails({ node, onOpenSubmap }: NodeDetailsProps) {
  const studyMinutes = node?.data.studyMinutes ?? 0
  const hours = Math.floor(studyMinutes / 60)
  const minutes = studyMinutes % 60

  return (
    <aside className="details-panel" aria-labelledby="details-title">
      <p className="section-caption">Detalhes do conceito</p>
      {node ? (
        <>
          <h2 id="details-title">{node.data.title}</h2>
          <p className="details-description">{node.data.description}</p>
          <div className="progress-heading">
            <label htmlFor="study-progress">Progresso</label>
            <strong>{node.data.progress}%</strong>
          </div>
          <progress id="study-progress" value={node.data.progress} max={100} />
          <dl className="study-time">
            <dt>Tempo estudado</dt>
            <dd>{hours > 0 ? hours + 'h ' : ''}{minutes}min</dd>
          </dl>
          <p className="mock-note">Progresso e tempo são dados mockados.</p>
          {node.data.submapId && (
            <button className="primary-button" onClick={() => onOpenSubmap(node)}>
              Explorar {node.data.title} →
            </button>
          )}
        </>
      ) : (
        <>
          <h2 id="details-title">Selecione um nó</h2>
          <p className="details-description">
            Toque ou clique em um conceito no mapa para ver sua descrição,
            progresso e tempo estudado.
          </p>
        </>
      )}
    </aside>
  )
}
