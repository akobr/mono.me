import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import {
  acceptInvitation,
  configureStoryteller,
  getAccount,
  getAuthConfiguration,
  getConfigurations,
  getConfigurationSchemas,
  getMembers,
  getTemplates,
  getViews,
  setMemberRole,
  StorytellerError,
} from '../index'
import { getGetMembersMockHandler, getStorytellerMock } from '../msw'
import {
  AcceptInvitationResponse,
  GetAccountResponse,
  GetAuthConfigurationResponse,
  GetConfigurationSchemasResponse,
  GetConfigurationsResponse,
  GetMembersResponse,
  GetTemplatesResponse,
  GetViewsResponse,
  SetMemberRoleBody,
} from '../zod'

// The generated MSW handlers answer every operation with Faker data, and the generated Zod schemas
// accept those answers: the three outputs agree with each other and with the fetcher.
const server = setupServer(...getStorytellerMock())

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => {
  server.resetHandlers()
  configureStoryteller({ baseUrl: 'http://localhost:7071/api', getAccessToken: () => 'token' })
})
afterAll(() => server.close())

describe('generated client against generated mocks', () => {
  it.each([
    ['getAccount', () => getAccount(), GetAccountResponse],
    ['getAuthConfiguration', () => getAuthConfiguration(), GetAuthConfigurationResponse],
    ['getMembers', () => getMembers('acme.billing'), GetMembersResponse],
    ['getViews', () => getViews('acme', 'billing'), GetViewsResponse],
    ['getConfigurations', () => getConfigurations('acme', 'billing', 'default'), GetConfigurationsResponse],
    ['getConfigurationSchemas', () => getConfigurationSchemas('acme', 'billing', 'default'), GetConfigurationSchemasResponse],
    ['getTemplates', () => getTemplates('acme', 'billing', 'default'), GetTemplatesResponse],
    ['acceptInvitation', () => acceptInvitation('0192f0aa'), AcceptInvitationResponse],
  ] as const)('%s returns data that passes its Zod schema', async (_name, call, schema) => {
    const response = await call()

    expect(schema.safeParse(response).success).toBe(true)
  })

  it('sends bodies as JSON that pass the Zod body schema', async () => {
    let received: unknown
    server.use(
      http.put('*/api/v1/access/points/:key/members/:accountId', async ({ request, params }) => {
        received = { body: await request.json(), params, authorization: request.headers.get('authorization') }
        return HttpResponse.json({ AccountId: params.accountId, Role: 'Reader' })
      }),
    )

    const member = await setMemberRole('acme.billing', 'user_02', { Role: 'Reader' })

    expect(member).toEqual({ AccountId: 'user_02', Role: 'Reader' })
    // The leading "*" of the handler URL adds a positional parameter "0" next to the named ones.
    expect(received).toMatchObject({ body: { Role: 'Reader' }, params: { key: 'acme.billing', accountId: 'user_02' }, authorization: 'Bearer token' })
    expect(SetMemberRoleBody.safeParse({ Role: 'Reader' }).success).toBe(true)
  })

  it('overrides a single operation and surfaces API errors as StorytellerError', async () => {
    server.use(
      http.get('*/api/v1/access/points/:key/members', () =>
        HttpResponse.json({ Message: 'No Administrator access.', ErrorCode: 'AccessDenied' }, { status: 403 })),
    )

    const error = await getMembers('acme.billing').catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(StorytellerError)
    expect(error).toMatchObject({ status: 403, errorCode: 'AccessDenied' })
  })

  it('accepts typed overrides through the generated handler factories', async () => {
    server.use(getGetMembersMockHandler([{ AccountId: 'user_01', Name: 'Ada', Role: 'Owner' }]))

    await expect(getMembers('acme')).resolves.toEqual([{ AccountId: 'user_01', Name: 'Ada', Role: 'Owner' }])
  })
})
