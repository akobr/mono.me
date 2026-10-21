import { spawn } from 'node:child_process'
import { createRequire } from 'node:module'
import { dirname, join } from 'node:path'
import { devMockUsage, parseDevMockArguments } from './mock-dataset.ts'

// `npm run dev:mock [-- --legacy | --large | --scenario=<name>] [vite options]`: Vite in the e2e mode (MSW mock API,
// fake user) on port 5174, with the chosen dataset in VITE_MOCK_DATASET, which app/mock/browser.ts reads at start-up.
// Node runs the TypeScript as it is (type stripping), like build/write-environment-config.ts. The exit code is set,
// not forced with process.exit: on Windows, exiting while stderr still flushes to a pipe aborts Node.
function start(): void {
  const parsed = parseDevMockArguments(process.argv.slice(2))
  if ('error' in parsed) {
    console.error(`${parsed.error}\n\n${devMockUsage}`)
    process.exitCode = 2
    return
  }

  // Vite does not export its bin; its package.json is exported and sits next to it.
  const vite = join(dirname(createRequire(import.meta.url).resolve('vite/package.json')), 'bin/vite.js')
  const hint = parsed.dataset === 'showcase' ? ' (npm run dev:mock -- --legacy for the previous one)' : ''
  console.log(`Mock dataset: ${parsed.dataset}${hint}`)

  const child = spawn(
    process.execPath,
    [vite, '--mode', 'e2e', '--port', '5174', '--strictPort', ...parsed.viteArguments],
    { stdio: 'inherit', env: { ...process.env, VITE_MOCK_DATASET: parsed.dataset } },
  )

  const signals = ['SIGINT', 'SIGTERM'] as const
  for (const signal of signals) {
    process.on(signal, () => child.kill(signal))
  }

  child.on('exit', (code, signal) => {
    if (signal) {
      // End the way Vite ended, without running the forwarding listeners again.
      signals.forEach((forwarded) => process.removeAllListeners(forwarded))
      process.kill(process.pid, signal)
      return
    }
    process.exitCode = code ?? 1
  })
}

start()
