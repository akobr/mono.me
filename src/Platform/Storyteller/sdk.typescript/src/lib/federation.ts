// Workload identity federation: exchange a platform token (GitHub Actions, Kubernetes, Vercel, Azure managed identity,
// AWS, Google Cloud) for a short-lived Storyteller token of a Federated machine (RFC 8693 token exchange).
import { getExchangeMachineTokenUrl } from '../generated/client/access/access'
import { StorytellerError } from './errors'

/** The subject_token_type values the token endpoint accepts; the token itself must be a JWT either way. */
export const FederatedSubjectTokenType = {
  /** GitHub Actions, Kubernetes, Vercel, AWS. */
  Jwt: 'urn:ietf:params:oauth:token-type:jwt',
  /** Google Cloud (metadata server identity tokens). */
  IdToken: 'urn:ietf:params:oauth:token-type:id_token',
  /** Azure managed identity (Entra access tokens). */
  AccessToken: 'urn:ietf:params:oauth:token-type:access_token',
} as const

export type FederatedSubjectTokenType = (typeof FederatedSubjectTokenType)[keyof typeof FederatedSubjectTokenType]

export interface FederatedTokenOptions {
  /** API root including the "/api" prefix, the same value as configureStoryteller({ baseUrl }). */
  baseUrl: string
  organization: string
  project: string
  /** Id of the Federated machine; sent as client_id. */
  machineId: string
  /** Type of the subject token; urn:ietf:params:oauth:token-type:jwt by default. */
  subjectTokenType?: FederatedSubjectTokenType
  /** Space-separated subset of the machine's scopes; the token gets all of them when omitted. */
  scope?: string
  /** Transport override for tests and special hosts. Defaults to the global fetch. */
  fetch?: typeof globalThis.fetch
}

export interface FederatedTokenExchangeOptions extends FederatedTokenOptions {
  /** The platform token (a JWT) whose audience, subject and claims match a federation rule of the machine. */
  subjectToken: string
}

export interface FederatedToken {
  /** The Storyteller token for Authorization: Bearer. */
  accessToken: string
  /** Expiry in milliseconds since the epoch (the Date.now() scale), counted from when the request was sent. */
  expiresAt: number
  /** The granted scopes, space separated. */
  scope: string
}

export interface FederatedTokenProviderOptions extends FederatedTokenOptions {
  /** Returns a current platform token; called before every exchange, never for a cached Storyteller token. */
  getSubjectToken: () => Promise<string> | string
}

/** Returns a Storyteller token; usable as getAccessToken of configureStoryteller. */
export type FederatedTokenProvider = (options?: { forceRefresh?: boolean }) => Promise<string>

const tokenExchangeGrantType = 'urn:ietf:params:oauth:grant-type:token-exchange'

// A cached token is replaced this long before it expires, so a request never leaves with a token about to expire.
// When that early exchange fails, the cached token is still used until it expires.
const refreshMarginMs = 60_000

/**
 * Exchanges a platform token for a Storyteller token at POST {baseUrl}/v1/{organization}/{project}/access/token.
 * Uses plain fetch, never the configured fetcher: the endpoint refuses any Authorization header, and a fetcher whose
 * getAccessToken is this exchange would call itself. OAuth errors become a StorytellerError whose errorCode is the
 * OAuth error (invalid_request, invalid_scope, temporarily_unavailable with status 503, …).
 */
export async function exchangeFederatedToken(options: FederatedTokenExchangeOptions): Promise<FederatedToken> {
  const target = `${options.baseUrl.replace(/\/+$/, '')}${getExchangeMachineTokenUrl(options.organization, options.project)}`
  const form = new URLSearchParams({
    grant_type: tokenExchangeGrantType,
    subject_token: options.subjectToken,
    subject_token_type: options.subjectTokenType ?? FederatedSubjectTokenType.Jwt,
    client_id: options.machineId,
  })

  if (options.scope) {
    form.set('scope', options.scope)
  }

  const send = options.fetch ?? globalThis.fetch
  const sentAt = Date.now()
  let response: Response

  try {
    response = await send(target, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded', Accept: 'application/json' },
      body: form,
      // The subject token must not follow a redirect to another host.
      redirect: 'error',
    })
  } catch (error) {
    // Only the path: the error never carries the subject token.
    throw new StorytellerError({
      status: 0,
      message: `The Storyteller token endpoint is not reachable (${new URL(target, 'http://localhost').pathname}).`,
      cause: error,
    })
  }

  const body = await readBody(response)

  if (!response.ok) {
    throw StorytellerError.fromResponse(response.status, response.statusText, body)
  }

  const record = typeof body === 'object' && body !== null ? (body as Record<string, unknown>) : {}
  const accessToken = record.access_token
  const expiresIn = record.expires_in

  if (typeof accessToken !== 'string' || accessToken.length === 0 || typeof expiresIn !== 'number' || !(expiresIn > 0)) {
    // The body is left out: it may hold a token.
    throw new StorytellerError({
      status: response.status,
      message: 'The Storyteller token endpoint returned no access token with an expiry.',
    })
  }

  return {
    accessToken,
    expiresAt: sentAt + expiresIn * 1000,
    scope: typeof record.scope === 'string' ? record.scope : (options.scope ?? ''),
  }
}

/**
 * A getAccessToken for configureStoryteller that exchanges a fresh platform token from getSubjectToken whenever the
 * cached Storyteller token is missing or within 60 s of its expiry. Concurrent callers share one exchange, and
 * forceRefresh (the fetcher's retry after a 401) drops the cached token. A failed exchange is not cached; when it
 * was started early (within those 60 s) and the cached token has not expired yet, the caller gets the cached token
 * instead of the error, and the next call tries again.
 */
export function createFederatedTokenProvider(options: FederatedTokenProviderOptions): FederatedTokenProvider {
  const { getSubjectToken, ...exchangeOptions } = options
  let cached: FederatedToken | undefined
  let pending: Promise<FederatedToken> | undefined

  const startExchange = (): Promise<FederatedToken> => {
    const exchange = (async () => exchangeFederatedToken({ ...exchangeOptions, subjectToken: await getSubjectToken() }))()
    const settle = () => {
      if (pending === exchange) {
        pending = undefined
      }
    }

    // Settles after the caller stored the promise in pending; the callers of pending see the rejection themselves.
    exchange.then((token) => {
      cached = token
      settle()
    }, settle)

    return exchange
  }

  return async ({ forceRefresh = false } = {}) => {
    if (forceRefresh) {
      cached = undefined
    }

    if (cached && Date.now() < cached.expiresAt - refreshMarginMs) {
      return cached.accessToken
    }

    pending ??= startExchange()

    try {
      return (await pending).accessToken
    } catch (error) {
      // The API still accepts the cached token until it expires. forceRefresh dropped it above, so the retry after a
      // 401 never gets it back.
      if (cached && Date.now() < cached.expiresAt) {
        return cached.accessToken
      }

      throw error
    }
  }
}

async function readBody(response: Response): Promise<unknown> {
  const text = await response.text()

  if (text.length === 0) {
    return undefined
  }

  if (!/^application\/(?:[\w.+-]+\+)?json\b/i.test(response.headers.get('content-type') ?? '')) {
    return text
  }

  try {
    return JSON.parse(text) as unknown
  } catch {
    return text
  }
}
