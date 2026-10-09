import './assets/css/main.css'
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { VueQueryPlugin } from '@tanstack/vue-query'
import ui from '@nuxt/ui/vue-plugin'
import type { Router } from 'vue-router'
import App from './App.vue'
import { configureApi } from './boot/api'
import { loadAppConfig } from './boot/app-config'
import { createAuth } from './boot/auth'
import { createQueryClient } from './boot/query'
import { setBootState, type BootState } from './boot/state'
import { createAppRouter } from './router'
import { onSignOut, useSessionStore } from './stores/session'

// The e2e mode (vite --mode e2e) serves the API from MSW and signs in a fake user. The checks are static,
// so production builds drop the mock modules entirely.
const isMock = import.meta.env.MODE === 'e2e'

async function boot(router: Router): Promise<BootState> {
  try {
    const config = await loadAppConfig()
    configureApi(config, () => void useSessionStore().signIn(router.currentRoute.value.fullPath))

    if (isMock) {
      const { createMockAuth } = await import('./mock/auth')
      return { status: 'ready', auth: createMockAuth(), config }
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
const router = createAppRouter()

app.use(createPinia())
setBootState(await boot(router))
onSignOut(() => queryClient.clear())

app.use(VueQueryPlugin, { queryClient })
app.use(router)
app.use(ui)
app.mount('#app')
