// Prepares the SDK for local use (the Aspire host's ui-admin-sdk resource, or `npm run prepare:local`):
// `npm ci` when package-lock.json changed, `npm run build` when a build input changed. Each step stores a SHA-256
// of its inputs in a stamp file and is skipped while the stamp matches; --force (or a deleted stamp) runs it again.
// Plain Node, no dependencies: it runs on a clean clone before anything is installed.
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { existsSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const force = process.argv.includes('--force')
const entries = ['index', 'vue-query', 'zod', 'msw', 'hyok']
const inputs = [
  ...entries.map((entry) => `${entry}.ts`),
  'lib',
  'generated',
  'tsup.config.ts',
  'tsconfig.json',
  'package.json',
  'package-lock.json',
]
const installStamp = join(root, 'node_modules', '.prepare-local-install')
const buildStamp = join(root, 'dist', '.prepare-local-build')

checkNode('22.18.0')

const lockHash = hashPaths(['package-lock.json'])
if (force || read(installStamp) !== lockHash) {
  run('npm', ['ci', '--no-audit', '--no-fund'])
  writeFileSync(installStamp, lockHash)
} else {
  console.log('SDK packages up to date')
}

const buildHash = hashPaths(inputs)
const distComplete = entries.every((entry) => existsSync(join(root, 'dist', `${entry}.js`)))
if (force || !distComplete || read(buildStamp) !== buildHash) {
  run('npm', ['run', 'build'])
  writeFileSync(buildStamp, buildHash)
} else {
  console.log('SDK build up to date')
}

function checkNode(minimum) {
  if (versionNumber(process.versions.node) < versionNumber(minimum)) {
    console.error(`Node ${minimum} or newer is required, found ${process.versions.node}.`)
    process.exit(1)
  }
}

function versionNumber(version) {
  const [major, minor, patch] = version.split('.').map(Number)
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

// SHA-256 over the sorted relative paths and contents of the given files and folders (recursively).
function hashPaths(paths) {
  const files = []
  const collect = (path) => {
    if (!existsSync(path)) return
    if (statSync(path).isDirectory()) {
      for (const name of readdirSync(path)) collect(join(path, name))
    } else {
      files.push(path)
    }
  }
  for (const path of paths) collect(join(root, path))
  const hash = createHash('sha256')
  for (const file of files.map((path) => relative(root, path).replaceAll('\\', '/')).toSorted()) {
    hash.update(file).update('\0').update(readFileSync(join(root, file))).update('\0')
  }
  return hash.digest('hex')
}
