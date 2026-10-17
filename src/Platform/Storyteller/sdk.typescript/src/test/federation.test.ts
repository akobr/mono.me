import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  configureStoryteller,
  createFederatedTokenProvider,
  exchangeFederatedToken,
  FederatedSubjectTokenType,
  getAccount,
  hasErrorCode,
  StorytellerError,
} from '../index'

const baseUrl = 'https://api.example.com/api'
const tokenUrl = `${baseUrl}/v1/:organization/:project/access/token`
const machineId = '0f8fad5b-d9cb-469f-a165-70867728950e'

interface TokenRequest {
  organization: string
  project: string
  pathname: string
  contentType: string | null
  authorization: string | null
  form: Record<string, string[]>
}

const server = setupServer()
let requests: TokenRequest[]

// Answers every exchange with a token named after the subject token, valid for expiresIn seconds.
function serveTokens(expiresIn = 900) {
  server.use(
    http.post(tokenUrl, async ({ request, params }) => {
      const form = new URLSearchParams(await request.text())
      const fields: Record<string, string[]> = {}
      for (const name of new Set(form.keys())) {
        fields[name] = form.getAll(name)
      }

      requests.push({
        organization: String(params.organization),
        project: String(params.project),
        pathname: new URL(request.url).pathname,
        contentType: request.headers.get('content-type'),
        authorization: request.headers.get('authorization'),
        form: fields,
      })

      return HttpResponse.json(
        {
          access_token: `st-${form.get('subject_token')}`,
          issued_token_type: 'urn:ietf:params:oauth:token-type:access_token',
          token_type: 'Bearer',
          expires_in: expiresIn,
          scope: form.get('scope') ?? 'Default.Read Annotation.Read',
        },
        { headers: { 'Cache-Control': 'no-store' } },
      )
    }),
  )
}

// The StorytellerError a promise rejects with; fails the test when it resolves or rejects with anything else.
async function rejection(promise: Promise<unknown>): Promise<StorytellerError> {
  try {
    await promise
  } catch (error) {
    if (error instanceof StorytellerError) {
      return error
    }

    throw error
  }

  throw new Error('Expected a StorytellerError.')
}

// Subject tokens subject-1, subject-2, … of one fake platform.
function subjectTokens() {
  let count = 0
  return vi.fn(async () => `subject-${++count}`)
}

const oauthError = (status: number, error: string, description?: string) =>
  http.post(tokenUrl, () => HttpResponse.json({ error, error_description: description }, { status }))

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => {
  requests = []
  // A configured fetcher with a user token must not leak into the exchange.
  configureStoryteller({ baseUrl, getAccessToken: () => 'user-token' })
})
afterEach(() => {
  server.resetHandlers()
  vi.useRealTimers()
})
afterAll(() => server.close())

describe('exchangeFederatedToken', () => {
  it('posts the RFC 8693 form without an Authorization header and returns the token with its expiry', async () => {
    serveTokens(900)
    const before = Date.now()

    const token = await exchangeFederatedToken({ baseUrl, organization: 'acme', project: 'billing', machineId, subjectToken: 'gh-1' })

    expect(token.accessToken).toBe('st-gh-1')
    expect(token.scope).toBe('Default.Read Annotation.Read')
    expect(token.expiresAt).toBeGreaterThanOrEqual(before + 900_000)
    expect(token.expiresAt).toBeLessThanOrEqual(Date.now() + 900_000)
    expect(requests).toHaveLength(1)
    expect(requests[0]).toEqual({
      organization: 'acme',
      project: 'billing',
      pathname: '/api/v1/acme/billing/access/token',
      contentType: expect.stringMatching(/^application\/x-www-form-urlencoded/),
      authorization: null,
      form: {
        grant_type: ['urn:ietf:params:oauth:grant-type:token-exchange'],
        subject_token: ['gh-1'],
        subject_token_type: ['urn:ietf:params:oauth:token-type:jwt'],
        client_id: [machineId],
      },
    })
  })

  it('encodes the path segments, trims the base URL and sends the token type and scope when given', async () => {
    serveTokens()

    const token = await exchangeFederatedToken({
      baseUrl: `${baseUrl}/`,
      organization: 'acme org',
      project: 'bill/ing',
      machineId,
      subjectToken: 'mi-1',
      subjectTokenType: FederatedSubjectTokenType.AccessToken,
      scope: 'Default.Read',
    })

    expect(token.scope).toBe('Default.Read')
    expect(requests[0]?.pathname).toBe('/api/v1/acme%20org/bill%2Fing/access/token')
    expect(requests[0]?.organization).toBe('acme org')
    expect(requests[0]?.project).toBe('bill/ing')
    expect(requests[0]?.form.subject_token_type).toEqual(['urn:ietf:params:oauth:token-type:access_token'])
    expect(requests[0]?.form.scope).toEqual(['Default.Read'])
  })

  it('turns an OAuth error body into a StorytellerError carrying the error code', async () => {
    server.use(oauthError(400, 'invalid_request', 'The subject token is not accepted for this machine.'))

    const error = await rejection(exchangeFederatedToken({ baseUrl, organization: 'acme', project: 'billing', machineId, subjectToken: 'gh-1' }))

    expect(error).toMatchObject({
      status: 400,
      errorCode: 'invalid_request',
      message: 'The subject token is not accepted for this machine.',
      body: { error: 'invalid_request', error_description: 'The subject token is not accepted for this machine.' },
    })
    expect(hasErrorCode(error, 'invalid_request')).toBe(true)
  })

  it('keeps the 503 status and the code of temporarily_unavailable, and a code without a description', async () => {
    server.use(oauthError(503, 'temporarily_unavailable', 'The keys of the trusted issuer could not be retrieved.'))
    await expect(exchangeFederatedToken({ baseUrl, organization: 'acme', project: 'billing', machineId, subjectToken: 't' }))
      .rejects.toMatchObject({ status: 503, errorCode: 'temporarily_unavailable' })

    server.use(oauthError(400, 'invalid_scope'))
    await expect(exchangeFederatedToken({ baseUrl, organization: 'acme', project: 'billing', machineId, subjectToken: 't', scope: 'Secrets' }))
      .rejects.toMatchObject({ status: 400, errorCode: 'invalid_scope', message: 'Storyteller API returned 400 Bad Request.' })
  })

  it('reports a network failure with status 0 and without the subject token', async () => {
    server.use(http.post(tokenUrl, () => HttpResponse.error()))

    const error = await rejection(exchangeFederatedToken({ baseUrl, organization: 'acme', project: 'billing', machineId, subjectToken: 'secret-subject' }))

    expect(error.status).toBe(0)
    expect(error.message).toBe('The Storyteller token endpoint is not reachable (/api/v1/acme/billing/access/token).')
    expect(error.message).not.toContain('secret-subject')
  })

  it('refuses a success body without a token or an expiry, without echoing the body', async () => {
    server.use(http.post(tokenUrl, () => HttpResponse.json({ access_token: 'st-leak', token_type: 'Bearer' })))

    const error = await rejection(exchangeFederatedToken({ baseUrl, organization: 'acme', project: 'billing', machineId, subjectToken: 't' }))

    expect(error.status).toBe(200)
    expect(error.body).toBeUndefined()
    expect(error.message).not.toContain('st-leak')
  })

  // redirect: 'error' keeps the subject token from following a redirect to another host.
  it('uses the given fetch and refuses redirects', async () => {
    const fetch = vi.fn(async () => HttpResponse.json({ access_token: 'st-x', token_type: 'Bearer', expires_in: 60, scope: 'Default.Read' }))

    const token = await exchangeFederatedToken({
      baseUrl,
      organization: 'acme',
      project: 'billing',
      machineId,
      subjectToken: 't',
      fetch: fetch as unknown as typeof globalThis.fetch,
    })

    expect(token.accessToken).toBe('st-x')
    expect(fetch).toHaveBeenCalledWith('https://api.example.com/api/v1/acme/billing/access/token', expect.objectContaining({ method: 'POST', redirect: 'error' }))
  })
})

describe('createFederatedTokenProvider', () => {
  const options = { baseUrl, organization: 'acme', project: 'billing', machineId }

  it('caches the Storyteller token until 60 s before its expiry', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-10-17T10:00:00Z'))
    serveTokens(900)
    const getSubjectToken = subjectTokens()
    const getAccessToken = createFederatedTokenProvider({ ...options, getSubjectToken })

    await expect(getAccessToken()).resolves.toBe('st-subject-1')
    vi.setSystemTime(new Date('2026-10-17T10:13:59Z'))
    await expect(getAccessToken()).resolves.toBe('st-subject-1')
    expect(getSubjectToken).toHaveBeenCalledTimes(1)

    vi.setSystemTime(new Date('2026-10-17T10:14:00Z'))
    await expect(getAccessToken()).resolves.toBe('st-subject-2')
    expect(getSubjectToken).toHaveBeenCalledTimes(2)
    expect(requests.map((request) => request.form.subject_token)).toEqual([['subject-1'], ['subject-2']])
  })

  it('shares one exchange between concurrent callers', async () => {
    serveTokens()
    const getSubjectToken = subjectTokens()
    const getAccessToken = createFederatedTokenProvider({ ...options, getSubjectToken })

    const tokens = await Promise.all([getAccessToken(), getAccessToken(), getAccessToken({ forceRefresh: false })])

    expect(tokens).toEqual(['st-subject-1', 'st-subject-1', 'st-subject-1'])
    expect(getSubjectToken).toHaveBeenCalledTimes(1)
    expect(requests).toHaveLength(1)
  })

  it('exchanges again on forceRefresh', async () => {
    serveTokens()
    const getAccessToken = createFederatedTokenProvider({ ...options, getSubjectToken: subjectTokens() })

    await expect(getAccessToken()).resolves.toBe('st-subject-1')
    await expect(getAccessToken({ forceRefresh: true })).resolves.toBe('st-subject-2')
    await expect(getAccessToken()).resolves.toBe('st-subject-2')
    expect(requests).toHaveLength(2)
  })

  it('does not cache a failure, whether of the exchange or of getSubjectToken', async () => {
    server.use(oauthError(503, 'temporarily_unavailable'))
    let fail = true
    const getAccessToken = createFederatedTokenProvider({
      ...options,
      getSubjectToken: () => {
        if (fail) {
          fail = false
          throw new Error('The projected token file is missing.')
        }

        return 'subject'
      },
    })

    await expect(getAccessToken()).rejects.toThrow('The projected token file is missing.')
    await expect(getAccessToken()).rejects.toMatchObject({ status: 503, errorCode: 'temporarily_unavailable' })

    server.resetHandlers()
    serveTokens()
    await expect(getAccessToken()).resolves.toBe('st-subject')
  })

  it('returns the cached token until it expires when an exchange within 60 s of its expiry fails', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-10-17T10:00:00Z'))
    serveTokens(900)
    const getSubjectToken = subjectTokens()
    const getAccessToken = createFederatedTokenProvider({ ...options, getSubjectToken })
    await expect(getAccessToken()).resolves.toBe('st-subject-1')

    // The keys of the trusted issuer cannot be fetched for a while.
    server.use(oauthError(503, 'temporarily_unavailable'))
    vi.setSystemTime(new Date('2026-10-17T10:14:30Z'))
    await expect(getAccessToken()).resolves.toBe('st-subject-1')

    // Every call tries again; concurrent callers share the attempt.
    vi.setSystemTime(new Date('2026-10-17T10:14:59.999Z'))
    await expect(Promise.all([getAccessToken(), getAccessToken()])).resolves.toEqual(['st-subject-1', 'st-subject-1'])
    expect(getSubjectToken).toHaveBeenCalledTimes(3)

    vi.setSystemTime(new Date('2026-10-17T10:15:00Z'))
    await expect(getAccessToken()).rejects.toMatchObject({ status: 503, errorCode: 'temporarily_unavailable' })

    server.resetHandlers()
    serveTokens(900)
    await expect(getAccessToken()).resolves.toBe('st-subject-5')
  })

  it('returns the cached token within 60 s of its expiry when getSubjectToken throws or the endpoint is not reachable', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-10-17T10:00:00Z'))
    serveTokens(900)
    let subject: string | Error = 'k8s-1'
    const getAccessToken = createFederatedTokenProvider({
      ...options,
      getSubjectToken: () => {
        if (subject instanceof Error) {
          throw subject
        }

        return subject
      },
    })
    await expect(getAccessToken()).resolves.toBe('st-k8s-1')

    vi.setSystemTime(new Date('2026-10-17T10:14:10Z'))
    subject = new Error('The projected token file is being rotated.')
    await expect(getAccessToken()).resolves.toBe('st-k8s-1')

    subject = 'k8s-2'
    server.use(http.post(tokenUrl, () => HttpResponse.error()))
    await expect(getAccessToken()).resolves.toBe('st-k8s-1')

    server.resetHandlers()
    serveTokens(900)
    await expect(getAccessToken()).resolves.toBe('st-k8s-2')
    expect(requests.map((request) => request.form.subject_token)).toEqual([['k8s-1'], ['k8s-2']])
  })

  it('rejects a failed forceRefresh even while the dropped token has not expired', async () => {
    serveTokens(900)
    const getAccessToken = createFederatedTokenProvider({ ...options, getSubjectToken: subjectTokens() })
    await expect(getAccessToken()).resolves.toBe('st-subject-1')

    server.use(oauthError(503, 'temporarily_unavailable'))
    await expect(getAccessToken({ forceRefresh: true })).rejects.toMatchObject({ status: 503, errorCode: 'temporarily_unavailable' })
    // The dropped token does not come back either.
    await expect(getAccessToken()).rejects.toMatchObject({ status: 503, errorCode: 'temporarily_unavailable' })
  })

  it('lets the fetcher call with the cached token when the early exchange fails, and fails the retry after a 401', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-10-17T10:00:00Z'))
    serveTokens(900)
    const authorizations: (string | null)[] = []
    let status = 200
    server.use(
      http.get(`${baseUrl}/v1/access/account`, ({ request }) => {
        authorizations.push(request.headers.get('authorization'))
        return status === 200 ? HttpResponse.json({ Id: machineId }) : new HttpResponse(null, { status })
      }),
    )
    configureStoryteller({ baseUrl, getAccessToken: createFederatedTokenProvider({ ...options, getSubjectToken: subjectTokens() }) })
    await expect(getAccount()).resolves.toMatchObject({ Id: machineId })

    server.use(oauthError(503, 'temporarily_unavailable'))
    vi.setSystemTime(new Date('2026-10-17T10:14:30Z'))
    await expect(getAccount()).resolves.toMatchObject({ Id: machineId })

    // The API refuses the cached token: the fetcher's retry forces an exchange, and its failure fails the call.
    status = 401
    await expect(getAccount()).rejects.toMatchObject({ status: 503, errorCode: 'temporarily_unavailable' })
    expect(authorizations).toEqual(['Bearer st-subject-1', 'Bearer st-subject-1', 'Bearer st-subject-1'])
  })

  it('works as getAccessToken of the fetcher: the API call carries the Storyteller token, the exchange none', async () => {
    serveTokens()
    let authorization: string | null = null
    server.use(
      http.get(`${baseUrl}/v1/access/account`, ({ request }) => {
        authorization = request.headers.get('authorization')
        return HttpResponse.json({ Id: machineId })
      }),
    )
    configureStoryteller({ baseUrl, getAccessToken: createFederatedTokenProvider({ ...options, getSubjectToken: () => 'k8s' }) })

    await expect(getAccount()).resolves.toMatchObject({ Id: machineId })

    expect(authorization).toBe('Bearer st-k8s')
    expect(requests).toHaveLength(1)
    expect(requests[0]?.authorization).toBeNull()
  })
})

describe('StorytellerError.fromResponse', () => {
  it('prefers ErrorResponse fields over OAuth ones', () => {
    const error = StorytellerError.fromResponse(400, 'Bad Request', { Message: 'Invalid.', ErrorCode: 'InvalidFederation', error: 'x' })

    expect(error.message).toBe('Invalid.')
    expect(error.errorCode).toBe('InvalidFederation')
  })
})
