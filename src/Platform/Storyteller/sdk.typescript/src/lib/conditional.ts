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

/** The effective configuration, or notModified when ifNoneMatch still matches. */
export function getConfigurationConditional(
  organization: string,
  project: string,
  view: string,
  key: string,
  options?: ConditionalReadOptions,
): Promise<ConditionalResult<Configuration>> {
  return readConditional<Configuration>(getGetConfigurationUrl(organization, project, view, key), options)
}

/** The resolved configuration, or notModified when ifNoneMatch still matches. */
export function getResolvedConfigurationConditional(
  organization: string,
  project: string,
  view: string,
  key: string,
  options?: ConditionalReadOptions,
): Promise<ConditionalResult<Configuration>> {
  return readConditional<Configuration>(getGetResolvedConfigurationUrl(organization, project, view, key), options)
}

async function readConditional<T>(url: string, options: ConditionalReadOptions = {}): Promise<ConditionalResult<T>> {
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

  const body = await readBody(response)

  if (!response.ok) {
    throw StorytellerError.fromResponse(response.status, response.statusText, body)
  }

  return { status: 200, notModified: false, value: body as T, ...read }
}
