// Conditional reads of configurations: send the ETag of the last read in If-None-Match and get 304 without a body
// while the configuration stays the same. The generated getConfiguration throws on a 304 and drops the headers.
import { getGetConfigurationUrl, getGetResolvedConfigurationUrl } from '../generated/client/configurations/configurations'
import type { Configuration } from '../generated/model'
import { StorytellerError } from './errors'
import { readBody, storytellerRequest } from './fetcher'

export interface ConditionalResult<T> {
  status: 200 | 304
  notModified: boolean
  /** The weak ETag as sent, to pass back in ifNoneMatch; undefined when the header is not readable (cross-origin without exposure). */
  etag?: string
  /** The body of a 200; undefined on a 304. */
  value?: T
  /** no-store on a resolved read by a caller that may read secrets, otherwise private, no-cache. */
  cacheControl?: string
  /** mtls-required on every resolved read by a machine with Secrets.Read but no mTLS when the project requires it for secrets. */
  secretsWithheld?: string
}

export type ConditionalReadOptions = RequestInit & {
  /** The ETag of the last read, sent as If-None-Match; it overrides an If-None-Match in headers, and '' removes that one. */
  ifNoneMatch?: string
}

/**
 * The formats of ?format=: the Content alone, without the envelope. flat is a JSON object of IConfiguration keys and
 * string values, properties Spring Boot properties, env a .env file, yaml YAML. Each format has its own ETag.
 */
export type ConfigurationFormat = 'flat' | 'properties' | 'env' | 'yaml'

/** The effective configuration, or notModified when ifNoneMatch still matches. */
export function getConfigurationConditional(
  organization: string,
  project: string,
  view: string,
  key: string,
  options?: ConditionalReadOptions,
): Promise<ConditionalResult<Configuration>> {
  return readConditional(getGetConfigurationUrl(organization, project, view, key), readConfiguration, options)
}

/** The resolved configuration, or notModified when ifNoneMatch still matches. */
export function getResolvedConfigurationConditional(
  organization: string,
  project: string,
  view: string,
  key: string,
  options?: ConditionalReadOptions,
): Promise<ConditionalResult<Configuration>> {
  return readConditional(getGetResolvedConfigurationUrl(organization, project, view, key), readConfiguration, options)
}

/** The effective configuration in a format, as text (flat too), or notModified when ifNoneMatch still matches. */
export function getConfigurationFormattedConditional(
  organization: string,
  project: string,
  view: string,
  key: string,
  format: ConfigurationFormat,
  options?: ConditionalReadOptions,
): Promise<ConditionalResult<string>> {
  const url = `${getGetConfigurationUrl(organization, project, view, key)}?format=${encodeURIComponent(format)}`
  return readConditional(url, readText, options)
}

/** The resolved configuration in a format, as text (flat too), or notModified when ifNoneMatch still matches. */
export function getResolvedConfigurationFormattedConditional(
  organization: string,
  project: string,
  view: string,
  key: string,
  format: ConfigurationFormat,
  options?: ConditionalReadOptions,
): Promise<ConditionalResult<string>> {
  const url = `${getGetResolvedConfigurationUrl(organization, project, view, key)}?format=${encodeURIComponent(format)}`
  return readConditional(url, readText, options)
}

async function readConditional<T>(
  url: string,
  readValue: (response: Response) => Promise<T>,
  options: ConditionalReadOptions = {},
): Promise<ConditionalResult<T>> {
  const { ifNoneMatch, ...init } = options
  const headers = new Headers(init.headers)

  // An empty tag clears the caller's header as well, so no stale tag goes out.
  if (ifNoneMatch) {
    headers.set('If-None-Match', ifNoneMatch)
  } else if (ifNoneMatch !== undefined) {
    headers.delete('If-None-Match')
  }

  const response = await storytellerRequest(url, { ...init, method: 'GET', headers })
  // The API sends these on a 200 and a 304 alike.
  const read = {
    etag: response.headers.get('etag') ?? undefined,
    cacheControl: response.headers.get('cache-control') ?? undefined,
    secretsWithheld: response.headers.get('x-2s-secrets-withheld') ?? undefined,
  }

  // Before the error mapping: for the fetcher a 304 is not ok.
  if (response.status === 304) {
    return { status: 304, notModified: true, ...read }
  }

  // Errors are JSON whatever was asked for, so a 422 of a format keeps its errorCode.
  if (!response.ok) {
    throw StorytellerError.fromResponse(response.status, response.statusText, await readBody(response))
  }

  return { status: 200, notModified: false, value: await readValue(response), ...read }
}

async function readConfiguration(response: Response): Promise<Configuration> {
  const body = await readBody(response)

  // A configuration is a JSON object; anything else is not the API's answer.
  if (body === null || typeof body !== 'object' || Array.isArray(body) || body instanceof Blob) {
    throw new StorytellerError({
      status: response.status,
      message: 'The Storyteller API answered without a configuration object.',
      body,
    })
  }

  return body as Configuration
}

// A formatted body is text whatever its media type, the JSON of flat included; an empty one (no values) is ''.
function readText(response: Response): Promise<string> {
  return response.text()
}
