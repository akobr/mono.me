import { getEventListeners } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { configureStoryteller, getStorytellerConfig, storytellerFetch, type StorytellerConfig } from '../lib/fetcher'
import { hasErrorCode, isAborted, isForbidden, isPreconditionFailed, isTimedOut, isTransientError, StorytellerError } from '../lib/errors'
import { deleteAnnotation } from '../generated/client/annotations/annotations'
import { getViewExportArchive, startViewExport, startViewImport } from '../generated/client/transfers/transfers'

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

/** Rejects with the signal's reason once it aborts, as fetch does, and never answers otherwise. */
function untilAborted(signal: AbortSignal | null | undefined): Promise<never> {
  return new Promise((_, reject) => {
    if (signal?.aborted) {
      reject(signal.reason as Error)
      return
    }

    signal?.addEventListener('abort', () => reject(signal.reason as Error), { once: true })
  })
}

/** A transport that answers only its signal: it rejects with the abort reason. */
function hangingFetch() {
  const calls: Call[] = []
  const fetch = vi.fn((url: RequestInfo | URL, init?: RequestInit) => {
    calls.push({ url: String(url), init: init ?? {} })
    return untilAborted(init?.signal)
  })

  return { fetch: fetch as unknown as typeof globalThis.fetch, calls }
}

/** A transport that ignores its signal and answers after delayMs (never without a delay). */
function deafFetch(delayMs?: number, answer: () => Response = () => json({ ok: true })) {
  const calls: Call[] = []
  const fetch = vi.fn((url: RequestInfo | URL, init?: RequestInit) => {
    calls.push({ url: String(url), init: init ?? {} })
    return new Promise<Response>((resolve) => {
      if (delayMs !== undefined) {
        setTimeout(() => resolve(answer()), delayMs)
      }
    })
  })

  return { fetch: fetch as unknown as typeof globalThis.fetch, calls }
}

/** A 200 whose body starts and never ends. */
const stalledBody = () =>
  new Response(
    new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new TextEncoder().encode('{"Id":'))
      },
    }),
    { status: 200, headers: { 'content-type': 'application/json' } },
  )

/** True while the promise has not settled. */
async function isPending(promise: Promise<unknown>): Promise<boolean> {
  const pending = Symbol('pending')
  return (await Promise.race([promise, Promise.resolve(pending)])) === pending
}

// Only the timers of the fetcher and of the fakes: the streams of Response keep their own scheduling.
const useFakeTimers = () => vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })

function configure(overrides: Partial<StorytellerConfig> & Pick<StorytellerConfig, 'fetch'>) {
  configureStoryteller({ baseUrl: 'https://api.example.com/api/', ...overrides })
}

afterEach(() => {
  vi.useRealTimers()
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

  it('returns zip archives as a Blob with their bytes', async () => {
    const bytes = new Uint8Array([0x50, 0x4b, 0x05, 0x06, 0xff, 0x00, 0x80])
    const { fetch } = fakeFetch(new Response(bytes, { status: 200, headers: { 'content-type': 'application/zip' } }))
    configure({ fetch })

    const archive = await getViewExportArchive('acme', 'web', 'default', 'job-1')

    expect(archive).toBeInstanceOf(Blob)
    expect(new Uint8Array(await archive.arrayBuffer())).toEqual(bytes)
  })

  it('still reports the JSON error of a request for an archive', async () => {
    const { fetch } = fakeFetch(json({ Message: 'Not ready.', ErrorCode: 'TransferNotReady' }, 409))
    configure({ fetch })

    const error = await getViewExportArchive('acme', 'web', 'default', 'job-1').catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(StorytellerError)
    expect(hasErrorCode(error, 'TransferNotReady')).toBe(true)
  })

  it('sends an import archive as the zip body with its options', async () => {
    const { fetch, calls } = fakeFetch(json({ Id: 'job-2', Status: 'Queued' }, 202))
    configure({ fetch })
    const archive = new Blob([new Uint8Array([0x50, 0x4b, 0x05, 0x06])], { type: 'application/zip' })

    const started = await startViewImport('acme', 'web', 'default', archive, {
      include: ['Annotations', 'Templates'],
      existing: 'Skip',
      dryRun: true,
      fileName: 'view.zip',
    })

    expect(started).toEqual({ Id: 'job-2', Status: 'Queued' })
    // the kinds go comma-separated (style form, explode false), as the API reads them
    expect(calls[0]?.url).toBe(
      'https://api.example.com/api/v1/acme/web/default/imports?include=Annotations%2CTemplates&existing=Skip&dryRun=true&fileName=view.zip',
    )
    expect(calls[0]?.init.method).toBe('POST')
    expect(calls[0]?.init.body).toBe(archive)
    expect(new Headers(calls[0]?.init.headers).get('Content-Type')).toBe('application/zip')
  })

  it('sends an export request with its format and kinds as JSON', async () => {
    const { fetch, calls } = fakeFetch(json({ Id: 'job-3', Status: 'Queued', Include: ['Annotations'] }, 202))
    configure({ fetch })

    const started = await startViewExport('acme', 'web', 'default', { Format: 'Git', Include: ['Annotations'] })

    expect(started.Include).toEqual(['Annotations'])
    expect(calls[0]?.url).toBe('https://api.example.com/api/v1/acme/web/default/exports')
    expect(calls[0]?.init.method).toBe('POST')
    expect(JSON.parse(String(calls[0]?.init.body))).toEqual({ Format: 'Git', Include: ['Annotations'] })
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
    const getAccessToken = vi.fn(() => tokens.shift())
    const first = fakeFetch(new Response(null, { status: 401 }), json({ Id: 'user-1' }))
    configure({ fetch: first.fetch, getAccessToken, onUnauthorized })

    await expect(storytellerFetch('/v1/access/account')).resolves.toEqual({ Id: 'user-1' })
    expect(first.calls.map((call) => new Headers(call.init.headers).get('Authorization'))).toEqual(['Bearer expired', 'Bearer fresh'])
    expect(getAccessToken.mock.calls).toEqual([[{ forceRefresh: false }], [{ forceRefresh: true }]])
    expect(onUnauthorized).not.toHaveBeenCalled()

    const second = fakeFetch(new Response(null, { status: 401 }), new Response(null, { status: 401 }))
    configure({ fetch: second.fetch, getAccessToken: () => tokens.shift(), onUnauthorized })

    const error = await storytellerFetch('/v1/access/account').catch((caught: unknown) => caught)
    expect((error as StorytellerError).status).toBe(401)
    expect(onUnauthorized).toHaveBeenCalledOnce()
  })

  it('cancels the body of the rejected 401 response before retrying', async () => {
    const rejected = new Response('{"Message":"expired"}', { status: 401, headers: { 'content-type': 'application/json' } })
    const cancel = vi.spyOn(rejected.body!, 'cancel')
    const { fetch } = fakeFetch(rejected, json({ Id: 'user-1' }))
    configure({ fetch, getAccessToken: () => 'token' })

    await expect(storytellerFetch('/v1/access/account')).resolves.toEqual({ Id: 'user-1' })
    expect(cancel).toHaveBeenCalledOnce()
  })

  it('encodes path parameters of the generated functions, so "#", "?" and "%" stay in the key', async () => {
    const { fetch, calls } = fakeFetch(new Response(null, { status: 204 }))
    configure({ fetch })

    await deleteAnnotation('acme', 'web', 'default', 'sbt.c#1?x=%2')

    expect(calls[0]!.url).toBe('https://api.example.com/api/v1/acme/web/default/annotations/sbt.c%231%3Fx%3D%252')
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
    expect(error.aborted).toBe(false)
    expect(error.message).toContain('/api/v1/access/account')
    expect(JSON.stringify({ message: error.message, body: error.body })).not.toContain('secret-token')

    const controller = new AbortController()
    controller.abort()
    configure({ fetch: globalThis.fetch })
    const aborted = (await storytellerFetch('/v1/access/account', { signal: controller.signal }).catch((caught: unknown) => caught)) as StorytellerError
    expect(aborted.status).toBe(0)
    expect(aborted.message).toBe('The request was aborted.')
    expect(isAborted(aborted)).toBe(true)
  })

  it('turns network failures into status 0 also with a relative base URL', async () => {
    const cause = new TypeError('fetch failed')
    const failing = vi.fn(async () => {
      throw cause
    }) as unknown as typeof globalThis.fetch
    configureStoryteller({ baseUrl: '/api', fetch: failing })

    const error = (await storytellerFetch('/v1/access/account').catch((caught: unknown) => caught)) as StorytellerError
    expect(error).toBeInstanceOf(StorytellerError)
    expect(error.status).toBe(0)
    expect(error.message).toContain('(/api/v1/access/account)')
    expect(error.cause).toBe(cause)
  })

  it('requires configuration first', () => {
    expect(getStorytellerConfig().baseUrl).toBe('http://unused')
  })
})

describe('storytellerFetch timeouts', () => {
  it('ends a request that does not answer within the configured timeoutMs', async () => {
    useFakeTimers()
    const { fetch, calls } = hangingFetch()
    configure({ fetch, getAccessToken: () => 'secret-token', timeoutMs: 1500 })

    const pending = storytellerFetch('/v1/access/account').catch((caught: unknown) => caught)
    await vi.advanceTimersByTimeAsync(1499)
    expect(await isPending(pending)).toBe(true)
    await vi.advanceTimersByTimeAsync(1)
    const error = (await pending) as StorytellerError

    expect(error).toBeInstanceOf(StorytellerError)
    expect(error).toMatchObject({
      status: 0,
      timedOut: true,
      aborted: false,
      message: 'The Storyteller API did not answer within 1.5 s (/api/v1/access/account).',
    })
    expect(isTimedOut(error)).toBe(true)
    expect(isAborted(error)).toBe(false)
    expect(isTransientError(error)).toBe(true)
    expect((error.cause as Error).name).toBe('TimeoutError')
    expect(JSON.stringify({ message: error.message, body: error.body })).not.toContain('secret-token')
    // fetch got the deadline's signal, never the option
    expect(calls[0]?.init.signal?.aborted).toBe(true)
    expect(calls[0]?.init).not.toHaveProperty('timeoutMs')
  })

  it('picks the limit per request with a function of the method and the URL', async () => {
    useFakeTimers()
    const { fetch } = deafFetch(1000)
    const timeoutMs = vi.fn(({ method }: { method: string; url: string }) => (method === 'GET' ? 500 : undefined))
    configure({ fetch, timeoutMs })

    const read = storytellerFetch('/v1/a').catch((caught: unknown) => caught)
    const write = storytellerFetch('/v1/b', { method: 'post' })
    await vi.advanceTimersByTimeAsync(1000)

    expect(await read).toMatchObject({ timedOut: true, message: 'The Storyteller API did not answer within 0.5 s (/api/v1/a).' })
    // undefined: no limit
    await expect(write).resolves.toEqual({ ok: true })
    expect(timeoutMs.mock.calls).toEqual([
      [{ method: 'GET', url: 'https://api.example.com/api/v1/a' }],
      [{ method: 'POST', url: 'https://api.example.com/api/v1/b' }],
    ])
  })

  it('takes a per-call timeoutMs over the configured one, and 0 disables the limit', async () => {
    useFakeTimers()
    const { fetch, calls } = deafFetch(1000)
    const timeoutMs = vi.fn(() => 500)
    configure({ fetch, timeoutMs })
    const controller = new AbortController()

    const longer = storytellerFetch('/v1/a', { timeoutMs: 2000 })
    const unlimited = storytellerFetch('/v1/b', { timeoutMs: 0, signal: controller.signal })
    await vi.advanceTimersByTimeAsync(1000)

    await expect(longer).resolves.toEqual({ ok: true })
    await expect(unlimited).resolves.toEqual({ ok: true })
    // the configured function is not asked when the call has its own limit
    expect(timeoutMs).not.toHaveBeenCalled()
    // without a deadline the caller's signal reaches fetch as it is
    expect(calls[1]?.init.signal).toBe(controller.signal)

    configure({ fetch })
    const limited = storytellerFetch('/v1/c', { timeoutMs: 250 }).catch((caught: unknown) => caught)
    await vi.advanceTimersByTimeAsync(1000)

    expect(await limited).toMatchObject({ timedOut: true, message: 'The Storyteller API did not answer within 0.25 s (/api/v1/c).' })
    for (const call of calls) {
      expect(call.init).not.toHaveProperty('timeoutMs')
    }
  })

  it('keeps an abort by the caller an abort, also with a deadline', async () => {
    useFakeTimers()
    const { fetch, calls } = hangingFetch()
    configure({ fetch, timeoutMs: 1000 })
    const controller = new AbortController()

    const pending = storytellerFetch('/v1/a', { signal: controller.signal }).catch((caught: unknown) => caught)
    await vi.advanceTimersByTimeAsync(100)
    controller.abort()
    const error = (await pending) as StorytellerError

    // the deadline went with the request
    expect(vi.getTimerCount()).toBe(0)
    expect(error).toMatchObject({ status: 0, aborted: true, timedOut: false, message: 'The request was aborted.' })
    expect(isTransientError(error)).toBe(false)
    // fetch saw the abort through the combined signal
    expect(calls[0]?.init.signal).not.toBe(controller.signal)
    expect(calls[0]?.init.signal?.aborted).toBe(true)

    // aborted before the call: nothing is sent
    const early = await storytellerFetch('/v1/b', { signal: controller.signal }).catch((caught: unknown) => caught)
    expect(isAborted(early)).toBe(true)
    expect(calls).toHaveLength(1)
  })

  it("reports the caller's AbortSignal.timeout as timedOut, not aborted", async () => {
    const { fetch, calls } = hangingFetch()
    configure({ fetch })

    const signal = AbortSignal.timeout(20)
    const error = (await storytellerFetch('/v1/access/account', { signal }).catch((caught: unknown) => caught)) as StorytellerError

    expect(error).toMatchObject({
      status: 0,
      timedOut: true,
      aborted: false,
      message: 'The Storyteller API did not answer in time (/api/v1/access/account).',
    })
    expect(isTransientError(error)).toBe(true)
    expect(calls[0]?.init.signal).toBe(signal)

    // the same under a longer deadline of its own, and with a TimeoutError of another realm (told apart by name)
    configure({ fetch, timeoutMs: 60_000 })
    const controller = new AbortController()
    const pending = storytellerFetch('/v1/b', { signal: controller.signal }).catch((caught: unknown) => caught)
    controller.abort(Object.assign(new Error('Too slow.'), { name: 'TimeoutError' }))

    expect(await pending).toMatchObject({ timedOut: true, aborted: false, message: 'The Storyteller API did not answer in time (/api/v1/b).' })
  })

  it('ends a request whose body does not arrive in time', async () => {
    useFakeTimers()
    const { fetch } = fakeFetch(stalledBody())
    configure({ fetch, timeoutMs: 1000 })

    const pending = storytellerFetch('/v1/a').catch((caught: unknown) => caught)
    await vi.advanceTimersByTimeAsync(1000)

    expect(await pending).toMatchObject({ status: 0, timedOut: true, message: 'The Storyteller API did not answer within 1 s (/api/v1/a).' })
  })

  it('ends a request whose transport ignores the signal, and discards its late answer', async () => {
    useFakeTimers()
    const late = json({ ok: true })
    const cancel = vi.spyOn(late.body!, 'cancel')
    const { fetch } = deafFetch(5000, () => late)
    configure({ fetch, timeoutMs: 1000 })
    const controller = new AbortController()

    const timedOut = storytellerFetch('/v1/a').catch((caught: unknown) => caught)
    await vi.advanceTimersByTimeAsync(1000)
    expect(isTimedOut(await timedOut)).toBe(true)

    await vi.advanceTimersByTimeAsync(4000)
    expect(cancel).toHaveBeenCalledOnce()

    const aborted = storytellerFetch('/v1/b', { signal: controller.signal }).catch((caught: unknown) => caught)
    controller.abort()
    expect(isAborted(await aborted)).toBe(true)
  })

  it('keeps one deadline across the retry after a 401', async () => {
    useFakeTimers()
    const calls: RequestInit[] = []
    const fetch = vi.fn((_url: RequestInfo | URL, init?: RequestInit) => {
      calls.push(init ?? {})
      return calls.length === 1
        ? new Promise<Response>((resolve) => setTimeout(() => resolve(new Response(null, { status: 401 })), 600))
        : untilAborted(init?.signal)
    }) as unknown as typeof globalThis.fetch
    const onUnauthorized = vi.fn()
    configure({ fetch, getAccessToken: () => 'token', onUnauthorized, timeoutMs: 1000 })

    const pending = storytellerFetch('/v1/access/account').catch((caught: unknown) => caught)
    await vi.advanceTimersByTimeAsync(600)
    expect(calls).toHaveLength(2)
    await vi.advanceTimersByTimeAsync(399)
    expect(await isPending(pending)).toBe(true)
    await vi.advanceTimersByTimeAsync(1)

    expect(await pending).toMatchObject({ timedOut: true, message: 'The Storyteller API did not answer within 1 s (/api/v1/access/account).' })
    expect(onUnauthorized).not.toHaveBeenCalled()
  })

  it('ends a token lookup that hangs, and keeps the error of one that fails', async () => {
    useFakeTimers()
    const { fetch, calls } = fakeFetch(json({}))
    configure({ fetch, getAccessToken: () => new Promise<string>(() => undefined), timeoutMs: 1000 })

    const pending = storytellerFetch('/v1/access/account').catch((caught: unknown) => caught)
    await vi.advanceTimersByTimeAsync(1000)

    expect(await pending).toMatchObject({ status: 0, timedOut: true })
    expect(calls).toHaveLength(0)

    const signIn = new Error('Sign in again.')
    configure({
      fetch,
      getAccessToken: () => {
        throw signIn
      },
      timeoutMs: 1000,
    })
    await expect(storytellerFetch('/v1/access/account')).rejects.toBe(signIn)
  })

  it('leaves no timer and no listener behind', async () => {
    useFakeTimers()
    const { fetch, calls } = fakeFetch(json({}), json({ Message: 'Gone.' }, 404))
    configure({ fetch, timeoutMs: 1000 })
    const controller = new AbortController()

    await storytellerFetch('/v1/a', { signal: controller.signal })
    await storytellerFetch('/v1/b', { signal: controller.signal }).catch(() => undefined)

    expect(vi.getTimerCount()).toBe(0)
    expect(getEventListeners(controller.signal, 'abort')).toHaveLength(0)
    for (const call of calls) {
      expect(getEventListeners(call.init.signal!, 'abort')).toHaveLength(0)
    }

    // and after a timeout
    const hanging = hangingFetch()
    configure({ fetch: hanging.fetch, timeoutMs: 1000 })
    const pending = storytellerFetch('/v1/c', { signal: controller.signal }).catch((caught: unknown) => caught)
    await vi.advanceTimersByTimeAsync(1000)

    expect(isTimedOut(await pending)).toBe(true)
    expect(vi.getTimerCount()).toBe(0)
    expect(getEventListeners(controller.signal, 'abort')).toHaveLength(0)
  })
})
