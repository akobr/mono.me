import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import type { Plugin } from 'vite'

// Serves (dev) and emits (build) msw's mockServiceWorker.js, but only in the e2e mode,
// so the worker script is never part of a production build and is not committed.
export function mockServiceWorker(): Plugin {
  const require = createRequire(import.meta.url)
  const source = () => readFileSync(require.resolve('msw/mockServiceWorker.js'), 'utf8')

  return {
    name: '2s-admin:mock-service-worker',
    configureServer(server) {
      server.middlewares.use('/mockServiceWorker.js', (_request, response) => {
        response.setHeader('Content-Type', 'text/javascript')
        response.end(source())
      })
    },
    generateBundle() {
      this.emitFile({ type: 'asset', fileName: 'mockServiceWorker.js', source: source() })
    },
  }
}
