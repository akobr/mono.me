// @vitest-environment node
import { execFile } from 'node:child_process'
import { access, copyFile, mkdir, mkdtemp, readFile, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'

// The deploy job runs build/write-environment-config.ts with Node's type stripping, which neither Vitest nor vue-tsc
// uses: syntax Node cannot strip (an enum, a namespace, a parameter property) or an import without its .ts extension
// would fail only on main, after the build. This runs the script as the step does, on a copy of the CSP file.
const root = fileURLToPath(new URL('../../..', import.meta.url))
const script = path.join(root, 'build/write-environment-config.ts')
const run = promisify(execFile)

const dsnKey = '0123456789abcdef'
const commit = '65c570ed0123456789abcdef0123456789abcdef'

// GitHub passes an empty string for a variable that is not set.
const variables = {
  API_BASE_URL: 'https://api.2s.42for.net/api',
  AUTH_API_HOSTNAME: 'auth.42for.net',
  ENVIRONMENT_NAME: 'production',
  SENTRY_DSN: `https://${dsnKey}@o1.ingest.de.sentry.io/2`,
  POSTHOG_PROJECT_TOKEN: 'phc_test',
  POSTHOG_HOST: '',
  RELEASE: commit,
}

describe('the environment configuration step of the deployment', () => {
  let cwd: string

  /** Runs the script with Node in a folder with dist/staticwebapp.config.json, as the deploy step does. */
  const writeEnvironmentConfig = (overrides: Partial<typeof variables> = {}) =>
    run(process.execPath, [script], { cwd, env: { ...process.env, ...variables, ...overrides } })

  beforeEach(async () => {
    cwd = await mkdtemp(path.join(tmpdir(), '2s-admin-environment-'))
    await mkdir(path.join(cwd, 'dist'))
    await copyFile(path.join(root, 'public/staticwebapp.config.json'), path.join(cwd, 'dist/staticwebapp.config.json'))
  })

  afterEach(async () => {
    await rm(cwd, { recursive: true, force: true })
  })

  it('runs with Node and writes app-config.json and the CSP origins', async () => {
    await writeEnvironmentConfig()

    expect(JSON.parse(await readFile(path.join(cwd, 'dist/app-config.json'), 'utf8'))).toEqual({
      apiBaseUrl: 'https://api.2s.42for.net/api',
      authApiHostname: 'auth.42for.net',
      environmentName: 'production',
      sentryDsn: variables.SENTRY_DSN,
      posthogProjectToken: 'phc_test',
      release: commit,
    })

    const configured = await readFile(path.join(cwd, 'dist/staticwebapp.config.json'), 'utf8')
    const csp = (JSON.parse(configured) as { globalHeaders: Record<string, string> }).globalHeaders[
      'Content-Security-Policy'
    ]
    expect(configured).not.toContain('__')
    expect(csp).toContain(
      "connect-src 'self' https://api.2s.42for.net https://o1.ingest.de.sentry.io https://eu.i.posthog.com https://api.workos.com https://auth.42for.net;",
    )
    expect(csp).not.toContain(dsnKey)
  })

  it('fails for a DSN that is not an https URL, without writing anything or printing the DSN', async () => {
    const failure = writeEnvironmentConfig({ SENTRY_DSN: `http://${dsnKey}@o1.ingest.de.sentry.io/2` })

    // A non-zero exit rejects; the code itself differs per platform.
    await expect(failure).rejects.toMatchObject({
      stderr: expect.stringContaining('UI_ADMIN_SENTRY_DSN must be an absolute https URL.'),
    })
    await expect(failure).rejects.toMatchObject({ stderr: expect.not.stringContaining(dsnKey) })
    await expect(access(path.join(cwd, 'dist/app-config.json'))).rejects.toThrow()
  })
})
