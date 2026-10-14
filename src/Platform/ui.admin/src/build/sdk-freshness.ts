import { existsSync, mkdirSync, readdirSync, readFileSync, statSync, watch, writeFileSync, type FSWatcher } from 'node:fs'
import { createHash } from 'node:crypto'
import { basename, dirname, join, resolve } from 'node:path'
import type { Plugin } from 'vite'

const sdk = '@42for.net/2splatform.sdk'

// The SDK is installed from a local folder (file:), so a rebuilt SDK changes neither the version nor the lockfile,
// and the dev server would keep its pre-bundled copy of the old one: the app then runs two instances of
// @tanstack/vue-query ("client.defaultQueryOptions is not a function"). The plugin stamps the installed dist,
// forces the re-optimization when the stamp differs from the last run, and restarts a running server when the
// installed dist changes (npm install after an SDK build).
export function sdkFreshness(): Plugin {
  let root = process.cwd()
  let cacheDir = join(root, 'node_modules/.vite')
  const dist = () => join(root, 'node_modules', sdk, 'dist')
  const stampFile = () => join(cacheDir, 'sdk-stamp')

  const stamp = () => {
    if (!existsSync(dist())) return ''
    const hash = createHash('sha256')
    for (const file of readdirSync(dist()).toSorted()) {
      const { size, mtimeMs } = statSync(join(dist(), file))
      hash.update(`${file}:${size}:${mtimeMs};`)
    }
    return hash.digest('hex')
  }
  const saveStamp = (value: string) => {
    mkdirSync(dirname(stampFile()), { recursive: true })
    writeFileSync(stampFile(), value)
  }

  return {
    name: '2s-admin:sdk-freshness',
    // Vitest runs a dev server too; its dependencies are not pre-bundled.
    apply: (_config, { command }) => command === 'serve' && !process.env.VITEST,
    config(config) {
      root = resolve(config.root ?? process.cwd())
      cacheDir = resolve(root, config.cacheDir ?? 'node_modules/.vite')
      const current = stamp()
      const previous = existsSync(stampFile()) ? readFileSync(stampFile(), 'utf8') : undefined
      saveStamp(current)
      return current !== previous ? { optimizeDeps: { force: true } } : undefined
    },
    configureServer(server) {
      // Vite's watcher ignores node_modules. The SDK is reinstalled by removing its scope folder
      // (README: rm -rf node_modules/@42for.net && npm install), so node_modules watches the scope folder come
      // back, and the scope folder, recursively, watches the files npm writes.
      const modules = join(root, 'node_modules')
      const scope = dirname(join(modules, sdk))
      let timer: ReturnType<typeof setTimeout> | undefined
      let scopeWatcher: FSWatcher | undefined
      const check = () => {
        // npm writes the files one by one; check once it is done.
        clearTimeout(timer)
        timer = setTimeout(() => {
          const current = stamp()
          if (!current || current === readFileSync(stampFile(), 'utf8')) return
          saveStamp(current)
          server.config.logger.info(`${sdk} changed, re-optimizing the dependencies`, { timestamp: true })
          void server.restart(true)
        }, 1000)
      }
      const watchScope = () => {
        scopeWatcher?.close()
        scopeWatcher = undefined
        if (!existsSync(scope)) return
        scopeWatcher = watch(scope, { recursive: true }, check).unref()
        scopeWatcher.on('error', () => {
          scopeWatcher?.close()
          scopeWatcher = undefined
        })
      }
      if (!existsSync(modules)) return
      // The watchers never keep the process alive; Vite closes the server on its own.
      const modulesWatcher = watch(modules, (_event, file) => {
        if (file !== basename(scope)) return
        watchScope()
        check()
      }).unref()
      watchScope()
      server.httpServer?.on('close', () => {
        clearTimeout(timer)
        modulesWatcher.close()
        scopeWatcher?.close()
      })
    },
  }
}
