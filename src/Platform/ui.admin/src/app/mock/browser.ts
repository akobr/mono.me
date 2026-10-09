import { setupWorker } from 'msw/browser'
import { getStorytellerMock } from '@42for.net/2splatform.sdk/msw'
import { resetDb, type MockScenario } from './db'
import { mockHandlers } from './handlers'

declare global {
  interface Window {
    storytellerMock?: { reset: (scenario?: MockScenario) => void }
  }
}

/** Starts MSW in the browser: the coherent mock handlers first, the generated Faker handlers as the fallback. */
export async function startMockApi(): Promise<void> {
  const worker = setupWorker(...mockHandlers, ...getStorytellerMock())

  await worker.start({ onUnhandledRequest: 'bypass', quiet: true, serviceWorker: { url: '/mockServiceWorker.js' } })

  window.storytellerMock = { reset: (scenario) => void resetDb(scenario) }
}
