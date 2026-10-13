import react from '@vitejs/plugin-react'
import { readFileSync } from 'node:fs'
import { defineConfig, loadEnv } from 'vite'

// https://vite.dev/config/
export default defineConfig(({ command, mode, isPreview }) => {
  if (command !== 'serve' || isPreview) return { plugins: [react()] }

  const env = loadEnv(mode, process.cwd(), 'TALYNOR_')
  const target = process.env.TALYNOR_API_PROXY_TARGET ?? env.TALYNOR_API_PROXY_TARGET
  if (!target) {
    throw new Error('Defina TALYNOR_API_PROXY_TARGET com a URL local da API.')
  }
  if ((process.env.TALYNOR_API_PROXY_INSECURE ?? env.TALYNOR_API_PROXY_INSECURE) === 'true') {
    throw new Error('Authenticated Frontend exige verificação TLS no proxy. Remova TALYNOR_API_PROXY_INSECURE.')
  }
  const certPath = process.env.TALYNOR_HTTPS_CERT ?? env.TALYNOR_HTTPS_CERT
  const keyPath = process.env.TALYNOR_HTTPS_KEY ?? env.TALYNOR_HTTPS_KEY
  if (!certPath || !keyPath) {
    throw new Error('Defina TALYNOR_HTTPS_CERT e TALYNOR_HTTPS_KEY com caminhos de certificado/chave HTTPS locais confiáveis, fora do repositório.')
  }
  if (target) {
    const url = new URL(target)
    if (url.protocol !== 'https:' || url.username || url.password) {
      throw new Error('Target do proxy autenticado deve ser uma URL HTTPS sem credenciais.')
    }
  }
  return {
    plugins: [react()],
    server: {
      host: 'localhost',
      https: { cert: readFileSync(certPath), key: readFileSync(keyPath) },
      proxy: { '/api': { target, changeOrigin: true, secure: true } },
    },
  }
})
