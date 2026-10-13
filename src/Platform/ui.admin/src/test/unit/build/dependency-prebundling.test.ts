// @vitest-environment node
import { glob, mkdtemp, readFile, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { afterAll, beforeAll, describe, expect, it } from 'vitest'
import { createServer, type ViteDevServer } from 'vite'

// The dev server must pre-bundle every dependency of the app when it starts. A dependency found later, when a page
// first imports it, makes Vite re-optimize the dependencies and reload the whole app.
const root = fileURLToPath(new URL('../../..', import.meta.url))

// Static and dynamic imports and re-exports; `import type` is erased and never reaches the browser.
const importRE =
  /(?:^|[\s;])(?:import|export)\s+(?!type\s)(?:[^'"]*?\sfrom\s+)?['"]([^'"]+)['"]|\bimport\(\s*['"]([^'"]+)['"]\s*\)/g
const isBare = (specifier: string) => /^[\w@]/.test(specifier) && !specifier.startsWith('@/')

// Vite never pre-bundles these: the routes are a virtual module, the Nuxt UI plugin excludes @nuxt/ui,
// and JSON files are served as they are.
const notPreBundled = (specifier: string) =>
  specifier === 'vue-router/auto-routes' || specifier.startsWith('@nuxt/ui/') || specifier.endsWith('.json')

/** Bare import specifiers of the app sources, each with the first file that imports it. */
async function bareImports(): Promise<Map<string, string>> {
  const imports = new Map<string, string>()
  for await (const file of glob('app/**/*.{vue,ts}', { cwd: root, exclude: (name) => name.endsWith('.d.ts') })) {
    const source = await readFile(path.join(root, file), 'utf8')
    for (const match of source.matchAll(importRE)) {
      const specifier = match[1] ?? match[2]!
      if (isBare(specifier) && !imports.has(specifier)) {
        imports.set(specifier, file)
      }
    }
  }
  return imports
}

describe('dependency pre-bundling of the dev server', () => {
  let cacheDir: string
  let server: ViteDevServer
  let dependencies: Set<string>

  beforeAll(async () => {
    cacheDir = await mkdtemp(path.join(tmpdir(), '2s-admin-deps-'))
    server = await createServer({
      root,
      configFile: path.join(root, 'vite.config.ts'),
      mode: 'development',
      cacheDir,
      logLevel: 'silent',
      server: { middlewareMode: true, hmr: false, ws: false },
      optimizeDeps: { force: true },
    })

    // The scan that runs when the server starts with an empty cache; no page has been requested yet.
    const optimizer = server.environments.client.depsOptimizer!
    await optimizer.init()
    await optimizer.scanProcessing
    // A dependency is still "discovered" while it is bundled and "optimized" once the bundle is written, so wait for
    // the bundling to settle and count only what was pre-bundled.
    await Promise.allSettled(Object.values(optimizer.metadata.discovered).map((dependency) => dependency.processing))
    dependencies = new Set(Object.keys(optimizer.metadata.optimized))
  }, 60_000)

  afterAll(async () => {
    await server?.close()
    await rm(cacheDir, { recursive: true, force: true })
  })

  it('finds every package the app imports', async () => {
    const imports = await bareImports()
    const missing = [...imports]
      .filter(([specifier]) => !notPreBundled(specifier) && !dependencies.has(specifier))
      .map(([specifier, file]) => `${specifier} (${file})`)

    expect(imports.size).toBeGreaterThan(0)
    expect(missing).toEqual([])
  })

  it('finds vue-router/experimental, which only the generated routes import', () => {
    expect(dependencies).toContain('vue-router/experimental')
  })
})
