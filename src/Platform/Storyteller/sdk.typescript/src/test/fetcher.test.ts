import { afterEach, describe, expect, it, vi } from 'vitest'
import { configureStoryteller, getStorytellerConfig, storytellerFetch, type StorytellerConfig } from '../lib/fetcher'
import { hasErrorCode, isForbidden, isPreconditionFailed, StorytellerError } from '../lib/errors'

interface Call {
  url: string
  init: RequestInit
}

function fakeFetch(...responses: Response[]) {
  const calls: Call[] = []
  const fetch = vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
    calls.push({ url: String(url), init: init ?? {} })
    const response = responses.shift()

    if (!response) {
      throw new Error('No more responses.')
    }

    return response
  })

  return { fetch: fetch as unknown as typeof globalThis.fetch, calls }
}

const json = (body: unknown, status = 200, contentType = 'application/json; charset=utf-8') =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': contentType } })

function configure(overrides: Partial<StorytellerConfig> & Pick<StorytellerConfig, 'fetch'>) {
  configureStoryteller({ baseUrl: 'https://api.example.com/api/', ...overrides })
}

afterEach(() => {
  configureStoryteller({ baseUrl: 'http://unused' })
})

describe('storytellerFetch', () => {
  it('prefixes the base URL and adds the bearer token and the configured headers', async () => {
    const { fetch, calls } = fakeFetch(json({ Id: 'user-1' }))
    configure({ fetch, getAccessToken: () => 'token-1', headers: { 'X-Client': 'test' } })

    const account = await storytellerFetch<{ Id: string }>('/v1/access/account', { method: 'GET', headers: { Accept: 'application/json' } })

    expect(account).toEqual({ Id: 'user-1' })
    expect(calls[0]?.url).toBe('https://api.example.com/api/v1/access/account')
    const headers = new Headers(calls[0]?.init.headers)
    expect(headers.get('Authorization')).toBe('Bearer token-1')
    expect(headers.get('X-Client')).toBe('test')
    expect(headers.get('Accept')).toBe('application/json')
  })

  it('sends no Authorization header without a token and keeps absolute URLs', async () => {
    const { fetch, calls } = fakeFetch(json({}))
    configure({ fetch, getAccessToken: async () => undefined })

    await storytellerFetch('https://other.example.com/v1/auth/configuration')

    expect(calls[0]?.url).toBe('https://other.example.com/v1/auth/configuration')
    expect(new Headers(calls[0]?.init.headers).has('Authorization')).toBe(false)
  })

  it('returns non-JSON bodies as text (PEM, unified diff)', async () => {
    const pem = '-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----'
    const { fetch } = fakeFetch(
      new Response(pem, { status: 200, headers: { 'content-type': 'application/x-pem-file' } }),
      new Response('@@ -1 +1 @@', { status: 200, headers: { 'content-type': 'text/plain' } }),
      json({ ok: true }, 200, 'application/problem+json'),
    )
    configure({ fetch })

    await expect(storytellerFetch<string>('/v1/access/certificate-authority')).resolves.toBe(pem)
    await expect(storytellerFetch<string>('/v1/o/p/v/configuration/k/versions/2/diff?format=unified')).resolves.toBe('@@ -1 +1 @@')
    await expect(storytellerFetch('/v1/x')).resolves.toEqual({ ok: true })
  })

  it('returns undefined for 204 and empty bodies', async () => {
    const { fetch } = fakeFetch(new Response(null, { status: 204 }), new Response('', { status: 200 }))
    configure({ fetch })

    await expect(storytellerFetch('/v1/a', { method: 'DELETE' })).resolves.toBeUndefined()
    await expect(storytellerFetch('/v1/b', { method: 'DELETE' })).resolves.toBeUndefined()
  })

  it('throws StorytellerError with message, hint and error code', async () => {
    const { fetch } = fakeFetch(json({ Message: 'No Administrator access.', Hint: 'Ask an owner.', ErrorCode: 'AccessDenied' }, 403))
    configure({ fetch })

    const error = await storytellerFetch('/v1/access/points/acme/members').catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(StorytellerError)
    expect(isForbidden(error)).toBe(true)
    expect(hasErrorCode(error, 'AccessDenied')).toBe(true)
    expect((error as StorytellerError).message).toBe('No Administrator access.')
    expect((error as StorytellerError).hint).toBe('Ask an owner.')
  })

  it('exposes schema errors of a 409 compliance failure', async () => {
    const body = {
      Message: 'The configuration violates the schema.',
      Errors: [{ AnnotationKey: 'exe.a.b.c', ViewName: 'default', Errors: ['#/retries: IntegerExpected'] }],
    }
    const { fetch } = fakeFetch(json(body, 409))
    configure({ fetch })

    const error = (await storytellerFetch('/v1/o/p/v/configuration/exe.a.b.c', { method: 'PATCH' }).catch((caught: unknown) => caught)) as StorytellerError

    expect(error.status).toBe(409)
    expect(error.schemaErrors?.[0]?.Errors).toEqual(['#/retries: IntegerExpected'])
  })

  it('reports a failed JSON Patch test as 412 PatchTestFailed', async () => {
    const { fetch } = fakeFetch(json({ Message: 'JSON Patch operation 0 failed.', ErrorCode: 'PatchTestFailed' }, 412))
    configure({ fetch })

    const error = await storytellerFetch('/v1/o/p/v/configuration/k', { method: 'PATCH' }).catch((caught: unknown) => caught)

    expect(isPreconditionFailed(error)).toBe(true)
    expect(hasErrorCode(error, 'PatchTestFailed')).toBe(true)
  })

  it('falls back to the status for bodies without a message', async () => {
    const { fetch } = fakeFetch(new Response('', { status: 404, statusText: 'Not Found' }), new Response('<html>oops</html>', { status: 500, headers: { 'content-type': 'text/html' } }))
    configure({ fetch })

    const notFound = (await storytellerFetch('/v1/a').catch((caught: unknown) => caught)) as StorytellerError
    const serverError = (await storytellerFetch('/v1/b').catch((caught: unknown) => caught)) as StorytellerError

    expect(notFound.message).toBe('Storyteller API returned 404 Not Found.')
    expect(serverError.status).toBe(500)
    expect(serverError.message).toBe('<html>oops</html>')
  })

  it('retries once with a fresh token after 401, then reports onUnauthorized', async () => {
    const tokens = ['expired', 'fresh', 'fresh-again', 'still-bad']
    const onUnauthorized = vi.fn()
    const first = fakeFetch(new Response(null, { status: 401 }), json({ Id: 'user-1' }))
    configure({ fetch: first.fetch, getAccessToken: () => tokens.shift(), onUnauthorized })

    await expect(storytellerFetch('/v1/access/account')).resolves.toEqual({ Id: 'user-1' })
    expect(first.calls.map((call) => new Headers(call.init.headers).get('Authorization'))).toEqual(['Bearer expired', 'Bearer fresh'])
    expect(onUnauthorized).not.toHaveBeenCalled()

    const second = fakeFetch(new Response(null, { status: 401 }), new Response(null, { status: 401 }))
    configure({ fetch: second.fetch, getAccessToken: () => tokens.shift(), onUnauthorized })

    const error = await storytellerFetch('/v1/access/account').catch((caught: unknown) => caught)
    expect((error as StorytellerError).status).toBe(401)
    expect(onUnauthorized).toHaveBeenCalledOnce()
  })

  it('does not retry a 401 without a token provider', async () => {
    const { fetch, calls } = fakeFetch(new Response(null, { status: 401 }))
    configure({ fetch })

    await expect(storytellerFetch('/v1/access/account')).rejects.toMatchObject({ status: 401 })
    expect(calls).toHaveLength(1)
  })

  it('turns network failures and aborts into status 0 without leaking the token', async () => {
    const failing = vi.fn(async () => {
      throw new TypeError('fetch failed')
    }) as unknown as typeof globalThis.fetch
    configure({ fetch: failing, getAccessToken: () => 'secret-token' })

    const error = (await storytellerFetch('/v1/access/account').catch((caught: unknown) => caught)) as StorytellerError
    expect(error.status).toBe(0)
    expect(error.message).toContain('/api/v1/access/account')
    expect(JSON.stringify({ message: error.message, body: error.body })).not.toContain('secret-token')

    const controller = new AbortController()
    controller.abort()
    configure({ fetch: globalThis.fetch })
    const aborted = (await storytellerFetch('/v1/access/account', { signal: controller.signal }).catch((caught: unknown) => caught)) as StorytellerError
    expect(aborted.status).toBe(0)
    expect(aborted.message).toBe('The request was aborted.')
  })

  it('requires configuration first', () => {
    expect(getStorytellerConfig().baseUrl).toBe('http://unused')
  })
})
