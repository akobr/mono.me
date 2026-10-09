import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import VueRouter from 'vue-router/vite'
import ui from '@nuxt/ui/vite'
import vueDevTools from 'vite-plugin-vue-devtools'
import { mockServiceWorker } from './build/mock-service-worker.ts'

export default defineConfig(({ command, mode }) => ({
  plugins: [
    // Vue Router 5 file-based routing; must come before vue().
    VueRouter({ routesFolder: 'app/pages', dts: 'app/typed-router.d.ts' }),
    vue(),
    ui({
      ui: { colors: { primary: 'indigo', neutral: 'zinc' } },
      autoImport: { dirs: ['app/composables', 'app/stores'], dts: 'app/auto-imports.d.ts' },
      components: { dirs: ['app/components'], dts: 'app/components.d.ts' },
      // Bundle the lucide icons used in the source, so no request goes to the Iconify API (CSP connect-src).
      icon: { clientBundle: { scan: true } },
    }),
    command === 'serve' && mode === 'development' && vueDevTools(),
    mode === 'e2e' && mockServiceWorker(),
  ],
  resolve: { alias: { '@': fileURLToPath(new URL('./app', import.meta.url)) } },
  worker: { format: 'es' },
  build: { target: 'es2022', sourcemap: true },
}))
