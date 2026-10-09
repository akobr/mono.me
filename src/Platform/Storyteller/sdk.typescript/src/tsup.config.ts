import { defineConfig } from 'tsup'

// One ESM bundle per entry point. Peers (vue, @tanstack/vue-query, zod, msw, @faker-js/faker) stay external,
// and code shared by the entries goes into chunks, so the fetcher configuration is a single module.
export default defineConfig({
  entry: ['index.ts', 'vue-query.ts', 'zod.ts', 'msw.ts'],
  format: ['esm'],
  target: 'es2022',
  platform: 'neutral',
  // tsup's declaration build still sets baseUrl, which TypeScript 6 deprecates.
  dts: { compilerOptions: { ignoreDeprecations: '6.0' } },
  sourcemap: true,
  clean: true,
  splitting: true,
  treeshake: true,
})
