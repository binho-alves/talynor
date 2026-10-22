import { AuthenticatedStudyWorkspace } from './study/AuthenticatedStudyWorkspace'
import { LoginView } from './components/LoginView'
import { useSession } from './session/useSession'
import './App.css'

function App() {
  const { session, busy, operationError, login, logout, retryBootstrap, revalidateSession, authoring, contentAuthoring, studySessions, flashcards, quizzes } = useSession()

  if (session.status === 'bootstrapping') {
    return <main className="session-page" aria-busy="true">
      <section className="session-card"><p className="brand">Talynor</p>
        <p role="status">Carregando sua sessão...</p></section>
    </main>
  }
  if (session.status === 'error') {
    return <main className="session-page">
      <section className="session-card" aria-labelledby="session-error-title">
        <p className="brand">Talynor</p>
        <h1 id="session-error-title">Não foi possível carregar sua sessão.</h1>
        <p role="alert">Verifique sua conexão e tente novamente.</p>
        <button className="primary-button" onClick={() => void retryBootstrap()}>Tentar novamente</button>
      </section>
    </main>
  }
  if (session.status === 'anonymous') {
    return <LoginView busy={busy === 'login'} error={operationError} message={session.message} onLogin={login} />
  }

  return <div className="app-shell">
    <header className="app-header">
      <div>
        <p className="brand">Talynor</p>
        <h1>Seu conhecimento, conectado</h1>
        <p className="intro">Explore conceitos e descubra novas conexões.</p>
      </div>
      <div className="session-actions">
        <span className="user-email">{session.user.email}</span>
        <button className="back-button" disabled={busy === 'logout'} onClick={() => void logout()}>
          {busy === 'logout' ? 'Saindo…' : 'Sair'}
        </button>
      </div>
    </header>
    {operationError && <p className="session-feedback" role="alert">{operationError}</p>}
    <span className="sr-only" role="status">{busy === 'logout' ? 'Encerrando sua sessão…' : ''}</span>
    <AuthenticatedStudyWorkspace key={`${session.user.id}:${session.workspace.id}`} revalidate={revalidateSession}
      authoring={authoring} contentAuthoring={contentAuthoring} client={studySessions} flashcards={flashcards} quizzes={quizzes} />
  </div>
}

export default App
