import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import { cspPlugin } from './src/security/csp'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), 'VITE_')
  return {
    plugins: [react(), cspPlugin(env.VITE_API_BASE_URL)],
  }
})
