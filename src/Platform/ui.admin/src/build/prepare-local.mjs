// Installs the admin UI's packages for local use (the Aspire host's ui-admin-installer resource, or
// `npm run prepare:local`). The SDK must be built first (its own `npm run prepare:local`, the ui-admin-sdk resource).
// The install key is the SHA-256 of package-lock.json plus the SDK's build stamp:
// - no stamp, another lockfile, or --force: `npm ci`;
// - only the SDK changed: the SDK is a packed copy (install-links=true), so it is reinstalled alone
//   (remove node_modules/@42for.net, `npm install`), and build/sdk-freshness.ts re-optimizes it;
// - otherwise nothing.
// Plain Node, no dependencies: it runs on a clean clone before anything is installed.
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const force = process.argv.includes('--force')
const sdkBuildStamp = resolve(root, '../../Storyteller/sdk.typescript/src/dist/.prepare-local-build')
const installStamp = join(root, 'node_modules', '.prepare-local-install')

checkNode('22.18.0')

// A SDK built by hand (npm run build) has no stamp: its dist stands in, so the SDK copy is reinstalled once.
const sdkDist = resolve(root, '../../Storyteller/sdk.typescript/src/dist')
if (!existsSync(join(sdkDist, 'index.js'))) {
  console.error('The SDK is not built. Run `npm run prepare:local` in src/Platform/Storyteller/sdk.typescript/src.')
  process.exit(1)
}
const sdkStamp = read(sdkBuildStamp) || 'unstamped'

const lockHash = createHash('sha256').update(readFileSync(join(root, 'package-lock.json'))).digest('hex')
const [installedLock, installedSdk] = read(installStamp).split(/\s+/)

if (force || installedLock !== lockHash) {
  run('npm', ['ci', '--no-audit', '--no-fund'])
} else if (installedSdk !== sdkStamp) {
  console.log('SDK changed, reinstalling it')
  rmSync(join(root, 'node_modules', '@42for.net'), { recursive: true, force: true })
  run('npm', ['install', '--no-save', '--no-audit', '--no-fund'])
} else {
  console.log('UI packages up to date')
  process.exit(0)
}
writeFileSync(installStamp, `${lockHash} ${sdkStamp}`)

function checkNode(minimum) {
  if (versionNumber(process.versions.node) < versionNumber(minimum)) {
    console.error(`Node ${minimum} or newer is required, found ${process.versions.node}.`)
    process.exit(1)
  }
}

function versionNumber(version) {
  // parseInt drops a prerelease suffix (24.0.0-pre → 24, 0, 0)
  const [major, minor, patch] = version.split('.').map((part) => Number.parseInt(part, 10) || 0)
  return (major * 1000 + minor) * 1000 + patch
}

function run(command, args) {
  console.log(`> ${command} ${args.join(' ')}`)
  const result = spawnSync(command, args, { cwd: root, stdio: 'inherit', shell: process.platform === 'win32' })
  if (result.error) throw result.error
  if (result.status !== 0) process.exit(result.status ?? 1)
}

function read(file) {
  return existsSync(file) ? readFileSync(file, 'utf8').trim() : ''
}
