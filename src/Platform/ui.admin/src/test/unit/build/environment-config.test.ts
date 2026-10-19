// @vitest-environment node
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { beforeAll, describe, expect, it } from 'vitest'
import { applyReplacements, environmentConfig, type EnvironmentVariables } from '../../../build/environment-config.ts'

// The deploy step writes app-config.json and the CSP origins of each environment from the variables of the GitHub
// environment (build/write-environment-config.ts); GitHub passes an empty string for a variable that is not set.
const root = fileURLToPath(new URL('../../..', import.meta.url))

const deployment: EnvironmentVariables = {
  API_BASE_URL: 'https://api.2s.42for.net/api',
  AUTH_API_HOSTNAME: 'auth.42for.net',
  ENVIRONMENT_NAME: 'production',
  SENTRY_DSN: '',
  POSTHOG_PROJECT_TOKEN: '',
  POSTHOG_HOST: '',
  RELEASE: '',
}
const dsn = 'https://0123456789abcdef@o1.ingest.de.sentry.io/2'
const token = 'phc_test'
const commit = '65c570ed0123456789abcdef0123456789abcdef'

/** The app-config.json the step writes. */
const appConfigJson = (variables: EnvironmentVariables) =>
  JSON.stringify(environmentConfig(variables).appConfig, null, 2)

/** What the inline script of the step wrote before the telemetry variables. */
const previousAppConfigJson = ({ API_BASE_URL, AUTH_API_HOSTNAME, ENVIRONMENT_NAME }: EnvironmentVariables) =>
  JSON.stringify(
    { apiBaseUrl: API_BASE_URL, authApiHostname: AUTH_API_HOSTNAME, environmentName: ENVIRONMENT_NAME || undefined },
    null,
    2,
  )

describe('environment configuration of a deployment', () => {
  it('writes the API address, the AuthKit domain and the environment name as before', () => {
    const { replacements } = environmentConfig(deployment)

    expect(appConfigJson(deployment)).toBe(previousAppConfigJson(deployment))
    expect(appConfigJson(deployment)).toBe(
      '{\n  "apiBaseUrl": "https://api.2s.42for.net/api",\n  "authApiHostname": "auth.42for.net",\n  "environmentName": "production"\n}',
    )
    expect(replacements).toMatchObject({
      __API_ORIGIN__: 'https://api.2s.42for.net',
      __AUTH_HOSTNAME__: 'auth.42for.net',
    })
  })

  it('leaves an empty environment name out, as before', () => {
    const variables = { ...deployment, ENVIRONMENT_NAME: '' }

    expect(appConfigJson(variables)).toBe(previousAppConfigJson(variables))
    expect(JSON.parse(appConfigJson(variables))).not.toHaveProperty('environmentName')
  })

  it.each([
    ['the API address', { API_BASE_URL: '' }],
    ['the AuthKit domain', { AUTH_API_HOSTNAME: '' }],
    ['both', { API_BASE_URL: undefined, AUTH_API_HOSTNAME: undefined }],
  ])('fails without %s', (_name, missing) => {
    expect(() => environmentConfig({ ...deployment, ...missing })).toThrow(
      'UI_ADMIN_API_BASE_URL and UI_ADMIN_AUTH_API_HOSTNAME are required.',
    )
  })

  it('writes the Sentry DSN, the PostHog token and host and the release', () => {
    const variables = {
      ...deployment,
      SENTRY_DSN: dsn,
      POSTHOG_PROJECT_TOKEN: token,
      POSTHOG_HOST: 'https://eu.i.posthog.com',
      RELEASE: commit,
    }

    expect(JSON.parse(appConfigJson(variables))).toEqual({
      apiBaseUrl: 'https://api.2s.42for.net/api',
      authApiHostname: 'auth.42for.net',
      environmentName: 'production',
      sentryDsn: dsn,
      posthogProjectToken: token,
      posthogHost: 'https://eu.i.posthog.com',
      release: commit,
    })
  })

  it('leaves empty telemetry variables out, and their origins', () => {
    const { replacements } = environmentConfig(deployment)

    expect(Object.keys(JSON.parse(appConfigJson(deployment)))).toEqual([
      'apiBaseUrl',
      'authApiHostname',
      'environmentName',
    ])
    expect(replacements).toMatchObject({ __SENTRY_ORIGIN__: '', __POSTHOG_ORIGIN__: '' })
  })

  it('allows the Sentry ingest origin, without the key of the DSN', () => {
    const { replacements } = environmentConfig({ ...deployment, SENTRY_DSN: dsn })

    expect(replacements).toMatchObject({ __SENTRY_ORIGIN__: 'https://o1.ingest.de.sentry.io', __POSTHOG_ORIGIN__: '' })
  })

  it('allows the EU PostHog host when only the token is set', () => {
    const { appConfig, replacements } = environmentConfig({ ...deployment, POSTHOG_PROJECT_TOKEN: token })

    expect(replacements).toMatchObject({ __SENTRY_ORIGIN__: '', __POSTHOG_ORIGIN__: 'https://eu.i.posthog.com' })
    // The app falls back to the same host.
    expect(appConfig.posthogHost).toBeUndefined()
  })

  it('allows the origin of the PostHog host', () => {
    const { replacements } = environmentConfig({
      ...deployment,
      POSTHOG_PROJECT_TOKEN: token,
      POSTHOG_HOST: 'https://ph.example.com/ingest/',
    })

    expect(replacements).toMatchObject({ __POSTHOG_ORIGIN__: 'https://ph.example.com' })
  })

  it('allows no PostHog origin for a host without a token', () => {
    const { replacements } = environmentConfig({ ...deployment, POSTHOG_HOST: 'https://eu.i.posthog.com' })

    expect(replacements).toMatchObject({ __POSTHOG_ORIGIN__: '' })
  })

  it.each(['http://o1.ingest.de.sentry.io/2', 'o1.ingest.de.sentry.io/2', '/api/2', 'not a URL'])(
    'fails for the DSN or the PostHog host %s, which is not an absolute https URL',
    (value) => {
      expect(() => environmentConfig({ ...deployment, SENTRY_DSN: value })).toThrow(
        'UI_ADMIN_SENTRY_DSN must be an absolute https URL.',
      )
      expect(() => environmentConfig({ ...deployment, POSTHOG_PROJECT_TOKEN: token, POSTHOG_HOST: value })).toThrow(
        'UI_ADMIN_POSTHOG_HOST must be an absolute https URL.',
      )
    },
  )

  describe('public/staticwebapp.config.json', () => {
    let text: string

    beforeAll(async () => {
      text = await readFile(path.join(root, 'public/staticwebapp.config.json'), 'utf8')
    })

    const csp = (configured: string) =>
      (JSON.parse(configured) as { globalHeaders: Record<string, string> }).globalHeaders['Content-Security-Policy']

    it('has a value for every placeholder', () => {
      const placeholders = new Set(text.match(/__[A-Z]+(?:_[A-Z]+)*__/g))

      expect([...placeholders].toSorted()).toEqual(Object.keys(environmentConfig(deployment).replacements).toSorted())
    })

    it.each([
      ['without telemetry', deployment],
      ['with Sentry and PostHog', { ...deployment, SENTRY_DSN: dsn, POSTHOG_PROJECT_TOKEN: token }],
    ])('leaves no placeholder %s', (_name, variables) => {
      const configured = applyReplacements(text, environmentConfig(variables).replacements)

      expect(configured).not.toContain('__')
      expect(csp(configured)).toContain('https://api.2s.42for.net')
      expect(csp(configured)).toContain('https://auth.42for.net')
    })

    it('lets the app connect to Sentry and PostHog when they are configured', () => {
      const configured = applyReplacements(
        text,
        environmentConfig({ ...deployment, SENTRY_DSN: dsn, POSTHOG_PROJECT_TOKEN: token }).replacements,
      )

      expect(csp(configured)).toContain(
        "connect-src 'self' https://api.2s.42for.net https://o1.ingest.de.sentry.io https://eu.i.posthog.com https://api.workos.com https://auth.42for.net;",
      )
      expect(csp(configured)).not.toContain('0123456789abcdef')
    })
  })
})
