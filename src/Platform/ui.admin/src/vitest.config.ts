import { defineConfig, mergeConfig } from 'vitest/config'
import viteConfig from './vite.config'

export default defineConfig((env) =>
  mergeConfig(viteConfig({ ...env, mode: 'test' }), {
    test: {
      environment: 'happy-dom',
      include: ['test/unit/**/*.test.ts'],
      setupFiles: ['test/setup.ts'],
      restoreMocks: true,
    },
  }),
)
