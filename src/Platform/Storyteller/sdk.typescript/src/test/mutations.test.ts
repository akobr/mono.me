import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { QueryClient } from '@tanstack/vue-query'
import { effectScope } from 'vue'
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { configureStoryteller, type JsonPatchOperation } from '../index'
import { getStorytellerMock } from '../msw'
import { getGetConfigurationQueryKey, invalidateByPath, usePatchConfiguration } from '../vue-query'
import { PatchConfigurationBody } from '../zod'

const server = setupServer(...getStorytellerMock())

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => {
  server.resetHandlers()
  configureStoryteller({ baseUrl: 'http://localhost:7071/api', getAccessToken: () => 'token' })
})
afterAll(() => server.close())

describe('mutation composables', () => {
  it('sends a JSON Patch document and invalidates the patched configuration by path', async () => {
    let received: { contentType: string | null; body: unknown; force: string | null } | undefined
    server.use(
      http.patch('*/api/v1/:organization/:project/:view/configuration/:key', async ({ request }) => {
        received = {
          contentType: request.headers.get('content-type'),
          body: await request.json(),
          force: new URL(request.url).searchParams.get('force'),
        }
        return HttpResponse.json({ Key: 'billing.retries', Content: { retries: 5 } })
      }),
    )

    const queryClient = new QueryClient()
    const queryKey = getGetConfigurationQueryKey('acme', 'billing', 'default', 'billing.retries')
    queryClient.setQueryData(queryKey, { Key: 'billing.retries', Content: { retries: 3 } })

    const patch: JsonPatchOperation[] = [
      { op: 'test', path: '/retries', value: 3 },
      { op: 'replace', path: '/retries', value: 5 },
    ]

    const scope = effectScope()
    const mutation = scope.run(() => usePatchConfiguration({
      mutation: { onSuccess: () => invalidateByPath(queryClient, '/v1/acme/billing/default/configuration/billing.retries') },
    }, queryClient))!

    await mutation.mutateAsync({ organization: 'acme', project: 'billing', view: 'default', key: 'billing.retries', data: patch, params: { force: true } })
    scope.stop()

    expect(received).toEqual({ contentType: 'application/json-patch+json', body: patch, force: 'true' })
    expect(queryClient.getQueryState(queryKey)?.isInvalidated).toBe(true)
  })

  it('rejects a failed JSON Patch test with a 412 StorytellerError', async () => {
    server.use(
      http.patch('*/api/v1/:organization/:project/:view/configuration/:key', () =>
        HttpResponse.json({ Message: 'Test operation failed at /retries.', ErrorCode: 'PatchTestFailed' }, { status: 412 })),
    )

    const queryClient = new QueryClient()
    const scope = effectScope()
    const mutation = scope.run(() => usePatchConfiguration(undefined, queryClient))!

    const error = await mutation
      .mutateAsync({ organization: 'acme', project: 'billing', view: 'default', key: 'billing.retries', data: [{ op: 'test', path: '/retries', value: 3 }] })
      .catch((caught: unknown) => caught)
    scope.stop()

    expect(error).toMatchObject({ status: 412, errorCode: 'PatchTestFailed' })
  })

  it('validates JSON Patch documents with the Zod body schema', () => {
    expect(PatchConfigurationBody.safeParse([{ op: 'move', from: '/a', path: '/b' }]).success).toBe(true)
    expect(PatchConfigurationBody.safeParse([{ op: 'rename', path: '/a' }]).success).toBe(false)
    expect(PatchConfigurationBody.safeParse({ op: 'add', path: '/a', value: 1 }).success).toBe(false)
  })
})
