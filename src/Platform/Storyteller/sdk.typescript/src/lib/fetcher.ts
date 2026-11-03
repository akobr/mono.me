import { StorytellerError } from './errors'

export interface StorytellerConfig {
  /** API root including the "/api" prefix, for example https://host/api. */
  baseUrl: string
  /**
   * Bearer token for user calls; return undefined to send no Authorization header.
   * forceRefresh is true for the one retry after a 401, so the provider can skip its cached token.
   */
  getAccessToken?: (options: { forceRefresh: boolean }) => Promise<string | undefined> | string | undefined
  /** Called once a 401 survived one token refresh, for example to start a new sign-in. */
  onUnauthorized?: () => void
  /** Transport override for tests and special hosts. Defaults to the global fetch. */
  fetch?: typeof globalThis.fetch
  /** Extra headers on every request, for example X-Client. */
  headers?: Record<string, string>
  /**
   * The longest a request may take in milliseconds, from the call until its body is read, across the token lookup and
   * the retry after a 401; a browser counts the time it queues the request as well. A function picks the limit per
   * request (method in upper case, url as fetch gets it: the base URL and the path), for example by method. Undefined
   * or 0: no limit. A request that runs out of time fails with StorytellerError status 0 and timedOut.
   */
  timeoutMs?: number | ((request: { method: string; url: string }) => number | undefined)
}

/** The options of storytellerFetch and of every generated function: RequestInit and a timeoutMs over the configured one (0: no limit). */
export type StorytellerRequestInit = RequestInit & {
  /** The limit of this request in milliseconds, over StorytellerConfig.timeoutMs; 0 disables it. Never reaches fetch. */
  timeoutMs?: number
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
 * - ends the request after timeoutMs (the per-call option, else the configured one),
 * - parses JSON bodies by content type, returns zip and octet-stream bodies as a Blob and any other content type as text,
 * - throws StorytellerError for non-2xx responses, network failures, timeouts and aborts.
 * Orval reads the number of its parameters: a third one would change the generated code.
 */
export async function storytellerFetch<T>(url: string, init: StorytellerRequestInit = {}): Promise<T> {
  return await runRequest(url, init, async (response) => {
    const body = await readBody(response)

    if (!response.ok) {
      throw StorytellerError.fromResponse(response.status, response.statusText, body)
    }

    return body as T
  })
}

/**
 * storytellerFetch without its body handling, for helpers that need the status and the headers (conditional reads):
 * read gets the response of any status and runs within the deadline. Network failures, timeouts and aborts, of the
 * body read too, become StorytellerError status 0; a StorytellerError of read passes as it is. Other failures, such as
 * a missing configuration or a failing getAccessToken, propagate as they are. Not exported from the package.
 */
export async function runRequest<T>(url: string, init: StorytellerRequestInit, read: (response: Response) => Promise<T>): Promise<T> {
  const current = getStorytellerConfig()
  const { timeoutMs, ...request } = init
  const target = /^https?:\/\//i.test(url) ? url : `${current.baseUrl}${url.startsWith('/') ? '' : '/'}${url}`
  const limitMs = limitOf(current, timeoutMs, request.method, target)
  const caller = request.signal ?? undefined

  if (limitMs === undefined) {
    // Without a deadline the caller's signal reaches fetch as it is.
    return await perform(current, target, request, read, { target, caller, signal: caller, timedOut: false })
  }

  // AbortSignal.any and AbortSignal.timeout are not in every runtime the SDK supports, so the deadline is a timer.
  const deadline = new AbortController()
  const attempt: Attempt = { target, caller, signal: deadline.signal, limitMs, timedOut: false }
  const forward = () => deadline.abort(caller?.reason)
  const timer = setTimeout(() => {
    // The first abort decides: a caller that aborted first stays aborted.
    if (!deadline.signal.aborted) {
      attempt.timedOut = true
      deadline.abort(new DOMException('The Storyteller API did not answer in time.', 'TimeoutError'))
    }
  }, limitMs)
  unref(timer)

  if (caller?.aborted) {
    forward()
  } else {
    caller?.addEventListener('abort', forward, { once: true })
  }

  try {
    return await perform(current, target, { ...request, signal: deadline.signal }, read, attempt)
  } finally {
    clearTimeout(timer)
    caller?.removeEventListener('abort', forward)
  }
}

/** One request: what its failures are mapped from. */
interface Attempt {
  target: string
  /** The signal the caller passed. */
  caller?: AbortSignal
  /** What the steps race against: the caller's signal combined with the deadline, or the caller's signal alone. */
  signal?: AbortSignal
  limitMs?: number
  /** True once the deadline fired before any abort of the caller. */
  timedOut: boolean
}

// setTimeout fires at once for a delay above 2^31 - 1 ms (about 24.8 days).
const maxTimerMs = 2_147_483_647

function limitOf(current: StorytellerConfig, perCall: number | undefined, method: string | undefined, url: string): number | undefined {
  const configured = current.timeoutMs
  const limit = perCall ?? (typeof configured === 'function' ? configured({ method: (method ?? 'GET').toUpperCase(), url }) : configured)

  // Undefined, 0 and anything else that is not a positive finite number: no deadline.
  return limit !== undefined && Number.isFinite(limit) && limit > 0 ? Math.min(limit, maxTimerMs) : undefined
}

async function perform<T>(
  current: StorytellerConfig,
  target: string,
  init: RequestInit,
  read: (response: Response) => Promise<T>,
  attempt: Attempt,
): Promise<T> {
  let response = await send(current, target, init, false, attempt)

  if (response.status === 401 && current.getAccessToken) {
    // The unread body would keep the first connection busy.
    await race(() => response.body?.cancel() ?? Promise.resolve(), attempt.signal).catch(() => undefined)
    response = await send(current, target, init, true, attempt)

    if (response.status === 401) {
      current.onUnauthorized?.()
    }
  }

  try {
    return await race(() => read(response), attempt.signal)
  } catch (error) {
    throw failure(error, attempt)
  }
}

async function send(current: StorytellerConfig, target: string, init: RequestInit, forceRefresh: boolean, attempt: Attempt): Promise<Response> {
  const headers = new Headers(init.headers)

  for (const [name, value] of Object.entries(current.headers ?? {})) {
    if (!headers.has(name)) {
      headers.set(name, value)
    }
  }

  let token: string | undefined

  try {
    token = await race(async () => await current.getAccessToken?.({ forceRefresh }), attempt.signal)
  } catch (error) {
    // A failing token provider keeps its own error; only an abort or the deadline during the lookup is the request's.
    if (!attempt.signal?.aborted) {
      throw error
    }

    throw failure(error, attempt)
  }

  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  try {
    // A transport override may ignore the signal; a response that comes after the race is discarded.
    return await race(() => (current.fetch ?? globalThis.fetch)(target, { ...init, headers }), attempt.signal, discard)
  } catch (error) {
    // The token never leaves this function: the error carries only the URL path and the cause.
    throw failure(error, attempt)
  }
}

/**
 * Runs work until the signal aborts, then rejects with the signal's reason; work does not start on an aborted signal.
 * The listener goes with the race. A value of work that comes after an abort goes to late.
 */
function race<T>(work: () => Promise<T>, signal: AbortSignal | undefined, late?: (value: T) => void): Promise<T> {
  if (signal?.aborted) {
    return Promise.reject(signal.reason as unknown)
  }

  // A work that throws at once fails like one that rejects.
  const pending = new Promise<T>((resolve) => resolve(work()))

  if (!signal) {
    return pending
  }

  return new Promise<T>((resolve, reject) => {
    const onAbort = () => reject(signal.reason as unknown)
    signal.addEventListener('abort', onAbort, { once: true })

    pending.then(
      (value) => {
        signal.removeEventListener('abort', onAbort)

        if (signal.aborted) {
          late?.(value)
        }

        resolve(value)
      },
      (error: unknown) => {
        signal.removeEventListener('abort', onAbort)
        reject(error)
      },
    )
  })
}

function discard(response: Response): void {
  response.body?.cancel().catch(() => undefined)
}

/**
 * The StorytellerError of a failed step. A StorytellerError passes as it is; the deadline is a timeout, an aborted
 * caller's signal an abort unless its reason is a TimeoutError (AbortSignal.timeout), an AbortError an abort, and
 * anything else a network failure. Errors and reasons are told apart by name: a DOMException of another realm (a
 * jsdom window, a polyfilled fetch) fails instanceof.
 */
function failure(error: unknown, attempt: Attempt): StorytellerError {
  if (error instanceof StorytellerError) {
    return error
  }

  // A relative base URL (same origin, a dev proxy) is resolved against a placeholder origin just to read the path.
  const path = new URL(attempt.target, 'http://localhost').pathname

  if (attempt.timedOut && attempt.limitMs !== undefined) {
    return new StorytellerError({
      status: 0,
      message: `The Storyteller API did not answer within ${seconds(attempt.limitMs)} s (${path}).`,
      timedOut: true,
      cause: error,
    })
  }

  const callerAborted = attempt.caller?.aborted ?? false

  if (callerAborted && nameOf(attempt.caller?.reason) === 'TimeoutError') {
    // A poller's AbortSignal.timeout stays transient.
    return new StorytellerError({ status: 0, message: `The Storyteller API did not answer in time (${path}).`, timedOut: true, cause: error })
  }

  if (callerAborted || nameOf(error) === 'AbortError') {
    return new StorytellerError({ status: 0, message: 'The request was aborted.', aborted: true, cause: error })
  }

  return new StorytellerError({ status: 0, message: `The Storyteller API is not reachable (${path}).`, cause: error })
}

function nameOf(value: unknown): unknown {
  return typeof value === 'object' && value !== null ? (value as { name?: unknown }).name : undefined
}

// No needless decimals: 30000 ms is "30", 1500 ms "1.5".
function seconds(milliseconds: number): string {
  return String(Number((milliseconds / 1000).toFixed(3)))
}

// A pending deadline must not keep a Node process alive; a browser's timer is a number without unref.
function unref(timer: unknown): void {
  const handle = timer as { unref?: () => void }
  handle.unref?.()
}

/** Reads a response body the way storytellerFetch does. Not exported from the package. */
export async function readBody(response: Response): Promise<unknown> {
  if (response.status === 204 || response.status === 205 || response.status === 304) {
    return undefined
  }

  // Archives (view exports) stay binary; reading them as text would corrupt them.
  if (response.ok && isBinary(response.headers.get('content-type') ?? '')) {
    return await response.blob()
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

function isBinary(contentType: string): boolean {
  return /^application\/(?:zip|octet-stream)\b/i.test(contentType)
}
