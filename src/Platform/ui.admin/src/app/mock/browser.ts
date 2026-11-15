import { setupWorker } from 'msw/browser'
import type { MachineAccess } from '@42for.net/2splatform.sdk'
import { getStorytellerMock } from '@42for.net/2splatform.sdk/msw'
import { clearCatalogSnapshots } from '@/stores/catalog'
import { accountActor } from './audit'
import {
  applyStartScenario,
  launchScenario,
  loadDb,
  resetDb,
  saveDb,
  type MockDb,
  type MockExhibition,
  type MockScenario,
} from './db'
import { getDocument, latest, putVersion, type ScopeRef } from './documents'
import { mockHandlers } from './all-handlers'
import { writeShowcaseSettings } from './showcase/settings'
import { setStaleSignIn } from './step-up'

declare global {
  interface Window {
    storytellerMock?: {
      reset: (scenario?: MockScenario) => void
      /** Saves a new configuration version as another user, like a concurrent edit (E2E conflict flow). */
      changeConfiguration: (key: string, changes: Record<string, unknown>, scope?: ScopeRef) => void
      /** Adds a machine to a project as its Owner would, for what a lower role may do with it (E2E access flows). */
      addMachine: (projectKey: string, machine: MachineAccess) => void
      /** Sets the exhibition, or none with null; onboarding asks for it once per page load (E2E onboarding flows). */
      setExhibition: (value: MockExhibition | null) => void
      /**
       * Makes the sign-in of the tab stale (false: recent again): the guarded operations answer the step-up 401 until
       * the mock sign-in with maxAge (424-264).
       */
      staleSignIn: (stale?: boolean) => void
    }
  }
}

const defaultScope: ScopeRef = { org: 'acme', project: 'billing', view: 'default' }

/**
 * A dataset starts: no catalog snapshot of another dataset shows for a scope of the same name, and the showcase brings
 * the names, colors and icons of its projects' types.
 */
async function startDataset(db: MockDb): Promise<void> {
  if (db.scenario === 'showcase') {
    writeShowcaseSettings()
  }
  await clearCatalogSnapshots()
}

/** Starts MSW in the browser: the coherent mock handlers first, the generated Faker handlers as the fallback. */
export async function startMockApi(): Promise<void> {
  // The seed is stored at once, so a reload keeps the dataset (and its timestamps) until it is reset.
  if (applyStartScenario(launchScenario(import.meta.env.VITE_MOCK_DATASET))) {
    await startDataset(resetDb())
  }
  const worker = setupWorker(...mockHandlers, ...getStorytellerMock())

  await worker.start({ onUnhandledRequest: 'bypass', quiet: true, serviceWorker: { url: '/mockServiceWorker.js' } })

  window.storytellerMock = {
    reset: (scenario) => void startDataset(resetDb(scenario)),
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
    setExhibition: (value) => {
      const db = loadDb()
      db.exhibition = value
      saveDb(db)
    },
    staleSignIn: (stale = true) => setStaleSignIn(stale),
  }
}
