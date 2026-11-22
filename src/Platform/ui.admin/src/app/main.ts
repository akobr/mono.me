import './assets/css/main.css'
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { VueQueryPlugin } from '@tanstack/vue-query'
import { z } from 'zod'
import ui from '@nuxt/ui/vue-plugin'
import type { Router } from 'vue-router'
import App from './App.vue'
import { configureApi } from './boot/api'
import { loadAppConfig } from './boot/app-config'
import { createAuth, loadAuthConfiguration } from './boot/auth'
import { createQueryClient } from './boot/query'
import { setBootState, type BootState } from './boot/state'
import { resetTelemetry, startTelemetry } from './boot/telemetry'
import { createAppRouter } from './router'
import { onSignOut, useSessionStore } from './stores/session'
// For its sign-out hook: the keys of a compared selection are in sessionStorage, and after a reload no page that
// loads the store may have been opened before the sign-out.
import './stores/view-transfer'
import { useStepUpStore } from './composables/useStepUp'

// The e2e mode (vite --mode e2e) serves the API from MSW and signs in a fake user. The checks are static,
// so production builds drop the mock modules entirely.
const isMock = import.meta.env.MODE === 'e2e'

async function boot(router: Router): Promise<BootState> {
  try {
    const config = await loadAppConfig()
    configureApi(
      config,
      () => void useSessionStore().signIn(router.currentRoute.value.fullPath),
      () => void useStepUpStore().ask(),
    )

    if (isMock) {
      const { createMockAuth } = await import('./mock/auth')
      // The discovery is kept in mock mode too: the screens read StepUp from it.
      return { status: 'ready', auth: createMockAuth(), authConfiguration: await loadAuthConfiguration(config), config }
    }

    return { ...(await createAuth(config)), config }
  } catch (error) {
    return { status: 'error', message: error instanceof Error ? error.message : String(error) }
  }
}

if (isMock) {
  const { startMockApi } = await import('./mock/browser')
  await startMockApi()
}

const app = createApp(App)
const queryClient = createQueryClient()
// Zod would probe `new Function` for its JIT, which the production CSP (script-src 'self') reports as a violation.
z.config({ jitless: true })

const router = createAppRouter()

app.use(createPinia())
const state = await boot(router)
setBootState(state)
// The configuration is known and AuthKit has removed code and state from the URL; the router and the mount come after,
// so Sentry's Vue error handler also catches the errors of the first render.
await startTelemetry(app, router, state.config)
onSignOut(() => queryClient.clear())
onSignOut(() => resetTelemetry())

app.use(VueQueryPlugin, { queryClient })
app.use(router)
app.use(ui)
app.mount('#app')
