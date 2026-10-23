import { afterEach, describe, expect, it, vi } from 'vitest'
import * as sdk from '../index'
import {
  configureStoryteller,
  getConfigurationConditional,
  getResolvedConfigurationConditional,
  isAborted,
  isNotFound,
  StorytellerError,
  type Configuration,
  type StorytellerConfig,
} from '../index'

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

const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8', ...headers } })

const notModified = (headers: Record<string, string> = {}) => new Response(null, { status: 304, headers })

function configure(overrides: Partial<StorytellerConfig> & Pick<StorytellerConfig, 'fetch'>) {
  configureStoryteller({ baseUrl: 'https://api.example.com/api/', ...overrides })
}

const sentHeaders = (call: Call | undefined) => new Headers(call?.init.headers)

const tag = 'W/"e.q1w2e3r4t5y6u7i8o9p0aZ"'
const configuration: Configuration = {
  AnnotationKey: 'exe.billing.api.prod',
  Version: 3,
  Content: { retries: 3, endpoint: 'https://billing.example.com' },
  Hash: 'a1b2c3d4',
}

const readFailure = (read: typeof getConfigurationConditional) =>
  read('acme', 'billing', 'default', 'exe.billing.api.prod', { ifNoneMatch: tag }).then(
    () => {
      throw new Error('The read did not fail.')
    },
    (caught: unknown) => caught as StorytellerError,
  )

afterEach(() => {
  configureStoryteller({ baseUrl: 'http://unused' })
})

describe('conditional reads', () => {
  it('reads a 200 with its ETag, then answers notModified on a 304 for the same tag', async () => {
    const cacheControl = 'private, no-cache'
    const { fetch, calls } = fakeFetch(
      json(configuration, 200, { etag: tag, 'cache-control': cacheControl }),
      notModified({ etag: tag, 'cache-control': cacheControl }),
    )
    configure({ fetch })

    const first = await getConfigurationConditional('acme', 'billing', 'default', 'exe.billing.api.prod')
    const second = await getConfigurationConditional('acme', 'billing', 'default', 'exe.billing.api.prod', { ifNoneMatch: first.etag })

    expect(first).toEqual({ status: 200, notModified: false, etag: tag, value: configuration, cacheControl })
    expect(second).toEqual({ status: 304, notModified: true, etag: tag, cacheControl })
    expect(second.value).toBeUndefined()
    expect(calls.map((call) => call.url)).toEqual([
      'https://api.example.com/api/v1/acme/billing/default/configuration/exe.billing.api.prod',
      'https://api.example.com/api/v1/acme/billing/default/configuration/exe.billing.api.prod',
    ])
    expect(calls.map((call) => call.init.method)).toEqual(['GET', 'GET'])
    expect(sentHeaders(calls[0]).has('If-None-Match')).toBe(false)
    // the weak prefix goes back unchanged
    expect(sentHeaders(calls[1]).get('If-None-Match')).toBe(tag)
  })

  it('reads the resolved configuration with no-store and withheld secrets on a 200 and a 304', async () => {
    const headers = { etag: 'W/"r.AAAAAAAAAAAAAAAAAAAAAA"', 'cache-control': 'no-store', 'x-2s-secrets-withheld': 'mtls-required' }
    const { fetch, calls } = fakeFetch(json(configuration, 200, headers), notModified(headers))
    configure({ fetch })

    const first = await getResolvedConfigurationConditional('acme', 'billing', 'default', 'exe.billing.api.prod')
    const second = await getResolvedConfigurationConditional('acme', 'billing', 'default', 'exe.billing.api.prod', { ifNoneMatch: first.etag })

    expect(calls[0]?.url).toBe('https://api.example.com/api/v1/acme/billing/default/configuration/exe.billing.api.prod/resolved')
    for (const result of [first, second]) {
      expect(result.etag).toBe(headers.etag)
      expect(result.cacheControl).toBe('no-store')
      expect(result.secretsWithheld).toBe('mtls-required')
    }
    expect(first.value).toEqual(configuration)
    expect(second.notModified).toBe(true)
  })

  it('sends ifNoneMatch over an If-None-Match of options.headers and keeps the other headers', async () => {
    const { fetch, calls } = fakeFetch(
      notModified({ etag: tag }),
      notModified({ etag: tag }),
      notModified({ etag: tag }),
      json(configuration, 200, { etag: tag }),
      json(configuration),
      json(configuration),
    )
    configure({ fetch })
    const stale = 'W/"e.old"'
    // a plain object, a Headers instance and tuples, the last two with the name in another case
    const variants: HeadersInit[] = [
      { 'If-None-Match': stale, Accept: 'application/json' },
      new Headers({ 'if-none-match': stale, Accept: 'application/json' }),
      [
        ['IF-NONE-MATCH', stale],
        ['Accept', 'application/json'],
      ],
    ]

    for (const headers of variants) {
      await getConfigurationConditional('acme', 'billing', 'default', 'exe.billing.api.prod', { headers, ifNoneMatch: tag })
    }
    // without ifNoneMatch, a header set by the caller goes out as it is
    await getConfigurationConditional('acme', 'billing', 'default', 'exe.billing.api.prod', { headers: { 'If-None-Match': '*' }, ifNoneMatch: undefined })
    // an empty tag sends nothing, and removes a header set by the caller
    await getConfigurationConditional('acme', 'billing', 'default', 'exe.billing.api.prod', { ifNoneMatch: '' })
    await getConfigurationConditional('acme', 'billing', 'default', 'exe.billing.api.prod', { headers: variants[0], ifNoneMatch: '' })

    for (const call of calls.slice(0, variants.length)) {
      const sent = sentHeaders(call)
      // a stale tag next to the new one would read back as both, joined by a comma
      expect(sent.get('If-None-Match')).toBe(tag)
      expect(sent.get('Accept')).toBe('application/json')
      expect(call.init).not.toHaveProperty('ifNoneMatch')
    }
    expect(sentHeaders(calls[3]).get('If-None-Match')).toBe('*')
    expect(sentHeaders(calls[4]).has('If-None-Match')).toBe(false)
    expect(sentHeaders(calls[5]).has('If-None-Match')).toBe(false)
    expect(sentHeaders(calls[5]).get('Accept')).toBe('application/json')
  })

  it('answers without an etag when the header is missing or not readable', async () => {
    const { fetch, calls } = fakeFetch(json(configuration), json(configuration))
    configure({ fetch })

    const first = await getConfigurationConditional('acme', 'billing', 'default', 'exe.billing.api.prod')
    const second = await getConfigurationConditional('acme', 'billing', 'default', 'exe.billing.api.prod', { ifNoneMatch: first.etag })

    expect(first).toEqual({ status: 200, notModified: false, value: configuration })
    expect(first.etag).toBeUndefined()
    expect(first.cacheControl).toBeUndefined()
    expect(first.secretsWithheld).toBeUndefined()
    expect(second.status).toBe(200)
    expect(sentHeaders(calls[1]).has('If-None-Match')).toBe(false)
  })

  it('forwards the signal and the other request options', async () => {
    const { fetch, calls } = fakeFetch(json(configuration, 200, { etag: tag }))
    configure({ fetch })
    const controller = new AbortController()

    await getResolvedConfigurationConditional('acme', 'billing', 'default', 'exe.billing.api.prod', {
      signal: controller.signal,
      cache: 'no-store',
      ifNoneMatch: tag,
    })

    expect(calls[0]?.init.signal).toBe(controller.signal)
    expect(calls[0]?.init.cache).toBe('no-store')
  })

  it('reports an aborted read as an aborted StorytellerError', async () => {
    const controller = new AbortController()
    controller.abort()
    configure({ fetch: globalThis.fetch })

    const error = await getConfigurationConditional('acme', 'billing', 'default', 'exe.billing.api.prod', {
      signal: controller.signal,
      ifNoneMatch: tag,
    }).catch((caught: unknown) => caught)

    expect(isAborted(error)).toBe(true)
    expect((error as StorytellerError).status).toBe(0)
  })

  it('maps every other status to StorytellerError, as the fetcher does', async () => {
    const { fetch } = fakeFetch(
      json({ Message: 'No Configuration.Read.', Hint: 'Grant the scope.', ErrorCode: 'AccessDenied' }, 403),
      new Response('', { status: 404, statusText: 'Not Found' }),
      json({ Message: 'A binding failed.', ErrorCode: 'binding.evaluation' }, 422),
      new Response(null, { status: 500 }),
    )
    configure({ fetch })

    const forbidden = await readFailure(getConfigurationConditional)
    const missing = await readFailure(getConfigurationConditional)
    const unresolvable = await readFailure(getResolvedConfigurationConditional)
    const failed = await readFailure(getResolvedConfigurationConditional)

    expect(forbidden).toBeInstanceOf(StorytellerError)
    expect(forbidden).toMatchObject({ status: 403, errorCode: 'AccessDenied', message: 'No Configuration.Read.', hint: 'Grant the scope.' })
    expect(isNotFound(missing)).toBe(true)
    expect(missing.message).toBe('Storyteller API returned 404 Not Found.')
    expect(unresolvable).toMatchObject({ status: 422, errorCode: 'binding.evaluation' })
    expect(failed).toMatchObject({ status: 500 })
  })

  it('keeps If-None-Match on the retry after a 401', async () => {
    const tokens = ['expired', 'fresh']
    const onUnauthorized = vi.fn()
    const { fetch, calls } = fakeFetch(new Response(null, { status: 401 }), notModified({ etag: tag }))
    configure({ fetch, getAccessToken: () => tokens.shift(), onUnauthorized })

    const result = await getConfigurationConditional('acme', 'billing', 'default', 'exe.billing.api.prod', { ifNoneMatch: tag })

    expect(result.notModified).toBe(true)
    expect(calls.map((call) => sentHeaders(call).get('Authorization'))).toEqual(['Bearer expired', 'Bearer fresh'])
    expect(calls.map((call) => sentHeaders(call).get('If-None-Match'))).toEqual([tag, tag])
    expect(onUnauthorized).not.toHaveBeenCalled()
  })

  it('exports the helpers from the package root, but not the internal request', () => {
    expect(sdk.getConfigurationConditional).toBeTypeOf('function')
    expect(sdk.getResolvedConfigurationConditional).toBeTypeOf('function')
    expect(sdk).not.toHaveProperty('storytellerRequest')
    expect(sdk).not.toHaveProperty('readBody')
  })
})
