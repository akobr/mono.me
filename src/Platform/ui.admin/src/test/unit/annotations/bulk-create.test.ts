import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { defineComponent } from 'vue'
import { DOMWrapper } from '@vue/test-utils'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import {
  deleteAnnotation,
  getAnnotation,
  getAnnotationHistory,
  setAnnotationsSimple,
  type Annotation,
} from '@42for.net/2splatform.sdk'
import AnnotationBulkCreateDialog from '@/components/annotations/AnnotationBulkCreateDialog.vue'
import { useAnnotationActions, type BulkCreateOutcome } from '@/composables/useAnnotationActions'
import { mockHandlers } from '@/mock/all-handlers'
import { annotationsOf, loadDb } from '@/mock/db'
import { keyOfSimpleLine } from '@/mock/simple-lines'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { mountScreen, settle } from '../../helpers'

// Bulk create (424-33) against the stateful mock API of the e2e mode, served by msw/node.
const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const scope = { org: 'acme', project: 'billing', view: 'default' }
const { org, project, view } = scope
const simpleUrl = '*/api/v1/:org/:project/:view/annotations/simple'

const keysOf = (annotations: Annotation[]) => annotations.map((annotation) => annotation.AnnotationKey)
const active = () => annotationsOf(loadDb(), org, project, view)
const subjects = (count: number) => Array.from({ length: count }, (_, index) => `sbt.bulk-${index}`)

/** The keys of every request to SetAnnotationsSimple; returning nothing lets the mock answer it. */
function recordBatches(): string[][] {
  const batches: string[][] = []
  server.use(
    http.post(simpleUrl, async ({ request }) => {
      batches.push((await request.clone().json()) as string[])
    }),
  )
  return batches
}

describe('keyOfSimpleLine', () => {
  it('reads a line like CreateAnnotationsFromStringAsync', () => {
    expect(keyOfSimpleLine('northwind')).toBe('sbt.northwind')
    expect(keyOfSimpleLine('northwind.invoicing')).toBe('usg.northwind.invoicing')
    expect(keyOfSimpleLine('northwind.invoicing.prod')).toBe('exe.northwind.invoicing.prod')
    expect(keyOfSimpleLine('northwind.invoicing.prod.pdf')).toBe('uxe.northwind.invoicing.prod.pdf')
    expect(keyOfSimpleLine('rst.invoicing')).toBe('rst.invoicing')
    expect(keyOfSimpleLine('unt.invoicing.pdf')).toBe('unt.invoicing.pdf')
    // the typed case of the code is kept
    expect(keyOfSimpleLine('SBT.northwind')).toBe('SBT.northwind')
  })

  it('reads a code with the wrong number of segments as a compact line, and drops empty segments', () => {
    expect(keyOfSimpleLine('exe.a.b')).toBe('exe.exe.a.b')
    expect(keyOfSimpleLine('exe')).toBe('sbt.exe')
    expect(keyOfSimpleLine('a..b')).toBe('usg.a.b')
    expect(keyOfSimpleLine('.a.')).toBe('sbt.a')
  })

  it('skips blank lines and five segments, and does not trim', () => {
    expect(keyOfSimpleLine('a.b.c.d.e')).toBeUndefined()
    expect(keyOfSimpleLine('')).toBeUndefined()
    expect(keyOfSimpleLine('   ')).toBeUndefined()
    expect(keyOfSimpleLine('  northwind')).toBe('sbt.  northwind')
  })
})

describe('the mock SetAnnotationsSimple', () => {
  it('creates the keys with their missing ancestors, in type order and roots first, a shared ancestor once', async () => {
    const created = await setAnnotationsSimple(org, project, view, [
      'exe.cyberdyne.invoicing.prod',
      'cnt.cyberdyne.dev',
      'northwind.billing',
      'rst.invoicing',
      'northwind.billing',
      'unt.invoicing.ocr',
    ])

    // rst.invoicing exists and the second northwind.billing repeats the first: both are skipped
    expect(keysOf(created)).toEqual([
      'unt.invoicing.ocr',
      'rst.billing',
      'usg.northwind.billing',
      'sbt.cyberdyne',
      'cnt.cyberdyne.dev',
      'usg.cyberdyne.invoicing',
      'cnt.cyberdyne.prod',
      'exe.cyberdyne.invoicing.prod',
    ])

    // each annotation gets only what its key gives
    const execution = await getAnnotation(org, project, view, 'exe.cyberdyne.invoicing.prod')
    expect(execution).toMatchObject({ Name: 'prod', AnnotationType: 'Execution' })
    expect(execution.Title).toBeUndefined()
    expect(execution.TimeZone).toBeUndefined()
    expect(created[0]).toMatchObject({ AnnotationType: 'Unit', UnitType: 'event', UnitDefinition: 'unknown' })

    // an ancestor is created for the first key that needs it, and its history says so
    const history = await getAnnotationHistory(org, project, view, 'sbt.cyberdyne')
    expect(history.Entries?.[0]).toMatchObject({
      Action: 'Created',
      Cause: { Kind: 'Ancestor', Key: 'cnt.cyberdyne.dev' },
    })
  })

  it('skips keys that exist, so a list where every key exists answers nothing', async () => {
    const list = ['northwind.billing', 'sbt.northwind']
    expect(keysOf(await setAnnotationsSimple(org, project, view, list))).toEqual([
      'rst.billing',
      'usg.northwind.billing',
    ])
    expect(await setAnnotationsSimple(org, project, view, list)).toEqual([])
  })

  it('re-creates a deleted key empty', async () => {
    await deleteAnnotation(org, project, view, 'sbt.contoso')
    expect(keysOf(await setAnnotationsSimple(org, project, view, ['contoso']))).toEqual(['sbt.contoso'])
    const history = await getAnnotationHistory(org, project, view, 'sbt.contoso')
    expect(history.Entries?.[0]?.Action).toBe('Recreated')
  })

  it('answers 400 above 100 entries, blank ones included, and 500 without a list', async () => {
    await expect(setAnnotationsSimple(org, project, view, [...subjects(100), ''])).rejects.toMatchObject({
      status: 400,
      message: 'The number of annotations to create is too large, maximum allowed is 100.',
      errorCode: undefined,
    })
    expect(active().has('sbt.bulk-0')).toBe(false)
    await expect(setAnnotationsSimple(org, project, view)).rejects.toMatchObject({ status: 500 })
  })

  it('needs the Contributor role', async () => {
    // northwind.portal: the mock user is a Reader
    await expect(setAnnotationsSimple('northwind', 'portal', 'default', ['sbt.cyberdyne'])).rejects.toMatchObject({
      status: 403,
      errorCode: 'AccessDenied',
    })
  })
})

const Harness = defineComponent({
  setup(_, { expose }) {
    const actions = useAnnotationActions()
    expose({ createMany: actions.createMany, progress: actions.progress })
    return () => null
  },
})

interface HarnessApi {
  createMany: (keys: string[]) => Promise<BulkCreateOutcome>
  progress: unknown
}

async function createMany(keys: string[]) {
  // the annotations page has loaded the catalog of the view
  await useCatalogStore().load(scope)
  const screen = await mountScreen(Harness, {})
  const harness = screen.inner().vm as unknown as HarnessApi
  const outcome = await harness.createMany(keys)
  // the catalog reloads in the background
  await settle(10)
  const progress = harness.progress
  screen.host.unmount()
  return { outcome, progress }
}

describe('useAnnotationActions().createMany', () => {
  it('sends 100 keys per request, in type order, and lands the existing keys in skipped', async () => {
    const batches = recordBatches()
    const keys = [...subjects(150), 'sbt.northwind', 'rst.bulk', 'sbt.bulk-7']

    const { outcome, progress } = await createMany(keys)

    // deduplicated: 152 keys; rst.bulk goes first, so it is not created as an ancestor of an earlier batch
    expect(batches.map((batch) => batch.length)).toEqual([100, 52])
    expect(outcome.failure).toBeUndefined()
    expect(outcome.created).toHaveLength(151)
    expect(outcome.created[0]!.AnnotationKey).toBe('rst.bulk')
    expect(outcome.skipped).toEqual(['sbt.northwind'])
    expect(progress).toBeUndefined()
    expect(active().has('sbt.bulk-149')).toBe(true)
    expect(useCatalogStore().byKey.has('sbt.bulk-149')).toBe(true)
  })

  it('stops at a failed batch and keeps what the batches before it created', async () => {
    let requests = 0
    server.use(
      http.post(simpleUrl, () => {
        if (++requests === 2) {
          return HttpResponse.json({ Message: 'Too many concurrent changes.' }, { status: 409 })
        }
      }),
    )

    const { outcome } = await createMany(subjects(250))

    expect(requests).toBe(2)
    expect(outcome.created).toHaveLength(100)
    expect(outcome.skipped).toEqual([])
    expect(outcome.failure).toMatchObject({ status: 409, message: 'Too many concurrent changes.' })
    expect(active().has('sbt.bulk-99')).toBe(true)
    expect(active().has('sbt.bulk-100')).toBe(false)
  })

  it('leaves the catalog alone once the user has moved to another view', async () => {
    const catalog = useCatalogStore()
    await catalog.load(scope)
    const screen = await mountScreen(Harness, {})
    const harness = screen.inner().vm as unknown as HarnessApi

    const run = harness.createMany(['sbt.bulk-0'])
    // the user goes to another view before the response comes
    const staging = { ...scope, view: 'staging' }
    useContextStore().set(staging)
    const loaded = catalog.load(staging)
    const outcome = await run
    await loaded
    await settle(10)
    screen.host.unmount()

    expect(keysOf(outcome.created)).toEqual(['sbt.bulk-0'])
    expect(active().has('sbt.bulk-0')).toBe(true)
    // neither written into the catalog of the other view nor replaced by a reload of the first one
    expect(catalog.scope).toEqual(staging)
    expect(catalog.byKey.has('sbt.bulk-0')).toBe(false)
    expect(catalog.byKey.size).toBe(annotationsOf(loadDb(), org, project, 'staging').size)
  })
})

describe('AnnotationBulkCreateDialog', () => {
  // A failed test must not leave its teleported dialog in the body for the next one.
  let unmount: (() => void) | undefined
  beforeEach(async () => {
    await useCatalogStore().forgetSnapshot(scope)
  })
  afterEach(() => {
    unmount?.()
    unmount = undefined
  })

  async function openDialog() {
    await useCatalogStore().load(scope)
    const closed: boolean[] = []
    const screen = await mountScreen(AnnotationBulkCreateDialog, {
      open: true,
      'onUpdate:open': (value: boolean) => closed.push(!value),
    })
    unmount = () => screen.host.unmount()
    await settle(6)
    return { closed }
  }

  // The modal is teleported to the body.
  const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')!
  const textarea = () => dialog().querySelector('textarea')!
  const button = (label: string) =>
    [...document.body.querySelectorAll('button')].find((candidate) => candidate.textContent?.trim() === label)
  const text = () => dialog().textContent!.replace(/\s+/g, ' ')
  const textOf = (element: Element) => element.textContent!.replace(/\s+/g, ' ').trim()
  /** The cells of the table in a region; the parts of a cell (badge, key, typed text, status, message) are spaced. */
  const rows = (region: string) =>
    [...dialog().querySelectorAll(`[role="region"][aria-label="${region}"] tbody tr`)].map((row) =>
      [...row.querySelectorAll('td')].map((cell) => {
        const wrapper = cell.firstElementChild
        return wrapper?.tagName === 'DIV' ? [...wrapper.children].map(textOf).join(' ') : textOf(cell)
      }),
    )

  async function type(value: string) {
    await new DOMWrapper(textarea()).setValue(value)
    // the review reads the text 150 ms after the last change
    await new Promise((resolve) => setTimeout(resolve, 200))
    await settle()
  }

  /** Focuses the button first, as a pointer or a key does, so the tests see where the focus goes afterwards. */
  async function click(label: string) {
    const target = button(label)!
    target.focus()
    target.click()
    await settle(12)
  }

  const pressCtrlEnter = () =>
    textarea().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true, bubbles: true }))

  it('explains the lines and reviews them against the catalog', async () => {
    await openDialog()
    expect(text()).toContain('How bulk create works')
    expect(text()).toContain('northwind.invoicing.production an Execution')
    expect(text()).toContain('In acme/billing, view default.')
    expect(button('Create 0 annotations')!.disabled).toBe(true)
    // the live region of the summary is there before the first paste, so the paste is read out
    const live = dialog().querySelector('p[aria-live="polite"]')!
    expect(textOf(live)).toBe('')

    await type('northwind.billing\nEXE.cyberdyne.invoicing.prod\n\nrst.invoicing\nusg.northwind.billing')

    expect(dialog().querySelector('p[aria-live="polite"]')).toBe(live)
    expect(textOf(live)).toBe('2 new · 1 already exists · 1 duplicate · 4 missing ancestors')
    expect(rows('Review')).toEqual([
      ['1', 'Usage usg.northwind.billing northwind.billing', 'New'],
      ['2', 'Execution exe.cyberdyne.invoicing.prod EXE.cyberdyne.invoicing.prod', 'New'],
      ['4', 'Responsibility rst.invoicing', 'Exists'],
      ['5', 'Usage usg.northwind.billing', 'Duplicate'],
    ])
    expect(text()).not.toContain('The catalog is still loading')
    expect(button('Create 2 annotations')!.disabled).toBe(false)
    expect(button('Remove invalid lines')).toBeUndefined()
  })

  it('blocks the create while a line is invalid, until the invalid lines are removed', async () => {
    await openDialog()
    await type('northwind.billing\nexe.a.b\nnorthwind\tinvoicing')

    expect(text()).toContain('1 new · 2 invalid · 1 missing ancestor')
    expect(rows('Review').slice(1)).toEqual([
      ['2', 'exe.a.b', 'Invalid An Execution key has 4 parts: exe.subject.responsibility.context'],
      ['3', 'northwind invoicing', 'Invalid Contains a tab: cells copied from a spreadsheet? Join them with dots.'],
    ])
    expect(button('Create 1 annotation')!.disabled).toBe(true)

    await click('Remove invalid lines')
    expect(textarea().value).toBe('northwind.billing')
    // the button goes away, the focus goes to the input
    expect(document.activeElement).toBe(textarea())
    await new Promise((resolve) => setTimeout(resolve, 200))
    await settle()
    expect(rows('Review')).toEqual([['1', 'Usage usg.northwind.billing northwind.billing', 'New']])
    expect(button('Create 1 annotation')!.disabled).toBe(false)
  })

  it('creates the new keys, shows the result and starts over with Create more', async () => {
    const { closed } = await openDialog()
    await type('northwind.billing\nexe.cyberdyne.invoicing.prod\nrst.invoicing')
    expect(text()).toContain('2 new · 1 already exists · 4 missing ancestors')

    await click('Create 2 annotations')

    expect(text()).toContain('Created 6 annotations: 2 from the list and 4 missing ancestors.')
    expect(text()).toContain('1 key already existed and was skipped.')
    expect(rows('Created annotations')).toEqual([
      ['Responsibility rst.billing', 'Missing ancestor'],
      ['Usage usg.northwind.billing', 'From the list'],
      ['Subject sbt.cyberdyne', 'Missing ancestor'],
      ['Usage usg.cyberdyne.invoicing', 'Missing ancestor'],
      ['Context cnt.cyberdyne.prod', 'Missing ancestor'],
      ['Execution exe.cyberdyne.invoicing.prod', 'From the list'],
    ])
    for (const key of ['exe.cyberdyne.invoicing.prod', 'usg.northwind.billing', 'rst.billing', 'cnt.cyberdyne.prod']) {
      expect(active().has(key), key).toBe(true)
      expect(useCatalogStore().byKey.has(key), key).toBe(true)
    }
    // Done takes the focus, described by the result
    const done = document.activeElement!
    expect(done.textContent?.trim()).toBe('Done')
    expect(textOf(document.getElementById(done.getAttribute('aria-describedby')!)!)).toContain(
      'Created 6 annotations: 2 from the list and 4 missing ancestors.',
    )

    await click('Create more')
    expect(textarea().value).toBe('')
    expect(document.activeElement).toBe(textarea())

    // the keys exist now
    await type('exe.cyberdyne.invoicing.prod')
    expect(rows('Review')).toEqual([['1', 'Execution exe.cyberdyne.invoicing.prod', 'Exists']])
    expect(button('Create 0 annotations')!.disabled).toBe(true)

    await click('Cancel')
    expect(closed).toEqual([true])
  })

  it('does not count a key of the list as a missing ancestor of another', async () => {
    await openDialog()
    // sbt.cyberdyne is an ancestor of the execution; rst.invoicing is in the catalog
    await type('sbt.cyberdyne\nexe.cyberdyne.invoicing.prod')
    expect(text()).toContain('2 new · 2 missing ancestors')

    await click('Create 2 annotations')

    expect(text()).toContain('Created 4 annotations: 2 from the list and 2 missing ancestors.')
    expect(rows('Created annotations')).toEqual([
      ['Subject sbt.cyberdyne', 'From the list'],
      ['Usage usg.cyberdyne.invoicing', 'Missing ancestor'],
      ['Context cnt.cyberdyne.prod', 'Missing ancestor'],
      ['Execution exe.cyberdyne.invoicing.prod', 'From the list'],
    ])
  })

  it('tells existing keys from repeated lines in the result', async () => {
    await openDialog()
    await type('northwind.billing\nrst.invoicing\nnorthwind.billing\nusg.northwind.billing')
    expect(text()).toContain('1 new · 1 already exists · 2 duplicates · 1 missing ancestor')

    await click('Create 1 annotation')

    expect(text()).toContain('Created 2 annotations: 1 from the list and 1 missing ancestor.')
    expect(text()).toContain('1 key already existed and was skipped. 2 repeated lines were left out.')
  })

  it('finds a legacy key with an upper-case code by its typed text', async () => {
    // the API keeps the typed case of a line's own code
    await setAnnotationsSimple(org, project, view, ['SBT.legacy'])
    await openDialog()
    await type('SBT.legacy')

    expect(rows('Review')).toEqual([['1', 'Subject sbt.legacy SBT.legacy', 'Exists']])
    expect(text()).toContain('1 already exists')
    expect(button('Create 0 annotations')!.disabled).toBe(true)
  })

  it('creates with Ctrl+Enter in the text, from the text as it is now', async () => {
    await openDialog()
    // no wait for the debounced review: Ctrl+Enter right after the paste
    await new DOMWrapper(textarea()).setValue('northwind.billing')
    pressCtrlEnter()
    await settle(12)

    expect(text()).toContain('Created 2 annotations: 1 from the list and 1 missing ancestor.')
    expect(active().has('usg.northwind.billing')).toBe(true)
  })

  it('does nothing on Ctrl+Enter while a line is invalid', async () => {
    const batches = recordBatches()
    await openDialog()
    await type('northwind.billing\nexe.a.b')

    pressCtrlEnter()
    await settle(12)

    expect(batches).toEqual([])
    expect(text()).not.toContain('Created')
    expect(active().has('usg.northwind.billing')).toBe(false)
  })

  it('keeps the count of the Create button while the run takes the keys into the catalog', async () => {
    let release!: () => void
    const second = new Promise<void>((resolve) => (release = resolve))
    let requests = 0
    server.use(
      http.post(simpleUrl, async () => {
        if (++requests === 2) {
          await second
        }
      }),
    )
    await openDialog()
    await type(Array.from({ length: 150 }, (_, index) => `bulk-${index}`).join('\n'))

    await click('Create 150 annotations')
    // the first batch is in the catalog, the second one waits
    expect(requests).toBe(2)
    expect(useCatalogStore().byKey.has('sbt.bulk-0')).toBe(true)
    expect(text()).toContain('Sent 100 of 150 keys')
    expect(text()).toContain('150 new')
    expect(button('Create 150 annotations')).toBeDefined()

    release()
    await settle(12)
    expect(text()).toContain('Created 150 annotations.')
  })

  it('restores a deleted key instead of creating it again', async () => {
    await deleteAnnotation(org, project, view, 'exe.northwind.invoicing.prod')
    const batches = recordBatches()
    await openDialog()
    await type('exe.northwind.invoicing.prod\nnorthwind.billing')
    expect(text()).toContain('2 new · 1 missing ancestor')

    await click('Create 2 annotations')
    expect(document.body.textContent).toContain('These annotations were deleted')
    expect(button('Restore and update')).toBeUndefined()
    await click('Restore')

    expect(text()).toContain('Created 2 annotations: 1 from the list and 1 missing ancestor.')
    expect(text()).toContain('1 deleted annotation was restored.')
    // the restored key is not sent again, so nothing is skipped
    expect(batches).toEqual([['usg.northwind.billing']])
    expect(text()).not.toContain('skipped')
    const history = await getAnnotationHistory(org, project, view, 'exe.northwind.invoicing.prod')
    expect(history.Entries?.[0]?.Action).toBe('Restored')
  })

  it('sends nothing after Cancel in the deleted-key question, and gives the focus back to the input', async () => {
    await deleteAnnotation(org, project, view, 'exe.northwind.invoicing.prod')
    const batches = recordBatches()
    await openDialog()
    await type('exe.northwind.invoicing.prod')

    await click('Create 1 annotation')
    const question = [...document.body.querySelectorAll<HTMLElement>('[role="dialog"]')].find((candidate) =>
      candidate.textContent?.includes('These annotations were deleted'),
    )!
    const cancel = [...question.querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === 'Cancel',
    )!
    cancel.focus()
    cancel.click()
    await settle(12)

    expect(batches).toEqual([])
    expect(text()).not.toContain('Created')
    expect(button('Create 1 annotation')!.disabled).toBe(false)
    expect(document.activeElement).toBe(textarea())
  })

  it('stops when the user moves to another view while the deleted-key question waits', async () => {
    await deleteAnnotation(org, project, view, 'exe.northwind.invoicing.prod')
    const batches = recordBatches()
    await openDialog()
    await type('exe.northwind.invoicing.prod')

    await click('Create 1 annotation')
    expect(document.body.textContent).toContain('These annotations were deleted')
    useContextStore().set({ ...scope, view: 'staging' })
    await click('Restore')

    expect(text()).toContain('You moved to another view, so the keys were not created in acme/billing, view default.')
    expect(batches).toEqual([])
    expect(active().has('exe.northwind.invoicing.prod')).toBe(false)
  })

  it('shows a failure with what was created, keeps the input and tries again', async () => {
    let requests = 0
    server.use(
      http.post(simpleUrl, () => {
        if (++requests === 2) {
          return HttpResponse.json({ Message: 'Too many concurrent changes.' }, { status: 409 })
        }
      }),
    )
    await openDialog()
    const list = Array.from({ length: 150 }, (_, index) => `bulk-${index}`).join('\n')
    await type(list)
    expect(text()).toContain('150 new')
    expect(rows('Review')).toHaveLength(150)
    expect(text()).not.toContain('more line')

    await click('Create 150 annotations')

    // an alert, so a screen reader reads it out wherever the focus is
    const alert = textOf(dialog().querySelector('[role="alert"]')!)
    expect(alert).toContain('Too many concurrent changes.')
    expect(alert).toContain(
      'At least 100 annotations were created before the error; creating the same list again is safe, because existing keys are skipped.',
    )
    expect(textarea().value).toBe(list)
    // the Create button had the focus, and was disabled while it ran
    expect(document.activeElement).toBe(textarea())

    // the first batch is in the catalog now, so only the rest is created
    await click('Try again')
    expect(text()).toContain('Created 50 annotations.')
    expect(text()).toContain('100 keys already existed and were skipped.')
    expect(active().has('sbt.bulk-149')).toBe(true)
  })

  const bulkLines = (count: number) => Array.from({ length: count }, (_, index) => `bulk-${index}`)

  it('takes at most 1,000 lines and lists the first 200', async () => {
    await openDialog()
    await type(bulkLines(1001).join('\n'))

    expect(text()).toContain('At most 1,000 keys at once; the list has 1,001.')
    expect(text()).toContain('and 801 more lines')
    expect(rows('Review')).toHaveLength(200)
    expect(button('Create 1,001 annotations')!.disabled).toBe(true)

    // exactly 1,000 is allowed
    await type(bulkLines(1000).join('\n'))
    expect(text()).not.toContain('At most')
    expect(button('Create 1,000 annotations')!.disabled).toBe(false)
  })

  it('lists an invalid line beyond the first 200', async () => {
    await openDialog()
    const lines = bulkLines(250)
    lines[229] = 'a..b'
    await type(lines.join('\n'))

    const shown = rows('Review')
    expect(shown).toHaveLength(201)
    expect(shown.at(-1)).toEqual([
      '230',
      'a..b',
      'Invalid Has an empty part: two dots in a row, or a dot at the start or the end.',
    ])
    expect(text()).toContain('and 49 more lines')
    expect(button('Create 249 annotations')!.disabled).toBe(true)
  })
})
