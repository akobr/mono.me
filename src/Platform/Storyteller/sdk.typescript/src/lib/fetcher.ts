import { StorytellerError } from './errors'

export interface StorytellerConfig {
  /** API root including the "/api" prefix, for example https://host/api. */
  baseUrl: string
  /** Bearer token for user calls; return undefined to send no Authorization header. */
  getAccessToken?: () => Promise<string | undefined> | string | undefined
  /** Called once a 401 survived one token refresh, for example to start a new sign-in. */
  onUnauthorized?: () => void
  /** Transport override for tests and special hosts. Defaults to the global fetch. */
  fetch?: typeof globalThis.fetch
  /** Extra headers on every request, for example X-Client. */
  headers?: Record<string, string>
}

/** Orval uses this as the error type of generated queries and mutations: every failure is a StorytellerError. */
// eslint-disable-next-line @typescript-eslint/no-unused-vars
export type ErrorType<TError> = StorytellerError

/** Orval uses this as the body type of generated mutations. */
export type BodyType<TBody> = TBody

let config: StorytellerConfig | undefined

/** Configures the module-level fetcher used by every generated function and composable. */
export function configureStoryteller(next: StorytellerConfig): void {
  config = { ...next, baseUrl: next.baseUrl.replace(/\/+$/, '') }
}

/** The current configuration; throws when configureStoryteller was not called. */
export function getStorytellerConfig(): StorytellerConfig {
  if (!config) {
    throw new Error('The Storyteller SDK is not configured. Call configureStoryteller({ baseUrl }) first.')
  }

  return config
}

/**
 * The Orval mutator: every generated request goes through this function.
 * - prefixes the base URL to the generated "/v1/..." path (absolute URLs are kept),
 * - adds the bearer token, retries once after a 401 with a fresh token,
 * - parses JSON bodies by content type and returns any other content type as text,
 * - throws StorytellerError for non-2xx responses and network failures.
 */
export async function storytellerFetch<T>(url: string, init: RequestInit = {}): Promise<T> {
  const current = getStorytellerConfig()
  const target = /^https?:\/\//i.test(url) ? url : `${current.baseUrl}${url.startsWith('/') ? '' : '/'}${url}`

  let response = await send(current, target, init)

  if (response.status === 401 && current.getAccessToken) {
    response = await send(current, target, init)

    if (response.status === 401) {
      current.onUnauthorized?.()
    }
  }

  const body = await readBody(response)

  if (!response.ok) {
    throw StorytellerError.fromResponse(response.status, response.statusText, body)
  }

  return body as T
}

async function send(current: StorytellerConfig, target: string, init: RequestInit): Promise<Response> {
  const headers = new Headers(init.headers)

  for (const [name, value] of Object.entries(current.headers ?? {})) {
    if (!headers.has(name)) {
      headers.set(name, value)
    }
  }

  const token = await current.getAccessToken?.()

  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  try {
    return await (current.fetch ?? globalThis.fetch)(target, { ...init, headers })
  } catch (error) {
    // The token never leaves this function: the error carries only the URL path and the cause.
    const aborted = error instanceof DOMException && error.name === 'AbortError'
    throw new StorytellerError({
      status: 0,
      message: aborted ? 'The request was aborted.' : `The Storyteller API is not reachable (${new URL(target).pathname}).`,
      cause: error,
    })
  }
}

async function readBody(response: Response): Promise<unknown> {
  if (response.status === 204 || response.status === 205 || response.status === 304) {
    return undefined
  }

  const text = await response.text()

  if (text.length === 0) {
    return undefined
  }

  const contentType = response.headers.get('content-type') ?? ''
  const isJson = /^application\/(?:[\w.+-]+\+)?json\b/i.test(contentType)

  if (!isJson) {
    // PEM certificates (application/x-pem-file), unified diffs (text/plain) and any other text.
    return text
  }

  try {
    return JSON.parse(text) as unknown
  } catch {
    return text
  }
}
