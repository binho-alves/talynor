import { useState, type SubmitEvent } from 'react'

type Props = {
  busy: boolean
  error?: string
  message?: string
  onLogin: (email: string, password: string) => Promise<void>
}

export function LoginView({ busy, error, message, onLogin }: Props) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  async function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    try { await onLogin(email, password) } finally { setPassword('') }
  }

  return (
    <main className="session-page">
      <section className="session-card" aria-labelledby="login-title">
        <p className="brand">Talynor</p>
        <h1 id="login-title">Entre para continuar</h1>
        <p className="intro">Entre para acessar seu workspace pessoal.</p>
        {message && <p role="status">{message}</p>}
        <form onSubmit={submit} aria-busy={busy} aria-describedby={error ? 'login-error' : undefined}>
          <label htmlFor="login-email">Email</label>
          <input id="login-email" name="email" type="email" autoComplete="username"
            required maxLength={256} value={email} disabled={busy}
            onChange={(event) => setEmail(event.target.value)} />
          <label htmlFor="login-password">Senha</label>
          <input id="login-password" name="password" type="password" autoComplete="current-password"
            required maxLength={128} value={password} disabled={busy}
            onChange={(event) => setPassword(event.target.value)} />
          {error && <p id="login-error" className="session-feedback" role="alert">{error}</p>}
          <button type="submit" className="primary-button" disabled={busy}>
            {busy ? 'Entrando…' : 'Entrar'}
          </button>
          <span className="sr-only" role="status">{busy ? 'Validando sua sessão…' : ''}</span>
        </form>
      </section>
    </main>
  )
}
