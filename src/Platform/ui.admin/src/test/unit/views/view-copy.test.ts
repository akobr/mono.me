import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { QueryClient, QueryObserver } from '@tanstack/vue-query'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import {
  deleteAnnotation,
  deleteConfiguration,
  getAnnotation,
  getAnnotationHistory,
  getDeletedAnnotation,
  getViews,
  patchConfiguration,
  purgeAnnotation,
  setAnnotation,
  setConfiguration,
  type Annotation,
} from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import {
  annotationStamp,
  copyCounts,
  copySummary,
  copyToView,
  copyWrites,
  stoppedMessage,
  viewCopyOutcomes,
  type ViewCopyItem,
  type ViewCopyOptions,
  type ViewCopyOutcome,
  type ViewCopyRequest,
} from '@/utils/view-copy'
import { annotationText, canonicalJson, readAnnotation, readConfiguration } from '@/utils/view-documents'

// The copy of annotations and configurations to another view (spec "Copy and Compare Annotations and Configurations
// Between Views", section 7), against the stateful mock API of the e2e mode, served by msw/node. In the default
// dataset acme/billing has the views default (the source) and staging (the target), whose annotations and
// configurations differ: the keys below were picked for their states, and a change of the generator shows here.
const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  server.events.removeAllListeners()
  server.resetHandlers()
})
afterAll(() => server.close())

const source = { org: 'acme', project: 'billing', view: 'default' }
const target = { ...source, view: 'staging' }
const { org, project } = source
const root = '/api/v1/acme/billing'

/** A request of the run. */
interface Sent {
  method: string
  /** The path below the project, with its query: "/staging/annotations/sbt.initech?includeInactive=true". */
  path: string
  ifMatch: string | null
  ifNoneMatch: string | null
  contentType: string | null
  body?: unknown
}

const line = (request: Sent) => `${request.method} ${request.path}`

/** The lookup of the deleted keys is a read that is sent by POST. */
const isLookup = (request: Sent) => request.path.endsWith('/annotations/deleted/lookup')

/** The requests from now on, in the order they were sent. */
function recordRequests() {
  const sent: Sent[] = []
  const bodies: Promise<void>[] = []
  server.events.on('request:start', ({ request }) => {
    const url = new URL(request.url)
    const entry: Sent = {
      method: request.method,
      path: `${url.pathname}${url.search}`.replace(root, ''),
      ifMatch: request.headers.get('if-match'),
      ifNoneMatch: request.headers.get('if-none-match'),
      contentType: request.headers.get('content-type'),
    }
    sent.push(entry)
    if (request.method !== 'GET') {
      // read from a clone at once: the handler reads the request itself
      const text = request.clone().text()
      bodies.push(
        text.then((body) => {
          entry.body = body ? JSON.parse(body) : undefined
        }),
      )
    }
  })

  const all = async () => {
    await Promise.all(bodies)
    return sent
  }
  return {
    all,
    lines: async () => (await all()).map(line),
    /** Everything that changes the API: every request but the GETs and the lookup of the deleted keys. */
    writes: async () => (await all()).filter((request) => request.method !== 'GET' && !isLookup(request)),
    lookups: async () => (await all()).filter(isLookup),
  }
}

type CopyRequest = Partial<ViewCopyRequest> & Pick<ViewCopyRequest, 'kind' | 'keys'>

/** A copy from default to staging that overwrites and copies missing annotations, unless the request says else. */
const copy = (request: CopyRequest, options: Partial<ViewCopyOptions> = {}) =>
  copyToView(
    { source, target: 'staging', existing: 'overwrite', missingAnnotations: true, ...request },
    { client: new QueryClient(), ...options },
  )

const row = (kind: ViewCopyItem['kind'], key: string, outcome: ViewCopyOutcome, message?: string): ViewCopyItem => ({
  kind,
  key,
  outcome,
  ...(message ? { message } : {}),
})
const annotation = (key: string, outcome: ViewCopyOutcome, message?: string) => row('Annotation', key, outcome, message)
const configuration = (key: string, outcome: ViewCopyOutcome, message?: string) =>
  row('Configuration', key, outcome, message)

/** The annotation of a view as the API answers it, an inactive one too. */
const annotationOf = (view: string, key: string) => getAnnotation(org, project, view, key, { includeInactive: true })
const inStaging = async (key: string) => !!(await readAnnotation(target, key)).annotation
const ownOf = async (view: string, key: string) => (await readConfiguration({ ...source, view }, key)).own
const textOf = async (view: string, key: string) => annotationText(await annotationOf(view, key))

/** A request with the body as it is written, for JSON that JSON.stringify cannot write (a number beyond 2^53). */
async function send(method: string, path: string, body?: string) {
  const response = await fetch(`http://api.test${root}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body,
  })
  expect(response.ok, `${method} ${path}: ${response.status}`).toBe(true)
}

const busy = () => HttpResponse.json({ Message: 'The database is busy.' }, { status: 500 })
const denied = () =>
  HttpResponse.json(
    { Message: 'The Contributor role on acme.billing is required.', ErrorCode: 'AccessDenied' },
    { status: 403 },
  )
const stagingAnnotation = (key: string) => `*${root}/staging/annotations/${key}`
const stagingConfiguration = (key: string) => `*${root}/staging/configuration/${key}`

const inexactMessage = 'It holds a number beyond 2^53. Copy it with Export & Import or sform.'
const changedMessage = 'It changed in staging meanwhile. Copy it again.'
const comparedMessage = 'It changed in staging since it was compared.'
const goneMessage = 'It is no longer in default.'
const deletedMessage = (key: string) => `${key} is deleted in staging. Restore it there first.`
const ancestorMessage = (key: string) => `Its ancestor ${key} was not copied.`
const missingAncestor = (key: string) => `Missing ancestor of ${key}.`
const neededMessage = 'Needed by its configuration.'

// Annotations: sbt.northwind differs between the views, sbt.initech and everything below it is only in default,
// uxe.northwind.notifications.staging.sms is the same in both, and cnt.contoso.us is only in staging.
const differs = 'sbt.northwind'
const onlySource = 'sbt.initech'
const same = 'uxe.northwind.notifications.staging.sms'
const onlyTarget = 'cnt.contoso.us'

describe('copyToView: annotations', () => {
  it('creates an annotation that the target lacks, and only if none is there', async () => {
    const original = await annotationOf('default', onlySource)
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [onlySource] })

    expect(result).toEqual({
      target: 'staging',
      dryRun: false,
      items: [annotation(onlySource, 'Created')],
      stopped: false,
      // what the run read in the source, which a copy after a check compares with
      sources: { [onlySource]: annotationText(original) },
    })
    // the views, the deleted keys, two reads, the deleted state of the key right before its create, and one write
    expect(await requests.lines()).toEqual([
      'GET /views',
      'POST /staging/annotations/deleted/lookup',
      `GET /default/annotations/${onlySource}?includeInactive=true`,
      `GET /staging/annotations/${onlySource}?includeInactive=true`,
      'POST /staging/annotations/deleted/lookup',
      `POST /staging/annotations/${onlySource}`,
    ])
    expect((await requests.lookups())[1]!.body).toEqual({ Keys: [onlySource] })
    const [write] = await requests.writes()
    expect(write).toMatchObject({ ifNoneMatch: '*', ifMatch: null, contentType: 'application/json' })
    // the body is the annotation of the source, as it was read
    expect(write!.body).toEqual(original)

    const copied = await annotationOf('staging', onlySource)
    expect(copied.ViewName).toBe('staging')
    expect(annotationText(copied)).toBe(annotationText(original))
  })

  it('overwrites an annotation that differs with the content of the source', async () => {
    const original = await annotationOf('default', differs)
    const before = await textOf('staging', differs)
    expect(before).not.toBe(annotationText(original))
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [differs] })

    expect(result.items).toEqual([annotation(differs, 'Updated')])
    const writes = await requests.writes()
    expect(writes.map(line)).toEqual([`POST /staging/annotations/${differs}`])
    // the mock sends no ETag, so the write takes any existing version; it is never a creation or a re-creation
    expect(writes[0]).toMatchObject({ ifMatch: '*', ifNoneMatch: null })
    expect(writes[0]!.body).toEqual(original)
    // what only staging had is gone: the content is replaced, not merged
    expect(await textOf('staging', differs)).toBe(annotationText(original))
    expect((await annotationOf('staging', differs)).ViewName).toBe('staging')
  })

  it('overwrites only the version it read: the ETag of the target is the If-Match of the write', async () => {
    const stored = await annotationOf('staging', differs)
    server.use(
      http.get(stagingAnnotation(differs), () => HttpResponse.json(stored, { headers: { ETag: '"a1.7c2f"' } })),
    )
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [differs] })

    expect(result.items).toEqual([annotation(differs, 'Updated')])
    const writes = await requests.writes()
    expect(writes.map(line)).toEqual([`POST /staging/annotations/${differs}`])
    // the tag as it was sent, quotes included
    expect(writes[0]).toMatchObject({ ifMatch: '"a1.7c2f"', ifNoneMatch: null })
  })

  it('says that the target changed when the API refuses the condition of a write', async () => {
    const before = await textOf('staging', differs)
    const refused = () =>
      HttpResponse.json(
        { Message: 'The annotation changed since it was read.', ErrorCode: 'AnnotationPreconditionFailed' },
        { status: 412 },
      )
    server.use(http.post(stagingAnnotation(differs), refused), http.post(stagingAnnotation(onlySource), refused))

    const result = await copy({ kind: 'annotations', keys: [differs, onlySource, 'cnt.initech.prod'] })

    expect(result.items).toEqual([
      // a key that appeared since it was looked for is not overwritten
      annotation(onlySource, 'Failed', changedMessage),
      annotation(differs, 'Failed', changedMessage),
      annotation('cnt.initech.prod', 'Skipped', ancestorMessage(onlySource)),
    ])
    expect(result.error).toBeUndefined()
    expect(await textOf('staging', differs)).toBe(before)
  })

  it('leaves an annotation with the same content alone: nothing is written', async () => {
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [same] })

    expect(result.items).toEqual([annotation(same, 'Unchanged')])
    expect(await requests.writes()).toEqual([])
    expect(await requests.lines()).toEqual([
      'GET /views',
      'POST /staging/annotations/deleted/lookup',
      `GET /default/annotations/${same}?includeInactive=true`,
      `GET /staging/annotations/${same}?includeInactive=true`,
    ])
  })

  it('takes labels in another order, a date with another offset and an empty member as the same content', async () => {
    const original = await annotationOf('default', differs)
    await setAnnotation(org, project, 'default', differs, {
      ...original,
      Description: undefined,
      Labels: ['pci', 'eu'],
      ValidFrom: '2026-03-01T10:00:00Z',
      ExpiresAt: '2030-01-01T00:00:00Z',
      IsDisabled: undefined,
    })
    await setAnnotation(org, project, 'staging', differs, {
      ...original,
      Description: '',
      Labels: ['eu', 'pci', 'eu'],
      ValidFrom: '2026-03-01T12:00:00+02:00',
      ExpiresAt: '2029-12-31T19:00:00-05:00',
      IsDisabled: false,
    })
    // the mock keeps what it was sent
    expect((await annotationOf('staging', differs)).Labels).toEqual(['eu', 'pci', 'eu'])
    expect((await annotationOf('staging', differs)).ValidFrom).toBe('2026-03-01T12:00:00+02:00')
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [differs] })

    expect(result.items).toEqual([annotation(differs, 'Unchanged')])
    expect(await requests.writes()).toEqual([])
  })

  it('skips an annotation that the target has with another content when asked to', async () => {
    const before = await textOf('staging', differs)
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [differs, onlySource, same], existing: 'skip' })

    expect(result.items).toEqual([
      // only what the target has with another content: a missing one is created, an equal one is unchanged
      annotation(onlySource, 'Created'),
      annotation(differs, 'Skipped', 'It is already in staging.'),
      annotation(same, 'Unchanged'),
    ])
    expect((await requests.writes()).map(line)).toEqual([`POST /staging/annotations/${onlySource}`])
    expect(await textOf('staging', differs)).toBe(before)
  })

  it('copies a missing ancestor from the source first, as a row of its own', async () => {
    const parent = await annotationOf('default', onlySource)
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: ['cnt.initech.prod'] })

    expect(result.items).toEqual([
      annotation(onlySource, 'Created', missingAncestor('cnt.initech.prod')),
      annotation('cnt.initech.prod', 'Created'),
    ])
    const writes = await requests.writes()
    expect(writes.map(line)).toEqual([
      `POST /staging/annotations/${onlySource}`,
      'POST /staging/annotations/cnt.initech.prod',
    ])
    expect(writes.map((write) => write.ifNoneMatch)).toEqual(['*', '*'])
    expect(writes[0]!.body).toEqual(parent)
    // the ancestor of the source, not the skeleton that the API creates with the content of its descendant
    expect(await textOf('staging', onlySource)).toBe(annotationText(parent))
    const history = await getAnnotationHistory(org, project, 'staging', onlySource)
    expect(history.Entries?.at(-1)).toMatchObject({ Action: 'Created' })
    expect(history.Entries?.at(-1)?.Cause).toBeUndefined()
  })

  it('copies every missing ancestor in merge order, and leaves the ancestors that the target has', async () => {
    const key = 'uxe.initech.invoicing.prod.pdf'
    // rst.invoicing and unt.invoicing.pdf are in staging with another content
    const kept = [await textOf('staging', 'rst.invoicing'), await textOf('staging', 'unt.invoicing.pdf')]
    expect(kept[0]).not.toBe(await textOf('default', 'rst.invoicing'))
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [key] })

    const ancestors = ['sbt.initech', 'usg.initech.invoicing', 'cnt.initech.prod', 'exe.initech.invoicing.prod']
    expect(result.items).toEqual([
      ...ancestors.map((ancestor) => annotation(ancestor, 'Created', missingAncestor(key))),
      annotation(key, 'Created'),
    ])
    expect((await requests.writes()).map(line)).toEqual(
      [...ancestors, key].map((written) => `POST /staging/annotations/${written}`),
    )
    for (const written of [...ancestors, key]) {
      expect(await textOf('staging', written), written).toBe(await textOf('default', written))
    }
    expect([await textOf('staging', 'rst.invoicing'), await textOf('staging', 'unt.invoicing.pdf')]).toEqual(kept)
  })

  it('leaves an ancestor that the source lacks too to the API', async () => {
    // the execution and the context of this unit of execution are in neither view
    const orphan = 'uxe.northwind.invoicing.legacy.pdf'
    await deleteAnnotation(org, project, 'staging', orphan)
    await purgeAnnotation(org, project, 'staging', orphan)
    expect(await inStaging(orphan)).toBe(false)
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [orphan] })

    expect(result.items).toEqual([annotation(orphan, 'Created')])
    const lines = await requests.lines()
    // they were looked for in both views
    for (const ancestor of ['cnt.northwind.legacy', 'exe.northwind.invoicing.legacy']) {
      expect(lines).toContain(`GET /staging/annotations/${ancestor}?includeInactive=true`)
      expect(lines).toContain(`GET /default/annotations/${ancestor}?includeInactive=true`)
    }
    expect((await requests.writes()).map(line)).toEqual([`POST /staging/annotations/${orphan}`])
    // the API created them with the write of their descendant
    expect(await inStaging('exe.northwind.invoicing.legacy')).toBe(true)
    expect(await inStaging('cnt.northwind.legacy')).toBe(true)
  })

  it('writes nothing below an ancestor whose write failed', async () => {
    server.use(http.post(stagingAnnotation(onlySource), busy))
    const requests = recordRequests()

    const result = await copy({
      kind: 'annotations',
      keys: ['exe.initech.invoicing.prod', 'cnt.initech.staging', 'cnt.initech.prod', differs],
    })

    expect(result.items).toEqual([
      // the others are copied as ever
      annotation(differs, 'Updated'),
      annotation(onlySource, 'Failed', 'The database is busy.'),
      annotation('cnt.initech.prod', 'Skipped', ancestorMessage(onlySource)),
      annotation('cnt.initech.staging', 'Skipped', ancestorMessage(onlySource)),
      annotation('exe.initech.invoicing.prod', 'Skipped', ancestorMessage(onlySource)),
    ])
    expect(result.error).toBeUndefined()
    // the ancestor was tried once, and the API never got a descendant to create a skeleton for
    expect((await requests.writes()).map(line)).toEqual([
      `POST /staging/annotations/${differs}`,
      `POST /staging/annotations/${onlySource}`,
    ])
    for (const key of [onlySource, 'cnt.initech.prod', 'cnt.initech.staging', 'usg.initech.invoicing']) {
      expect(await inStaging(key), key).toBe(false)
    }

    // copying again once the API answers completes the copy
    server.resetHandlers()
    const again = await copy({
      kind: 'annotations',
      keys: ['exe.initech.invoicing.prod', 'cnt.initech.staging', 'cnt.initech.prod', differs],
    })
    expect(again.items).toEqual([
      annotation(differs, 'Unchanged'),
      annotation(onlySource, 'Created', missingAncestor('cnt.initech.prod')),
      annotation('cnt.initech.prod', 'Created'),
      annotation('cnt.initech.staging', 'Created'),
      annotation('usg.initech.invoicing', 'Created', missingAncestor('exe.initech.invoicing.prod')),
      annotation('exe.initech.invoicing.prod', 'Created'),
    ])
  })

  it('writes nothing below a selected key whose write failed', async () => {
    server.use(http.post(stagingAnnotation(onlySource), busy))
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: ['cnt.initech.prod', onlySource] })

    expect(result.items).toEqual([
      annotation(onlySource, 'Failed', 'The database is busy.'),
      annotation('cnt.initech.prod', 'Skipped', ancestorMessage(onlySource)),
    ])
    expect((await requests.writes()).map(line)).toEqual([`POST /staging/annotations/${onlySource}`])
  })

  it('skips a key that is deleted in the target, and every key below a deleted one, without a request', async () => {
    // sbt.contoso goes with everything below it; cnt.contoso.test was never in staging
    await deleteAnnotation(org, project, 'staging', 'sbt.contoso')
    await deleteAnnotation(org, project, 'staging', 'exe.northwind.dunning.prod')
    const requests = recordRequests()

    const result = await copy({
      kind: 'annotations',
      keys: [
        'exe.contoso.invoicing.test',
        'cnt.contoso.test',
        'cnt.contoso.prod',
        'exe.northwind.dunning.prod',
        'sbt.contoso',
      ],
    })

    expect(result.items).toEqual([
      annotation('sbt.contoso', 'Skipped', deletedMessage('sbt.contoso')),
      annotation('cnt.contoso.prod', 'Skipped', deletedMessage('sbt.contoso')),
      annotation('cnt.contoso.test', 'Skipped', deletedMessage('sbt.contoso')),
      annotation('exe.contoso.invoicing.test', 'Skipped', deletedMessage('sbt.contoso')),
      annotation('exe.northwind.dunning.prod', 'Skipped', deletedMessage('exe.northwind.dunning.prod')),
    ])
    // the deleted keys are looked up in the target: the keys and all their ancestors, in merge order
    const all = await requests.all()
    expect(all.map(line)).toEqual(['GET /views', 'POST /staging/annotations/deleted/lookup'])
    expect(all[1]!.body).toEqual({
      Keys: [
        'rst.dunning',
        'rst.invoicing',
        'sbt.contoso',
        'sbt.northwind',
        'usg.contoso.invoicing',
        'usg.northwind.dunning',
        'cnt.contoso.prod',
        'cnt.contoso.test',
        'cnt.northwind.prod',
        'exe.contoso.invoicing.test',
        'exe.northwind.dunning.prod',
      ],
    })
    // nothing was re-created: a re-creation would empty the configuration
    expect((await getDeletedAnnotation(org, project, 'staging', 'sbt.contoso')).Deletion).toBeDefined()
    expect(await inStaging('sbt.contoso')).toBe(false)
    expect(await inStaging('cnt.contoso.test')).toBe(false)
  })

  it('skips a key that is no longer in the source, and deletes nothing that only the target has', async () => {
    const before = await textOf('staging', onlyTarget)
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: ['sbt.nobody', onlyTarget] })

    expect(result.items).toEqual([
      annotation('sbt.nobody', 'Skipped', goneMessage),
      annotation(onlyTarget, 'Skipped', goneMessage),
    ])
    expect(await requests.writes()).toEqual([])
    expect(await inStaging('sbt.nobody')).toBe(false)
    expect(await textOf('staging', onlyTarget)).toBe(before)
  })

  it('fails an annotation whose values hold a number beyond 2^53, and writes nothing below it', async () => {
    const inexact = async (key: string, values: string) => {
      const body = JSON.stringify({ ...(await annotationOf('default', key)), Values: 'VALUES' })
      await send('POST', `/default/annotations/${key}`, body.replace('"VALUES"', values))
    }
    // the server keeps 9007199254740993; the browser, like the mock, reads 9007199254740992
    await inexact(differs, '{"id": 9007199254740993}')
    await inexact(onlySource, '{"limits": [{"max": 1e21}]}')
    const before = await textOf('staging', differs)
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [differs, onlySource, 'cnt.initech.prod'] })

    expect(result.items).toEqual([
      annotation(onlySource, 'Failed', inexactMessage),
      annotation(differs, 'Failed', inexactMessage),
      annotation('cnt.initech.prod', 'Skipped', ancestorMessage(onlySource)),
    ])
    expect(await requests.writes()).toEqual([])
    expect(await textOf('staging', differs)).toBe(before)
  })

  it('fails a missing ancestor with a number beyond 2^53, and skips the key that needs it', async () => {
    const parent = JSON.stringify({ ...(await annotationOf('default', onlySource)), Values: 'VALUES' })
    await send('POST', `/default/annotations/${onlySource}`, parent.replace('"VALUES"', '{"id": 9007199254740993}'))
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: ['cnt.initech.prod'] })

    expect(result.items).toEqual([
      annotation(onlySource, 'Failed', inexactMessage),
      annotation('cnt.initech.prod', 'Skipped', ancestorMessage(onlySource)),
    ])
    expect(await requests.writes()).toEqual([])
  })

  it('writes a mixed selection in merge order, whatever the order of the keys', async () => {
    const keys = [
      'uxe.initech.invoicing.prod.pdf',
      'exe.initech.invoicing.prod',
      'cnt.initech.prod',
      differs,
      'usg.initech.invoicing',
      onlySource,
      'unt.invoicing.pdf',
      'rst.invoicing',
    ]
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys })

    // ancestors before descendants, the keys of one type in key order; every key is a row of the selection
    const ordered = [
      'rst.invoicing',
      'unt.invoicing.pdf',
      onlySource,
      differs,
      'usg.initech.invoicing',
      'cnt.initech.prod',
      'exe.initech.invoicing.prod',
      'uxe.initech.invoicing.prod.pdf',
    ]
    expect(result.items).toEqual([
      annotation('rst.invoicing', 'Updated'),
      annotation('unt.invoicing.pdf', 'Updated'),
      annotation(onlySource, 'Created'),
      annotation(differs, 'Updated'),
      annotation('usg.initech.invoicing', 'Created'),
      annotation('cnt.initech.prod', 'Created'),
      annotation('exe.initech.invoicing.prod', 'Created'),
      annotation('uxe.initech.invoicing.prod.pdf', 'Created'),
    ])
    expect((await requests.writes()).map(line)).toEqual(ordered.map((key) => `POST /staging/annotations/${key}`))
    for (const key of ordered) {
      expect(await textOf('staging', key), key).toBe(await textOf('default', key))
    }
  })

  it('copies a key once, however often it is given', async () => {
    const progress: [number, number][] = []
    const requests = recordRequests()

    // the selection of a page: the run reads it and leaves it as it is
    const keys = Object.freeze([onlySource, differs, onlySource, onlySource, differs])
    const result = await copy(
      { kind: 'annotations', keys },
      { onProgress: (done, total) => progress.push([done, total]) },
    )

    expect(keys).toEqual([onlySource, differs, onlySource, onlySource, differs])
    expect(result.items).toEqual([annotation(onlySource, 'Created'), annotation(differs, 'Updated')])
    expect((await requests.writes()).map(line)).toEqual([
      `POST /staging/annotations/${onlySource}`,
      `POST /staging/annotations/${differs}`,
    ])
    expect((await requests.lookups())[0]!.body).toEqual({ Keys: [onlySource, differs] })
    expect(progress.at(-1)).toEqual([2, 2])
  })

  it('copies the missing ancestor of two selected keys once', async () => {
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: ['cnt.initech.staging', 'cnt.initech.prod'] })

    expect(result.items).toEqual([
      annotation(onlySource, 'Created', missingAncestor('cnt.initech.prod')),
      annotation('cnt.initech.prod', 'Created'),
      annotation('cnt.initech.staging', 'Created'),
    ])
    expect((await requests.writes()).map(line)).toEqual([
      `POST /staging/annotations/${onlySource}`,
      'POST /staging/annotations/cnt.initech.prod',
      'POST /staging/annotations/cnt.initech.staging',
    ])
  })

  it('copies a selected ancestor as its own row, not as the missing ancestor of its descendant', async () => {
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: ['cnt.initech.prod', onlySource] })

    expect(result.items).toEqual([annotation(onlySource, 'Created'), annotation('cnt.initech.prod', 'Created')])
    expect((await requests.writes()).map(line)).toEqual([
      `POST /staging/annotations/${onlySource}`,
      'POST /staging/annotations/cnt.initech.prod',
    ])
  })

  it('sends one request at a time', async () => {
    // ancestors have to be there before their descendants, and the mock loses updates when two writes overlap
    let open = 0
    let most = 0
    server.events.on('request:start', () => {
      most = Math.max(most, ++open)
    })
    server.events.on('response:mocked', () => {
      open--
    })

    const result = await copy({
      kind: 'annotations',
      keys: ['uxe.initech.invoicing.prod.pdf', 'cnt.initech.staging', differs, same, 'sbt.contoso', 'sbt.nobody'],
    })

    expect(result.items).toHaveLength(10)
    expect(most).toBe(1)
    expect(open).toBe(0)
  })

  it('copies in the other direction too: from staging into the production view', async () => {
    const original = await annotationOf('staging', differs)
    const kept = await textOf('staging', onlyTarget)
    const requests = recordRequests()

    const result = await copyToView(
      {
        source: target,
        target: 'default',
        kind: 'annotations',
        keys: [onlyTarget, differs, onlySource],
        existing: 'overwrite',
        missingAnnotations: true,
      },
      { client: new QueryClient() },
    )

    expect(result).toEqual({
      target: 'default',
      dryRun: false,
      items: [
        // only default has it: nothing to copy, and nothing is deleted there
        annotation(onlySource, 'Skipped', 'It is no longer in staging.'),
        annotation(differs, 'Updated'),
        annotation(onlyTarget, 'Created'),
      ],
      stopped: false,
      sources: expect.any(Object),
    })
    // the deleted keys at the start, and the deleted state of the one key it creates right before its create
    expect((await requests.lookups()).map(line)).toEqual([
      'POST /default/annotations/deleted/lookup',
      'POST /default/annotations/deleted/lookup',
    ])
    expect((await requests.writes()).map(line)).toEqual([
      `POST /default/annotations/${differs}`,
      `POST /default/annotations/${onlyTarget}`,
    ])
    expect(await textOf('default', differs)).toBe(annotationText(original))
    expect(await textOf('default', onlyTarget)).toBe(kept)
    // the source of a copy is only read
    expect(await textOf('staging', differs)).toBe(annotationText(original))
    expect((await readAnnotation(source, onlySource)).annotation).toBeDefined()
  })

  it('copies an annotation that is not active, and finds one in the target', async () => {
    const paused: Annotation = {
      ProjectName: project,
      ViewName: 'default',
      AnnotationKey: 'sbt.paused',
      Name: 'paused',
      AnnotationType: 'Subject',
      Title: 'Paused',
      IsDisabled: true,
      ExpiresAt: '2020-01-01T00:00:00Z',
    }
    await setAnnotation(org, project, 'default', 'sbt.paused', paused)
    // disabled in staging: the plain read of the API answers 404 for it
    await setAnnotation(org, project, 'staging', differs, {
      ...(await annotationOf('staging', differs)),
      IsDisabled: true,
    })
    await expect(getAnnotation(org, project, 'staging', differs)).rejects.toMatchObject({ status: 404 })
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: ['sbt.paused', differs] })

    expect(result.items).toEqual([annotation(differs, 'Updated'), annotation('sbt.paused', 'Created')])
    const writes = await requests.writes()
    // an update of the disabled one, not a creation over it
    expect(writes.map((write) => [line(write), write.ifNoneMatch])).toEqual([
      [`POST /staging/annotations/${differs}`, null],
      ['POST /staging/annotations/sbt.paused', '*'],
    ])
    expect(await annotationOf('staging', 'sbt.paused')).toMatchObject({
      IsDisabled: true,
      ExpiresAt: '2020-01-01T00:00:00Z',
    })
  })

  it('fails the key whose read failed, and goes on', async () => {
    server.use(
      http.get(`*${root}/default/annotations/sbt.contoso`, busy),
      http.get(stagingAnnotation('sbt.fabrikam'), busy),
    )
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: ['sbt.contoso', 'sbt.fabrikam', differs] })

    expect(result.items).toEqual([
      annotation('sbt.contoso', 'Failed', 'The database is busy.'),
      annotation('sbt.fabrikam', 'Failed', 'The database is busy.'),
      annotation(differs, 'Updated'),
    ])
    expect(result.error).toBeUndefined()
    expect((await requests.writes()).map(line)).toEqual([`POST /staging/annotations/${differs}`])
  })
})

// Own configurations: sbt.contoso differs between the views, usg.northwind.invoicing is the same in both,
// sbt.northwind has none in either, and exe.northwind.reporting.test has one in default while staging has neither
// its annotation nor its context cnt.northwind.test.
const differing = 'sbt.contoso'
const equal = 'usg.northwind.invoicing'
const none = 'sbt.northwind'
const unannotated = 'exe.northwind.reporting.test'

describe('copyToView: configurations', () => {
  it('creates the configuration with a POST when the target has no own document', async () => {
    await deleteConfiguration(org, project, 'staging', differing)
    const document = (await ownOf('default', differing))!
    const version = (await readConfiguration(source, differing)).version
    const requests = recordRequests()

    const result = await copy({ kind: 'configurations', keys: [differing] })

    expect(result).toEqual({
      target: 'staging',
      dryRun: false,
      items: [configuration(differing, 'Created')],
      stopped: false,
      sources: { [differing]: canonicalJson(document) },
    })
    // POST has no condition, so the copy reads the document back: one written meanwhile would be merged into
    expect(await requests.lines()).toEqual([
      'GET /views',
      'POST /staging/annotations/deleted/lookup',
      `GET /default/configuration/${differing}`,
      `GET /default/configuration/${differing}/versions/${version}`,
      `GET /staging/annotations/${differing}?includeInactive=true`,
      `GET /staging/configuration/${differing}`,
      `GET /staging/configuration/${differing}/versions/0`,
      `POST /staging/configuration/${differing}`,
      `GET /staging/configuration/${differing}`,
      expect.stringMatching(new RegExp(`^GET /staging/configuration/${differing.replace(/\./g, '\\.')}/versions/\\d+$`)),
    ])
    const [write] = await requests.writes()
    expect(write!.body).toEqual(document)
    expect(write!.contentType).toBe('application/json')
    expect(await ownOf('staging', differing)).toEqual(document)
  })

  it('creates it with a patch of the root when the own document of the target is a written version without members', async () => {
    // the editor's "Create empty": a stored document that counts as none; a PATCH has a version to test, a POST no
    // condition at all
    await deleteConfiguration(org, project, 'staging', differing)
    await setConfiguration(org, project, 'staging', differing, {})
    expect((await readConfiguration(target, differing)).version).toBeGreaterThan(0)
    const document = (await ownOf('default', differing))!
    const requests = recordRequests()

    const result = await copy({ kind: 'configurations', keys: [differing] })

    expect(result.items).toEqual([configuration(differing, 'Created')])
    const writes = await requests.writes()
    expect(writes.map(line)).toEqual([`PATCH /staging/configuration/${differing}`])
    expect(writes[0]!.body).toEqual([
      { op: 'test', path: '', value: {} },
      { op: 'replace', path: '', value: document },
    ])
    expect(await ownOf('staging', differing)).toEqual(document)
  })

  it('fails a first write that another client wrote a document into meanwhile, which the POST merged into', async () => {
    await deleteConfiguration(org, project, 'staging', differing)
    expect((await readConfiguration(target, differing)).version).toBe(0)
    const document = (await ownOf('default', differing))!
    // another client writes a member of its own between the copy's read and its POST
    let concurrent = false
    server.use(
      http.post(`*/api/v1/${org}/${project}/staging/configuration/${differing}`, async () => {
        if (!concurrent) {
          concurrent = true
          await setConfiguration(org, project, 'staging', differing, { concurrent: 'value' })
        }
        return undefined
      }),
    )

    const result = await copy({ kind: 'configurations', keys: [differing] })

    expect(result.items).toEqual([
      configuration(
        differing,
        'Failed',
        'staging got its own configuration meanwhile, and the copy was merged into it. Check it there.',
      ),
    ])
    expect(await ownOf('staging', differing)).toEqual({ ...document, concurrent: 'value' })
  })

  it('skips a configuration whose annotation went from the target after the run found it there', async () => {
    // the annotation is read as there, and gone by the time its configuration is read
    server.use(
      http.get(`*/api/v1/${org}/${project}/staging/configuration/${differing}`, () =>
        HttpResponse.json({ Message: 'The annotation does not exist.', ErrorCode: 'NotFound' }, { status: 404 }),
      ),
    )
    const requests = recordRequests()

    const result = await copy({ kind: 'configurations', keys: [differing] })

    expect(result.items).toEqual([
      configuration(differing, 'Skipped', 'Its annotation is not in staging any more.'),
    ])
    expect(await requests.writes()).toEqual([])
  })

  it('replaces a different own document with a patch of the root: a test of what it read, then a replace', async () => {
    // names that a JSON Pointer has to escape, which the API does not decode
    await setConfiguration(org, project, 'default', differing, {
      'a/b': { 'c~d': 1, '/': [] },
      'm~n': [1, { '~1': '/' }],
      '': 'no name',
    })
    const document = (await ownOf('default', differing))!
    const before = (await ownOf('staging', differing))!
    expect(Object.keys(document)).toEqual(expect.arrayContaining(['a/b', 'm~n', '', 'baseUrl']))
    expect(canonicalJson(before)).not.toBe(canonicalJson(document))
    const requests = recordRequests()

    const result = await copy({ kind: 'configurations', keys: [differing] })

    expect(result.items).toEqual([configuration(differing, 'Updated')])
    const writes = await requests.writes()
    expect(writes.map(line)).toEqual([`PATCH /staging/configuration/${differing}`])
    expect(writes[0]!.contentType).toBe('application/json-patch+json')
    // exactly the patch of sform and the MCP server: no path to escape
    expect(writes[0]!.body).toEqual([
      { op: 'test', path: '', value: before },
      { op: 'replace', path: '', value: document },
    ])
    expect(JSON.stringify(writes[0]!.body)).toBe(
      JSON.stringify([
        { op: 'test', path: '', value: before },
        { op: 'replace', path: '', value: document },
      ]),
    )

    const copied = (await ownOf('staging', differing))!
    expect(copied).toEqual(document)
    expect(canonicalJson(copied)).toBe(canonicalJson(document))
    // what only staging had is gone, and the names arrived as they are
    expect(Object.keys(copied).toSorted()).toEqual(Object.keys(document).toSorted())
    expect(copied['a/b']).toEqual({ 'c~d': 1, '/': [] })
    expect(copied['m~n']).toEqual([1, { '~1': '/' }])
  })

  it('leaves an equal own document alone, also one with its members in another order', async () => {
    const requests = recordRequests()
    expect((await copy({ kind: 'configurations', keys: [equal] })).items).toEqual([configuration(equal, 'Unchanged')])
    expect(await requests.writes()).toEqual([])

    // the same document, written with its members the other way round
    const document = (await ownOf('default', differing))!
    const reversed = Object.fromEntries(Object.entries(document).toReversed())
    await deleteConfiguration(org, project, 'staging', differing)
    await setConfiguration(org, project, 'staging', differing, reversed)
    expect(JSON.stringify(await ownOf('staging', differing))).not.toBe(JSON.stringify(document))

    const again = recordRequests()
    expect((await copy({ kind: 'configurations', keys: [differing] })).items).toEqual([
      configuration(differing, 'Unchanged'),
    ])
    expect(await again.writes()).toEqual([])
  })

  it('skips a configuration that the target has with another content when asked to', async () => {
    await deleteConfiguration(org, project, 'staging', equal)
    const before = await ownOf('staging', differing)
    const requests = recordRequests()

    const result = await copy({ kind: 'configurations', keys: [differing, equal], existing: 'skip' })

    expect(result.items).toEqual([
      configuration(differing, 'Skipped', 'staging has its own configuration.'),
      // the target has none: there is nothing to skip
      configuration(equal, 'Created'),
    ])
    expect((await requests.writes()).map(line)).toEqual([`POST /staging/configuration/${equal}`])
    expect(await ownOf('staging', differing)).toEqual(before)
  })

  it('skips a key without an own document in the source, and one whose document has no members', async () => {
    await deleteConfiguration(org, project, 'default', equal)
    await setConfiguration(org, project, 'default', equal, {})
    const before = await ownOf('staging', equal)
    expect(before).toBeDefined()
    const requests = recordRequests()

    const result = await copy({ kind: 'configurations', keys: [none, equal, 'sbt.nobody'] })

    const message = 'default has no own configuration for it.'
    expect(result.items).toEqual([
      configuration('sbt.nobody', 'Skipped', message),
      configuration(none, 'Skipped', message),
      configuration(equal, 'Skipped', message),
    ])
    const all = await requests.all()
    // the target is not even read, and nothing of it is emptied or deleted
    expect(all.filter((request) => request.path.startsWith('/staging') && !isLookup(request))).toEqual([])
    expect(await requests.writes()).toEqual([])
    expect(await ownOf('staging', equal)).toEqual(before)
  })

  it('copies a missing annotation and its missing ancestors first when asked to', async () => {
    const document = (await ownOf('default', unannotated))!
    const requests = recordRequests()

    const result = await copy({ kind: 'configurations', keys: [unannotated], missingAnnotations: true })

    expect(result.items).toEqual([
      annotation('cnt.northwind.test', 'Created', missingAncestor(unannotated)),
      annotation(unannotated, 'Created', neededMessage),
      configuration(unannotated, 'Created'),
    ])
    const writes = await requests.writes()
    expect(writes.map(line)).toEqual([
      'POST /staging/annotations/cnt.northwind.test',
      `POST /staging/annotations/${unannotated}`,
      `POST /staging/configuration/${unannotated}`,
    ])
    expect(writes.map((write) => write.ifNoneMatch)).toEqual(['*', '*', null])
    expect(writes[2]!.body).toEqual(document)
    expect(await textOf('staging', unannotated)).toBe(await textOf('default', unannotated))
    expect(await textOf('staging', 'cnt.northwind.test')).toBe(await textOf('default', 'cnt.northwind.test'))
    expect(await ownOf('staging', unannotated)).toEqual(document)
  })

  it('skips a configuration whose annotation is missing when not asked to copy it, and writes nothing', async () => {
    const requests = recordRequests()

    const result = await copy({ kind: 'configurations', keys: [unannotated], missingAnnotations: false })

    expect(result.items).toEqual([configuration(unannotated, 'Skipped', 'Its annotation is not in staging.')])
    expect(await requests.writes()).toEqual([])
    expect(await inStaging(unannotated)).toBe(false)
    expect(await inStaging('cnt.northwind.test')).toBe(false)
  })

  it('fails a document with a root member named $remove or $patch, which a first write would run', async () => {
    await setConfiguration(org, project, 'default', differing, { $remove: ['region'] })
    await setConfiguration(org, project, 'default', equal, { $patch: [{ op: 'remove', path: '/mode' }] })
    // below the root it is a member like any other
    await setConfiguration(org, project, 'default', 'unt.invoicing.pdf', { nested: { $remove: ['x'], $patch: [] } })
    const before = await ownOf('staging', differing)
    const requests = recordRequests()

    const result = await copy({ kind: 'configurations', keys: [differing, equal, 'unt.invoicing.pdf'] })

    const message = 'It has a member named $remove or $patch, which a first write would run as a directive.'
    expect(result.items).toEqual([
      configuration('unt.invoicing.pdf', 'Updated'),
      configuration(differing, 'Failed', message),
      configuration(equal, 'Failed', message),
    ])
    expect((await requests.writes()).map(line)).toEqual(['PATCH /staging/configuration/unt.invoicing.pdf'])
    expect(await ownOf('staging', differing)).toEqual(before)
    expect(await ownOf('staging', 'unt.invoicing.pdf')).toEqual(await ownOf('default', 'unt.invoicing.pdf'))
  })

  it('fails a document with a number beyond 2^53', async () => {
    await send('POST', `/default/configuration/${differing}`, '{"limits": {"max": 9007199254740993}}')
    const before = await ownOf('staging', differing)
    const requests = recordRequests()

    const result = await copy({ kind: 'configurations', keys: [differing] })

    expect(result.items).toEqual([configuration(differing, 'Failed', inexactMessage)])
    expect(await requests.writes()).toEqual([])
    expect(await ownOf('staging', differing)).toEqual(before)
  })

  it('fails a document whose target holds a number beyond 2^53, which the test of a patch cannot hold', async () => {
    // the server keeps 9007199254740993; the browser, like the mock, reads 9007199254740992
    await send('POST', `/staging/configuration/${differing}`, '{"limit": 9007199254740993}')
    const before = (await ownOf('staging', differing))!
    expect(before.limit).toBe(9007199254740992)
    // the document of default has no such number, and differs
    expect(canonicalJson(before)).not.toBe(canonicalJson(await ownOf('default', differing)))
    const message = 'Its configuration in staging holds a number beyond 2^53. Copy it with Export & Import or sform.'

    // a check says what the copy does
    for (const dryRun of [true, false]) {
      const requests = recordRequests()

      const result = await copy({ kind: 'configurations', keys: [differing, equal], dryRun })

      expect(result.items, `dryRun ${dryRun}`).toEqual([
        configuration(differing, 'Failed', message),
        // the failure of one key does not stop the run
        configuration(equal, 'Unchanged'),
      ])
      expect(result.error).toBeUndefined()
      // no patch whose test would hold the rounded number, and answer 412 on every try
      expect(await requests.writes()).toEqual([])
      server.events.removeAllListeners()
    }

    // Skip writes nothing to a document of the target, so its number is no matter
    const requests = recordRequests()
    const skipped = await copy({ kind: 'configurations', keys: [differing], existing: 'skip' })
    expect(skipped.items).toEqual([configuration(differing, 'Skipped', 'staging has its own configuration.')])
    expect(await requests.writes()).toEqual([])
    expect(await ownOf('staging', differing)).toEqual(before)
  })

  it('says which schema of the target a document does not satisfy', async () => {
    // staging takes one region only; the document of default has the other
    await send(
      'POST',
      `/staging/configuration-schema/${differing}?force=true`,
      JSON.stringify({
        type: 'object',
        properties: { region: { enum: [(await ownOf('staging', differing))!.region] } },
      }),
    )
    const before = await ownOf('staging', differing)
    const requests = recordRequests()

    const result = await copy({ kind: 'configurations', keys: [differing, equal] })

    expect(result.items).toEqual([
      configuration(differing, 'Failed', 'It does not satisfy the schema of staging: /region Not in enumeration'),
      // the failure of one key does not stop the run
      configuration(equal, 'Unchanged'),
    ])
    expect(result.error).toBeUndefined()
    expect((await requests.writes()).map(line)).toEqual([`PATCH /staging/configuration/${differing}`])
    expect(await ownOf('staging', differing)).toEqual(before)
  })

  it('names the schema failure without a detail when the answer has none for the key', async () => {
    server.use(
      http.patch(stagingConfiguration(differing), () =>
        HttpResponse.json(
          {
            Message: 'The configuration does not comply with its schema.',
            ErrorCode: 'SchemaValidationFailed',
            Errors: [],
          },
          { status: 409 },
        ),
      ),
    )

    const result = await copy({ kind: 'configurations', keys: [differing] })

    expect(result.items).toEqual([configuration(differing, 'Failed', 'It does not satisfy the schema of staging.')])
  })

  it('says that the target changed when the test of the patch fails', async () => {
    // somebody writes the configuration of staging between the read and the patch; the mock then answers the patch
    server.use(
      http.patch(stagingConfiguration(differing), async () => {
        await setConfiguration(org, project, 'staging', differing, { changedMeanwhile: true })
      }),
    )
    const requests = recordRequests()

    const result = await copy({ kind: 'configurations', keys: [differing] })

    expect(result.items).toEqual([configuration(differing, 'Failed', changedMessage)])
    expect(result.error).toBeUndefined()
    expect((await requests.writes()).map(line)).toContain(`PATCH /staging/configuration/${differing}`)
    // the change of the other writer is kept
    expect(await ownOf('staging', differing)).toMatchObject({ changedMeanwhile: true })
    expect(canonicalJson(await ownOf('staging', differing))).not.toBe(canonicalJson(await ownOf('default', differing)))
  })

  it('skips a key that is deleted in the target, or below a deleted key, without a request', async () => {
    await deleteAnnotation(org, project, 'staging', 'sbt.contoso')
    const requests = recordRequests()

    const result = await copy({ kind: 'configurations', keys: ['exe.contoso.invoicing.test', differing] })

    expect(result.items).toEqual([
      configuration(differing, 'Skipped', deletedMessage('sbt.contoso')),
      // its annotation was never in staging: it would be created below a deleted subject
      configuration('exe.contoso.invoicing.test', 'Skipped', deletedMessage('sbt.contoso')),
    ])
    expect(await requests.lines()).toEqual(['GET /views', 'POST /staging/annotations/deleted/lookup'])
  })

  it('never writes a configuration for a key whose annotation is not in the target', async () => {
    const descendant = 'uxe.northwind.reporting.test.xlsx'
    const keys = [unannotated, descendant]
    const configurationWrites = async (requests: ReturnType<typeof recordRequests>) =>
      (await requests.writes()).filter((request) => request.path.includes('/configuration'))

    // not asked to copy the annotation
    let requests = recordRequests()
    let result = await copy({ kind: 'configurations', keys, missingAnnotations: false })
    expect(result.items).toEqual([
      configuration(unannotated, 'Skipped', 'Its annotation is not in staging.'),
      configuration(descendant, 'Skipped', 'Its annotation is not in staging.'),
    ])
    expect(await requests.writes()).toEqual([])

    // the write of an ancestor fails: nothing below it is written, neither annotation nor configuration
    server.events.removeAllListeners()
    server.use(http.post(stagingAnnotation('cnt.northwind.test'), busy))
    requests = recordRequests()
    result = await copy({ kind: 'configurations', keys })
    expect(result.items).toEqual([
      annotation('cnt.northwind.test', 'Failed', 'The database is busy.'),
      annotation(unannotated, 'Skipped', ancestorMessage('cnt.northwind.test')),
      configuration(unannotated, 'Skipped', 'Its annotation was not copied.'),
      annotation(descendant, 'Skipped', ancestorMessage('cnt.northwind.test')),
      configuration(descendant, 'Skipped', 'Its annotation was not copied.'),
    ])
    expect((await requests.writes()).map(line)).toEqual(['POST /staging/annotations/cnt.northwind.test'])

    // the write of the annotation itself fails: its ancestor is there, its configuration is not written
    server.events.removeAllListeners()
    server.resetHandlers()
    server.use(http.post(stagingAnnotation(unannotated), busy))
    requests = recordRequests()
    result = await copy({ kind: 'configurations', keys })
    expect(result.items).toEqual([
      annotation('cnt.northwind.test', 'Created', missingAncestor(unannotated)),
      annotation(unannotated, 'Failed', 'The database is busy.'),
      configuration(unannotated, 'Skipped', 'Its annotation was not copied.'),
      annotation(descendant, 'Skipped', ancestorMessage(unannotated)),
      configuration(descendant, 'Skipped', 'Its annotation was not copied.'),
    ])
    expect(await configurationWrites(requests)).toEqual([])
    expect(await inStaging(unannotated)).toBe(false)

    // the annotation appears while it is created: the API refuses the creation, and the configuration waits
    server.events.removeAllListeners()
    server.resetHandlers()
    server.use(
      http.post(stagingAnnotation(unannotated), () =>
        HttpResponse.json(
          { Message: 'The annotation exists.', ErrorCode: 'AnnotationPreconditionFailed' },
          { status: 412 },
        ),
      ),
    )
    requests = recordRequests()
    result = await copy({ kind: 'configurations', keys: [unannotated] })
    expect(result.items).toEqual([
      annotation(unannotated, 'Failed', changedMessage),
      configuration(unannotated, 'Skipped', 'Its annotation was not copied.'),
    ])
    expect(await configurationWrites(requests)).toEqual([])
  })

  it('never changes an annotation of the target, also one that appears while the run looks for it', async () => {
    // staging gets the annotation, with another content, between the two reads of the run
    await setAnnotation(org, project, 'staging', unannotated, {
      ...(await annotationOf('default', unannotated)),
      Title: 'Made in staging meanwhile',
    })
    let reads = 0
    server.use(
      http.get(stagingAnnotation(unannotated), () =>
        ++reads === 1
          ? HttpResponse.json({ Message: 'The annotation does not exist.', ErrorCode: 'NotFound' }, { status: 404 })
          : undefined,
      ),
    )
    const requests = recordRequests()

    const result = await copy({ kind: 'configurations', keys: [unannotated] })

    // "Copy missing annotations too" creates annotations; it never overwrites one
    expect((await requests.writes()).map(line)).toEqual([`POST /staging/configuration/${unannotated}`])
    expect(result.items).toEqual([configuration(unannotated, 'Created')])
    expect((await annotationOf('staging', unannotated)).Title).toBe('Made in staging meanwhile')
  })

  it('copies in the other direction too: from staging into the production view', async () => {
    const document = (await ownOf('staging', differing))!
    const before = (await ownOf('default', differing))!
    const requests = recordRequests()

    const result = await copyToView(
      {
        source: target,
        target: 'default',
        kind: 'configurations',
        keys: [differing],
        existing: 'overwrite',
        missingAnnotations: true,
      },
      { client: new QueryClient() },
    )

    expect(result.items).toEqual([configuration(differing, 'Updated')])
    const writes = await requests.writes()
    expect(writes.map(line)).toEqual([`PATCH /default/configuration/${differing}`])
    expect(writes[0]!.body).toEqual([
      { op: 'test', path: '', value: before },
      { op: 'replace', path: '', value: document },
    ])
    expect(await ownOf('default', differing)).toEqual(document)
    expect(await ownOf('staging', differing)).toEqual(document)
  })

  it('leaves the annotations of the target alone when it copies their configurations', async () => {
    // sbt.contoso differs as an annotation too
    const before = await textOf('staging', differing)
    expect(before).not.toBe(await textOf('default', differing))
    const requests = recordRequests()

    await copy({ kind: 'configurations', keys: [differing, equal] })

    expect((await requests.writes()).filter((request) => request.path.includes('/annotations/'))).toEqual([])
    expect(await textOf('staging', differing)).toBe(before)
  })
})

describe('copyToView: what a check showed, and what changed since', () => {
  it('skips a key whose source changed since the check, and copies the others', async () => {
    const plan = await copy({ kind: 'configurations', keys: [differing, 'usg.northwind.invoicing'], dryRun: true })
    expect(Object.keys(plan.sources).toSorted()).toEqual([differing, 'usg.northwind.invoicing'].toSorted())
    // another client changes the source of one key after the check
    await setConfiguration(org, project, 'default', differing, { changedAfterTheCheck: true })
    const requests = recordRequests()

    const result = await copy({
      kind: 'configurations',
      keys: [differing, 'usg.northwind.invoicing'],
      checked: plan.sources,
    })

    expect(result.items).toEqual([
      configuration(differing, 'Skipped', 'It changed in default since it was checked.'),
      configuration('usg.northwind.invoicing', 'Unchanged'),
    ])
    expect(await requests.writes()).toEqual([])
  })

  it('skips an annotation whose source changed since the check', async () => {
    const plan = await copy({ kind: 'annotations', keys: [differs], dryRun: true })
    expect(plan.items).toEqual([annotation(differs, 'Updated')])
    const changed = await annotationOf('default', differs)
    await setAnnotation(org, project, 'default', differs, { ...changed, Title: 'Changed after the check' })
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [differs], checked: plan.sources })

    expect(result.items).toEqual([annotation(differs, 'Skipped', 'It changed in default since it was checked.')])
    expect(await requests.writes()).toEqual([])
  })

  it('does not create a key that was deleted in the target after the run looked up the deleted keys', async () => {
    // the lookup at the start finds nothing; the one right before the create finds the key deleted meanwhile
    server.use(
      http.post(`*/api/v1/${org}/${project}/staging/annotations/deleted/lookup`, async ({ request }) => {
        const { Keys } = (await request.clone().json()) as { Keys: string[] }
        return Keys.length === 1 && Keys[0] === onlySource
          ? HttpResponse.json([{ ...(await annotationOf('default', onlySource)), ViewName: 'staging' }])
          : undefined
      }),
    )
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [onlySource] })

    expect(result.items).toEqual([
      annotation(onlySource, 'Skipped', `${onlySource} is deleted in staging. Restore it there first.`),
    ])
    expect(await requests.writes()).toEqual([])
  })
})

describe('copyToView: the run', () => {
  const selection = ['sbt.contoso', 'sbt.fabrikam', onlySource, differs]

  it('fails as a whole for a target that is not a view of the project: a typing error creates no view', async () => {
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [onlySource, differs], target: 'stagin' })

    expect(result).toMatchObject({ target: 'stagin', dryRun: false, items: [], stopped: false })
    expect(result.error).toMatchObject({ message: 'stagin is not a view of this project.' })
    expect(await requests.lines()).toEqual(['GET /views'])
    expect((await getViews(org, project)).map((view) => view.Name)).toEqual(['default', 'staging'])
  })

  it('fails as a whole when the target is the source itself', async () => {
    const before = await textOf('default', differs)
    for (const kind of ['annotations', 'configurations'] as const) {
      const requests = recordRequests()

      const result = await copy({ kind, keys: [differs, differing], target: 'default' })

      expect(result.items).toEqual([])
      expect(result.error).toBeInstanceOf(Error)
      expect(await requests.lines()).toEqual(['GET /views'])
      server.events.removeAllListeners()
    }
    expect(await textOf('default', differs)).toBe(before)
  })

  it('fails as a whole when the views or the deleted keys cannot be read', async () => {
    server.use(http.get(`*${root}/views`, busy))
    let requests = recordRequests()
    let result = await copy({ kind: 'annotations', keys: [onlySource] })
    expect(result.items).toEqual([])
    expect(result.error).toMatchObject({ status: 500, message: 'The database is busy.' })
    expect(await requests.lines()).toEqual(['GET /views'])

    server.events.removeAllListeners()
    server.resetHandlers()
    server.use(http.post(`*${root}/staging/annotations/deleted/lookup`, busy))
    requests = recordRequests()
    result = await copy({ kind: 'configurations', keys: [differing] })
    expect(result.items).toEqual([])
    expect(result.error).toMatchObject({ status: 500 })
    expect(await requests.lines()).toEqual(['GET /views', 'POST /staging/annotations/deleted/lookup'])
  })

  it('writes nothing for a key that is no annotation key', async () => {
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [onlySource, 'no key at all'] })

    // the API refuses the lookup, so the run ends before its first key
    expect(result.items).toEqual([])
    expect(result.error).toMatchObject({ status: 400 })
    expect(await requests.writes()).toEqual([])
  })

  it('does nothing for an empty selection', async () => {
    const progress: [number, number][] = []
    const requests = recordRequests()

    const result = await copy(
      { kind: 'annotations', keys: [] },
      { onProgress: (done, total) => progress.push([done, total]) },
    )

    expect(result).toEqual({ target: 'staging', dryRun: false, items: [], stopped: false, sources: {} })
    // no lookup without keys: the API refuses an empty list
    expect(await requests.lines()).toEqual(['GET /views'])
    expect(progress).toEqual([[0, 0]])
  })

  it('looks the deleted keys up a hundred at a time, all of them in the target', async () => {
    // 60 executions with their own subjects: 241 keys with the ancestors
    const keys = Array.from({ length: 60 }, (_, index) => `exe.bulk${index}.invoicing.prod`)
    const deleted = 'uxe.northwind.dunning.staging.letter'
    await deleteAnnotation(org, project, 'staging', deleted)
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [...keys, deleted] })

    const lookups = await requests.lookups()
    expect(lookups.map(line)).toEqual(Array.from({ length: 3 }, () => 'POST /staging/annotations/deleted/lookup'))
    const sent = lookups.map((lookup) => (lookup.body as { Keys: string[] }).Keys)
    expect(sent.map((batch) => batch.length)).toEqual([100, 100, 48])
    expect(new Set(sent.flat()).size).toBe(248)
    // found in the last batch
    expect(sent[2]).toContain(deleted)
    expect(result.items.at(-1)).toEqual(annotation(deleted, 'Skipped', deletedMessage(deleted)))
    expect(result.items).toHaveLength(61)
    expect(await requests.writes()).toEqual([])
  })

  it('skips an annotation that is not what the comparison saw, and copies one that is', async () => {
    const seen = annotationStamp(await annotationOf('staging', differs))
    expect(seen).toEqual(expect.any(String))
    const expected = Object.freeze({
      [differs]: seen,
      // seen without an annotation in staging, and it still has none
      [onlySource]: null,
      // seen with another stamp
      'sbt.contoso': '2020-01-01T00:00:00.000Z',
      // seen without an annotation, and it has one
      'sbt.fabrikam': null,
      // seen in staging, and gone since
      'sbt.umbrella': '2020-01-01T00:00:00.000Z',
    })
    const before = await textOf('staging', 'sbt.contoso')
    const requests = recordRequests()

    const result = await copy({
      kind: 'annotations',
      keys: [...Object.keys(expected), 'cnt.umbrella.prod', 'sbt.globex'],
      expected,
    })

    expect(result.items).toEqual([
      annotation('sbt.contoso', 'Skipped', comparedMessage),
      annotation('sbt.fabrikam', 'Skipped', comparedMessage),
      // a key that the comparison did not see is copied as ever
      annotation('sbt.globex', 'Updated'),
      annotation(onlySource, 'Created'),
      annotation(differs, 'Updated'),
      annotation('sbt.umbrella', 'Skipped', comparedMessage),
      // and nothing is created below a key that was not copied
      annotation('cnt.umbrella.prod', 'Skipped', ancestorMessage('sbt.umbrella')),
    ])
    expect((await requests.writes()).map(line)).toEqual([
      'POST /staging/annotations/sbt.globex',
      `POST /staging/annotations/${onlySource}`,
      `POST /staging/annotations/${differs}`,
    ])
    expect(await textOf('staging', 'sbt.contoso')).toBe(before)
    expect(await inStaging('sbt.umbrella')).toBe(false)
  })

  it('skips an annotation that changed in the target after the comparison', async () => {
    const stored = await annotationOf('staging', differs)
    const expected = { [differs]: annotationStamp(stored) }
    await setAnnotation(org, project, 'staging', differs, { ...stored, Title: 'Changed since the comparison' })
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: [differs], expected })

    expect(result.items).toEqual([annotation(differs, 'Skipped', comparedMessage)])
    expect(await requests.writes()).toEqual([])
    expect((await annotationOf('staging', differs)).Title).toBe('Changed since the comparison')
  })

  it('skips a configuration whose version is not the one the comparison saw, and copies one that is', async () => {
    const version = (await readConfiguration(target, differing)).version
    expect(version).toBeGreaterThan(0)
    await setConfiguration(org, project, 'default', equal, { added: true })
    const stale = (await readConfiguration(target, equal)).version - 1
    const before = await ownOf('staging', equal)
    const requests = recordRequests()

    const result = await copy({
      kind: 'configurations',
      keys: [differing, equal, unannotated],
      // the comparison read version 0 for a key whose annotation staging lacks
      expected: { [differing]: version, [equal]: stale, [unannotated]: 0 },
    })

    expect(result.items).toEqual([
      configuration(differing, 'Updated'),
      configuration(equal, 'Skipped', comparedMessage),
      annotation('cnt.northwind.test', 'Created', missingAncestor(unannotated)),
      annotation(unannotated, 'Created', neededMessage),
      configuration(unannotated, 'Created'),
    ])
    expect((await requests.writes()).map(line)).toEqual([
      `PATCH /staging/configuration/${differing}`,
      'POST /staging/annotations/cnt.northwind.test',
      `POST /staging/annotations/${unannotated}`,
      `POST /staging/configuration/${unannotated}`,
    ])
    expect(await ownOf('staging', equal)).toEqual(before)
  })

  it('takes the decisions of a copy in a dry run, and sends no write: annotations', async () => {
    const keys = [same, 'exe.initech.invoicing.prod', 'sbt.nobody', 'cnt.initech.prod', differs, onlySource, onlyTarget]
    const before = await textOf('staging', differs)
    const requests = recordRequests()

    const check = await copy({ kind: 'annotations', keys, dryRun: true })

    expect(check.dryRun).toBe(true)
    expect(check.items).toEqual([
      annotation(onlySource, 'Created'),
      annotation('sbt.nobody', 'Skipped', goneMessage),
      annotation(differs, 'Updated'),
      annotation(onlyTarget, 'Skipped', goneMessage),
      // its subject would be there by then
      annotation('cnt.initech.prod', 'Created'),
      annotation('usg.initech.invoicing', 'Created', missingAncestor('exe.initech.invoicing.prod')),
      annotation('exe.initech.invoicing.prod', 'Created'),
      annotation(same, 'Unchanged'),
    ])
    // the lookup of the deleted keys is the only request that is no GET
    const all = await requests.all()
    expect(all.filter((request) => request.method !== 'GET').map(line)).toEqual([
      'POST /staging/annotations/deleted/lookup',
    ])
    expect(await inStaging(onlySource)).toBe(false)
    expect(await textOf('staging', differs)).toBe(before)

    // the copy that follows does what the check said
    server.events.removeAllListeners()
    const writes = recordRequests()
    const real = await copy({ kind: 'annotations', keys })
    expect(real.dryRun).toBe(false)
    expect(real.items).toEqual(check.items)
    const written = await writes.writes()
    expect(written.map(line)).toEqual(
      [onlySource, differs, 'cnt.initech.prod', 'usg.initech.invoicing', 'exe.initech.invoicing.prod'].map(
        (key) => `POST /staging/annotations/${key}`,
      ),
    )
    // a copy sends POSTs and PATCHes only: it never deletes
    expect(new Set((await writes.all()).map((request) => request.method))).toEqual(new Set(['GET', 'POST']))

    // copying again is safe: what was copied comes back unchanged
    server.events.removeAllListeners()
    const again = recordRequests()
    expect((await copy({ kind: 'annotations', keys })).items).toEqual([
      annotation(onlySource, 'Unchanged'),
      annotation('sbt.nobody', 'Skipped', goneMessage),
      annotation(differs, 'Unchanged'),
      annotation(onlyTarget, 'Skipped', goneMessage),
      annotation('cnt.initech.prod', 'Unchanged'),
      annotation('exe.initech.invoicing.prod', 'Unchanged'),
      annotation(same, 'Unchanged'),
    ])
    expect(await again.writes()).toEqual([])
  })

  it('takes the decisions of a copy in a dry run, and sends no write: configurations', async () => {
    const descendant = 'uxe.northwind.reporting.test.xlsx'
    const keys = [descendant, none, unannotated, equal, differing]
    const before = await ownOf('staging', differing)
    const requests = recordRequests()

    const check = await copy({ kind: 'configurations', keys, dryRun: true })

    expect(check.dryRun).toBe(true)
    expect(check.items).toEqual([
      configuration(differing, 'Updated'),
      configuration(none, 'Skipped', 'default has no own configuration for it.'),
      configuration(equal, 'Unchanged'),
      annotation('cnt.northwind.test', 'Created', missingAncestor(unannotated)),
      annotation(unannotated, 'Created', neededMessage),
      configuration(unannotated, 'Created'),
      // its execution and its context would be there by then
      annotation(descendant, 'Created', neededMessage),
      configuration(descendant, 'Created'),
    ])
    const all = await requests.all()
    expect(all.filter((request) => request.method !== 'GET').map(line)).toEqual([
      'POST /staging/annotations/deleted/lookup',
    ])
    expect(await ownOf('staging', differing)).toEqual(before)
    expect(await inStaging(unannotated)).toBe(false)
    expect(await inStaging('cnt.northwind.test')).toBe(false)

    server.events.removeAllListeners()
    const writes = recordRequests()
    const real = await copy({ kind: 'configurations', keys })
    expect(real.items).toEqual(check.items)
    expect((await writes.writes()).map(line)).toEqual([
      `PATCH /staging/configuration/${differing}`,
      'POST /staging/annotations/cnt.northwind.test',
      `POST /staging/annotations/${unannotated}`,
      `POST /staging/configuration/${unannotated}`,
      `POST /staging/annotations/${descendant}`,
      `POST /staging/configuration/${descendant}`,
    ])
    expect(new Set((await writes.all()).map((request) => request.method))).toEqual(new Set(['GET', 'POST', 'PATCH']))
    for (const key of [differing, unannotated, descendant]) {
      expect(await ownOf('staging', key), key).toEqual(await ownOf('default', key))
    }

    server.events.removeAllListeners()
    const again = recordRequests()
    expect((await copy({ kind: 'configurations', keys })).items).toEqual([
      configuration(differing, 'Unchanged'),
      configuration(none, 'Skipped', 'default has no own configuration for it.'),
      configuration(equal, 'Unchanged'),
      configuration(unannotated, 'Unchanged'),
      configuration(descendant, 'Unchanged'),
    ])
    expect(await again.writes()).toEqual([])
  })

  it('copies nothing when it was stopped before its first key', async () => {
    const progress: [number, number][] = []
    const requests = recordRequests()

    const result = await copy(
      { kind: 'annotations', keys: selection },
      { signal: AbortSignal.abort(), onProgress: (done, total) => progress.push([done, total]) },
    )

    expect(result.stopped).toBe(true)
    expect(result.error).toBeUndefined()
    expect(result.items).toEqual(selection.map((key) => annotation(key, 'Skipped', stoppedMessage)))
    expect(await requests.writes()).toEqual([])
    expect(progress).toEqual([[0, 4]])
  })

  it('finishes the key under way when it is stopped, and skips the others', async () => {
    const controller = new AbortController()
    const requests = recordRequests()

    const result = await copy(
      { kind: 'annotations', keys: selection },
      { signal: controller.signal, onProgress: (done) => done === 2 && controller.abort() },
    )

    expect(result.stopped).toBe(true)
    expect(result.items).toEqual([
      annotation('sbt.contoso', 'Updated'),
      annotation('sbt.fabrikam', 'Updated'),
      annotation(onlySource, 'Skipped', stoppedMessage),
      annotation(differs, 'Skipped', stoppedMessage),
    ])
    expect(stoppedMessage).toBe('Not copied, the copy was stopped.')
    expect((await requests.writes()).map(line)).toEqual([
      'POST /staging/annotations/sbt.contoso',
      'POST /staging/annotations/sbt.fabrikam',
    ])
    expect(await inStaging(onlySource)).toBe(false)
  })

  it('names the configurations that a stopped copy did not reach', async () => {
    const controller = new AbortController()

    const result = await copy(
      { kind: 'configurations', keys: [unannotated, differing, equal] },
      { signal: controller.signal, onProgress: (done) => done === 1 && controller.abort() },
    )

    expect(result.stopped).toBe(true)
    expect(result.items).toEqual([
      configuration(differing, 'Updated'),
      configuration(equal, 'Skipped', stoppedMessage),
      configuration(unannotated, 'Skipped', stoppedMessage),
    ])
    expect(await inStaging(unannotated)).toBe(false)
  })

  it('is not stopped by a signal that fires during its last key', async () => {
    const controller = new AbortController()

    const result = await copy(
      { kind: 'annotations', keys: [onlySource, differs] },
      { signal: controller.signal, onProgress: (done) => done === 2 && controller.abort() },
    )

    expect(result.stopped).toBe(false)
    expect(result.items).toEqual([annotation(onlySource, 'Created'), annotation(differs, 'Updated')])
  })

  it('ends as a whole at a 403, with the rows so far and nothing after', async () => {
    server.use(http.post(stagingAnnotation(onlySource), denied))
    const progress: [number, number][] = []
    const requests = recordRequests()

    const result = await copy(
      { kind: 'annotations', keys: selection },
      { onProgress: (done, total) => progress.push([done, total]) },
    )

    expect(result.error).toMatchObject({ status: 403, errorCode: 'AccessDenied' })
    expect(result.stopped).toBe(false)
    expect(result.items).toEqual([annotation('sbt.contoso', 'Updated'), annotation('sbt.fabrikam', 'Updated')])
    // the refused write is the last request: the fourth key is not even read
    const lines = await requests.lines()
    expect(lines.at(-1)).toBe(`POST /staging/annotations/${onlySource}`)
    expect(lines.filter((sent) => sent.includes(differs))).toEqual([])
    expect(progress).toEqual([
      [0, 4],
      [1, 4],
      [2, 4],
    ])
  })

  it('ends as a whole at a 403 of a read, of an ancestor and of a configuration too', async () => {
    server.use(http.get(`*${root}/default/annotations/${onlySource}`, denied))
    let result = await copy({ kind: 'annotations', keys: selection })
    expect(result.error).toMatchObject({ status: 403 })
    expect(result.items.map((item) => item.key)).toEqual(['sbt.contoso', 'sbt.fabrikam'])

    // the write of a missing ancestor
    server.resetHandlers()
    server.use(http.post(stagingAnnotation('cnt.northwind.test'), denied))
    const requests = recordRequests()
    result = await copy({ kind: 'configurations', keys: [unannotated, equal, 'uxe.northwind.reporting.test.xlsx'] })
    expect(result.error).toMatchObject({ status: 403 })
    expect(result.items).toEqual([configuration(equal, 'Unchanged')])
    expect((await requests.lines()).at(-1)).toBe('POST /staging/annotations/cnt.northwind.test')

    // the write of a configuration
    server.resetHandlers()
    server.use(http.patch(stagingConfiguration(differing), denied))
    result = await copy({ kind: 'configurations', keys: [differing, equal] })
    expect(result.error).toMatchObject({ status: 403 })
    expect(result.items).toEqual([])
  })

  it('ends as a whole at a 401, after the one retry of the SDK', async () => {
    const unauthorized = () => HttpResponse.json({ Message: 'The token has expired.' }, { status: 401 })
    server.use(http.post(stagingAnnotation('sbt.fabrikam'), unauthorized))
    const requests = recordRequests()

    const result = await copy({ kind: 'annotations', keys: selection })

    expect(result.error).toMatchObject({ status: 401 })
    expect(result.items).toEqual([annotation('sbt.contoso', 'Updated')])
    expect((await requests.writes()).map(line)).toEqual([
      'POST /staging/annotations/sbt.contoso',
      'POST /staging/annotations/sbt.fabrikam',
      'POST /staging/annotations/sbt.fabrikam',
    ])
  })

  it('fails the key whose write answers 500, and goes on', async () => {
    server.use(http.post(stagingAnnotation('sbt.fabrikam'), busy), http.patch(stagingConfiguration(differing), busy))
    const progress: [number, number][] = []

    const annotations = await copy(
      { kind: 'annotations', keys: selection },
      { onProgress: (done, total) => progress.push([done, total]) },
    )

    expect(annotations.error).toBeUndefined()
    expect(annotations.items).toEqual([
      annotation('sbt.contoso', 'Updated'),
      annotation('sbt.fabrikam', 'Failed', 'The database is busy.'),
      annotation(onlySource, 'Created'),
      annotation(differs, 'Updated'),
    ])
    // the progress counts the keys that were handled, a failed one too
    expect(progress).toEqual([
      [0, 4],
      [1, 4],
      [2, 4],
      [3, 4],
      [4, 4],
    ])

    const configurations = await copy({ kind: 'configurations', keys: [differing, unannotated] })
    expect(configurations.error).toBeUndefined()
    expect(configurations.items).toEqual([
      configuration(differing, 'Failed', 'The database is busy.'),
      annotation('cnt.northwind.test', 'Created', missingAncestor(unannotated)),
      annotation(unannotated, 'Created', neededMessage),
      configuration(unannotated, 'Created'),
    ])
  })

  /** A client with a query per key, as the pages that show the views keep them. */
  function cached(...keys: (readonly unknown[])[]) {
    const client = new QueryClient()
    keys.forEach((key) => client.setQueryData(key, 'cached'))
    return { client, invalidated: () => keys.filter((key) => client.getQueryState(key)?.isInvalidated) }
  }

  const staged = ['v1', org, project, 'staging'] as const
  const compared = {
    annotation: [...staged, 'annotations', differs, 'view-compare', { includeDeleted: true }],
    detail: [...staged, 'annotations', differs],
    history: [...staged, 'annotations', differing, 'history'],
    otherAnnotation: [...staged, 'annotations', 'sbt.fabrikam', 'view-compare'],
    configuration: [...staged, 'configuration', differing, 'view-compare'],
    resolved: [...staged, 'configuration', differing, 'resolved'],
    // the effective document of a descendant holds the configuration of its ancestor
    descendant: [...staged, 'configuration', 'cnt.contoso.prod', 'view-compare'],
    summaries: [...staged, 'configurations'],
    templates: [...staged, 'templates'],
    sourceAnnotation: ['v1', org, project, 'default', 'annotations', differs, 'view-compare'],
    sourceConfiguration: ['v1', org, project, 'default', 'configuration', differing, 'view-compare'],
    otherProject: ['v1', org, 'shipping', 'staging', 'configuration', differing],
  }

  it('invalidates the queries of an annotation it wrote, and every configuration of the target', async () => {
    const cache = cached(...Object.values(compared))

    await copy({ kind: 'annotations', keys: [differs] }, { client: cache.client })

    expect(cache.invalidated()).toEqual([
      compared.annotation,
      compared.detail,
      // an annotation that was created makes its configuration readable
      compared.configuration,
      compared.resolved,
      compared.descendant,
      compared.summaries,
    ])
  })

  it('invalidates the queries of a configuration it wrote, and every configuration of the target', async () => {
    const cache = cached(...Object.values(compared))

    await copy({ kind: 'configurations', keys: [differing] }, { client: cache.client })

    expect(cache.invalidated()).toEqual([
      // a configuration save is an entry in the history of its annotation
      compared.history,
      compared.configuration,
      compared.resolved,
      compared.descendant,
      compared.summaries,
    ])
  })

  it('invalidates nothing after a dry run, and nothing when nothing was written', async () => {
    const cache = cached(...Object.values(compared))

    await copy({ kind: 'annotations', keys: [differs, onlySource], dryRun: true }, { client: cache.client })
    await copy({ kind: 'configurations', keys: [differing, unannotated], dryRun: true }, { client: cache.client })
    await copy({ kind: 'annotations', keys: [same] }, { client: cache.client })
    await copy({ kind: 'configurations', keys: [equal, none] }, { client: cache.client })
    await copy({ kind: 'annotations', keys: [differs], existing: 'skip' }, { client: cache.client })
    await copy({ kind: 'annotations', keys: [differs], target: 'stagin' }, { client: cache.client })

    expect(cache.invalidated()).toEqual([])
  })

  it('invalidates a key that it skipped because the target is not what the comparison saw', async () => {
    const cache = cached(...Object.values(compared))

    // also in a check: the section of the comparison shows what the target holds now
    await copy(
      { kind: 'annotations', keys: [differs], expected: { [differs]: '2020-01-01T00:00:00.000Z' }, dryRun: true },
      { client: cache.client },
    )
    expect(cache.invalidated()).toEqual([compared.annotation, compared.detail])

    await copy({ kind: 'configurations', keys: [differing], expected: { [differing]: 0 } }, { client: cache.client })
    expect(cache.invalidated()).toEqual([
      compared.annotation,
      compared.detail,
      compared.history,
      compared.configuration,
      compared.resolved,
      compared.summaries,
    ])
  })

  // What the comparison saw guards a write only. A target that changed since, and holds the content of the source by
  // now, needs no write: it is unchanged, not skipped.
  it('finds an annotation unchanged that got the content of the source since it was compared', async () => {
    const moved = 'sbt.fabrikam'
    const seen = {
      [differs]: annotationStamp(await annotationOf('staging', differs)),
      // seen without an annotation in staging
      [onlySource]: null,
      [moved]: annotationStamp(await annotationOf('staging', moved)),
    }
    // another client copied two of them to staging since, and changed the third one there
    await setAnnotation(org, project, 'staging', differs, await annotationOf('default', differs))
    await setAnnotation(org, project, 'staging', onlySource, await annotationOf('default', onlySource))
    await setAnnotation(org, project, 'staging', moved, {
      ...(await annotationOf('staging', moved)),
      Title: 'Changed since the comparison',
    })
    // staging is not what the comparison saw: the stamps are other ones, and two annotations read as in default
    expect(annotationStamp(await annotationOf('staging', differs))).not.toBe(seen[differs])
    expect(await textOf('staging', differs)).toBe(await textOf('default', differs))
    expect(await textOf('staging', onlySource)).toBe(await textOf('default', onlySource))

    // a check decides as the copy does
    for (const dryRun of [true, false]) {
      const cache = cached(...Object.values(compared))
      const requests = recordRequests()

      const result = await copy(
        { kind: 'annotations', keys: [differs, onlySource, moved], expected: seen, dryRun },
        { client: cache.client },
      )

      expect(result.items, `dryRun ${dryRun}`).toEqual([
        annotation(moved, 'Skipped', comparedMessage),
        annotation(onlySource, 'Unchanged'),
        annotation(differs, 'Unchanged'),
      ])
      // nothing is sent to staging but its reads and the lookup of the deleted keys
      expect((await requests.all()).filter((request) => request.method !== 'GET').map(line)).toEqual([
        'POST /staging/annotations/deleted/lookup',
      ])
      expect(await requests.writes()).toEqual([])
      // the page showed something else for all three, so their sections read staging again; nothing was written, so
      // the configurations of staging are left alone
      expect(cache.invalidated()).toEqual([compared.annotation, compared.detail, compared.otherAnnotation])
      server.events.removeAllListeners()
    }
    expect((await annotationOf('staging', moved)).Title).toBe('Changed since the comparison')
  })

  it('finds a configuration unchanged that got the content of the source since it was compared', async () => {
    const seen = {
      [differing]: (await readConfiguration(target, differing)).version,
      [equal]: (await readConfiguration(target, equal)).version,
    }
    // another client saved the document of default in staging since, and added a member to the other one there
    await patchConfiguration(org, project, 'staging', differing, [
      { op: 'replace', path: '', value: await ownOf('default', differing) },
    ])
    await patchConfiguration(org, project, 'staging', equal, [{ op: 'add', path: '/changedSince', value: true }])
    expect((await readConfiguration(target, differing)).version).not.toBe(seen[differing])
    expect(await ownOf('staging', differing)).toEqual(await ownOf('default', differing))
    const before = await ownOf('staging', equal)
    expect(before).toMatchObject({ changedSince: true })
    const other = [...staged, 'configuration', equal, 'view-compare']

    for (const dryRun of [true, false]) {
      const cache = cached(...Object.values(compared), other)
      const requests = recordRequests()

      const result = await copy(
        { kind: 'configurations', keys: [differing, equal], expected: seen, dryRun },
        { client: cache.client },
      )

      expect(result.items, `dryRun ${dryRun}`).toEqual([
        configuration(differing, 'Unchanged'),
        configuration(equal, 'Skipped', comparedMessage),
      ])
      expect((await requests.all()).filter((request) => request.method !== 'GET').map(line)).toEqual([
        'POST /staging/annotations/deleted/lookup',
      ])
      expect(await requests.writes()).toEqual([])
      // the queries of both keys, as after a save of each; not every configuration of staging, as after a write
      expect(cache.invalidated()).toEqual([
        compared.history,
        compared.configuration,
        compared.resolved,
        compared.summaries,
        other,
      ])
      server.events.removeAllListeners()
    }
    expect(await ownOf('staging', equal)).toEqual(before)
  })

  it('invalidates what it wrote before a 403 ended it', async () => {
    server.use(http.post(stagingAnnotation(differs), denied))
    const cache = cached(...Object.values(compared))

    const result = await copy({ kind: 'annotations', keys: [onlySource, differs] }, { client: cache.client })

    expect(result.error).toMatchObject({ status: 403 })
    expect(cache.invalidated()).toEqual([
      compared.configuration,
      compared.resolved,
      compared.descendant,
      compared.summaries,
    ])
  })

  // 424-166: a result does not wait for the queries the run invalidates.
  it('returns its result while a query it invalidated does not answer', async () => {
    const client = new QueryClient()
    let reads = 0
    // an open comparison of the target: its first read answers, the refresh after the copy never does
    const observer = new QueryObserver(client, {
      queryKey: compared.descendant,
      queryFn: () => (++reads === 1 ? 'read' : new Promise<string>(() => {})),
    })
    const stop = observer.subscribe(() => {})
    await vi.waitFor(() => expect(observer.getCurrentResult().isSuccess).toBe(true))

    let timer: ReturnType<typeof setTimeout> | undefined
    const result = await Promise.race([
      copy({ kind: 'configurations', keys: [differing] }, { client }),
      new Promise<'waits for the refresh'>((resolve) => {
        timer = setTimeout(() => resolve('waits for the refresh'), 4000)
      }),
    ])
    clearTimeout(timer)

    expect(result).toMatchObject({ items: [configuration(differing, 'Updated')] })
    // the refresh was started all the same
    await vi.waitFor(() => expect(reads).toBe(2))
    expect(observer.getCurrentResult().isFetching).toBe(true)
    stop()
    client.clear()
  })
})

describe('the summary of a copy', () => {
  const items: ViewCopyItem[] = [
    annotation('sbt.a', 'Created', missingAncestor('cnt.a.b')),
    annotation('cnt.a.b', 'Created'),
    configuration('cnt.a.b', 'Updated'),
    ...['c', 'd', 'e', 'f', 'g'].map((name) => configuration(`sbt.${name}`, 'Unchanged')),
    configuration('sbt.h', 'Skipped', 'default has no own configuration for it.'),
    configuration('sbt.i', 'Failed', inexactMessage),
  ]

  it('counts the documents of every outcome', () => {
    expect(copyCounts(items)).toEqual({ Created: 2, Updated: 1, Unchanged: 5, Skipped: 1, Failed: 1 })
    expect(copyCounts([])).toEqual({ Created: 0, Updated: 0, Unchanged: 0, Skipped: 0, Failed: 0 })
    expect(Object.keys(copyCounts([]))).toEqual([...viewCopyOutcomes])
  })

  it('counts what a copy writes: the created and the updated documents, annotations of ancestors too', () => {
    expect(copyWrites(items)).toBe(3)
    expect(copyWrites(items.filter((item) => item.outcome !== 'Created' && item.outcome !== 'Updated'))).toBe(0)
    expect(copyWrites([])).toBe(0)
  })

  it('words a copy and a check, and leaves out the counts that are zero', () => {
    expect(copySummary({ items, dryRun: false })).toBe('2 created, 1 updated, 5 unchanged, 1 skipped, 1 failed')
    expect(copySummary({ items, dryRun: true })).toBe('To create 2 · To update 1 · Unchanged 5 · To skip 1 · Fails 1')

    const some = items.filter((item) => item.outcome === 'Updated' || item.outcome === 'Skipped')
    expect(copySummary({ items: some, dryRun: false })).toBe('1 updated, 1 skipped')
    expect(copySummary({ items: some, dryRun: true })).toBe('To update 1 · To skip 1')

    expect(copySummary({ items: [], dryRun: false })).toBe('nothing copied')
    expect(copySummary({ items: [], dryRun: true })).toBe('Nothing to copy')
  })

  it('words the result of a run', async () => {
    const keys = [differs, onlySource, same, 'sbt.nobody']
    const check = await copy({ kind: 'annotations', keys, dryRun: true })
    expect(copySummary(check)).toBe('To create 1 · To update 1 · Unchanged 1 · To skip 1')
    expect(copyWrites(check.items)).toBe(2)

    const real = await copy({ kind: 'annotations', keys })
    expect(copySummary(real)).toBe('1 created, 1 updated, 1 unchanged, 1 skipped')
    expect(copyCounts(real.items)).toEqual({ Created: 1, Updated: 1, Unchanged: 1, Skipped: 1, Failed: 0 })
  })

  it('takes the stamp of an annotation from its last change, or from its creation', () => {
    const base = { ProjectName: project, ViewName: 'staging', AnnotationKey: 'sbt.a', Name: 'a' } as Annotation
    const created = { At: '2026-01-01T00:00:00Z', By: { Kind: 'System' as const } }
    const modified = { At: '2026-02-01T00:00:00Z', By: { Kind: 'System' as const } }

    expect(annotationStamp({ ...base, Created: created, Modified: modified })).toBe('2026-02-01T00:00:00Z')
    expect(annotationStamp({ ...base, Created: created })).toBe('2026-01-01T00:00:00Z')
    expect(annotationStamp(base)).toBeNull()
    expect(annotationStamp(undefined)).toBeNull()
  })
})
