import { defineConfig } from 'orval'

// The OpenAPI document is passed by scripts/generate.mjs (SPEC), newest open.api.v*.json by default.
const spec = process.env.SPEC ?? '../open.api.v0.8.91.json'
const mutator = { path: 'lib/fetcher.ts', name: 'storytellerFetch' }

// Corrections of the document before generation, see the script.
const input = { target: spec, override: { transformer: 'scripts/openapi-transformer.mjs' } }

export default defineConfig({
  // Framework-agnostic request functions: the root entry of the package.
  client: {
    input,
    output: {
      mode: 'tags-split',
      target: 'generated/client/storyteller.ts',
      schemas: 'generated/model',
      client: 'fetch',
      httpClient: 'fetch',
      formatter: 'prettier',
      override: {
        mutator,
        fetch: { includeHttpResponseReturnType: false },
      },
    },
  },
  // TanStack Query for Vue composables and their query keys, plus MSW handlers with Faker data.
  vueQuery: {
    input,
    output: {
      mode: 'tags-split',
      target: 'generated/vue-query/storyteller.ts',
      schemas: 'generated/model',
      client: 'vue-query',
      httpClient: 'fetch',
      formatter: 'prettier',
      mock: {
        generators: [{ type: 'msw', useExamples: true, delay: false, baseUrl: '*/api' }],
      },
      override: {
        mutator,
        fetch: { includeHttpResponseReturnType: false },
        query: { signal: true, shouldExportKeys: true },
      },
    },
  },
  // Zod 4 schemas of parameters, bodies and responses.
  zod: {
    input,
    output: {
      mode: 'tags-split',
      target: 'generated/zod/storyteller.ts',
      client: 'zod',
      formatter: 'prettier',
      override: {
        zod: { version: 4 },
      },
    },
  },
})
