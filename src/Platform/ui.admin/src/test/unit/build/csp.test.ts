// @vitest-environment node
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { beforeAll, describe, expect, it } from 'vitest'

// Only Azure Static Web Apps serves the CSP of public/staticwebapp.config.json; Playwright runs vite preview without
// it, so a wrong directive would show only after a deployment. The deploy step fills the placeholders.
const root = fileURLToPath(new URL('../../..', import.meta.url))

/** The sources of each directive of a policy. */
const directives = (policy: string) =>
  new Map(
    policy
      .split(';')
      .map((directive) => directive.trim().split(/\s+/))
      .map(([name, ...sources]): [string, string[]] => [name!, sources]),
  )

describe('Content-Security-Policy of the deployment', () => {
  let csp: Map<string, string[]>

  beforeAll(async () => {
    const config = JSON.parse(await readFile(path.join(root, 'public/staticwebapp.config.json'), 'utf8')) as {
      globalHeaders: Record<string, string>
    }
    csp = directives(config.globalHeaders['Content-Security-Policy']!)
  })

  it('lets the app connect to the API, Sentry, PostHog and AuthKit', () => {
    expect(csp.get('connect-src')).toEqual([
      "'self'",
      '__API_ORIGIN__',
      '__SENTRY_ORIGIN__',
      '__POSTHOG_ORIGIN__',
      'https://api.workos.com',
      'https://__AUTH_HOSTNAME__',
    ])
  })

  it('runs scripts and workers of the app only', () => {
    // Both SDKs are bundled (posthog-js/no-external), so nothing is injected and no worker is created.
    expect(csp.get('script-src')).toEqual(["'self'"])
    expect(csp.get('worker-src')).toEqual(["'self'"])
  })
})
