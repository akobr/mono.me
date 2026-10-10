// Runs the visual suite in the Playwright Linux image (the same as CI), so screenshots compare across machines.
//   node test/e2e/run-in-container.mjs            compare with the committed baselines
//   node test/e2e/run-in-container.mjs --update   write new baselines into test/e2e/__screenshots__
// The repository is mounted read-only and copied without node_modules; only the screenshots (and the report on
// failure) are written back.
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const app = resolve(here, '../..')
const repo = resolve(app, '../../../..')
const { version } = createRequire(import.meta.url)('@playwright/test/package.json')
const update = process.argv.includes('--update')

const ui = 'src/Platform/ui.admin/src'
const sdk = 'src/Platform/Storyteller/sdk.typescript/src'
const script = [
  'set -e',
  `tar -C /repo --exclude=node_modules --exclude=test-results --exclude=obj -cf - ${sdk} ${ui} | tar -C /work -xf -`,
  `cd /work/${sdk} && npx -y npm@11 ci --no-audit --no-fund --loglevel=error`,
  `cd /work/${ui} && npx -y npm@11 ci --no-audit --no-fund --loglevel=error`,
  `npx playwright test --project=visual ${update ? '--update-snapshots' : ''} || status=$?`,
  'cp -r test/e2e/__screenshots__/. /out/screenshots/',
  'if [ -n "$status" ]; then mkdir -p /out/results && cp -r test-results/. /out/results/; fi',
  'exit ${status:-0}',
].join('\n')

execFileSync(
  'docker',
  [
    'run',
    '--rm',
    '--ipc=host',
    '-e',
    'CI=1',
    '-v',
    `${repo}:/repo:ro`,
    '-v',
    `${resolve(app, 'test/e2e/__screenshots__')}:/out/screenshots`,
    '-v',
    `${resolve(app, 'test-results')}:/out/results`,
    '-w',
    '/work',
    `mcr.microsoft.com/playwright:v${version}-noble`,
    'bash',
    '-c',
    script,
  ],
  { stdio: 'inherit' },
)
