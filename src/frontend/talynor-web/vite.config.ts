import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

// https://vite.dev/config/
export default defineConfig(({ command, mode, isPreview }) => {
  if (command !== 'serve' || isPreview) return { plugins: [react()] }

  const env = loadEnv(mode, process.cwd(), 'TALYNOR_')
  const target = process.env.TALYNOR_API_PROXY_TARGET ?? env.TALYNOR_API_PROXY_TARGET
  if (!target) {
    throw new Error('Defina TALYNOR_API_PROXY_TARGET com a URL local da API.')
  }
  const insecure = (process.env.TALYNOR_API_PROXY_INSECURE ?? env.TALYNOR_API_PROXY_INSECURE) === 'true'
  if (target) {
    const url = new URL(target)
    if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password) {
      throw new Error('Target do proxy deve ser uma URL HTTP(S) sem credenciais.')
    }
    if (insecure && (url.protocol !== 'https:' || !['localhost', '127.0.0.1', '[::1]'].includes(url.hostname))) {
      throw new Error('Exceção TLS permitida somente para HTTPS localhost no proxy de desenvolvimento.')
    }
  }
  return {
    plugins: [react()],
    server: {
      proxy: target ? { '/api': { target, changeOrigin: true, secure: !insecure } } : undefined,
    },
  }
})
