import { afterEach, describe, expect, it, vi } from 'vitest'
import * as sdk from '../index'
import {
  configureStoryteller,
  getAnnotationVersioned,
  hasErrorCode,
  isConflict,
  isNotFound,
  isPreconditionFailed,
  isTimedOut,
  patchAnnotationVersioned,
  setAnnotationVersioned,
  StorytellerError,
  type Annotation,
  type AnnotationPatchBody,
  type JsonPatchOperation,
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

/** A transport that answers only its signal: it rejects with the abort reason, as fetch does. */
function hangingFetch() {
  const calls: Call[] = []
  const fetch = vi.fn(
    (url: RequestInfo | URL, init?: RequestInit) =>
      new Promise<Response>((_, reject) => {
        calls.push({ url: String(url), init: init ?? {} })
        const signal = init?.signal

        if (signal?.aborted) {
          reject(signal.reason as Error)
          return
        }

        signal?.addEventListener('abort', () => reject(signal.reason as Error), { once: true })
      }),
  )

  return { fetch: fetch as unknown as typeof globalThis.fetch, calls }
}

const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8', ...headers } })

function configure(overrides: Partial<StorytellerConfig> & Pick<StorytellerConfig, 'fetch'>) {
  configureStoryteller({ baseUrl: 'https://api.example.com/api/', ...overrides })
}

const sentHeaders = (call: Call | undefined) => new Headers(call?.init.headers)

const url = 'https://api.example.com/api/v1/acme/billing/default/annotations/sbt.customer'
const tag = '"a1.AAAAAAAAAAAAAAAAAAAAAA"'
const newTag = '"a1.BBBBBBBBBBBBBBBBBBBBBB"'
const annotation: Annotation = {
  ProjectName: 'billing',
  ViewName: 'default',
  AnnotationKey: 'sbt.customer',
  Name: 'customer',
  AnnotationType: 'Subject',
  Title: 'Customer',
  Labels: ['core'],
  Values: { plan: 'pro' },
}

const failureOf = (write: Promise<unknown>) =>
  write.then(
    () => {
      throw new Error('The request did not fail.')
    },
    (caught: unknown) => caught as StorytellerError,
  )

afterEach(() => {
  vi.useRealTimers()
  configureStoryteller({ baseUrl: 'http://unused' })
})

describe('versioned annotation reads', () => {
  it('reads the annotation with its ETag, by GET of the generated URL', async () => {
    const { fetch, calls } = fakeFetch(json(annotation, 200, { etag: tag }))
    configure({ fetch })

    const result = await getAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer')

    expect(result).toEqual({ value: annotation, etag: tag })
    expect(calls[0]?.url).toBe(url)
    expect(calls[0]?.init.method).toBe('GET')
    expect(calls[0]?.init.body).toBeUndefined()
    const sent = sentHeaders(calls[0])
    expect(sent.has('Content-Type')).toBe(false)
    expect(sent.has('If-Match')).toBe(false)
    expect(sent.has('If-None-Match')).toBe(false)
  })

  it('sends includeInactive as the generated getAnnotation does, and never to fetch', async () => {
    const { fetch, calls } = fakeFetch(json(annotation), json(annotation), json(annotation))
    configure({ fetch })

    await getAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', { includeInactive: true })
    await getAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', { includeInactive: false })
    // an encoded key, and no query while undefined
    await getAnnotationVersioned('a b', 'billing', 'default', 'sbt.a+b?c#d', { includeInactive: undefined })

    expect(calls.map((call) => call.url)).toEqual([
      `${url}?includeInactive=true`,
      `${url}?includeInactive=false`,
      'https://api.example.com/api/v1/a%20b/billing/default/annotations/sbt.a%2Bb%3Fc%23d',
    ])
    expect(sdk.getGetAnnotationUrl('acme', 'billing', 'default', 'sbt.customer', { includeInactive: true })).toBe(
      '/v1/acme/billing/default/annotations/sbt.customer?includeInactive=true',
    )
    for (const call of calls) {
      expect(call.init).not.toHaveProperty('includeInactive')
    }
  })

  it('answers without an etag when the header is missing, empty or not readable', async () => {
    const { fetch } = fakeFetch(json(annotation), json(annotation, 200, { etag: '' }))
    configure({ fetch })

    const missing = await getAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer')
    const empty = await getAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer')

    expect(missing).toEqual({ value: annotation })
    expect(missing.etag).toBeUndefined()
    expect(empty.etag).toBeUndefined()
  })

  it('forwards the signal and the other request options', async () => {
    const { fetch, calls } = fakeFetch(json(annotation, 200, { etag: tag }))
    configure({ fetch })
    const controller = new AbortController()

    await getAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', { signal: controller.signal, cache: 'no-store' })

    expect(calls[0]?.init.signal).toBe(controller.signal)
    expect(calls[0]?.init.cache).toBe('no-store')
  })
})

describe('versioned annotation writes', () => {
  it('posts the annotation as JSON and returns the stored one with the new ETag', async () => {
    const stored = { ...annotation, Title: 'Customers' }
    const { fetch, calls } = fakeFetch(json(stored, 200, { etag: newTag }))
    configure({ fetch })

    const result = await setAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', { ...annotation, Title: 'Customers' }, { ifMatch: tag })

    expect(result).toEqual({ value: stored, etag: newTag })
    expect(calls[0]?.url).toBe(url)
    expect(calls[0]?.init.method).toBe('POST')
    expect(JSON.parse(String(calls[0]?.init.body))).toEqual(stored)
    const sent = sentHeaders(calls[0])
    expect(sent.get('Content-Type')).toBe('application/json')
    expect(sent.get('If-Match')).toBe(tag)
    expect(sent.has('If-None-Match')).toBe(false)
    expect(calls[0]?.init).not.toHaveProperty('ifMatch')
    expect(calls[0]?.init).not.toHaveProperty('ifNoneMatch')
  })

  it('creates only with ifNoneMatch *, and sends no condition without one', async () => {
    const { fetch, calls } = fakeFetch(json(annotation, 200, { etag: tag }), json(annotation, 200, { etag: tag }))
    configure({ fetch })

    await setAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', annotation, { ifNoneMatch: '*' })
    await setAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', annotation)

    expect(sentHeaders(calls[0]).get('If-None-Match')).toBe('*')
    expect(sentHeaders(calls[0]).has('If-Match')).toBe(false)
    expect(sentHeaders(calls[1]).has('If-None-Match')).toBe(false)
    expect(sentHeaders(calls[1]).has('If-Match')).toBe(false)
  })

  it('sends ifMatch and ifNoneMatch over the headers of the same name, and an empty one removes them', async () => {
    const responses = Array.from({ length: 6 }, () => json(annotation, 200, { etag: newTag }))
    const { fetch, calls } = fakeFetch(...responses)
    configure({ fetch })
    const stale = '"a1.old"'
    // a plain object, a Headers instance and tuples, the last two with the names in another case
    const variants: HeadersInit[] = [
      { 'If-Match': stale, 'If-None-Match': stale, Accept: 'application/json' },
      new Headers({ 'if-match': stale, 'if-none-match': stale, Accept: 'application/json' }),
      [
        ['IF-MATCH', stale],
        ['IF-NONE-MATCH', stale],
        ['Accept', 'application/json'],
      ],
    ]

    for (const headers of variants) {
      await setAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', annotation, { headers, ifMatch: tag, ifNoneMatch: '*' })
    }
    // without the options, the headers set by the caller go out as they are
    await setAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', annotation, { headers: variants[0], ifMatch: undefined })
    // an empty tag sends nothing, and removes a header set by the caller
    await setAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', annotation, { headers: variants[0], ifMatch: '', ifNoneMatch: '' })
    await setAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', annotation, { ifMatch: '', ifNoneMatch: '' })

    for (const call of calls.slice(0, variants.length)) {
      const sent = sentHeaders(call)
      // a stale tag next to the new one would read back as both, joined by a comma
      expect(sent.get('If-Match')).toBe(tag)
      expect(sent.get('If-None-Match')).toBe('*')
      expect(sent.get('Accept')).toBe('application/json')
    }
    expect(sentHeaders(calls[3]).get('If-Match')).toBe(stale)
    expect(sentHeaders(calls[3]).get('If-None-Match')).toBe(stale)
    for (const call of calls.slice(4)) {
      expect(sentHeaders(call).has('If-Match')).toBe(false)
      expect(sentHeaders(call).has('If-None-Match')).toBe(false)
    }
    expect(sentHeaders(calls[4]).get('Accept')).toBe('application/json')
  })

  it('sends its own Content-Type over one of the caller', async () => {
    const { fetch, calls } = fakeFetch(json(annotation), json(annotation))
    configure({ fetch })

    await setAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', annotation, { headers: { 'content-type': 'text/plain' } })
    await patchAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', { Title: 'Customers' }, { headers: { 'Content-Type': 'application/json' } })

    expect(sentHeaders(calls[0]).get('Content-Type')).toBe('application/json')
    expect(sentHeaders(calls[1]).get('Content-Type')).toBe('application/merge-patch+json')
  })
})

describe('versioned annotation patches', () => {
  it('sends an object as a merge patch with its explicit nulls', async () => {
    const stored = { ...annotation, Values: { plan: 'enterprise' } }
    const { fetch, calls } = fakeFetch(json(stored, 200, { etag: newTag }))
    configure({ fetch })

    const result = await patchAnnotationVersioned(
      'acme',
      'billing',
      'default',
      'sbt.customer',
      // null clears or removes; an undefined member is left out, so it keeps its value
      { Description: null, Title: undefined, Values: { plan: 'enterprise', old: null } },
      { ifMatch: tag },
    )

    expect(result).toEqual({ value: stored, etag: newTag })
    expect(calls[0]?.url).toBe(url)
    expect(calls[0]?.init.method).toBe('PATCH')
    expect(calls[0]?.init.body).toBe('{"Description":null,"Values":{"plan":"enterprise","old":null}}')
    const sent = sentHeaders(calls[0])
    expect(sent.get('Content-Type')).toBe('application/merge-patch+json')
    expect(sent.get('If-Match')).toBe(tag)
    expect(sent.has('If-None-Match')).toBe(false)
    expect(calls[0]?.init).not.toHaveProperty('ifMatch')
  })

  it('sends a plain object of any member names as a merge patch', async () => {
    const { fetch, calls } = fakeFetch(json(annotation))
    configure({ fetch })
    // the API matches member names case-insensitively
    const patch: AnnotationPatchBody = { title: 'Customers', labels: null }

    await patchAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', patch)

    expect(calls[0]?.init.body).toBe('{"title":"Customers","labels":null}')
    expect(sentHeaders(calls[0]).get('Content-Type')).toBe('application/merge-patch+json')
    expect(sentHeaders(calls[0]).has('If-Match')).toBe(false)
  })

  it('sends an array as a JSON Patch', async () => {
    const { fetch, calls } = fakeFetch(json(annotation, 200, { etag: newTag }))
    configure({ fetch })
    const operations: JsonPatchOperation[] = [
      { op: 'test', path: '/Title', value: 'Customer' },
      { op: 'add', path: '/Labels/-', value: 'billing' },
      { op: 'add', path: '/Values/plan', value: null },
      { op: 'remove', path: '/Description' },
    ]

    const result = await patchAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', operations)

    expect(result.etag).toBe(newTag)
    expect(calls[0]?.init.method).toBe('PATCH')
    expect(calls[0]?.init.body).toBe(JSON.stringify(operations))
    expect(JSON.parse(String(calls[0]?.init.body))).toEqual(operations)
    expect(sentHeaders(calls[0]).get('Content-Type')).toBe('application/json-patch+json')
  })

  it('sends ifMatch over an If-Match of the headers, and an empty one removes it', async () => {
    const { fetch, calls } = fakeFetch(json(annotation), json(annotation), json(annotation))
    configure({ fetch })
    const headers = { 'if-match': '"a1.old"' }

    await patchAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', [], { headers, ifMatch: tag })
    await patchAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', [], { headers })
    await patchAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', [], { headers, ifMatch: '' })

    expect(calls.map((call) => sentHeaders(call).get('If-Match'))).toEqual([tag, '"a1.old"', null])
  })

  it('rejects a patch that is neither an array nor an object before any request', async () => {
    const { fetch } = fakeFetch()
    configure({ fetch })

    for (const patch of [null, 'Title', 5]) {
      const error = await patchAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', patch as unknown as AnnotationPatchBody).catch(
        (caught: unknown) => caught,
      )
      expect(error).toBeInstanceOf(TypeError)
    }
    expect(fetch).not.toHaveBeenCalled()
  })
})

describe('versioned annotation errors', () => {
  it('maps a missing and a deleted key of a patch to 404 with its errorCode', async () => {
    const { fetch } = fakeFetch(
      json({ Message: "The annotation 'sbt.customer' does not exist.", Hint: 'POST it to create it.', ErrorCode: 'AnnotationNotFound' }, 404),
      json({ Message: "The annotation 'sbt.customer' is deleted.", Hint: 'Restore it first.', ErrorCode: 'AnnotationDeleted' }, 404),
    )
    configure({ fetch })

    const missing = await failureOf(patchAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', { Title: 'x' }))
    const deleted = await failureOf(patchAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', { Title: 'x' }))

    expect(missing).toBeInstanceOf(StorytellerError)
    expect(isNotFound(missing)).toBe(true)
    expect(hasErrorCode(missing, 'AnnotationNotFound')).toBe(true)
    expect(missing).toMatchObject({ message: "The annotation 'sbt.customer' does not exist.", hint: 'POST it to create it.' })
    expect(hasErrorCode(deleted, 'AnnotationDeleted')).toBe(true)
  })

  it('maps a failed condition to 412 with its errorCode', async () => {
    const { fetch } = fakeFetch(
      json({ Message: 'The annotation changed since it was read.', ErrorCode: 'AnnotationPreconditionFailed' }, 412),
      json({ Message: 'The annotation already exists.', ErrorCode: 'AnnotationPreconditionFailed' }, 412),
      json({ Message: 'The test operation failed.', ErrorCode: 'PatchTestFailed' }, 412),
    )
    configure({ fetch })

    const changed = await failureOf(setAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', annotation, { ifMatch: tag }))
    const exists = await failureOf(setAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', annotation, { ifNoneMatch: '*' }))
    const testFailed = await failureOf(patchAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', [{ op: 'test', path: '/Title', value: 'x' }]))

    for (const error of [changed, exists]) {
      expect(isPreconditionFailed(error)).toBe(true)
      expect(hasErrorCode(error, 'AnnotationPreconditionFailed')).toBe(true)
    }
    expect(changed.message).toBe('The annotation changed since it was read.')
    expect(isPreconditionFailed(testFailed)).toBe(true)
    expect(hasErrorCode(testFailed, 'PatchTestFailed')).toBe(true)
  })

  it('maps every other status to StorytellerError, as the fetcher does', async () => {
    const { fetch } = fakeFetch(
      json({ Message: 'The annotation is not active.', ErrorCode: 'AnnotationInactive' }, 404),
      json({ Message: 'Changed concurrently three times in a row.' }, 409),
      json({ Message: 'Labels must be strings.', ErrorCode: 'InvalidAnnotation' }, 400),
      new Response('', { status: 500, statusText: 'Internal Server Error' }),
    )
    configure({ fetch })

    const inactive = await failureOf(getAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer'))
    const conflict = await failureOf(setAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', annotation))
    const invalid = await failureOf(patchAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', { Labels: [1] }))
    const failed = await failureOf(getAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer'))

    expect(hasErrorCode(inactive, 'AnnotationInactive')).toBe(true)
    expect(isConflict(conflict)).toBe(true)
    expect(invalid).toMatchObject({ status: 400, errorCode: 'InvalidAnnotation' })
    expect(failed).toMatchObject({ status: 500, message: 'Storyteller API returned 500 Internal Server Error.' })
  })

  it('rejects a 200 whose body is not an annotation object', async () => {
    const { fetch } = fakeFetch(
      new Response(null, { status: 200, headers: { etag: tag } }),
      new Response('not json', { status: 200, headers: { 'content-type': 'text/plain' } }),
      json([annotation]),
    )
    configure({ fetch })

    const empty = await failureOf(getAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer'))
    const textual = await failureOf(setAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', annotation))
    const array = await failureOf(patchAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', { Title: 'x' }))

    for (const error of [empty, textual, array]) {
      expect(error).toBeInstanceOf(StorytellerError)
      expect(error).toMatchObject({ status: 200, message: 'The Storyteller API answered without an annotation object.' })
    }
  })

  it('keeps If-Match, the body and the Content-Type on the retry after a 401', async () => {
    const tokens = ['expired', 'fresh']
    const onUnauthorized = vi.fn()
    const { fetch, calls } = fakeFetch(new Response(null, { status: 401 }), json(annotation, 200, { etag: newTag }))
    configure({ fetch, getAccessToken: () => tokens.shift(), onUnauthorized })

    const result = await patchAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', { Title: null }, { ifMatch: tag })

    expect(result.etag).toBe(newTag)
    expect(calls.map((call) => sentHeaders(call).get('Authorization'))).toEqual(['Bearer expired', 'Bearer fresh'])
    expect(calls.map((call) => sentHeaders(call).get('If-Match'))).toEqual([tag, tag])
    expect(calls.map((call) => sentHeaders(call).get('Content-Type'))).toEqual(['application/merge-patch+json', 'application/merge-patch+json'])
    expect(calls.map((call) => call.init.body)).toEqual(['{"Title":null}', '{"Title":null}'])
    expect(onUnauthorized).not.toHaveBeenCalled()
  })

  it('ends a write after its timeoutMs with the hint that the API may still apply it', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    const hanging = hangingFetch()
    configure({ fetch: hanging.fetch })

    const unanswered = patchAnnotationVersioned('acme', 'billing', 'default', 'sbt.customer', { Title: 'x' }, { ifMatch: tag, timeoutMs: 1000 }).catch(
      (caught: unknown) => caught,
    )
    await vi.advanceTimersByTimeAsync(1000)
    const error = await unanswered

    expect(isTimedOut(error)).toBe(true)
    expect(error).toMatchObject({ status: 0, hint: 'The API may still apply the change: read it again before you repeat it.' })
    expect(hanging.calls[0]?.init).not.toHaveProperty('timeoutMs')
  })

  it('exports the helpers from the package root, but not the internal request', () => {
    expect(sdk.getAnnotationVersioned).toBeTypeOf('function')
    expect(sdk.setAnnotationVersioned).toBeTypeOf('function')
    expect(sdk.patchAnnotationVersioned).toBeTypeOf('function')
    expect(sdk.setAnnotation).toBeTypeOf('function')
    expect(sdk.patchAnnotation).toBeTypeOf('function')
    expect(sdk).not.toHaveProperty('runRequest')
    expect(sdk).not.toHaveProperty('readBody')
    expect(sdk).not.toHaveProperty('sendVersioned')
    expect(sdk).not.toHaveProperty('readAnnotation')
  })
})
