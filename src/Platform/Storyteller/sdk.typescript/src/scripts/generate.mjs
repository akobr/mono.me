// Regenerates src/generated from an OpenAPI document and sets the package version from info.version.
// Usage: npm run generate                       (newest ../open.api.v*.json)
//        SPEC=../open.api.v0.9.0.json npm run generate
import { execSync } from 'node:child_process'
import { readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const specDirectory = resolve(root, '..')

const versionOf = (name) => name.match(/^open\.api\.v([\d.]+)\.json$/)?.[1]?.split('.').map(Number)

const newestSpec = () => {
  const candidates = readdirSync(specDirectory).filter((name) => versionOf(name))

  candidates.sort((left, right) => {
    const a = versionOf(left)
    const b = versionOf(right)
    for (let index = 0; index < Math.max(a.length, b.length); index++) {
      const difference = (a[index] ?? 0) - (b[index] ?? 0)
      if (difference !== 0) return difference
    }
    return 0
  })

  if (candidates.length === 0) {
    throw new Error(`No open.api.v*.json in ${specDirectory}.`)
  }

  return `../${candidates.at(-1)}`
}

const spec = process.env.SPEC ?? newestSpec()
console.log(`Generating the SDK from ${spec}`)

rmSync(join(root, 'generated'), { recursive: true, force: true })
execSync('npx orval --config orval.config.ts', { cwd: root, stdio: 'inherit', env: { ...process.env, SPEC: spec } })

// "0.8.91.29486" → "0.8.91": the npm version follows the API's major.minor.patch.
const document = JSON.parse(readFileSync(resolve(root, spec), 'utf8'))
const version = String(document.info?.version ?? '').split('.').slice(0, 3).join('.')
const packagePath = join(root, 'package.json')
const packageJson = JSON.parse(readFileSync(packagePath, 'utf8'))

if (version && packageJson.version !== version) {
  packageJson.version = version
  writeFileSync(packagePath, `${JSON.stringify(packageJson, null, 2)}\n`)
  console.log(`Package version set to ${version}`)
}
