import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import {
  deleteAnnotation,
  getAnnotation,
  getAnnotationHistory,
  patchAnnotation,
  patchAnnotationVersioned,
  setAnnotation,
  type Annotation,
} from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { applyAnnotationPatch, mergeValues } from '@/mock/annotation-patch'
import { loadDb, resetDb, saveDb } from '@/mock/db'

// PATCH v1/{organization}/{project}/{view}/annotations/{key} of the mock API (424-225) answers as the API does
// (annotation-lifecycle.md, "Merge writes (PATCH)"). Ada is the Owner of acme.billing and a Reader of northwind.portal.

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterAll(() => server.close())
beforeEach(() => {
  resetDb('default')
})

const [org, project, view] = ['acme', 'billing', 'default']
const base = `http://api.test/api/v1/${org}/${project}/${view}/annotations`

interface Answer {
  status: number
  headers: Headers
  body: Record<string, unknown> | undefined
}

async function patch(
  key: string,
  body: unknown,
  contentType: string | undefined = Array.isArray(body)
    ? 'application/json-patch+json'
    : 'application/merge-patch+json',
  options: { text?: string; headers?: Record<string, string> } = {},
): Promise<Answer> {
  const response = await fetch(`${base}/${encodeURIComponent(key)}`, {
    method: 'PATCH',
    headers: { ...(contentType ? { 'Content-Type': contentType } : {}), ...options.headers },
    body: options.text ?? JSON.stringify(body),
  })
  const text = await response.text()
  return { status: response.status, headers: response.headers, body: text ? JSON.parse(text) : undefined }
}

const subject = (fields: Partial<Annotation> = {}): Annotation => ({
  ProjectName: project,
  ViewName: view,
  AnnotationKey: 'sbt.northwind',
  Name: 'northwind',
  AnnotationType: 'Subject',
  Title: 'Northwind',
  ...fields,
})

/** sbt.northwind written whole first, so a test starts from a known state. */
const seedSubject = (fields: Partial<Annotation> = {}) =>
  setAnnotation(org, project, view, 'sbt.northwind', subject(fields))

const read = (key = 'sbt.northwind') => getAnnotation(org, project, view, key, { includeInactive: true })
const history = async (key = 'sbt.northwind') => (await getAnnotationHistory(org, project, view, key)).Entries ?? []

describe('the mock PatchAnnotation: requests', () => {
  it('takes a merge patch and a JSON Patch by media type, and application/json or none by the body', async () => {
    await seedSubject()

    expect((await patch('sbt.northwind', { Title: 'A' })).body).toMatchObject({ Title: 'A' })
    expect((await patch('sbt.northwind', [{ op: 'replace', path: '/Title', value: 'B' }])).body).toMatchObject({
      Title: 'B',
    })
    expect((await patch('sbt.northwind', { Title: 'C' }, 'application/json')).body).toMatchObject({ Title: 'C' })
    expect(
      (await patch('sbt.northwind', [{ op: 'replace', path: '/Title', value: 'D' }], 'Application/JSON; charset=utf-8'))
        .body,
    ).toMatchObject({ Title: 'D' })
  })

  it('decides by the body when the request has no Content-Type', async () => {
    await seedSubject()
    let sent: string | null | undefined
    server.events.on('request:start', ({ request }) => void (sent ??= request.headers.get('Content-Type')))

    // bytes, not a string: fetch gives a string body a text/plain media type
    const response = await fetch(`${base}/sbt.northwind`, {
      method: 'PATCH',
      body: new TextEncoder().encode(JSON.stringify([{ op: 'replace', path: '/Title', value: 'No header' }])),
    })
    server.events.removeAllListeners('request:start')

    expect(sent).toBeNull()
    expect(response.status).toBe(200)
    expect(await read()).toMatchObject({ Title: 'No header' })
  })

  it('refuses another media type, or one that does not parse, with 415 and Accept-Patch', async () => {
    for (const contentType of [
      'text/plain',
      'application/merge-patch+json; =',
      'application/merge-patch+json; charset',
    ]) {
      const answer = await patch('sbt.northwind', { Title: 'A' }, contentType)

      expect(answer.status, contentType).toBe(415)
      expect(answer.body).toMatchObject({ ErrorCode: 'InvalidContentType' })
      expect(answer.headers.get('Accept-Patch')).toBe('application/merge-patch+json, application/json-patch+json')
    }
    await seedSubject()
    expect((await patch('sbt.northwind', { Title: 'A' }, 'application/merge-patch+json; charset=utf-8')).status).toBe(
      200,
    )
  })

  it('refuses a body that is not JSON, does not fit its format, or has a malformed operation with 400 PatchInvalid', async () => {
    const cases: [unknown, string | undefined, string?][] = [
      [undefined, 'application/merge-patch+json', '{ not json'],
      [undefined, 'application/merge-patch+json', ''],
      [[{ op: 'add', path: '/Title', value: 'x' }], 'application/merge-patch+json'],
      [{ Title: 'x' }, 'application/json-patch+json'],
      ['text', 'application/json'],
      [[{ op: 'rename', path: '/Title' }], undefined],
      [[{ op: 'add', path: 'Title', value: 'x' }], undefined],
      [[{ op: 'add', path: '/Title' }], undefined],
      [[null], undefined],
      [[{ op: ['replace'], path: '/Title', value: 'x' }], undefined],
    ]
    for (const [body, contentType, text] of cases) {
      const answer = await patch('sbt.northwind', body, contentType ?? 'application/json-patch+json', { text })
      expect(answer.status, JSON.stringify(body ?? text)).toBe(400)
      expect(answer.body).toMatchObject({ ErrorCode: 'PatchInvalid' })
    }
    // a malformed operation answers before the key is looked up
    expect((await patch('sbt.nobody', [{ op: ['replace'], path: '/Title', value: 'x' }])).status).toBe(400)
  })

  it('refuses If-None-Match, an invalid key and a Reader', async () => {
    expect(
      (await patch('sbt.northwind', { Title: 'A' }, undefined, { headers: { 'If-None-Match': '*' } })).body,
    ).toMatchObject({
      ErrorCode: 'InvalidPrecondition',
    })

    const invalidKey = await patch('boots.app', { Title: 'A' })
    expect(invalidKey.status).toBe(400)
    expect(invalidKey.body).toMatchObject({ ErrorCode: 'InvalidKey' })

    const db = loadDb()
    db.account!.AccessMap!['acme.billing'] = 'Reader'
    saveDb(db)
    expect((await patch('sbt.northwind', { Title: 'A' })).status).toBe(403)
  })

  it('never creates or re-creates a key: 404 AnnotationNotFound and AnnotationDeleted', async () => {
    const missing = await patch('sbt.nobody', { Title: 'A' })
    expect(missing.status).toBe(404)
    expect(missing.body).toMatchObject({ ErrorCode: 'AnnotationNotFound' })
    await expect(read('sbt.nobody')).rejects.toMatchObject({ status: 404 })

    await deleteAnnotation(org, project, view, 'sbt.contoso')
    const deleted = await patch('sbt.contoso', { Title: 'A' })
    expect(deleted.status).toBe(404)
    expect(deleted.body).toMatchObject({ ErrorCode: 'AnnotationDeleted' })
    expect(deleted.body?.Hint).toContain('restore it')
    await expect(read('sbt.contoso')).rejects.toMatchObject({ status: 404 })
  })

  it('answers the SDK: patchAnnotation and patchAnnotationVersioned in both formats', async () => {
    await seedSubject({ Labels: ['eu'] })

    expect(await patchAnnotation(org, project, view, 'sbt.northwind', { Title: 'Merged' })).toMatchObject({
      Title: 'Merged',
    })
    const { value } = await patchAnnotationVersioned(org, project, view, 'sbt.northwind', [
      { op: 'add', path: '/Labels/-', value: 'pci' },
    ])
    expect(value).toMatchObject({ Title: 'Merged', Labels: ['eu', 'pci'] })
    await expect(
      patchAnnotationVersioned(org, project, view, 'sbt.northwind', [{ op: 'test', path: '/Title', value: 'Other' }]),
    ).rejects.toMatchObject({ status: 412, errorCode: 'PatchTestFailed' })
  })
})

describe('the mock PatchAnnotation: a merge patch', () => {
  it('changes only the members it names, clears a member with null, and keeps the others', async () => {
    await seedSubject({
      Description: 'Old',
      DocumentationLink: 'https://docs.example.com/northwind',
      Labels: ['eu'],
      Values: { plan: 'basic' },
      TimeZone: 'Europe/Prague',
    })

    const { body } = await patch('sbt.northwind', { Title: 'New', Description: null, timezone: 'UTC' })

    expect(body).toMatchObject({
      Title: 'New',
      TimeZone: 'UTC',
      DocumentationLink: 'https://docs.example.com/northwind',
      Labels: ['eu'],
      Values: { plan: 'basic' },
    })
    expect(body).not.toHaveProperty('Description')
    expect(await read()).not.toHaveProperty('Description')
  })

  it('merges Values name by name and recursively, with ordinal names; null removes a name', async () => {
    await seedSubject({ Values: { plan: 'basic', Plan: 'kept', limits: { cpu: 2, memory: 4 }, old: 1, list: [1, 2] } })

    const { body } = await patch('sbt.northwind', {
      Values: {
        plan: 'pro',
        limits: { memory: 8, cpu: null },
        old: null,
        list: [3],
        region: { zone: 'eu', none: null },
      },
    })

    expect(body?.Values).toEqual({
      plan: 'pro',
      Plan: 'kept',
      limits: { memory: 8 },
      list: [3],
      region: { zone: 'eu' },
    })
  })

  it('replaces Labels whole and Values with null', async () => {
    await seedSubject({ Labels: ['eu', 'pci'], Values: { plan: 'basic' } })

    const { body } = await patch('sbt.northwind', { Labels: ['us'], Values: null })

    expect(body?.Labels).toEqual(['us'])
    expect(body).not.toHaveProperty('Values')
  })

  it('ignores the members of the stored type that a write ignores, and refuses unknown and repeated ones', async () => {
    await seedSubject()
    const current = await read()

    // a body that was read can be sent back
    expect((await patch('sbt.northwind', { ...current, Title: 'Read and sent back' })).body).toMatchObject({
      Title: 'Read and sent back',
      AnnotationKey: 'sbt.northwind',
    })

    for (const body of [
      { Foo: 1 },
      { SubjectKey: 'sbt.northwind' },
      { UnitType: 'event' },
      { Title: 'a', title: 'b' },
    ]) {
      const answer = await patch('sbt.northwind', body)
      expect(answer.status, JSON.stringify(body)).toBe(400)
      expect(answer.body).toMatchObject({ ErrorCode: 'InvalidAnnotation' })
    }
    expect((await read()).Title).toBe('Read and sent back')
  })

  it('ignores the keys of the ancestors on a context, and takes a unit trigger only on units', async () => {
    const context = await read('cnt.northwind.prod')
    expect((await patch('cnt.northwind.prod', { SubjectKey: 'sbt.other', Title: 'Prod' })).body).toMatchObject({
      Title: 'Prod',
      SubjectKey: (context as unknown as Record<string, unknown>).SubjectKey,
    })

    const unit = await patch('unt.invoicing.pdf', { UnitType: 'queue', UnitDefinition: 'orders.>' })
    expect(unit.body).toMatchObject({ UnitType: 'queue', UnitDefinition: 'orders.>' })

    const cleared = await patch('unt.invoicing.pdf', { UnitType: null })
    expect(cleared.status).toBe(400)
    expect(cleared.body).toMatchObject({ ErrorCode: 'InvalidAnnotation' })
    expect(String(cleared.body?.Message)).toContain('UnitType')
  })

  it('checks the type of every member, naming it', async () => {
    await seedSubject()

    for (const [body, member] of [
      [{ IsDisabled: 'yes' }, 'IsDisabled'],
      [{ ValidFrom: 'soon' }, 'ValidFrom'],
      [{ Title: 3 }, 'Title'],
      [{ Labels: 'eu' }, 'Labels'],
      [{ Labels: ['eu', 1] }, 'Labels'],
      [{ Values: [1] }, 'Values'],
    ] as const) {
      const answer = await patch('sbt.northwind', body)
      expect(answer.status, JSON.stringify(body)).toBe(400)
      expect(answer.body).toMatchObject({ ErrorCode: 'InvalidAnnotation' })
      expect(String(answer.body?.Message)).toContain(member)
    }
  })

  it('checks the result as a full write is checked', async () => {
    await seedSubject()

    const answer = await patch('sbt.northwind', { Title: 'x'.repeat(201) })

    expect(answer.status).toBe(400)
    expect(answer.body).toMatchObject({ ErrorCode: 'InvalidAnnotation' })
    expect(String(answer.body?.Message)).toContain('Title has 201 characters')
  })
})

describe('the mock PatchAnnotation: a JSON Patch', () => {
  it('appends a label, keeping the first occurrence ordinally, and sets one value', async () => {
    await seedSubject({ Labels: ['eu'] })

    const { body } = await patch('sbt.northwind', [
      { op: 'add', path: '/Labels/-', value: 'pci' },
      { op: 'add', path: '/Labels/-', value: 'eu' },
      { op: 'add', path: '/Labels/-', value: 'EU' },
      { op: 'add', path: '/Values/plan', value: 'pro' },
      { op: 'add', path: '/Values/a~1b', value: null },
    ])

    expect(body?.Labels).toEqual(['eu', 'pci', 'EU'])
    // a JSON Patch can set a value to null, a merge patch cannot
    expect(body?.Values).toEqual({ plan: 'pro', 'a/b': null })
  })

  it('works on members that are unset: Labels and Values are always present', async () => {
    await seedSubject()

    const { body } = await patch('sbt.northwind', [
      { op: 'replace', path: '/Description', value: 'Set' },
      { op: 'add', path: '/Labels/-', value: 'first' },
    ])

    expect(body).toMatchObject({ Description: 'Set', Labels: ['first'] })
  })

  it('answers a failed test, also on a missing path, with 412 PatchTestFailed and writes nothing', async () => {
    await seedSubject({ Labels: ['eu'] })

    for (const operations of [
      [
        { op: 'test', path: '/Labels/0', value: 'us' },
        { op: 'remove', path: '/Labels/0' },
      ],
      [
        { op: 'test', path: '/Labels/5', value: 'eu' },
        { op: 'remove', path: '/Labels/5' },
      ],
      [{ op: 'test', path: '/Values/missing/deep', value: 1 }],
    ]) {
      const answer = await patch('sbt.northwind', operations)
      expect(answer.status, JSON.stringify(operations)).toBe(412)
      expect(answer.body).toMatchObject({ ErrorCode: 'PatchTestFailed' })
    }
    expect((await read()).Labels).toEqual(['eu'])
  })

  it('tests an object value by its members, also inside an array', async () => {
    await seedSubject({ Values: { limits: { cpu: 2, memory: 4 }, zones: [{ name: 'eu', weight: 1 }] } })

    const equal = await patch('sbt.northwind', [
      { op: 'test', path: '/Values/limits', value: { memory: 4, cpu: 2 } },
      { op: 'test', path: '/Values/zones', value: [{ weight: 1, name: 'eu' }] },
      { op: 'replace', path: '/Title', value: 'Tested' },
    ])
    expect(equal.status).toBe(200)
    expect(equal.body).toMatchObject({ Title: 'Tested' })

    for (const value of [{ cpu: 2 }, { cpu: 2, memory: 8 }, [{ name: 'eu' }]]) {
      const path = Array.isArray(value) ? '/Values/zones' : '/Values/limits'
      const unequal = await patch('sbt.northwind', [{ op: 'test', path, value }])
      expect(unequal.status, JSON.stringify(value)).toBe(412)
      expect(unequal.body).toMatchObject({ ErrorCode: 'PatchTestFailed' })
    }
  })

  it('removes a label guarded by its test', async () => {
    await seedSubject({ Labels: ['eu', 'pci', 'EU'] })

    const { body } = await patch('sbt.northwind', [
      { op: 'test', path: '/Labels/2', value: 'EU' },
      { op: 'remove', path: '/Labels/2' },
      { op: 'test', path: '/Labels/0', value: 'eu' },
      { op: 'remove', path: '/Labels/0' },
    ])

    expect(body?.Labels).toEqual(['pci'])
  })

  it('answers another failed operation with 400 PatchInvalid, and a member outside the document with InvalidAnnotation', async () => {
    await seedSubject()

    for (const operations of [
      [{ op: 'remove', path: '/Values/missing' }],
      [{ op: 'add', path: '/Values/missing/deep', value: 1 }],
      [{ op: 'replace', path: '/UnitType', value: 'event' }],
    ]) {
      const answer = await patch('sbt.northwind', operations)
      expect(answer.status, JSON.stringify(operations)).toBe(400)
      expect(answer.body).toMatchObject({ ErrorCode: 'PatchInvalid' })
    }

    const unknown = await patch('sbt.northwind', [{ op: 'add', path: '/title', value: 'lower case' }])
    expect(unknown.status).toBe(400)
    expect(unknown.body).toMatchObject({ ErrorCode: 'InvalidAnnotation' })
  })
})

describe('the mock PatchAnnotation: the write', () => {
  it('writes nothing for a patch that changes nothing: no audit entry, the stored annotation back', async () => {
    await seedSubject({ Labels: ['eu'], IsDisabled: true })
    const before = await read()
    const entries = (await history()).length

    for (const body of [
      {},
      [],
      { IsDisabled: true, Title: 'Northwind' },
      [{ op: 'add', path: '/Labels/-', value: 'eu' }],
    ]) {
      expect((await patch('sbt.northwind', body)).body).toEqual(before)
    }
    expect(await history()).toHaveLength(entries)
  })

  it('records an Updated entry with only the changed members', async () => {
    await seedSubject({ Description: 'Kept', Labels: ['eu'] })

    await patch('sbt.northwind', [
      { op: 'replace', path: '/Title', value: 'Northwind Traders' },
      { op: 'add', path: '/Labels/-', value: 'pci' },
    ])

    const [latest] = await history()
    expect(latest).toMatchObject({ Action: 'Updated', Labels: { Added: ['pci'], Removed: [] } })
    expect(latest!.Fields!.map((field) => field.Field)).toEqual(['Title'])
  })

  it('patches an inactive annotation: re-enables a disabled one', async () => {
    await seedSubject({ IsDisabled: true })

    const { body } = await patch('sbt.northwind', { IsDisabled: null })

    expect(body).not.toHaveProperty('IsDisabled')
    expect((await getAnnotation(org, project, view, 'sbt.northwind')).AnnotationKey).toBe('sbt.northwind')
  })

  it('keeps a member changed by another write between two patches', async () => {
    await seedSubject({ Title: 'Northwind', Values: { plan: 'basic' } })

    await patch('sbt.northwind', { Values: { plan: 'pro' } })
    await patch('sbt.northwind', { Title: 'Renamed' })

    expect(await read()).toMatchObject({ Title: 'Renamed', Values: { plan: 'pro' } })
  })

  it('records a merge of Values alone as an update', async () => {
    await seedSubject({ Values: { plan: 'basic', limits: { cpu: 2 } } })

    await patch('sbt.northwind', { Values: { limits: { cpu: 4 } } })

    const [latest] = await history()
    expect(latest).toMatchObject({ Action: 'Updated' })
    expect((await read()).Values).toEqual({ plan: 'basic', limits: { cpu: 4 } })
  })

  it('keeps every write of concurrent patches', async () => {
    await seedSubject({ Labels: ['eu'] })

    await Promise.all([
      patch('sbt.northwind', { Title: 'Concurrent' }),
      patch('sbt.contoso', { IsDisabled: true }),
      patch('sbt.fabrikam', { Description: 'Concurrent' }),
      patch('sbt.northwind', [{ op: 'add', path: '/Labels/-', value: 'pci' }]),
    ])

    expect(await read()).toMatchObject({ Title: 'Concurrent', Labels: ['eu', 'pci'] })
    expect(await read('sbt.contoso')).toMatchObject({ IsDisabled: true })
    expect(await read('sbt.fabrikam')).toMatchObject({ Description: 'Concurrent' })
  })

  it('keeps a patch made while a restore reads its body', async () => {
    await deleteAnnotation(org, project, view, 'sbt.contoso')
    // the restore's body arrives only after the patch was sent, so its handler waits for it inside the database section
    let release = () => {}
    const gate = new Promise<void>((resolve) => (release = resolve))
    const body = new ReadableStream<Uint8Array>({
      async start(controller) {
        await gate
        controller.enqueue(new TextEncoder().encode(JSON.stringify({ Keys: ['sbt.contoso'], Descendants: false })))
        controller.close()
      },
    })
    const restoring = fetch(`${base}/deleted/restore`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body,
      duplex: 'half',
    } as RequestInit)
    await new Promise((resolve) => setTimeout(resolve, 50))
    const patching = patch('sbt.northwind', { Title: 'Patched during a restore' })
    await new Promise((resolve) => setTimeout(resolve, 50))
    release()

    expect((await restoring).status).toBe(200)
    expect((await patching).status).toBe(200)

    expect(await read()).toMatchObject({ Title: 'Patched during a restore' })
    expect(await read('sbt.contoso')).toMatchObject({ AnnotationKey: 'sbt.contoso' })
  })

  it('writes nothing to the stored database for a patch that changes nothing or misses its key', async () => {
    const before = JSON.stringify(loadDb())

    expect((await patch('sbt.northwind', {})).status).toBe(200)
    expect((await patch('sbt.nobody', { Title: 'A' })).status).toBe(404)

    expect(JSON.stringify(loadDb())).toBe(before)
  })
})

describe('the mock PatchAnnotation: details of the API', () => {
  it('names the failing operation, and refuses a move into its own child', async () => {
    await seedSubject({ Values: { a: { b: 1 } } })

    const failed = await patch('sbt.northwind', [
      { op: 'replace', path: '/Title', value: 'x' },
      { op: 'add', path: '/Labels/-', value: 'eu' },
      { op: 'remove', path: '/Values/missing' },
    ])
    expect(failed.status).toBe(400)
    expect(String(failed.body?.Message)).toMatch(/^JSON Patch operation 2 failed/)

    const moved = await patch('sbt.northwind', [{ op: 'move', from: '/Values/a', path: '/Values/a/c' }])
    expect(moved.status).toBe(400)
    expect(moved.body).toMatchObject({ ErrorCode: 'PatchInvalid' })
  })

  it('takes ISO 8601 dates only, and checks the documentation link as a full write does', async () => {
    await seedSubject()

    expect((await patch('sbt.northwind', { ValidFrom: '2026-11-01T09:30:00+01:00' })).status).toBe(200)
    expect((await patch('sbt.northwind', { ValidFrom: 'Nov 22 2026' })).body).toMatchObject({
      ErrorCode: 'InvalidAnnotation',
    })
    const link = await patch('sbt.northwind', { DocumentationLink: 'ftp://files.example.com/doc' })
    expect(link.status).toBe(400)
    expect(String(link.body?.Message)).toContain('DocumentationLink must be an absolute http or https URL.')
  })

  it('never changes the stored annotation it applies a patch to', () => {
    const stored = subject({ Labels: ['eu'], Values: { plan: 'basic', limits: { cpu: 2 } } })
    const before = structuredClone(stored)

    for (const [format, body] of [
      ['merge', { Values: { plan: 'pro', limits: { cpu: null } }, Labels: ['us'] }],
      [
        'json-patch',
        [
          { op: 'add', path: '/Labels/-', value: 'us' },
          { op: 'remove', path: '/Values/plan' },
        ],
      ],
    ] as const) {
      expect(applyAnnotationPatch(stored, format, body)).toHaveProperty('annotation')
    }
    expect(stored).toEqual(before)
  })
})

describe('mergeValues', () => {
  it('is RFC 7396 with ordinal names', () => {
    expect(mergeValues({ a: 1, A: 2, b: { c: 1 } }, { a: null, b: { d: 2 }, e: { f: null, g: 1 } })).toEqual({
      A: 2,
      b: { c: 1, d: 2 },
      e: { g: 1 },
    })
    expect(mergeValues({ a: [1, { b: null }] }, { a: [2, { c: null }] })).toEqual({ a: [2, { c: null }] })
    expect(mergeValues('scalar', { a: 1 })).toEqual({ a: 1 })
  })

  it('takes a value named __proto__ as a name in a JSON Patch, without touching a prototype', async () => {
    await seedSubject({ Values: { plan: 'basic' } })

    const { status, body } = await patch('sbt.northwind', [
      { op: 'add', path: '/Values/__proto__', value: null },
      { op: 'add', path: '/Values/limits', value: JSON.parse('{"__proto__": {"cpu": 2}}') },
    ])

    expect(status).toBe(200)
    expect(Object.keys(body!.Values as object).toSorted()).toEqual(['__proto__', 'limits', 'plan'])
    expect(Object.keys((body!.Values as { limits: object }).limits)).toEqual(['__proto__'])
    expect(({} as Record<string, unknown>).cpu).toBeUndefined()
  })

  it('takes a value named __proto__ as a name', () => {
    const merged = mergeValues({}, JSON.parse('{"__proto__": {"polluted": true}}')) as Record<string, unknown>
    expect(Object.keys(merged)).toEqual(['__proto__'])
    expect(({} as Record<string, unknown>).polluted).toBeUndefined()
  })
})
