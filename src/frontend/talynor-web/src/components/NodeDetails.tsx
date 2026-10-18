import type { ReactNode } from 'react'
import type { KnowledgeNode } from '../types/knowledge'

type NodeDetailsProps = {
  node?: KnowledgeNode
  onOpenSubmap: (node: KnowledgeNode) => void
  children?: ReactNode
}

export function NodeDetails({ node, onOpenSubmap, children }: NodeDetailsProps) {
  return (
    <aside className="details-panel" aria-labelledby="details-title">
      <p className="section-caption">Detalhes do conceito</p>
      {node ? (
        <>
          <h2 id="details-title">{node.data.title}</h2>
          <p className="details-description">{node.data.description || 'Sem descrição.'}</p>
          {node.data.childMapId && (
            <button className="primary-button" onClick={() => onOpenSubmap(node)}>
              Explorar {node.data.title} →
            </button>
          )}
        </>
      ) : (
        <>
          <h2 id="details-title">Selecione um nó</h2>
          <p className="details-description">
            Toque ou clique em um conceito no mapa para ver sua descrição e explorar um submapa disponível.
          </p>
        </>
      )}
      {children}
    </aside>
  )
}
