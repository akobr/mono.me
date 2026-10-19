// The configuration of one deployment: one build serves every environment, so the deploy step writes
// dist/app-config.json and fills the origins of the CSP in dist/staticwebapp.config.json from the variables of the
// GitHub environment (write-environment-config.ts). Kept free of I/O so the unit tests run it as it is.

/** The variables of the deploy step; GitHub passes an empty string for a variable that is not set. */
export interface EnvironmentVariables {
  API_BASE_URL?: string
  AUTH_API_HOSTNAME?: string
  ENVIRONMENT_NAME?: string
  SENTRY_DSN?: string
  POSTHOG_PROJECT_TOKEN?: string
  POSTHOG_HOST?: string
  /** The commit SHA. */
  RELEASE?: string
}

/** The content of app-config.json, read by app/boot/app-config.ts. */
export interface AppConfigFile {
  apiBaseUrl: string
  authApiHostname: string
  environmentName?: string
  sentryDsn?: string
  posthogProjectToken?: string
  posthogHost?: string
  release?: string
}

export type Placeholder = '__API_ORIGIN__' | '__AUTH_HOSTNAME__' | '__SENTRY_ORIGIN__' | '__POSTHOG_ORIGIN__'

export interface EnvironmentConfig {
  appConfig: AppConfigFile
  /** The value of each placeholder of staticwebapp.config.json. */
  replacements: Record<Placeholder, string>
}

/** The host the app uses when posthogHost is missing (app/boot/app-config.ts). */
export const DEFAULT_POSTHOG_HOST = 'https://eu.i.posthog.com'

const nonEmpty = (value: string | undefined) => value || undefined

/** The origin of an absolute https URL; the variable is named in the error, its value is not. */
function httpsOrigin(value: string, variable: string): string {
  let url: URL | undefined
  try {
    url = new URL(value)
  } catch {
    // not a URL
  }
  if (url?.protocol !== 'https:') {
    throw new Error(`${variable} must be an absolute https URL.`)
  }
  return url.origin
}

/**
 * The app-config.json of a deployment and the values of the CSP placeholders. Sentry and PostHog are optional: an
 * empty variable is left out, and so is its origin. The origin of the DSN has no key (https://<key>@<host>/<id>).
 */
export function environmentConfig(variables: EnvironmentVariables): EnvironmentConfig {
  const apiBaseUrl = nonEmpty(variables.API_BASE_URL)
  const authApiHostname = nonEmpty(variables.AUTH_API_HOSTNAME)
  if (!apiBaseUrl || !authApiHostname) {
    throw new Error('UI_ADMIN_API_BASE_URL and UI_ADMIN_AUTH_API_HOSTNAME are required.')
  }

  const sentryDsn = nonEmpty(variables.SENTRY_DSN)
  const posthogProjectToken = nonEmpty(variables.POSTHOG_PROJECT_TOKEN)
  const posthogHost = nonEmpty(variables.POSTHOG_HOST)
  const sentryOrigin = sentryDsn ? httpsOrigin(sentryDsn, 'UI_ADMIN_SENTRY_DSN') : ''
  const posthogOrigin = posthogHost ? httpsOrigin(posthogHost, 'UI_ADMIN_POSTHOG_HOST') : DEFAULT_POSTHOG_HOST

  return {
    // JSON.stringify leaves out the undefined fields.
    appConfig: {
      apiBaseUrl,
      authApiHostname,
      environmentName: nonEmpty(variables.ENVIRONMENT_NAME),
      sentryDsn,
      posthogProjectToken,
      posthogHost,
      release: nonEmpty(variables.RELEASE),
    },
    replacements: {
      __API_ORIGIN__: new URL(apiBaseUrl).origin,
      __AUTH_HOSTNAME__: authApiHostname,
      __SENTRY_ORIGIN__: sentryOrigin,
      // PostHog does not start without its token, whatever the host.
      __POSTHOG_ORIGIN__: posthogProjectToken ? posthogOrigin : '',
    },
  }
}

/** Fills the placeholders; an empty origin leaves two spaces in connect-src, which the CSP grammar allows. */
export function applyReplacements(text: string, replacements: Record<Placeholder, string>): string {
  return Object.entries(replacements).reduce(
    (result, [placeholder, value]) => result.replaceAll(placeholder, value),
    text,
  )
}
