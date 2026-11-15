import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import { mockHandlers } from '@/mock/all-handlers'
import { accountActor } from '@/mock/audit'
import { loadDb, resetDb, saveDb } from '@/mock/db'
import { patchProjectSettings, projectSettingsOf, storeProjectSettings } from '@/mock/project-settings'

// GET, PUT and PATCH v1/{organization}/{project}/settings of the mock API (424-266) answer as the API does (access.md,
// "Project settings"). Ada is the Owner of acme.billing, a Contributor of acme.shipping and a Reader of northwind.portal.

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterAll(() => server.close())
beforeEach(() => {
  resetDb('default')
})

const base = 'http://api.test/api/v1'

interface Answer {
  status: number
  headers: Headers
  body: Record<string, unknown> | undefined
}

async function call(
  method: string,
  path: string,
  options: { body?: unknown; text?: string; headers?: Record<string, string> } = {},
): Promise<Answer> {
  const headers = { ...(options.body !== undefined ? { 'Content-Type': 'application/json' } : {}), ...options.headers }
  const response = await fetch(`${base}/${path}`, {
    method,
    headers,
    body: options.text ?? (options.body !== undefined ? JSON.stringify(options.body) : undefined),
  })
  const text = await response.text()
  return { status: response.status, headers: response.headers, body: text ? JSON.parse(text) : undefined }
}

const settings = (org = 'acme', project = 'billing', headers: Record<string, string> = {}) =>
  call('GET', `${org}/${project}/settings`, { headers })
const put = (body: unknown, headers: Record<string, string> = {}, path = 'acme/billing') =>
  call('PUT', `${path}/settings`, { body, headers })
const patch = (body: unknown, headers: Record<string, string> = {}) =>
  call('PATCH', 'acme/billing/settings', {
    body,
    headers: { 'Content-Type': 'application/merge-patch+json', ...headers },
  })

/** The path and the rule of every problem of a 400 InvalidSettings. */
const rulesOf = (answer: Answer) =>
  ((answer.body?.Details ?? []) as { Path: string; Rule: string }[]).map(({ Path, Rule }) => [Path, Rule])

const auditOf = (key = 'acme.billing') =>
  (loadDb().keyValueAudit?.[key] ?? []).filter((event) => event.Operation === 'ProjectSettingsChange')

const company = {
  Types: { sbt: { Label: 'Company', Plural: 'Companies', Color: 'pink', Icon: 'i-lucide-building-2' } },
}

describe('mock project settings: reads', () => {
  it('answers the defaults of a project that has none, with the ETag of version 0, for every member', async () => {
    const answer = await settings()

    expect(answer.status).toBe(200)
    expect(answer.body).toEqual({ Types: {}, Version: 0 })
    expect(answer.headers.get('ETag')).toBe('"0"')
    expect(answer.headers.get('Cache-Control')).toBe('private, no-cache')
    expect((await settings('northwind', 'portal')).status).toBe(200)
    expect((await settings('acme', 'returns')).body).toMatchObject({ ErrorCode: 'AccessDenied' })
  })

  it('answers 304 for a matching If-None-Match, compared weakly, and ignores a header that is no list of tags', async () => {
    for (const header of ['"0"', 'W/"0"', '*', '"5", "0"']) {
      const answer = await settings('acme', 'billing', { 'If-None-Match': header })
      expect(answer.status, header).toBe(304)
      expect(answer.body).toBeUndefined()
      expect(answer.headers.get('ETag')).toBe('"0"')
    }
    expect((await settings('acme', 'billing', { 'If-None-Match': '"1"' })).status).toBe(200)
    expect((await settings('acme', 'billing', { 'If-None-Match': 'zero' })).status).toBe(200)
  })

  it('lists the settings of every project in the projects and on the access point', async () => {
    await put(company)

    const projects = await call('GET', 'access/projects')
    const listed = (projects.body as unknown as { Key: string; Settings: unknown }[]).find(
      (summary) => summary.Key === 'acme.billing',
    )
    expect(listed?.Settings).toMatchObject({ ...company, Version: 1 })
    expect(
      (projects.body as unknown as { Key: string; Settings: unknown }[]).find(
        (summary) => summary.Key === 'acme.shipping',
      )?.Settings,
    ).toEqual({
      Types: {},
      Version: 0,
    })
    expect((await call('GET', 'access/points/acme.billing')).body).toMatchObject({ Settings: { Version: 1 } })
    expect((await call('GET', 'access/points/acme')).body).not.toHaveProperty('Settings')
  })
})

describe('mock project settings: PUT', () => {
  it('stores the settings normalized, raises the version, stamps them and records the change', async () => {
    const answer = await put({
      types: { SBT: { label: ' Company ', plural: 'Companies', color: 'PINK', icon: 'i-lucide-building-2' } },
      defaultTimeZone: 'europe/prague',
      DateFormat: 'yyyy-mm-dd',
    })

    expect(answer.status).toBe(200)
    expect(answer.headers.get('ETag')).toBe('"1"')
    expect(answer.body).toMatchObject({
      ...company,
      DefaultTimeZone: 'Europe/Prague',
      DateFormat: 'yyyy-mm-dd',
      Version: 1,
      Modified: { By: { Kind: 'Account', Id: 'user_01MOCKADA' } },
    })
    expect(loadDb().accessPoints['acme.billing']?.Settings).toEqual(answer.body)
    expect(auditOf()).toEqual([
      expect.objectContaining({ Detail: 'v0→v1: Types.sbt, DefaultTimeZone, DateFormat', Channel: 'User' }),
    ])
    expect((await settings()).body).toEqual(answer.body)
  })

  it('keeps the version of a write that changes nothing and records nothing', async () => {
    await put(company)
    const again = await put({ ...company, Version: 9, Format: '2s.project-settings.v1' })

    expect(again.status).toBe(200)
    expect(again.body).toMatchObject({ Version: 1 })
    expect(auditOf()).toHaveLength(1)
  })

  it('writes under If-Match only at the version it names, and answers 412 with the current version', async () => {
    await put(company)

    const stale = await put({ Types: {} }, { 'If-Match': '"0"' })
    expect(stale.status).toBe(412)
    expect(stale.body).toEqual({
      Message: 'The project settings changed meanwhile; they are at version 1.',
      ErrorCode: 'SettingsPreconditionFailed',
      Details: { Version: 1 },
    })
    // a strong comparison: a weak tag and a version in another spelling never match
    expect((await put({ Types: {} }, { 'If-Match': 'W/"1"' })).status).toBe(412)
    expect((await put({ Types: {} }, { 'If-Match': '"01"' })).status).toBe(412)

    const current = await put({ Types: {}, DateFormat: 'dd.mm.yyyy' }, { 'If-Match': '"7", "1"' })
    expect(current.body).toMatchObject({ DateFormat: 'dd.mm.yyyy', Version: 2 })
    expect((await put({ Types: {} }, { 'If-Match': '*' })).body).toMatchObject({ Version: 3 })
  })

  it('refuses a malformed If-Match and any If-None-Match with 400 InvalidPrecondition', async () => {
    const refused: Record<string, string>[] = [
      { 'If-Match': '1' },
      { 'If-Match': '*, "1"' },
      { 'If-Match': ' ' },
      { 'If-None-Match': '*' },
    ]
    for (const headers of refused) {
      const answer = await put(company, headers)
      expect(answer.status, JSON.stringify(headers)).toBe(400)
      expect(answer.body).toMatchObject({ ErrorCode: 'InvalidPrecondition' })
    }
    expect(loadDb().accessPoints['acme.billing']?.Settings).toBeUndefined()
  })

  it('lists every problem of invalid settings, before the condition', async () => {
    const answer = await put(
      {
        Types: { sbt: { Plural: 'Companies', Color: 'chartreuse' } },
        DefaultTimeZone: 'Central Europe Standard Time',
        Theme: 'dark',
      },
      { 'If-Match': '"5"' },
    )

    expect(answer.status).toBe(400)
    expect(answer.body).toMatchObject({ ErrorCode: 'InvalidSettings' })
    expect(answer.body?.Message).toMatch(/^The project settings have 4 problems; the first is \$\.Theme: /)
    expect(rulesOf(answer)).toEqual([
      ['$.Theme', 'unknown-member'],
      ['$.Types.sbt.Plural', 'plural-without-label'],
      ['$.Types.sbt.Color', 'color'],
      ['$.DefaultTimeZone', 'time-zone'],
    ])
  })

  it('refuses an empty body and one that is not JSON with one problem at $', async () => {
    const empty = await call('PUT', 'acme/billing/settings', {
      text: ' ',
      headers: { 'Content-Type': 'application/json' },
    })
    expect(empty.body).toMatchObject({
      ErrorCode: 'InvalidSettings',
      Details: [{ Path: '$', Rule: 'type', Message: 'The project settings must be a JSON object.' }],
    })

    const broken = await call('PUT', 'acme/billing/settings', {
      text: '{ "Types": ',
      headers: { 'Content-Type': 'application/json' },
    })
    expect(broken.status).toBe(400)
    expect(broken.body).toMatchObject({ ErrorCode: 'InvalidSettings', Details: [{ Path: '$', Rule: 'type' }] })
  })

  it('needs the Administrator role', async () => {
    expect((await put(company, {}, 'acme/shipping')).status).toBe(403)
    expect((await put(company, {}, 'northwind/portal')).status).toBe(403)
    expect(loadDb().accessPoints['acme.shipping']?.Settings).toBeUndefined()
  })
})

describe('mock project settings: PATCH', () => {
  it('merges a patch: a member left out stays, null removes, Types null resets every type', async () => {
    await put({
      Types: { sbt: { Label: 'Company', Color: 'pink' }, rst: { Label: 'Solution' } },
      DefaultTimeZone: 'Europe/Prague',
      DateFormat: 'dd.mm.yyyy',
    })

    const merged = await patch(
      { types: { SBT: { Color: 'rose', Label: null }, rst: null }, DateFormat: null },
      { 'If-Match': '"1"' },
    )
    expect(merged.status).toBe(200)
    expect(merged.body).toMatchObject({
      Types: { sbt: { Color: 'rose' } },
      DefaultTimeZone: 'Europe/Prague',
      Version: 2,
    })
    expect(merged.body).not.toHaveProperty('DateFormat')
    expect(auditOf().at(-1)?.Detail).toBe('v1→v2: Types.rst, Types.sbt, DateFormat')

    const reset = await patch({ Types: null })
    expect(reset.body).toMatchObject({ Types: {}, DefaultTimeZone: 'Europe/Prague', Version: 3 })
    expect((await patch({ Types: null })).body).toMatchObject({ Version: 3 })
  })

  it('refuses names that differ only in case and unknown names, also with null', async () => {
    const answer = await patch({
      DateFormat: 'dd.mm.yyyy',
      dateFormat: null,
      Types: { sbt: { Label: 'A' }, SBT: null },
      Theme: null,
    })

    expect(answer.status).toBe(400)
    expect(rulesOf(answer)).toEqual(
      expect.arrayContaining([
        ['$.Theme', 'unknown-member'],
        ['$.DateFormat', 'case-collision'],
        ['$.Types.sbt', 'type-code'],
      ]),
    )
    expect(loadDb().accessPoints['acme.billing']?.Settings).toBeUndefined()
  })

  it('checks the condition before the patched settings', async () => {
    const answer = await patch({ DefaultTimeZone: 'Nowhere/City' }, { 'If-Match': '"3"' })
    expect(answer.status).toBe(412)
    expect(answer.body).toMatchObject({ Details: { Version: 0 } })

    const invalid = await patch({ DefaultTimeZone: 'Nowhere/City' }, { 'If-Match': '"0"' })
    expect(invalid.body).toMatchObject({
      ErrorCode: 'InvalidSettings',
      Details: [{ Path: '$.DefaultTimeZone', Rule: 'time-zone' }],
    })
  })

  it('takes merge patch, JSON or no media type, and answers 415 for another', async () => {
    expect(
      (await patch({ DateFormat: 'dd/mm/yyyy' }, { 'Content-Type': 'application/json; charset=utf-8' })).status,
    ).toBe(200)

    const plain = await call('PATCH', 'acme/billing/settings', {
      text: '{}',
      headers: { 'Content-Type': 'text/plain' },
    })
    expect(plain.status).toBe(415)
    expect(plain.body).toMatchObject({ ErrorCode: 'InvalidContentType' })
    expect(plain.headers.get('Accept-Patch')).toBe('application/merge-patch+json')
  })

  it('answers 400 PatchInvalid for a body that is not a JSON object', async () => {
    for (const text of ['', '[]', '{ nope']) {
      const answer = await call('PATCH', 'acme/billing/settings', {
        text,
        headers: { 'Content-Type': 'application/merge-patch+json' },
      })
      expect(answer.status, text).toBe(400)
      expect(answer.body).toMatchObject({ ErrorCode: 'PatchInvalid' })
    }
  })
})

describe('mock project settings: a change of another person', () => {
  it('raises the version as the hook of the e2e tests writes it, so a write of the read before answers 412', async () => {
    const read = await settings()

    // what window.storytellerMock.changeProjectSettings does
    const db = loadDb()
    const result = patchProjectSettings(projectSettingsOf(db, 'acme.billing'), { DateFormat: 'mm/dd/yyyy' })
    expect(result.valid).toBe(true)
    storeProjectSettings(
      db,
      'acme.billing',
      result.valid ? result.settings : { Types: {} },
      accountActor('user_01MOCKGRACE'),
    )
    saveDb(db)

    const stale = await put({ Types: {} }, { 'If-Match': read.headers.get('ETag')! })
    expect(stale.status).toBe(412)
    expect(stale.body).toMatchObject({ Details: { Version: 1 } })
    expect((await settings()).body).toMatchObject({
      DateFormat: 'mm/dd/yyyy',
      Version: 1,
      Modified: { By: { Id: 'user_01MOCKGRACE', Name: 'Grace Hopper' } },
    })
    expect(auditOf().at(-1)?.Actor).toMatchObject({ Id: 'user_01MOCKGRACE' })
  })
})
