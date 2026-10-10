import { setupWorker } from 'msw/browser'
import { getStorytellerMock } from '@42for.net/2splatform.sdk/msw'
import { applyScenarioFromUrl, loadDb, resetDb, saveDb, type MockScenario } from './db'
import { getDocument, latest, putVersion, type ScopeRef } from './documents'
import { mockHandlers } from './all-handlers'

declare global {
  interface Window {
    storytellerMock?: {
      reset: (scenario?: MockScenario) => void
      /** Saves a new configuration version as another user, like a concurrent edit (E2E conflict flow). */
      changeConfiguration: (key: string, changes: Record<string, unknown>, scope?: ScopeRef) => void
    }
  }
}

const defaultScope: ScopeRef = { org: 'acme', project: 'billing', view: 'default' }

/** Starts MSW in the browser: the coherent mock handlers first, the generated Faker handlers as the fallback. */
export async function startMockApi(): Promise<void> {
  applyScenarioFromUrl()
  const worker = setupWorker(...mockHandlers, ...getStorytellerMock())

  await worker.start({ onUnhandledRequest: 'bypass', quiet: true, serviceWorker: { url: '/mockServiceWorker.js' } })

  window.storytellerMock = {
    reset: (scenario) => void resetDb(scenario),
    changeConfiguration: (key, changes, scope = defaultScope) => {
      const db = loadDb()
      const id = `configuration:${key}` as const
      const content = { ...latest(getDocument(db, scope, id))?.Content, ...changes }
      putVersion(db, scope, id, content).Author = 'Grace Hopper'
      saveDb(db)
    },
  }
}
