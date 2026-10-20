import { setupWorker } from 'msw/browser'
import type { MachineAccess } from '@42for.net/2splatform.sdk'
import { getStorytellerMock } from '@42for.net/2splatform.sdk/msw'
import { accountActor } from './audit'
import { applyScenarioFromUrl, loadDb, resetDb, saveDb, type MockScenario } from './db'
import { getDocument, latest, putVersion, type ScopeRef } from './documents'
import { mockHandlers } from './all-handlers'

declare global {
  interface Window {
    storytellerMock?: {
      reset: (scenario?: MockScenario) => void
      /** Saves a new configuration version as another user, like a concurrent edit (E2E conflict flow). */
      changeConfiguration: (key: string, changes: Record<string, unknown>, scope?: ScopeRef) => void
      /** Adds a machine to a project as its Owner would, for what a lower role may do with it (E2E access flows). */
      addMachine: (projectKey: string, machine: MachineAccess) => void
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
      putVersion(db, scope, id, content).Modified.By = accountActor('user_01MOCKGRACE')
      saveDb(db)
    },
    addMachine: (projectKey, machine) => {
      const db = loadDb()
      ;((db.machines ??= {})[projectKey] ??= []).push(machine)
      saveDb(db)
    },
  }
}
