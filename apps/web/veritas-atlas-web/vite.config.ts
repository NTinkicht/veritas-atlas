import { defineConfig, loadEnv } from 'vite'
import { configDefaults } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { cspPlugin } from './src/security/csp'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), 'VITE_')
  return {
    plugins: [react(), cspPlugin(env.VITE_API_BASE_URL)],
    // Playwright specs live in e2e/ and run via `npm run test:e2e`, never under vitest.
    test: { exclude: [...configDefaults.exclude, 'e2e/**'] },
  }
})
