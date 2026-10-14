import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import VueRouter from 'vue-router/vite'
import ui from '@nuxt/ui/vite'
import vueDevTools from 'vite-plugin-vue-devtools'
import { mockServiceWorker } from './build/mock-service-worker.ts'
import { sdkFreshness } from './build/sdk-freshness.ts'

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
      // TypeScript files are scanned too: type, status and navigation icons are defined there.
      icon: { clientBundle: { scan: { globInclude: ['app/**/*.{vue,ts}'] } } },
    }),
    command === 'serve' && mode === 'development' && vueDevTools(),
    mode === 'e2e' && mockServiceWorker(),
    sdkFreshness(),
  ],
  resolve: { alias: { '@': fileURLToPath(new URL('./app', import.meta.url)) } },
  // The dev server pre-bundles the dependencies it finds by crawling from index.html. The pages sit behind the
  // virtual vue-router/auto-routes module and the components are auto-imported, so the crawl would miss most of
  // the app, and every page that imports a new package would re-optimize the dependencies and reload the app.
  optimizeDeps: {
    entries: ['index.html', 'app/**/*.{vue,ts}', '!app/**/*.d.ts'],
    // Imported only by the generated routes.
    include: ['vue-router/experimental'],
  },
  worker: { format: 'es' },
  build: {
    target: 'es2022',
    sourcemap: true,
    // Fonts stay files: inlined data: fonts would need font-src data: in the CSP (spec section 16).
    assetsInlineLimit: (file) => (/\.(woff2?|ttf|otf)$/.test(file) ? false : undefined),
  },
}))
