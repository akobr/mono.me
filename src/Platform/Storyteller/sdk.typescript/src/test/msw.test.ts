import { readdirSync } from 'node:fs'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import {
  acceptInvitation,
  configureStoryteller,
  getAccount,
  getAuthConfiguration,
  getConfigurationComposition,
  getConfigurations,
  getConfigurationSchemas,
  getKeyValuePolicy,
  getKeyValues,
  getMembers,
  getTemplates,
  getViewExport,
  getViewExportArchive,
  getViewImport,
  getViews,
  setMemberRole,
  startViewExport,
  startViewImport,
  StorytellerError,
} from '../index'
import * as mswEntry from '../msw'
import { getGetMembersMockHandler, getStorytellerMock } from '../msw'
import {
  AcceptInvitationResponse,
  GetAccountResponse,
  GetAuthConfigurationResponse,
  GetConfigurationCompositionResponse,
  GetConfigurationSchemasResponse,
  GetConfigurationsResponse,
  GetKeyValuePolicyResponse,
  GetKeyValuesResponse,
  GetMembersResponse,
  GetTemplatesResponse,
  GetViewExportResponse,
  GetViewImportResponse,
  GetViewsResponse,
  SetMemberRoleBody,
  StartViewExportResponse,
  StartViewImportResponse,
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
    ['getConfigurationComposition', () => getConfigurationComposition('acme', 'billing', 'default', 'exe.a.b.c'), GetConfigurationCompositionResponse],
    ['getConfigurationSchemas', () => getConfigurationSchemas('acme', 'billing', 'default'), GetConfigurationSchemasResponse],
    ['getTemplates', () => getTemplates('acme', 'billing', 'default'), GetTemplatesResponse],
    ['getKeyValues', () => getKeyValues('acme', 'billing', 'default'), GetKeyValuesResponse],
    ['getKeyValuePolicy', () => getKeyValuePolicy('acme', 'billing'), GetKeyValuePolicyResponse],
    ['acceptInvitation', () => acceptInvitation('0192f0aa'), AcceptInvitationResponse],
    ['startViewExport', () => startViewExport('acme', 'billing', 'default', { Format: 'Git', Include: ['Templates'] }), StartViewExportResponse],
    ['getViewExport', () => getViewExport('acme', 'billing', 'default', 'job_01'), GetViewExportResponse],
    ['startViewImport', () => startViewImport('acme', 'billing', 'default', new Blob(['PK'], { type: 'application/zip' }), { include: ['Templates'], existing: 'Skip' }), StartViewImportResponse],
    ['getViewImport', () => getViewImport('acme', 'billing', 'default', 'job_02'), GetViewImportResponse],
  ] as const)('%s returns data that passes its Zod schema', async (_name, call, schema) => {
    const response = await call()

    expect(schema.safeParse(response).success).toBe(true)
  })

  it('downloads the archive of an export as a Blob', async () => {
    await expect(getViewExportArchive('acme', 'billing', 'default', 'job_01')).resolves.toBeInstanceOf(Blob)
  })

  // msw.ts lists the tags by hand: a tag that is generated but missing there would serve and export nothing.
  it('serves and exports the handlers of every generated tag', () => {
    const served = new Set(getStorytellerMock().map((handler) => handler.info.header))
    const tags = readdirSync(new URL('../generated/vue-query', import.meta.url), { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((entry) => entry.name)

    expect(tags).toContain('transfers')
    for (const tag of tags) {
      const name = `get${tag.split('-').map((part) => part.charAt(0).toUpperCase() + part.slice(1)).join('')}Mock`
      const getTagMock = (mswEntry as Record<string, unknown>)[name]

      expect(getTagMock, name).toBeTypeOf('function')
      const headers = (getTagMock as () => Array<{ info: { header: string } }>)().map((handler) => handler.info.header)
      expect(headers.length, name).toBeGreaterThan(0)
      expect(headers.filter((header) => !served.has(header)), name).toEqual([])
    }
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
