import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import type { SetupContext } from 'vue'
import type { VueWrapper } from '@vue/test-utils'
import type { QueryClient } from '@tanstack/vue-query'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { routes } from 'vue-router/auto-routes'
import {
  deleteAnnotation,
  getAnnotation,
  getConfiguration,
  getConfigurationVersion,
  patchConfiguration,
  setAnnotation,
  setConfiguration,
  type View,
} from '@42for.net/2splatform.sdk'
import DiffEditor from '@/components/editor/DiffEditor.vue'
import ViewCopyDialog from '@/components/views/ViewCopyDialog.vue'
import { mockHandlers } from '@/mock/all-handlers'
import SelectionPage from '@/pages/[org]/[project]/[view]/modeling/compare.selection.vue'
import { invalidateAnnotation, invalidateConfiguration } from '@/queries/invalidation'
import { contextGuard } from '@/router/guards/context'
import { useViewTransferStore } from '@/stores/view-transfer'
import { annotationText, canonicalJson, readAnnotation, readConfiguration } from '@/utils/view-documents'
import { mountInApp, mountScreen, settle } from '../../helpers'

// The comparison of a selection of annotations or configurations across two views, and the copy from it (424-309),
// against the stateful mock API. In the default dataset acme/billing has the views default and staging, whose
// annotations and configurations differ: the keys below were picked for their states, and a change of the generator
// shows here.

// the navbar needs the dashboard shell; the stand-in shows its title and what the page puts on its right
vi.mock('@/components/shell/AppNavbar.vue', async () => {
  const vue = await import('vue')
  return {
    default: {
      name: 'AppNavbar',
      props: ['title'],
      setup:
        (props: { title?: string }, { slots }: SetupContext) =>
        () =>
          vue.h('header', { 'data-navbar': props.title }, slots.right?.()),
    },
  }
})

/**
 * Reports every observed element as visible at once, as a page whose sections are all near its visible part. The
 * observer of happy-dom never calls back.
 */
class VisibleObserver {
  static margins: (string | undefined)[] = []

  constructor(
    private readonly callback: IntersectionObserverCallback,
    options?: IntersectionObserverInit,
  ) {
    VisibleObserver.margins.push(options?.rootMargin)
  }

  observe(target: Element) {
    queueMicrotask(() =>
      this.callback(
        [{ isIntersecting: true, target } as IntersectionObserverEntry],
        this as unknown as IntersectionObserver,
      ),
    )
  }

  unobserve() {}
  disconnect() {}
  takeRecords() {
    return []
  }
}

const server = setupServer(...mockHandlers)
// unmounted before the body is cleared, so a closing tooltip or dialog does not patch removed nodes
const mounted: { host: { unmount: () => void } }[] = []
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
beforeEach(() => {
  VisibleObserver.margins = []
  vi.stubGlobal('IntersectionObserver', VisibleObserver)
})
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  server.events.removeAllListeners()
  server.resetHandlers()
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
})
afterAll(() => server.close())

const org = 'acme'
const project = 'billing'
const production = { org, project, view: 'default' }
const staging = { org, project, view: 'staging' }

const views = (...names: string[]) =>
  names.map((name): View => ({ Name: name, IsDefault: name === 'default', IsRegistered: true }))

/** The paths, with their queries, of the requests made from now on. */
function recordRequests() {
  const urls: string[] = []
  server.events.on('request:start', ({ request }) => {
    const url = new URL(request.url)
    urls.push(`${url.pathname}${url.search}`)
  })
  return urls
}

/** What changes the API from now on, as "POST /api/v1/…": every request but the GETs and the lookup of deleted keys. */
function recordWrites() {
  const writes: string[] = []
  server.events.on('request:start', ({ request }) => {
    const { pathname } = new URL(request.url)
    if (request.method !== 'GET' && !pathname.endsWith('/annotations/deleted/lookup')) {
      writes.push(`${request.method} ${pathname}`)
    }
  })
  return writes
}

const annotationReads = (urls: string[]) => urls.filter((url) => /\/annotations\/[a-z]{3}\./.test(url))
const configurationReads = (urls: string[]) => urls.filter((url) => url.includes('/configuration/'))

async function mountSelection(query: string, source = { org, project, view: 'default' }, role = 'Owner') {
  const view = await mountScreen(
    SelectionPage,
    {},
    {
      path: `/${source.org}/${source.project}/${source.view}/modeling/compare/selection?${query}`,
      scope: source,
      accessMap: { 'acme.billing': role, 'acme.shipping': role },
      routes,
    },
  )
  mounted.push(view)
  // the store follows the route as in the app, before a navigation lands
  view.router.beforeEach(contextGuard)
  await settle()
  return view
}

const keysQuery = (...keys: string[]) => keys.map((key) => `key=${key}`).join('&')

/** The app keeps a document fresh for half a minute (boot/query.ts); the client of the tests reads it every time. */
const keepFresh = (view: { queryClient: QueryClient }) =>
  view.queryClient.setDefaultOptions({ queries: { retry: false, staleTime: 30_000 } })

/** A slow machine takes longer than the second `vi.waitFor` waits by default. */
const eventually = (assertion: () => void) => vi.waitFor(assertion, { timeout: 3000 })

const text = () => document.body.textContent ?? ''
const clean = (element: Element | null | undefined) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? ''
const sections = () => [...document.body.querySelectorAll<HTMLElement>('section[data-key]')]
const keys = () => sections().map((candidate) => candidate.dataset.key)
const section = (key: string) => sections().find((candidate) => candidate.dataset.key === key)!
/** The parts of the heading of a section, spaced: the type, the key, the state badges and the changed lines. */
const heading = (key: string) =>
  [...(section(key).querySelector('h3')?.firstElementChild?.children ?? [])].map(clean).filter(Boolean).join(' ')
const toggleOf = (key: string) => section(key).querySelector<HTMLButtonElement>('h3 button')
const isOpen = (key: string) => toggleOf(key)?.getAttribute('aria-expanded') === 'true'
const editor = (key: string) => section(key).querySelector<HTMLElement>('[data-diff]')
const notes = (key: string) => [...section(key).querySelectorAll('ul li')].map(clean)
const links = (key: string) =>
  [...section(key).querySelectorAll('a')].map((link) => `${clean(link)}: ${link.getAttribute('href')}`)

const chips = () => [...document.body.querySelectorAll('[role="group"][aria-label="Filter by result"] button')]
const chip = (label: string) => chips().find((candidate) => clean(candidate) === label) as HTMLButtonElement
const pressed = () =>
  chips()
    .filter((candidate) => candidate.getAttribute('aria-pressed') === 'true')
    .map(clean)
const button = (label: string) =>
  [...document.body.querySelectorAll<HTMLElement>('a, button')].find((candidate) => clean(candidate) === label)!
const tab = (label: string) =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button[role="tab"]')].find(
    (candidate) => clean(candidate) === label,
  )!
const selectedTab = (group: string) =>
  clean(document.body.querySelector(`[role="group"][aria-label="${group}"] [role="tab"][aria-selected="true"]`))
const live = () => document.body.querySelector('p[aria-live="polite"]')?.textContent
/** Every document is read: the live region is there and has nothing more to say. */
const compared = () => eventually(() => expect(live()).toBe(''))
const targetSelect = (view: { host: VueWrapper }) => view.host.findComponent({ name: 'Select' })
/** The paragraphs of the page; the legend is one of them. */
const paragraphs = () => [...document.body.querySelectorAll('p')].map(clean)

const ownLegend = (target = 'staging') =>
  'Only the selected keys are compared. A copy moves what is green and never deletes: ' +
  `a document that only ${target} has stays.`
const effectiveLegend = 'What staging and default read. A copy moves only own configurations.'
const inheritedNote =
  'The own configurations are the same. The difference is inherited (ancestors, the template, key values), ' +
  'and a copy does not change it.'

// --- the copy ---

/** The copy of what the filter shows: the button of the page, not the one of a section or of the dialog. */
const copyButton = () =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (candidate) => /^Copy\b/.test(clean(candidate)) && !candidate.closest('section[data-key], [role="dialog"]'),
  )!
/** Why the copy of the page is off: the text that describes its button. */
function copyReason() {
  const id = copyButton().getAttribute('aria-describedby')
  return id ? clean(document.getElementById(id)) : undefined
}
/**
 * The copy of the page is on, with this label: the documents are read, and the views of the project have arrived
 * and say that the target is one of them.
 */
const copyOn = (label: string) =>
  eventually(() => {
    expect(clean(copyButton())).toBe(label)
    expect(copyButton().disabled).toBe(false)
    expect(copyReason()).toBeUndefined()
  })
/** The copy of one key, in its section. */
const sectionCopy = (key: string, target = 'staging') =>
  [...section(key).querySelectorAll<HTMLButtonElement>('button')].find(
    (candidate) => clean(candidate) === `Copy to ${target}`,
  )
/** The keys whose section offers the copy of that key. */
const copyable = (target = 'staging') => keys().filter((key) => sectionCopy(key!, target))
/** What the section of a key offers beside its heading, in its order. */
const actions = (key: string) =>
  [...section(key).querySelectorAll('a, button')].filter((candidate) => !candidate.closest('h3')).map(clean)

// the copy dialog is teleported to the body
const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')
const dialogButton = (label: string) =>
  [...(dialog()?.querySelectorAll<HTMLButtonElement>('button') ?? [])].find((candidate) => clean(candidate) === label)
const copyDialog = (view: { host: VueWrapper }) => view.host.findComponent(ViewCopyDialog)

/** Check, then the copy of the documents the check found, in the open dialog. The result stays open. */
async function checkAndCopy(writes: number, target = 'staging') {
  await click(dialogButton('Check')!)
  const label = `Copy ${writes} to ${target}`
  await eventually(() => expect(dialogButton(label)?.disabled).toBe(false))
  expect(clean(dialog())).toContain('Checked: nothing was written')
  await click(dialogButton(label)!)
  await eventually(() => expect(dialogButton('Done')).toBeDefined())
}

async function done(view: { host: VueWrapper }) {
  await click(dialogButton('Done')!)
  await eventually(() => expect(dialog()).toBeNull())
  expect(copyDialog(view).props('open')).toBe(false)
}

async function click(element: HTMLElement) {
  element.click()
  await settle()
}

/** Opens the copy of a key from its section as a pointer or a key does: its button has the focus then. */
async function openSectionCopy(key: string, target = 'staging') {
  const opener = sectionCopy(key, target)!
  opener.focus()
  await click(opener)
  await eventually(() => expect(clean(dialog())).toContain(`Copy ${key} to ${target}`))
  return opener
}

/** Tabs take a press, not a click. */
async function press(element: HTMLElement) {
  element.dispatchEvent(new MouseEvent('mousedown', { button: 0, bubbles: true }))
  await settle()
}

describe('The comparison of a selection of annotations', () => {
  const selection = [
    'sbt.northwind',
    'sbt.initech',
    'sbt.nowhere',
    'sbt.globex',
    'uxe.northwind.notifications.staging.sms',
    'cnt.contoso.us',
  ]

  it('shows one section per key, in key order, with its state, its changed lines and its documents', async () => {
    await deleteAnnotation(org, project, 'staging', 'sbt.globex')
    const requests = recordRequests()
    await mountSelection(`kind=annotations&to=staging&${keysQuery(...selection)}`)

    await compared()
    expect(keys()).toEqual(selection.toSorted())
    expect(heading('cnt.contoso.us')).toBe('Context cnt.contoso.us Only in staging +0 −12')
    expect(heading('sbt.globex')).toMatch(/^Subject sbt\.globex Deleted in staging \+\d+ −0$/)
    expect(heading('sbt.initech')).toBe('Subject sbt.initech Only in default +12 −0')
    expect(heading('sbt.northwind')).toBe('Subject sbt.northwind Different +2 −9')
    expect(heading('sbt.nowhere')).toBe('Subject sbt.nowhere Not in either view')
    expect(heading('uxe.northwind.notifications.staging.sms')).toBe(
      'Unit of execution uxe.northwind.notifications.staging.sms Identical',
    )
    // the colors of the counts are not their only form
    expect(clean(section('sbt.northwind').querySelector('[role="img"][aria-label="2 lines added, 9 removed"]'))).toBe(
      '+2 −9',
    )

    expect(clean(document.body.querySelector('h2'))).toBe('6 annotations: default → staging')
    expect(button('Annotations').getAttribute('href')).toBe('/acme/billing/default/annotations')
    expect(paragraphs()).toContain(ownLegend())
    expect(document.body.querySelector('[role="group"][aria-label="Configuration content"]')).toBeNull()
    expect(chips().map(clean)).toEqual([
      'All 6',
      'Different 1',
      'Only in default 1',
      'Only in staging 1',
      'Identical 1',
      'Not in either view 1',
      'Deleted in staging 1',
    ])
    expect(pressed()).toEqual(['All 6'])

    // different documents and a document on one side start open, the others closed
    expect(selection.toSorted().map(isOpen)).toEqual([true, false, true, true, false, false])
    // a key without a document on either side has nothing to open
    expect(toggleOf('sbt.nowhere')).toBeNull()

    // the target is the old side and the source the new one; a side without a document is an empty text
    expect(editor('sbt.initech')?.textContent).toMatch(/^\n---\n\{\n {2}"Title": "Initech subject"/)
    expect(editor('cnt.contoso.us')?.textContent).toMatch(/\}\n---\n$/)
    const diff = editor('sbt.northwind')!
    expect(diff.textContent?.split('\n---\n')).toHaveLength(2)
    expect(diff.hasAttribute('embedded')).toBe(true)
    expect(diff.getAttribute('original-label')).toBe('sbt.northwind in staging')
    expect(diff.getAttribute('modified-label')).toBe('sbt.northwind in default')
    expect(diff.getAttribute('layout')).toBe('auto')
    // an editor is built 400 px before its section is seen
    expect(new Set(VisibleObserver.margins)).toEqual(new Set(['400px 0px']))

    // each side that has the document links to it; a deleted key links to the Deleted page of the target
    expect(links('sbt.northwind')).toEqual([
      'Open in default: /acme/billing/default/annotations/sbt.northwind',
      'Open in staging: /acme/billing/staging/annotations/sbt.northwind',
    ])
    expect(links('sbt.initech')).toEqual(['Open in default: /acme/billing/default/annotations/sbt.initech'])
    expect(links('cnt.contoso.us')).toEqual(['Open in staging: /acme/billing/staging/annotations/cnt.contoso.us'])
    expect(links('sbt.nowhere')).toEqual([])
    expect(links('sbt.globex')).toEqual([
      'Open in default: /acme/billing/default/annotations/sbt.globex',
      'Deleted: /acme/billing/staging/modeling/deleted',
    ])

    // one read per key and view; only the target is asked for a deleted annotation
    expect(annotationReads(requests).toSorted()).toEqual(
      selection
        .flatMap((key) => [
          `/api/v1/acme/billing/default/annotations/${key}?includeInactive=true`,
          `/api/v1/acme/billing/staging/annotations/${key}?includeInactive=true&includeDeleted=true`,
        ])
        .toSorted(),
    )
  })

  it('filters the sections by a chip, and returns to All when the chip loses its last key', async () => {
    server.use(
      http.get('*/api/v1/acme/billing/staging/annotations/sbt.initech', () =>
        HttpResponse.json({ Message: 'The view is busy.' }, { status: 500 }),
      ),
    )
    await mountSelection(`kind=annotations&to=staging&${keysQuery('sbt.northwind', 'sbt.initech')}`)

    await eventually(() => expect(chips().map(clean)).toEqual(['All 2', 'Different 1', 'Failed 1']))
    expect(heading('sbt.initech')).toBe('Subject sbt.initech Failed')
    const alert = section('sbt.initech').querySelector('[role="alert"]') ?? section('sbt.initech')
    expect(clean(alert)).toContain('The view is busy.')

    await click(chip('Different 1'))
    expect(keys()).toEqual(['sbt.northwind'])
    expect(pressed()).toEqual(['Different 1'])
    // the pressed chip is released by a second click
    await click(chip('Different 1'))
    expect(keys()).toEqual(['sbt.initech', 'sbt.northwind'])
    expect(pressed()).toEqual(['All 2'])

    await click(chip('Failed 1'))
    expect(keys()).toEqual(['sbt.initech'])

    // the read succeeds on Retry: no key is failed any more, so its chip and its filter are gone
    server.resetHandlers()
    await click(button('Retry'))
    await eventually(() => expect(chips().map(clean)).toEqual(['All 2', 'Different 1', 'Only in default 1']))
    expect(pressed()).toEqual(['All 2'])
    expect(keys()).toEqual(['sbt.initech', 'sbt.northwind'])
    expect(heading('sbt.initech')).toBe('Subject sbt.initech Only in default +12 −0')
    // its documents arrived only now, and a document on one side starts open
    expect(isOpen('sbt.initech')).toBe(true)
  })

  it('says how many keys are compared while their documents load', async () => {
    let answer!: () => void
    const answered = new Promise<void>((resolve) => (answer = resolve))
    // returning nothing lets the mock answer it
    server.use(
      http.get('*/api/v1/acme/billing/staging/annotations/sbt.initech', async () => {
        await answered
      }),
    )
    await mountSelection(`kind=annotations&to=staging&${keysQuery('sbt.northwind', 'sbt.initech')}`)

    await eventually(() => expect(live()).toBe('Compared 1 of 2…'))
    expect(heading('sbt.initech')).toBe('Subject sbt.initech Loading…')
    expect(toggleOf('sbt.initech')).toBeNull()
    expect(chips().map(clean)).toEqual(['All 2', 'Different 1'])

    answer()
    await compared()
    expect(heading('sbt.initech')).toBe('Subject sbt.initech Only in default +12 −0')
  })

  it('opens and closes a section, and all of them', async () => {
    const identical = 'uxe.northwind.notifications.staging.sms'
    await mountSelection(`kind=annotations&to=staging&${keysQuery('sbt.northwind', identical)}`)
    await compared()
    expect([isOpen('sbt.northwind'), isOpen(identical)]).toEqual([true, false])
    expect(editor(identical)).toBeNull()

    await click(toggleOf('sbt.northwind')!)
    expect(isOpen('sbt.northwind')).toBe(false)
    expect(editor('sbt.northwind')).toBeNull()

    await click(button('Expand all'))
    await eventually(() => expect(editor(identical)).not.toBeNull())
    expect([isOpen('sbt.northwind'), isOpen(identical)]).toEqual([true, true])
    expect(toggleOf(identical)!.getAttribute('aria-controls')).toBe(editor(identical)!.parentElement!.id)

    await click(button('Collapse all'))
    expect([isOpen('sbt.northwind'), isOpen(identical)]).toEqual([false, false])
    expect(document.body.querySelector('[data-diff]')).toBeNull()
  })

  it('gives every document the layout of the page', async () => {
    await mountSelection(`kind=annotations&to=staging&${keysQuery('sbt.northwind', 'sbt.initech')}`)
    await eventually(() => expect(editor('sbt.initech')).not.toBeNull())
    expect(selectedTab('Diff layout')).toBe('Auto')

    await press(tab('Unified'))

    expect(selectedTab('Diff layout')).toBe('Unified')
    expect(editor('sbt.northwind')!.getAttribute('layout')).toBe('unified')
    expect(editor('sbt.initech')!.getAttribute('layout')).toBe('unified')
  })

  it('builds no editor for a section that is not near the visible part of the page', async () => {
    // the observer of happy-dom: it never reports an element as visible
    vi.unstubAllGlobals()
    await mountSelection(`kind=annotations&to=staging&${keysQuery('sbt.northwind', 'sbt.initech')}`)

    await compared()
    await settle()
    expect([isOpen('sbt.northwind'), isOpen('sbt.initech')]).toEqual([true, true])
    expect(document.body.querySelector('[data-diff]')).toBeNull()
    // the open section keeps the room of its editor
    const body = document.getElementById(toggleOf('sbt.northwind')!.getAttribute('aria-controls')!)!
    expect(body.children).toHaveLength(1)
    expect(body.firstElementChild!.getAttribute('aria-hidden')).toBe('true')
  })

  it('keeps a section open when its documents are read again and are the same now', async () => {
    const view = await mountSelection(`kind=annotations&to=staging&${keysQuery('sbt.northwind')}`)
    await eventually(() => expect(heading('sbt.northwind')).toBe('Subject sbt.northwind Different +2 −9'))

    // staging gets the annotation of default, and the save refreshes the annotation of that view
    const source = await getAnnotation(org, project, 'default', 'sbt.northwind', { includeInactive: true })
    await setAnnotation(org, project, 'staging', 'sbt.northwind', source)
    await invalidateAnnotation(view.queryClient, staging, 'sbt.northwind')

    await eventually(() => expect(heading('sbt.northwind')).toBe('Subject sbt.northwind Identical'))
    expect(isOpen('sbt.northwind')).toBe(true)
    expect(chips().map(clean)).toEqual(['All 1', 'Identical 1'])
  })

  it('never calls a document with a number beyond 2^53 identical', async () => {
    // the same annotation in both views, with a number that the browser rounds
    const key = 'uxe.northwind.notifications.staging.sms'
    for (const view of ['default', 'staging']) {
      const annotation = await getAnnotation(org, project, view, key, { includeInactive: true })
      await setAnnotation(org, project, view, key, { ...annotation, Values: { limit: 9007199254740994 } })
    }
    await mountSelection(`kind=annotations&to=staging&${keysQuery(key)}`)

    await eventually(() => expect(heading(key)).toBe(`Unit of execution ${key} Different`))
    expect(notes(key)).toEqual(['It holds a number beyond 2^53, which the browser cannot compare exactly.'])
    expect(chips().map(clean)).toEqual(['All 1', 'Different 1'])
  })

  // A key may be changed, restored or deleted in a view between two comparisons, in less than the half minute that
  // the app keeps a document fresh.
  it('reads both views again when it is opened again, however fresh the app keeps a document', async () => {
    const key = 'sbt.northwind'
    const view = await mountSelection(`kind=annotations&to=staging&${keysQuery(key)}`)
    keepFresh(view)
    await eventually(() => expect(heading(key)).toBe('Subject sbt.northwind Different +2 −9'))

    // the reader leaves for the comparison of another key, and staging gets the annotation of default meanwhile
    await view.router.push(
      `/acme/billing/default/modeling/compare/selection?kind=annotations&to=staging&${keysQuery('sbt.initech')}`,
    )
    await eventually(() => expect(keys()).toEqual(['sbt.initech']))
    const source = await getAnnotation(org, project, 'default', key, { includeInactive: true })
    await setAnnotation(org, project, 'staging', key, source)
    const requests = recordRequests()

    // the browser's Back opens the first comparison again
    view.router.back()

    // not what it read the first time
    await eventually(() => expect(heading(key)).toBe('Subject sbt.northwind Identical'))
    expect(annotationReads(requests).toSorted()).toEqual([
      `/api/v1/acme/billing/default/annotations/${key}?includeInactive=true`,
      `/api/v1/acme/billing/staging/annotations/${key}?includeInactive=true&includeDeleted=true`,
    ])
  })
})

describe('The comparison of a selection of configurations', () => {
  const inherited = 'uxe.northwind.notifications.prod.push'
  const selection = [
    'sbt.contoso',
    'usg.northwind.invoicing',
    inherited,
    'sbt.northwind',
    'exe.northwind.reporting.test',
    'cnt.contoso.us',
  ]
  const query = `kind=configurations&to=staging&${keysQuery(...selection)}`

  it('compares the own documents, and says when only the effective ones differ', async () => {
    await mountSelection(query)

    await compared()
    expect(keys()).toEqual(selection.toSorted())
    expect(heading('cnt.contoso.us')).toBe('Context cnt.contoso.us Own configuration only in staging +0 −4')
    expect(heading('exe.northwind.reporting.test')).toBe(
      'Execution exe.northwind.reporting.test Own configuration only in default +22 −0',
    )
    expect(heading('sbt.contoso')).toBe('Subject sbt.contoso Different +1 −1')
    expect(heading('sbt.northwind')).toBe('Subject sbt.northwind No own configuration')
    expect(heading('usg.northwind.invoicing')).toBe('Usage usg.northwind.invoicing Identical')
    expect(heading(inherited)).toBe(`Unit of execution ${inherited} Identical Effective differs`)

    // a configuration needs its annotation, which one view does not have
    expect(notes('cnt.contoso.us')).toEqual(['The annotation is not in default'])
    expect(notes('exe.northwind.reporting.test')).toEqual(['The annotation is not in staging'])
    expect(notes('sbt.contoso')).toEqual([])

    expect(clean(document.body.querySelector('h2'))).toBe('6 configurations: default → staging')
    expect(button('Configurations').getAttribute('href')).toBe('/acme/billing/default/configurations')
    expect(selectedTab('Configuration content')).toBe('Own')
    expect(paragraphs()).toContain(ownLegend())
    expect(paragraphs()).not.toContain(effectiveLegend)
    expect(chips().map(clean)).toEqual([
      'All 6',
      'Different 1',
      'Own configuration only in default 1',
      'Own configuration only in staging 1',
      'Identical 2',
      'No own configuration 1',
      'Effective differs 1',
    ])

    // the own documents with their members in the order of their names: staging on the old side
    expect(editor('sbt.contoso')?.textContent).toBe(
      [
        '{\n  "baseUrl": "https://api.contoso.example",\n  "region": "eu-west"\n}',
        '{\n  "baseUrl": "https://api.contoso.example",\n  "region": "us-east"\n}',
      ].join('\n---\n'),
    )
    expect(links('sbt.contoso')).toEqual([
      'Open in default: /acme/billing/default/configurations/sbt.contoso',
      'Open in staging: /acme/billing/staging/configurations/sbt.contoso',
    ])
    expect(links('sbt.northwind')).toEqual([])

    // Effective differs is a filter too
    await click(chip('Effective differs 1'))
    expect(keys()).toEqual([inherited])
  })

  it('shows the effective documents without reading again', async () => {
    const view = await mountSelection(query)
    await compared()
    const requests = recordRequests()

    await press(tab('Effective'))

    await eventually(() => expect(view.router.currentRoute.value.query.content).toBe('effective'))
    await eventually(() => expect(selectedTab('Configuration content')).toBe('Effective'))
    // the legend says what a copy moves: the own configurations, not what is shown now
    expect(paragraphs()).toContain(effectiveLegend)
    expect(paragraphs()).not.toContain(ownLegend())
    expect(heading('cnt.contoso.us')).toMatch(/^Context cnt\.contoso\.us Only in staging \+0 −\d+$/)
    expect(heading('exe.northwind.reporting.test')).toMatch(
      /^Execution exe\.northwind\.reporting\.test Only in default \+\d+ −0$/,
    )
    expect(heading('sbt.contoso')).toBe('Subject sbt.contoso Different +1 −1')
    // without an own document it still reads what it inherits, the same in both views
    expect(heading('sbt.northwind')).toBe('Subject sbt.northwind Identical')
    expect(heading(inherited)).toMatch(new RegExp(`^Unit of execution ${inherited} Different \\+\\d+ −\\d+$`))
    expect(notes(inherited)).toEqual([inheritedNote])
    expect(chips().map(clean)).toEqual([
      'All 6',
      'Different 2',
      'Only in default 1',
      'Only in staging 1',
      'Identical 2',
    ])
    // the sections start again by their new states
    expect(isOpen(inherited)).toBe(true)
    expect(isOpen('sbt.northwind')).toBe(false)
    expect(links('sbt.northwind')).toEqual([
      'Open in default: /acme/billing/default/configurations/sbt.northwind',
      'Open in staging: /acme/billing/staging/configurations/sbt.northwind',
    ])
    // one read per view gave both documents
    expect(configurationReads(requests)).toEqual([])

    // Swap views keeps the content, and Own takes it out of the address
    expect(button('Swap views').getAttribute('href')).toContain('content=effective')
    await press(tab('Own'))
    await eventually(() => expect(view.router.currentRoute.value.query.content).toBeUndefined())
    expect(heading(inherited)).toBe(`Unit of execution ${inherited} Identical Effective differs`)
  })

  it('reads a configuration again after a save in its view', async () => {
    const view = await mountSelection(`kind=configurations&to=staging&${keysQuery('sbt.contoso')}`)
    await eventually(() => expect(heading('sbt.contoso')).toBe('Subject sbt.contoso Different +1 −1'))

    // staging gets the own document of default, which is the version of its effective one
    const { Version } = await getConfiguration(org, project, 'default', 'sbt.contoso')
    const own = await getConfigurationVersion(org, project, 'default', 'sbt.contoso', Version!)
    await patchConfiguration(org, project, 'staging', 'sbt.contoso', [{ op: 'replace', path: '', value: own.Content }])
    await invalidateConfiguration(view.queryClient, staging, 'sbt.contoso')

    await eventually(() => expect(heading('sbt.contoso')).toBe('Subject sbt.contoso Identical'))
    expect(isOpen('sbt.contoso')).toBe(true)
  })
})

// The page is where a copy starts (spec "Copy and Compare Annotations and Configurations Between Views", sections 5
// and 7): "Copy {n} to {target}" for what the filter shows, and "Copy to {target}" in the section of a key.
describe('The copy from a comparison of annotations', () => {
  const identical = 'uxe.northwind.notifications.staging.sms'
  const selection = ['sbt.northwind', 'sbt.initech', 'sbt.nowhere', 'sbt.globex', identical, 'cnt.contoso.us']
  const query = `kind=annotations&to=staging&${keysQuery(...selection)}`
  /** The annotation of a view as the API answers it, an inactive one too. */
  const annotationOf = (view: string, key: string) => getAnnotation(org, project, view, key, { includeInactive: true })

  it('offers the keys that a copy would change, never a key that is deleted in the target', async () => {
    await deleteAnnotation(org, project, 'staging', 'sbt.globex')
    await mountSelection(query)
    await compared()

    // sbt.northwind differs and sbt.initech is only in default; sbt.globex is in default too, but deleted in staging
    expect(heading('sbt.globex')).toMatch(/^Subject sbt\.globex Deleted in staging \+\d+ −0$/)
    await copyOn('Copy 2 to staging')
    expect(copyable()).toEqual(['sbt.initech', 'sbt.northwind'])

    // the copy of a key is the first action of its section
    expect(actions('sbt.northwind')).toEqual(['Copy to staging', 'Open in default', 'Open in staging'])
    expect(actions('sbt.initech')).toEqual(['Copy to staging', 'Open in default'])
    expect(actions('sbt.globex')).toEqual(['Open in default', 'Deleted'])
    expect(actions('cnt.contoso.us')).toEqual(['Open in staging'])
    expect(actions(identical)).toEqual(['Open in default', 'Open in staging'])
    expect(actions('sbt.nowhere')).toEqual([])
  })

  it('counts what the active filter shows, and is off with a reason when that is nothing to copy', async () => {
    await deleteAnnotation(org, project, 'staging', 'sbt.globex')
    const view = await mountSelection(query)
    await compared()
    await copyOn('Copy 2 to staging')

    await click(chip('Different 1'))
    expect(clean(copyButton())).toBe('Copy 1 to staging')
    expect(copyable()).toEqual(['sbt.northwind'])

    await click(chip('Only in default 1'))
    expect(clean(copyButton())).toBe('Copy 1 to staging')
    expect(copyable()).toEqual(['sbt.initech'])

    // nothing of these would change in staging: a copy never deletes, and never restores
    for (const label of ['Only in staging 1', 'Identical 1', 'Not in either view 1', 'Deleted in staging 1']) {
      await click(chip(label))
      expect(pressed()).toEqual([label])
      expect(clean(copyButton())).toBe('Copy to staging')
      expect(copyButton().disabled).toBe(true)
      // the reason is the description of the button, and one text beside it
      expect(copyReason()).toBe('Nothing to copy')
      expect(text().split('Nothing to copy')).toHaveLength(2)
      expect(copyable()).toEqual([])
    }

    await click(copyButton())
    expect(copyDialog(view).props('open')).toBe(false)
    expect(dialog()).toBeNull()

    // the pressed chip is released: all keys again
    await click(chip('Deleted in staging 1'))
    expect(pressed()).toEqual(['All 6'])
    expect(clean(copyButton())).toBe('Copy 2 to staging')
    expect(copyButton().disabled).toBe(false)
    expect(copyReason()).toBeUndefined()
    expect(text()).not.toContain('Nothing to copy')
  })

  it('says that there is nothing to copy only once every document is read', async () => {
    let answer!: () => void
    const answered = new Promise<void>((resolve) => (answer = resolve))
    // returning nothing lets the mock answer it
    server.use(
      http.get('*/api/v1/acme/billing/staging/annotations/sbt.initech', async () => {
        await answered
      }),
    )
    await mountSelection(`kind=annotations&to=staging&${keysQuery(identical, 'sbt.initech')}`)

    await eventually(() => expect(live()).toBe('Compared 1 of 2…'))
    // the document that is read is identical, and the other one may still differ
    expect(clean(copyButton())).toBe('Copy to staging')
    expect(copyButton().disabled).toBe(true)
    expect(copyReason()).toBeUndefined()
    expect(text()).not.toContain('Nothing to copy')

    answer()
    await compared()
    await copyOn('Copy 1 to staging')
  })

  it('is off for a target that is not a view of the project, in every section too', async () => {
    const view = await mountSelection(`kind=annotations&to=legacy&${keysQuery('sbt.northwind', 'sbt.initech')}`)
    await compared()
    // both are only in default: a copy would change them, if legacy were a view
    expect(heading('sbt.initech')).toBe('Subject sbt.initech Only in default +12 −0')

    await eventually(() => expect(copyReason()).toBe('legacy is not a view of this project'))
    expect(copyButton().disabled).toBe(true)
    expect(text().split('legacy is not a view of this project')).toHaveLength(2)
    expect(copyable('legacy')).toEqual([])
    expect(actions('sbt.initech')).toEqual(['Open in default'])

    await click(copyButton())
    expect(copyDialog(view).props('open')).toBe(false)
    expect(dialog()).toBeNull()
  })

  it('is off while the views of the project cannot be read, in every section too', async () => {
    server.use(
      http.get('*/api/v1/:org/:project/views', () => HttpResponse.json({ Message: 'No views' }, { status: 500 })),
    )
    const view = await mountSelection(`kind=annotations&to=staging&${keysQuery('sbt.northwind', 'sbt.initech')}`)
    await compared()
    await eventually(() =>
      expect(view.queryClient.getQueryState(['v1', 'acme', 'billing', 'views'])?.status).toBe('error'),
    )
    // the comparison itself needs no list of views
    expect(heading('sbt.northwind')).toBe('Subject sbt.northwind Different +2 −9')

    // a copy goes only to a view that the project lists, and nobody knows whether staging is one
    expect(copyable()).toEqual([])
    expect(copyButton().disabled).toBe(true)
    await click(copyButton())
    expect(copyDialog(view).props('open')).toBe(false)
    expect(dialog()).toBeNull()
  })

  it('opens the copy with the compared target, the keys and what the page read in the target', async () => {
    const view = await mountSelection(
      `kind=annotations&to=staging&${keysQuery('sbt.northwind', 'sbt.initech', identical)}`,
    )
    await compared()
    await copyOn('Copy 2 to staging')
    const stamp = (await annotationOf('staging', 'sbt.northwind')).Modified!.At
    expect(stamp).toBeTruthy()
    expect(copyDialog(view).props('open')).toBe(false)

    await click(copyButton())

    expect(copyDialog(view).props()).toMatchObject({
      open: true,
      kind: 'annotations',
      keys: ['sbt.initech', 'sbt.northwind'],
      target: 'staging',
      // the page is where the copy is compared
      compare: false,
    })
    // the stamp of the annotation the page read in staging; none for a key that staging lacks
    expect(copyDialog(view).props('expected')).toEqual({ 'sbt.initech': null, 'sbt.northwind': stamp })
    // the target is the compared one: it is text, with nothing to pick
    await eventually(() => expect(clean(dialog())).toContain('Copy 2 annotations to staging'))
    expect(dialog()!.querySelector('[role="combobox"]')).toBeNull()
    expect(clean(dialog())).toContain('When staging already has it')

    await click(dialogButton('Cancel')!)
    await eventually(() => expect(dialog()).toBeNull())

    // the copy of one key, from its section
    await click(sectionCopy('sbt.northwind')!)
    expect(copyDialog(view).props()).toMatchObject({
      open: true,
      kind: 'annotations',
      keys: ['sbt.northwind'],
      target: 'staging',
      compare: false,
    })
    expect(copyDialog(view).props('expected')).toEqual({ 'sbt.northwind': stamp })
    await eventually(() => expect(clean(dialog())).toContain('Copy sbt.northwind to staging'))
  })

  it('turns a copied section into Identical, and the count, the chips and the filter follow', async () => {
    const view = await mountSelection(
      `kind=annotations&to=staging&${keysQuery('sbt.northwind', 'sbt.initech', identical)}`,
    )
    await compared()
    const all = ['sbt.initech', 'sbt.northwind', identical]
    expect(keys()).toEqual(all)
    expect(chips().map(clean)).toEqual(['All 3', 'Different 1', 'Only in default 1', 'Identical 1'])
    await copyOn('Copy 2 to staging')
    // the reader closes the section that started open, and opens the one that started closed
    expect(all.map(isOpen)).toEqual([true, true, false])
    await click(toggleOf('sbt.northwind')!)
    await click(toggleOf(identical)!)
    expect(all.map(isOpen)).toEqual([true, false, true])

    // the copy of what the filter shows: the annotation that is only in default
    await click(chip('Only in default 1'))
    expect(keys()).toEqual(['sbt.initech'])
    expect(clean(copyButton())).toBe('Copy 1 to staging')
    await click(copyButton())
    expect(copyDialog(view).props('keys')).toEqual(['sbt.initech'])

    // Check writes nothing
    await click(dialogButton('Check')!)
    await eventually(() => expect(clean(dialog())).toContain('To create 1'))
    expect((await readAnnotation(staging, 'sbt.initech')).annotation).toBeUndefined()
    expect(heading('sbt.initech')).toBe('Subject sbt.initech Only in default +12 −0')

    await click(dialogButton('Copy 1 to staging')!)
    await eventually(() => expect(clean(dialog())).toContain('Copied to staging'))
    expect(clean(dialog())).toContain('1 created')
    const copied = (await readAnnotation(staging, 'sbt.initech')).annotation
    expect(copied && annotationText(copied)).toBe(annotationText(await annotationOf('default', 'sbt.initech')))
    // the page itself is the comparison of the copy
    expect(dialogButton('Compare with staging')).toBeUndefined()
    await done(view)

    // the key is identical now; its chip is gone, so the filter is All again
    await eventually(() => expect(heading('sbt.initech')).toBe('Subject sbt.initech Identical'))
    expect(chips().map(clean)).toEqual(['All 3', 'Different 1', 'Identical 2'])
    expect(pressed()).toEqual(['All 3'])
    // the sections are where they were, as open as they were
    expect(keys()).toEqual(all)
    expect(all.map(isOpen)).toEqual([true, false, true])
    expect(clean(copyButton())).toBe('Copy 1 to staging')
    expect(copyable()).toEqual(['sbt.northwind'])
    expect(actions('sbt.initech')).toEqual(['Open in default', 'Open in staging'])

    // the copy of the last key that differs, from its section
    await click(sectionCopy('sbt.northwind')!)
    await checkAndCopy(1)
    expect(clean(dialog())).toContain('1 updated')
    await done(view)

    await eventually(() => expect(chips().map(clean)).toEqual(['All 3', 'Identical 3']))
    expect(heading('sbt.northwind')).toBe('Subject sbt.northwind Identical')
    expect(keys()).toEqual(all)
    expect(all.map(isOpen)).toEqual([true, false, true])
    // nothing is left to copy
    expect(clean(copyButton())).toBe('Copy to staging')
    expect(copyButton().disabled).toBe(true)
    expect(copyReason()).toBe('Nothing to copy')
    expect(copyable()).toEqual([])
  })

  it('skips an annotation that changed in the target since it was compared, and shows what it is now', async () => {
    const key = 'sbt.northwind'
    const view = await mountSelection(`kind=annotations&to=staging&${keysQuery(key)}`)
    await compared()
    expect(heading(key)).toBe('Subject sbt.northwind Different +2 −9')

    // somebody else changes the annotation in staging; the page still shows what it read
    const read = await annotationOf('staging', key)
    await setAnnotation(org, project, 'staging', key, { ...read, Title: 'Changed meanwhile' })
    await copyOn('Copy 1 to staging')
    expect(editor(key)!.textContent).not.toContain('Changed meanwhile')

    await click(copyButton())
    expect(copyDialog(view).props('expected')).toEqual({ [key]: read.Modified!.At })
    await click(dialogButton('Copy')!)

    await eventually(() => expect(clean(dialog())).toContain('It changed in staging since it was compared.'))
    expect(clean(dialog())).toContain('1 skipped')
    // the change was not written over
    expect((await annotationOf('staging', key)).Title).toBe('Changed meanwhile')
    await done(view)

    // its section shows what staging holds now
    await eventually(() => expect(editor(key)!.textContent).toContain('"Title": "Changed meanwhile"'))
    expect(heading(key)).toMatch(/^Subject sbt\.northwind Different \+\d+ −\d+$/)
    expect(clean(copyButton())).toBe('Copy 1 to staging')

    // and the copy of what the page shows now goes through
    await click(copyButton())
    await click(dialogButton('Copy')!)
    await eventually(() => expect(clean(dialog())).toContain('1 updated'))
    await done(view)
    await eventually(() => expect(heading(key)).toBe('Subject sbt.northwind Identical'))
    expect((await annotationOf('staging', key)).Title).toBe((await annotationOf('default', key)).Title)
  })

  it('finds an annotation unchanged that got the content of default since it was compared', async () => {
    const key = 'sbt.northwind'
    const view = await mountSelection(`kind=annotations&to=staging&${keysQuery(key)}`)
    await compared()
    expect(heading(key)).toBe('Subject sbt.northwind Different +2 −9')

    // somebody else copies the annotation to staging; the page still shows what it read
    const read = await annotationOf('staging', key)
    await setAnnotation(org, project, 'staging', key, await annotationOf('default', key))
    await copyOn('Copy 1 to staging')
    const writes = recordWrites()

    await click(copyButton())
    expect(copyDialog(view).props('expected')).toEqual({ [key]: read.Modified!.At })
    await click(dialogButton('Copy')!)
    await eventually(() => expect(dialogButton('Done')).toBeDefined())

    // staging is not what the page showed, but it needs no write: that is no reason to skip the key
    expect(clean(dialog())).toContain('Copied to staging')
    expect(clean(dialog())).toContain('1 unchanged')
    expect(clean(dialog())).not.toContain('It changed in staging since it was compared.')
    expect(writes).toEqual([])
    await done(view)

    // the page reads the key again all the same, and has nothing left to copy
    await eventually(() => expect(heading(key)).toBe('Subject sbt.northwind Identical'))
    expect(chips().map(clean)).toEqual(['All 1', 'Identical 1'])
    expect(clean(copyButton())).toBe('Copy to staging')
    expect(copyButton().disabled).toBe(true)
    expect(copyReason()).toBe('Nothing to copy')
    expect(copyable()).toEqual([])
  })

  it('copies what the check found, also when the check made the page read a changed key again', async () => {
    const changed = 'sbt.northwind'
    const view = await mountSelection(`kind=annotations&to=staging&${keysQuery(changed, 'sbt.initech')}`)
    await compared()
    // somebody else changes one of the annotations in staging; the page still shows what it read
    const read = await annotationOf('staging', changed)
    await setAnnotation(org, project, 'staging', changed, { ...read, Title: 'Changed meanwhile' })
    await copyOn('Copy 2 to staging')

    await click(copyButton())
    expect(copyDialog(view).props('expected')).toEqual({ 'sbt.initech': null, [changed]: read.Modified!.At })
    await click(dialogButton('Check')!)
    await eventually(() => expect(clean(dialog())).toContain('To create 1 · To skip 1'))
    expect(clean(dialog())).toContain('It changed in staging since it was compared.')
    // the check made the page read the changed key again, behind the dialog
    await eventually(() => expect(editor(changed)!.textContent).toContain('"Title": "Changed meanwhile"'))
    // the copy still goes by what the page showed when it was opened
    expect(copyDialog(view).props('expected')).toEqual({ 'sbt.initech': null, [changed]: read.Modified!.At })

    // so it does what the check said: one document, not the changed one
    await click(dialogButton('Copy 1 to staging')!)
    await eventually(() => expect(dialogButton('Done')).toBeDefined())
    expect(clean(dialog())).toContain('1 created, 1 skipped')
    expect((await annotationOf('staging', changed)).Title).toBe('Changed meanwhile')
    await done(view)

    await eventually(() => expect(heading('sbt.initech')).toBe('Subject sbt.initech Identical'))
    expect(heading(changed)).toMatch(/^Subject sbt\.northwind Different \+\d+ −\d+$/)
  })

  it('finds what it copied unchanged when a copy that ended early is run again', async () => {
    const view = await mountSelection(`kind=annotations&to=staging&${keysQuery('sbt.initech', 'sbt.northwind')}`)
    await compared()
    // the role is gone when the second annotation is written, which ends the copy as a whole
    server.use(
      http.post('*/api/v1/acme/billing/staging/annotations/sbt.northwind', () =>
        HttpResponse.json(
          { Message: 'The Contributor role on acme.billing is required.', ErrorCode: 'AccessDenied' },
          { status: 403 },
        ),
      ),
    )

    await copyOn('Copy 2 to staging')
    await click(copyButton())
    await click(dialogButton('Copy')!)
    await eventually(() => expect(dialogButton('Try again')).toBeDefined())
    expect(clean(dialog())).toContain('The copy to staging did not finish')
    expect(clean(dialog())).toContain('1 created')

    // the role is back; the annotation that was created is the copy's own change, not one made since the comparison
    server.resetHandlers()
    await click(dialogButton('Try again')!)
    await click(dialogButton('Copy')!)
    await eventually(() => expect(dialogButton('Done')).toBeDefined())
    expect(clean(dialog())).toContain('Copied to staging')
    expect(clean(dialog())).toContain('1 updated, 1 unchanged')
    expect(dialogButton('Try again')).toBeUndefined()
    await done(view)

    await eventually(() => expect(chips().map(clean)).toEqual(['All 2', 'Identical 2']))
  })
})

describe('The copy from a comparison of configurations', () => {
  const inherited = 'uxe.northwind.notifications.prod.push'
  const selection = [
    'sbt.contoso',
    'usg.northwind.invoicing',
    inherited,
    'sbt.northwind',
    'exe.northwind.reporting.test',
    'cnt.contoso.us',
  ]
  const query = `kind=configurations&to=staging&${keysQuery(...selection)}`
  /** The own configuration of a key in a view, as the page compares it. */
  const ownOf = async (scope: typeof staging, key: string) => canonicalJson((await readConfiguration(scope, key)).own)

  it('offers the keys whose own configuration a copy would change, whatever is shown', async () => {
    const view = await mountSelection(query)
    await compared()

    // sbt.contoso differs, and exe.northwind.reporting.test has an own configuration only in default
    await copyOn('Copy 2 to staging')
    expect(copyable()).toEqual(['exe.northwind.reporting.test', 'sbt.contoso'])
    expect(actions('sbt.contoso')).toEqual(['Copy to staging', 'Open in default', 'Open in staging'])
    // an own configuration that only staging has stays: a copy never deletes
    expect(actions('cnt.contoso.us')).toEqual(['Open in staging'])

    // the own configurations of this key are the same: a copy changes nothing, although the effective ones differ
    await click(chip('Effective differs 1'))
    expect(keys()).toEqual([inherited])
    expect(copyButton().disabled).toBe(true)
    expect(copyReason()).toBe('Nothing to copy')
    await click(chip('Effective differs 1'))

    await press(tab('Effective'))
    await eventually(() => expect(view.router.currentRoute.value.query.content).toBe('effective'))
    await eventually(() => expect(selectedTab('Configuration content')).toBe('Effective'))

    // the same two keys: the copy moves own configurations, not what is shown
    expect(clean(copyButton())).toBe('Copy 2 to staging')
    expect(copyable()).toEqual(['exe.northwind.reporting.test', 'sbt.contoso'])
    // a section whose own configurations are the same has no copy, and says why
    expect(heading(inherited)).toMatch(new RegExp(`^Unit of execution ${inherited} Different \\+\\d+ −\\d+$`))
    expect(actions(inherited)).toEqual(['Open in default', 'Open in staging'])
    expect(notes(inherited)).toEqual([inheritedNote])
    expect(notes('sbt.contoso')).toEqual([])

    // of the two documents that differ, a copy changes one
    await click(chip('Different 2'))
    expect(keys()).toEqual([inherited, 'sbt.contoso'].toSorted())
    expect(clean(copyButton())).toBe('Copy 1 to staging')
    await click(chip('Identical 2'))
    expect(copyReason()).toBe('Nothing to copy')
  })

  it('opens the copy with the versions of the own configurations the page read in the target', async () => {
    const view = await mountSelection(query)
    await compared()
    const { Version } = await getConfiguration(org, project, 'staging', 'sbt.contoso')
    expect(Version).toBeGreaterThan(0)
    await copyOn('Copy 2 to staging')

    await click(copyButton())

    expect(copyDialog(view).props()).toMatchObject({
      open: true,
      kind: 'configurations',
      keys: ['exe.northwind.reporting.test', 'sbt.contoso'],
      target: 'staging',
      compare: false,
    })
    // the annotation of exe.northwind.reporting.test is not in staging, so nothing is stored for it there
    expect(copyDialog(view).props('expected')).toEqual({ 'exe.northwind.reporting.test': 0, 'sbt.contoso': Version })
    await eventually(() => expect(clean(dialog())).toContain('Copy 2 configurations to staging'))
    expect(dialog()!.querySelector('[role="combobox"]')).toBeNull()
    expect(clean(dialog())).toContain('Copy missing annotations too')
  })

  it('turns the copied sections into Identical, with the annotation that the target lacked', async () => {
    const missing = 'exe.northwind.reporting.test'
    const view = await mountSelection(query)
    await compared()
    expect(notes(missing)).toEqual(['The annotation is not in staging'])
    expect(selection.toSorted().map(isOpen)).toEqual([true, true, true, false, false, false])

    // two keys of the page, and four documents: the configurations, and for the execution its annotation and the
    // context above it, which staging lacks too
    await copyOn('Copy 2 to staging')
    await click(copyButton())
    await click(dialogButton('Check')!)
    await eventually(() => expect(clean(dialog())).toContain('To create 3 · To update 1'))
    expect((await readAnnotation(staging, missing)).annotation).toBeUndefined()
    await click(dialogButton('Copy 4 to staging')!)
    await eventually(() => expect(dialogButton('Done')).toBeDefined())
    expect(clean(dialog())).toContain('Copied to staging')
    expect(clean(dialog())).toContain('3 created, 1 updated')
    expect(dialogButton('Compare with staging')).toBeUndefined()
    await done(view)

    expect((await readAnnotation(staging, missing)).annotation).toBeDefined()
    expect((await readAnnotation(staging, 'cnt.northwind.test')).annotation).toBeDefined()

    expect(await ownOf(staging, 'sbt.contoso')).toBe(await ownOf(production, 'sbt.contoso'))
    expect(await ownOf(staging, missing)).toBe(await ownOf(production, missing))

    await eventually(() => expect(heading('sbt.contoso')).toBe('Subject sbt.contoso Identical'))
    await eventually(() => expect(heading(missing)).toBe(`Execution ${missing} Identical`))
    // its annotation is in staging now
    expect(notes(missing)).toEqual([])
    expect(actions(missing)).toEqual(['Open in default', 'Open in staging'])
    expect(chips().map(clean)).toEqual([
      'All 6',
      'Own configuration only in staging 1',
      'Identical 4',
      'No own configuration 1',
      'Effective differs 1',
    ])
    // the sections are where they were, as open as they were
    expect(keys()).toEqual(selection.toSorted())
    expect(selection.toSorted().map(isOpen)).toEqual([true, true, true, false, false, false])
    expect(copyButton().disabled).toBe(true)
    expect(copyReason()).toBe('Nothing to copy')
    expect(copyable()).toEqual([])
  })

  it('makes a descendant Identical too in Effective, with the copy of the configuration of its ancestor', async () => {
    // The own configuration of the execution differs between the views. Its unit of execution inherits it: the own
    // configurations of the unit are the same, and its effective ones differ only by what the execution gives them.
    const ancestor = 'exe.northwind.notifications.prod'
    const view = await mountSelection(
      `kind=configurations&to=staging&content=effective&${keysQuery(ancestor, inherited)}`,
    )
    await compared()
    expect(paragraphs()).toContain(effectiveLegend)
    expect(heading(ancestor)).toMatch(new RegExp(`^Execution ${ancestor} Different \\+\\d+ −\\d+$`))
    expect(heading(inherited)).toMatch(new RegExp(`^Unit of execution ${inherited} Different \\+\\d+ −\\d+$`))
    expect(notes(ancestor)).toEqual([])
    expect(notes(inherited)).toEqual([inheritedNote])
    expect(chips().map(clean)).toEqual(['All 2', 'Different 2'])
    // only the execution has something to copy
    await copyOn('Copy 1 to staging')
    expect(copyable()).toEqual([ancestor])
    expect([isOpen(ancestor), isOpen(inherited)]).toEqual([true, true])

    await click(sectionCopy(ancestor)!)
    expect(copyDialog(view).props()).toMatchObject({ kind: 'configurations', keys: [ancestor], target: 'staging' })
    await checkAndCopy(1)
    expect(clean(dialog())).toContain('1 updated')
    await done(view)

    expect(await ownOf(staging, ancestor)).toBe(await ownOf(production, ancestor))
    // the copy wrote one document, and both keys read the same in both views now
    await eventually(() => expect(heading(ancestor)).toBe(`Execution ${ancestor} Identical`))
    await eventually(() => expect(heading(inherited)).toBe(`Unit of execution ${inherited} Identical`))
    expect(notes(inherited)).toEqual([])
    expect(chips().map(clean)).toEqual(['All 2', 'Identical 2'])
    expect([isOpen(ancestor), isOpen(inherited)]).toEqual([true, true])
    expect(copyReason()).toBe('Nothing to copy')
    expect(copyable()).toEqual([])

    // and the own configurations say so too: nothing differs any more
    await press(tab('Own'))
    await eventually(() => expect(selectedTab('Configuration content')).toBe('Own'))
    expect(heading(inherited)).toBe(`Unit of execution ${inherited} Identical`)
    expect(chips().map(clean)).toEqual(['All 2', 'Identical 2'])
  })

  it('skips a configuration that changed in the target since it was compared, and shows what it is now', async () => {
    const key = 'sbt.contoso'
    const view = await mountSelection(`kind=configurations&to=staging&${keysQuery(key)}`)
    await compared()
    expect(heading(key)).toBe('Subject sbt.contoso Different +1 −1')
    const { Version } = await getConfiguration(org, project, 'staging', key)

    // somebody else saves the configuration in staging; the page still shows what it read
    await patchConfiguration(org, project, 'staging', key, [{ op: 'replace', path: '/region', value: 'ap-south' }])
    await copyOn('Copy 1 to staging')
    expect(editor(key)!.textContent).not.toContain('ap-south')

    await click(copyButton())
    expect(copyDialog(view).props('expected')).toEqual({ [key]: Version })
    await click(dialogButton('Copy')!)

    await eventually(() => expect(clean(dialog())).toContain('It changed in staging since it was compared.'))
    expect(clean(dialog())).toContain('1 skipped')
    // the saved configuration was not written over
    expect(await ownOf(staging, key)).toContain('"region": "ap-south"')
    await done(view)

    // its section shows what staging holds now
    await eventually(() => expect(editor(key)!.textContent).toContain('"region": "ap-south"'))
    expect(heading(key)).toBe('Subject sbt.contoso Different +1 −1')
    expect(clean(copyButton())).toBe('Copy 1 to staging')
  })

  it('finds a configuration unchanged that got the content of default since it was compared', async () => {
    const key = 'sbt.contoso'
    const view = await mountSelection(`kind=configurations&to=staging&${keysQuery(key)}`)
    await compared()
    expect(heading(key)).toBe('Subject sbt.contoso Different +1 −1')
    const { Version } = await getConfiguration(org, project, 'staging', key)

    // somebody else saves the document of default in staging; the page still shows what it read
    const own = (await readConfiguration(production, key)).own
    await patchConfiguration(org, project, 'staging', key, [{ op: 'replace', path: '', value: own }])
    await copyOn('Copy 1 to staging')
    const writes = recordWrites()

    await click(copyButton())
    expect(copyDialog(view).props('expected')).toEqual({ [key]: Version })
    await click(dialogButton('Copy')!)
    await eventually(() => expect(dialogButton('Done')).toBeDefined())

    expect(clean(dialog())).toContain('Copied to staging')
    expect(clean(dialog())).toContain('1 unchanged')
    expect(clean(dialog())).not.toContain('It changed in staging since it was compared.')
    expect(writes).toEqual([])
    await done(view)

    await eventually(() => expect(heading(key)).toBe('Subject sbt.contoso Identical'))
    expect(chips().map(clean)).toEqual(['All 1', 'Identical 1'])
    expect(copyButton().disabled).toBe(true)
    expect(copyReason()).toBe('Nothing to copy')
    expect(copyable()).toEqual([])
  })
})

// The copy is opened for the comparison as it is: its keys, and what the page saw of the target, hold for no other.
describe('The copy of a comparison that changes under it', () => {
  const key = 'sbt.northwind'
  const address = (query: string) => `/acme/billing/default/modeling/compare/selection?${query}`
  const title = () => clean(document.body.querySelector('h2'))

  it('closes, and drops its keys and what it saw of the target, when Swap views turns it around', async () => {
    const view = await mountSelection(`kind=annotations&to=staging&${keysQuery(key, 'sbt.initech')}`)
    await compared()
    await copyOn('Copy 2 to staging')
    await openSectionCopy(key)
    expect(copyDialog(view).props()).toMatchObject({ open: true, target: 'staging' })
    expect(copyDialog(view).props('keys')).toEqual([key])
    expect(Object.keys(copyDialog(view).props('expected') ?? {})).toEqual([key])

    await click(button('Swap views'))
    await eventually(() => expect(title()).toBe('2 annotations: staging → default'))

    // a copy would go the other way now, by what the page saw of staging
    expect(copyDialog(view).props()).toMatchObject({ open: false, target: 'default' })
    expect(copyDialog(view).props('keys')).toEqual([])
    expect(copyDialog(view).props('expected')).toEqual({})
    await eventually(() => expect(dialog()).toBeNull())

    // the copy of the comparison as it is now goes by what the page reads of default
    await compared()
    await copyOn('Copy 1 to default')
    const stamp = (await getAnnotation(org, project, 'default', key, { includeInactive: true })).Modified!.At
    await openSectionCopy(key, 'default')
    expect(copyDialog(view).props()).toMatchObject({ open: true, target: 'default' })
    expect(copyDialog(view).props('keys')).toEqual([key])
    expect(copyDialog(view).props('expected')).toEqual({ [key]: stamp })
  })

  // The copy is opened in the comparison of the one annotation of default with the target of the case. Back then
  // opens the comparison before it, of the view and the address of the case: it differs in the one thing named.
  it.each([
    ['other keys', 'default', `kind=annotations&to=staging&${keysQuery(key, 'sbt.initech')}`, 'staging'],
    ['another target', 'default', `kind=annotations&to=qa&${keysQuery(key)}`, 'staging'],
    ['the other kind', 'default', `kind=configurations&to=staging&${keysQuery(key)}`, 'staging'],
    ['another source', 'staging', `kind=annotations&to=qa&${keysQuery(key)}`, 'qa'],
  ])('closes, and drops its keys and what it saw, when Back opens a comparison of %s', async (_, from, query, to) => {
    server.use(http.get('*/api/v1/:org/:project/views', () => HttpResponse.json(views('default', 'staging', 'qa'))))
    const view = await mountSelection(query, { org, project, view: from })
    await compared()
    const before = title()
    expect(before).not.toBe(`1 annotation: default → ${to}`)
    await view.router.push(address(`kind=annotations&to=${to}&${keysQuery(key)}`))
    await eventually(() => expect(title()).toBe(`1 annotation: default → ${to}`))
    await compared()
    await copyOn(`Copy 1 to ${to}`)
    await openSectionCopy(key, to)
    expect(copyDialog(view).props()).toMatchObject({ open: true, kind: 'annotations', target: to })
    expect(copyDialog(view).props('keys')).toEqual([key])
    expect(Object.keys(copyDialog(view).props('expected') ?? {})).toEqual([key])

    view.router.back()
    await eventually(() => expect(title()).toBe(before))

    expect(copyDialog(view).props('open')).toBe(false)
    expect(copyDialog(view).props('keys')).toEqual([])
    expect(copyDialog(view).props('expected')).toEqual({})
    await eventually(() => expect(dialog()).toBeNull())
  })
})

// The dialog returns the focus to the button that opened it. A button that went with what it copied, or that is off
// now, cannot take it: the page gives the focus a home, so it does not fall to the top of the document.
describe('The focus after a copy from a comparison', () => {
  const key = 'sbt.northwind'
  const query = `kind=annotations&to=staging&${keysQuery(key, 'sbt.initech')}`
  const title = () => document.body.querySelector<HTMLElement>('h2')!

  /** Copies in the open dialog, and waits until the page behind it shows the keys as copied. */
  async function copyAndWait(...copied: string[]) {
    await click(dialogButton('Copy')!)
    await eventually(() => expect(dialogButton('Done')).toBeDefined())
    await eventually(() => copied.forEach((each) => expect(heading(each)).toMatch(/ Identical$/)))
  }

  it('goes to the heading of the copied section, whose copy button is gone', async () => {
    const view = await mountSelection(query)
    await compared()
    await copyOn('Copy 2 to staging')
    const opener = await openSectionCopy(key)
    expect(dialog()!.contains(document.activeElement)).toBe(true)

    await copyAndWait(key)
    // the section has nothing to copy any more
    expect(sectionCopy(key)).toBeUndefined()
    expect(opener.isConnected).toBe(false)
    await done(view)

    expect(document.activeElement).toBe(toggleOf(key))
    expect(clean(document.activeElement)).toContain('sbt.northwind')
  })

  it('goes to the heading of the first copied section when the copy of the page is off afterwards', async () => {
    const view = await mountSelection(query)
    await compared()
    await copyOn('Copy 2 to staging')
    const opener = copyButton()
    opener.focus()
    await click(opener)
    // the keys of the page, in its order
    expect(copyDialog(view).props('keys')).toEqual(['sbt.initech', key])

    await copyAndWait('sbt.initech', key)
    // the button is there, and off: nothing is left to copy
    expect(opener.isConnected).toBe(true)
    expect(opener.disabled).toBe(true)
    await done(view)

    expect(document.activeElement).toBe(toggleOf('sbt.initech'))
  })

  it('goes to the title of the page when the filter does not show the copied section any more', async () => {
    // two subjects that differ between the views
    const view = await mountSelection(`kind=annotations&to=staging&${keysQuery(key, 'sbt.contoso')}`)
    await compared()
    await copyOn('Copy 2 to staging')
    await click(chip('Different 2'))
    const opener = await openSectionCopy(key)

    await click(dialogButton('Copy')!)
    await eventually(() => expect(dialogButton('Done')).toBeDefined())
    // the copied key is identical now, and the filter shows the other one only
    await eventually(() => expect(keys()).toEqual(['sbt.contoso']))
    expect(pressed()).toEqual(['Different 1'])
    expect(opener.isConnected).toBe(false)
    await done(view)

    expect(document.activeElement).toBe(title())
    expect(clean(document.activeElement)).toBe('2 annotations: default → staging')
  })

  it('stays with the dialog, which returns it to the button that opened it, while that button is there', async () => {
    const view = await mountSelection(query)
    await compared()
    await copyOn('Copy 2 to staging')
    const opener = await openSectionCopy(key)
    const home = vi.spyOn(toggleOf(key)!, 'focus')
    const top = vi.spyOn(title(), 'focus')

    await click(dialogButton('Cancel')!)
    await eventually(() => expect(dialog()).toBeNull())
    await settle()
    expect(copyDialog(view).props('open')).toBe(false)

    // the page does not take the focus from where the dialog puts it
    expect(opener.isConnected).toBe(true)
    expect(home).not.toHaveBeenCalled()
    expect(top).not.toHaveBeenCalled()
    expect(document.activeElement).toBe(opener)
  })
})

describe('The views of a comparison', () => {
  const selection = keysQuery('sbt.northwind', 'sbt.initech')

  it.each([
    ['without ?to=', ''],
    ['instead of ?to= naming the view of the route', '&to=default'],
  ])('preselects the only other view %s', async (_, to) => {
    const view = await mountSelection(`kind=annotations${to}&${selection}`)

    // ?to= naming the view of the route is no target
    await eventually(() => expect(view.router.currentRoute.value.query.to).toBe('staging'))
    expect(view.router.currentRoute.value.query).toMatchObject({
      kind: 'annotations',
      key: ['sbt.northwind', 'sbt.initech'],
    })
    await eventually(() => expect(heading('sbt.northwind')).toBe('Subject sbt.northwind Different +2 −9'))
    expect(targetSelect(view).props('modelValue')).toBe('staging')
    expect(clean(document.body.querySelector('[role="combobox"][aria-label="Target view"]'))).toBe('staging')
  })

  it('asks for the target among several views, and remembers the choice', async () => {
    server.use(http.get('*/api/v1/:org/:project/views', () => HttpResponse.json(views('default', 'staging', 'qa'))))
    const requests = recordRequests()
    const view = await mountSelection(`kind=annotations&${selection}`)

    await eventually(() => expect(text()).toContain('Pick the view to compare with'))
    expect(targetSelect(view).props('items')).toEqual(['staging', 'qa'])
    expect(view.router.currentRoute.value.query.to).toBeUndefined()
    expect(sections()).toEqual([])
    // nothing is read without a target
    expect(annotationReads(requests)).toEqual([])
    expect(button('Swap views').hasAttribute('href')).toBe(false)

    targetSelect(view).vm.$emit('update:modelValue', 'qa')

    await eventually(() => expect(view.router.currentRoute.value.query.to).toBe('qa'))
    expect(useViewTransferStore().lastTarget('acme.billing')).toBe('qa')
    await eventually(() => expect(clean(document.body.querySelector('h2'))).toBe('2 annotations: default → qa'))
    // the mock has no data in that view
    await eventually(() =>
      expect(heading('sbt.northwind')).toMatch(/^Subject sbt\.northwind Only in default \+\d+ −0$/),
    )
  })

  it('preselects the target last used in the project while it is still a view', async () => {
    server.use(http.get('*/api/v1/:org/:project/views', () => HttpResponse.json(views('default', 'staging', 'qa'))))
    useViewTransferStore().rememberTarget('acme.billing', 'qa')
    const view = await mountSelection(`kind=annotations&${selection}`)

    await eventually(() => expect(view.router.currentRoute.value.query.to).toBe('qa'))
  })

  it('preselects nothing when the target last used is no view any more', async () => {
    server.use(http.get('*/api/v1/:org/:project/views', () => HttpResponse.json(views('default', 'staging', 'qa'))))
    useViewTransferStore().rememberTarget('acme.billing', 'gone')
    const view = await mountSelection(`kind=annotations&${selection}`)

    await eventually(() => expect(text()).toContain('Pick the view to compare with'))
    expect(view.router.currentRoute.value.query.to).toBeUndefined()
  })

  it('compares with a ?to= that is not a listed view, and keeps it', async () => {
    const requests = recordRequests()
    const view = await mountSelection(`kind=annotations&to=legacy&${selection}`)

    await eventually(() => expect(heading('sbt.initech')).toBe('Subject sbt.initech Only in default +12 −0'))
    expect(view.router.currentRoute.value.query.to).toBe('legacy')
    expect(clean(document.body.querySelector('h2'))).toBe('2 annotations: default → legacy')
    // the select shows what is compared
    expect(targetSelect(view).props('items')).toEqual(['staging', 'legacy'])
    expect(targetSelect(view).props('modelValue')).toBe('legacy')
    expect(annotationReads(requests).filter((url) => url.includes('/legacy/'))).toHaveLength(2)
  })

  it('turns the comparison around with Swap views, and reads no view against itself on the way', async () => {
    const view = await mountSelection(`kind=annotations&to=staging&${selection}`)
    keepFresh(view)
    await compared()
    const swap = button('Swap views')
    expect(swap.getAttribute('href')).toBe(
      '/acme/billing/staging/modeling/compare/selection?kind=annotations&to=default&key=sbt.initech&key=sbt.northwind',
    )
    const requests = recordRequests()

    await click(swap)

    await eventually(() => expect(clean(document.body.querySelector('h2'))).toBe('2 annotations: staging → default'))
    expect(view.router.currentRoute.value.path).toBe('/acme/billing/staging/modeling/compare/selection')
    expect(view.router.currentRoute.value.query.to).toBe('default')
    // the same documents, on the other sides
    await eventually(() => expect(heading('sbt.northwind')).toBe('Subject sbt.northwind Different +9 −2'))
    expect(heading('sbt.initech')).toBe('Subject sbt.initech Only in default +0 −12')
    expect(editor('sbt.initech')?.textContent).toMatch(/\}\n---\n$/)
    expect(targetSelect(view).props('modelValue')).toBe('default')
    expect(button('Annotations').getAttribute('href')).toBe('/acme/billing/staging/annotations')
    // default is the target now, so it is the view that is asked for deleted annotations
    expect(annotationReads(requests).toSorted()).toEqual(
      ['sbt.initech', 'sbt.northwind']
        .flatMap((key) => [
          `/api/v1/acme/billing/default/annotations/${key}?includeInactive=true&includeDeleted=true`,
          `/api/v1/acme/billing/staging/annotations/${key}?includeInactive=true`,
        ])
        .toSorted(),
    )
  })

  it('tells a key that is deleted in the target after Swap views', async () => {
    await deleteAnnotation(org, project, 'default', 'sbt.initech')
    keepFresh(await mountSelection(`kind=annotations&to=staging&${keysQuery('sbt.initech')}`))
    // the source is read without its deleted annotations
    await eventually(() => expect(heading('sbt.initech')).toBe('Subject sbt.initech Not in either view'))

    await click(button('Swap views'))

    await eventually(() => expect(heading('sbt.initech')).toBe('Subject sbt.initech Deleted in default'))
    expect(links('sbt.initech')).toEqual(['Deleted: /acme/billing/default/modeling/deleted'])
  })

  it('shows the production badge beside a default target', async () => {
    const badge = () =>
      [...document.body.querySelectorAll('span')].some(
        (candidate) => candidate.children.length === 0 && clean(candidate) === 'prod',
      )
    await mountSelection(`kind=annotations&to=staging&${selection}`)
    await compared()
    expect(badge()).toBe(false)

    await click(button('Swap views'))

    await eventually(() => expect(badge()).toBe(true))
    expect(text()).toContain('now')
    expect(text()).toContain('after a copy')
    // the legend and the copy name the target of the comparison as it is now
    expect(paragraphs()).toContain(ownLegend('default'))
    await copyOn('Copy 1 to default')
    expect(copyable('default')).toEqual(['sbt.northwind'])
  })

  it('follows a navigation to another view and another target, and reads only their documents', async () => {
    server.use(http.get('*/api/v1/:org/:project/views', () => HttpResponse.json(views('default', 'staging', 'qa'))))
    const view = await mountSelection(`kind=annotations&to=staging&${selection}`)
    await compared()
    const requests = recordRequests()

    // from default with staging to qa with default: qa is not compared with staging on the way
    await expect(
      view.router.push(`/acme/billing/qa/modeling/compare/selection?kind=annotations&to=default&${selection}`),
    ).resolves.toBeUndefined()

    await eventually(() => expect(clean(document.body.querySelector('h2'))).toBe('2 annotations: qa → default'))
    await compared()
    expect(annotationReads(requests).filter((url) => url.includes('/staging/'))).toEqual([])
    expect(annotationReads(requests).filter((url) => url.includes('/qa/'))).toHaveLength(2)
  })

  // The context guard puts the next project in the store before the navigation lands (424-64).
  it('preselects nothing before a navigation to another project has landed', async () => {
    // acme/shipping has only its default view, so no target is picked there
    const view = await mountSelection(`kind=annotations&${selection}`, { org, project: 'shipping', view: 'default' })
    await eventually(() => expect(text()).toContain('No other view to compare with'))
    // the views of the next project are known, so its only other view could be taken at once
    view.queryClient.setQueryData(['v1', 'acme', 'billing', 'views'], views('default', 'staging'))

    // a preselection on the way would cancel the navigation
    await expect(
      view.router.push(`/acme/billing/default/modeling/compare/selection?kind=annotations&${selection}`),
    ).resolves.toBeUndefined()

    expect(view.router.currentRoute.value.path).toBe('/acme/billing/default/modeling/compare/selection')
    await eventually(() => expect(view.router.currentRoute.value.query.to).toBe('staging'))
  })
})

describe('The views of the project that cannot be read', () => {
  it('says so with Retry instead of asking to pick a view from an empty list', async () => {
    let failing = true
    server.use(
      http.get('*/api/v1/acme/billing/views', () =>
        failing ? HttpResponse.json({ Message: 'The database is busy.' }, { status: 500 }) : undefined,
      ),
    )
    await mountSelection(`kind=annotations&${keysQuery('sbt.northwind')}`)

    await eventually(() => expect(text()).toContain('The views of the project could not be loaded'))
    expect(text()).not.toContain('Pick the view to compare with')

    failing = false
    await click(button('Retry'))
    await eventually(() => expect(keys()).toEqual(['sbt.northwind']))
  })
})

describe('The comparison for a Reader', () => {
  // Modeling is open to every member (424-272), and a comparison only reads; a copy writes to the target, which needs
  // the Contributor role.
  it('compares, without any copy', async () => {
    const view = await mountSelection(
      `kind=annotations&to=staging&${keysQuery('sbt.northwind', 'sbt.initech')}`,
      production,
      'Reader',
    )
    await compared()

    expect(heading('sbt.northwind')).toBe('Subject sbt.northwind Different +2 −9')
    expect(heading('sbt.initech')).toBe('Subject sbt.initech Only in default +12 −0')
    // no copy of the page, of a section or of the dialog, and the legend says nothing of a copy
    expect(copyButton()).toBeUndefined()
    expect(copyable()).toEqual([])
    expect(actions('sbt.northwind')).toEqual(['Open in default', 'Open in staging'])
    expect(copyDialog(view).exists()).toBe(false)
    expect(paragraphs()).toContain('Only the selected keys are compared.')
    expect(text()).not.toContain('after a copy')
    expect(text()).toContain('this view')
  })

  it('compares the effective configurations, and says nothing of a copy', async () => {
    await mountSelection(
      `kind=configurations&to=staging&content=effective&${keysQuery('sbt.contoso')}`,
      production,
      'Reader',
    )
    await compared()

    expect(paragraphs()).toContain('What staging and default read.')
    expect(copyButton()).toBeUndefined()
    expect(copyable()).toEqual([])
  })

  it('says how to start a comparison from a detail', async () => {
    await mountSelection('kind=annotations&to=staging', production, 'Reader')

    expect(text()).toContain('Open an annotation or a configuration and choose Compare with view in its More actions.')
  })
})

describe('The comparison as a page of Modeling', () => {
  it('selects the Compare views tab, and a click on it opens Compare views itself', async () => {
    const view = await mountSelection(`kind=annotations&to=staging&${keysQuery('sbt.northwind')}`)
    await compared()
    // the tabs of Modeling are the first ones of the page
    const modeling = document.body.querySelector('[role="tablist"]')!
    expect(clean(modeling.querySelector('[role="tab"][aria-selected="true"]'))).toBe('Compare views')

    await press(tab('Compare views'))

    // the navigation loads the page of Compare views first
    await vi.waitFor(() => expect(view.router.currentRoute.value.path).toBe('/acme/billing/default/modeling/compare'), {
      timeout: 15_000,
    })
  }, 20_000)
})

describe('A comparison without documents', () => {
  it('says how to start one without keys', async () => {
    const requests = recordRequests()
    await mountSelection('kind=annotations&to=staging')

    expect(text()).toContain('Nothing to compare')
    expect(text()).toContain('Select rows on Annotations or Configurations and choose Compare with view.')
    expect(button('Annotations').getAttribute('href')).toBe('/acme/billing/default/annotations')
    expect(button('Configurations').getAttribute('href')).toBe('/acme/billing/default/configurations')
    expect(document.body.querySelector('h2')?.textContent).toBe('Nothing to compare')
    expect(annotationReads(requests)).toEqual([])
  })

  it('says that a long selection is gone in another browser tab', async () => {
    await mountSelection('kind=annotations&to=staging&set=1abc2de')

    expect(text()).toContain('Nothing to compare')
    expect(text()).toContain(
      'The selection is gone. A comparison of many rows opens only in the browser tab that started it.',
    )
  })

  it('compares the keys of a long selection that the browser tab kept', async () => {
    const set = useViewTransferStore().remember(['sbt.northwind', 'sbt.initech'])
    await mountSelection(`kind=annotations&to=staging&set=${set}`)

    await compared()
    expect(keys()).toEqual(['sbt.initech', 'sbt.northwind'])
    // the other side of the comparison finds the same list
    expect(button('Swap views').getAttribute('href')).toBe(
      `/acme/billing/staging/modeling/compare/selection?kind=annotations&to=default&set=${set}`,
    )
  })

  it('says that the project has no other view', async () => {
    const requests = recordRequests()
    // acme/shipping has only its default view
    const view = await mountSelection(`kind=annotations&${keysQuery('sbt.northwind')}`, {
      org,
      project: 'shipping',
      view: 'default',
    })

    await eventually(() => expect(text()).toContain('No other view to compare with'))
    expect(text()).not.toContain('Pick the view to compare with')
    expect(clean(document.body.querySelector('h2'))).toBe('1 annotation: default')
    expect(view.router.currentRoute.value.query.to).toBeUndefined()
    expect(annotationReads(requests)).toEqual([])
  })

  it('compares the first 100 keys of a longer address, four reads at a time', async () => {
    let reading = 0
    let most = 0
    const isRead = (request: Request) => /\/annotations\/sbt\./.test(new URL(request.url).pathname)
    server.events.on('request:start', ({ request }) => {
      if (isRead(request)) {
        most = Math.max(most, ++reading)
      }
    })
    server.events.on('response:mocked', ({ request }) => {
      if (isRead(request)) {
        reading--
      }
    })
    const many = Array.from({ length: 101 }, (_, index) => `sbt.key-${String(index).padStart(3, '0')}`)
    await mountSelection(`kind=annotations&to=staging&${keysQuery(...many)}`)

    expect(text()).toContain('Only the first 100 keys are compared.')
    expect(clean(document.body.querySelector('h2'))).toBe('100 annotations: default → staging')
    // two hundred reads take their time
    await vi.waitFor(() => expect(chips().map(clean)).toEqual(['All 100', 'Not in either view 100']), {
      timeout: 15_000,
    })
    expect(keys()).toEqual(many.slice(0, 100))
    expect(most).toBe(4)
  }, 20_000)
})

describe('DiffEditor', () => {
  const before = '{\n  "retries": 1\n}'
  const after = '{\n  "retries": 2\n}'
  const names = () =>
    [...document.body.querySelectorAll('[role="textbox"]')].map((candidate) => candidate.getAttribute('aria-label'))

  /** The editor itself: the other tests replace it. */
  function mountEditor(props: Record<string, unknown>) {
    const view = mountInApp(DiffEditor, { original: before, modified: after, ...props }, [], { DiffEditor })
    mounted.push(view)
    return view
  }

  it('names its editors, and has its toolbar and its side labels', async () => {
    mountEditor({ layout: 'split' })
    await settle()

    expect(names()).toEqual(['Before', 'After'])
    expect(document.body.querySelector('[role="tablist"]')).not.toBeNull()
    expect(text()).toContain('Collapse unchanged')
    expect([...document.body.querySelector('.grid.grid-cols-2')!.children].map(clean)).toEqual(['Before', 'After'])
    expect(document.body.querySelector('.diff-editor-embedded')).toBeNull()

    await press(tab('Unified'))
    expect(names()).toEqual(['After, compared with Before'])
  })

  it('is one document of a list when embedded: no toolbar, no side labels, the layout of its props', async () => {
    const view = mountEditor({
      embedded: true,
      layout: 'split',
      originalLabel: 'sbt.contoso in staging',
      modifiedLabel: 'sbt.contoso in default',
    })
    await settle()

    expect(names()).toEqual(['sbt.contoso in staging', 'sbt.contoso in default'])
    expect(document.body.querySelector('[role="tablist"]')).toBeNull()
    expect(text()).not.toContain('Collapse unchanged')
    expect(document.body.querySelector('.grid.grid-cols-2')).toBeNull()
    expect(document.body.querySelector('.diff-editor-embedded')).not.toBeNull()

    // the page changes the layout of all its documents
    await view.setProps({ layout: 'unified' })
    expect(names()).toEqual(['sbt.contoso in default, compared with sbt.contoso in staging'])
    expect(document.body.querySelector('.cm-mergeView')).toBeNull()
  })
})

describe('A comparison whose documents are read again', () => {
  it('offers no copy while a side is read again, and offers it once the side is read', async () => {
    const view = await mountSelection(`kind=annotations&to=staging&${keysQuery('sbt.northwind')}`)
    await compared()
    await copyOn('Copy 1 to staging')
    // the cached documents show while the target is read again, as when a comparison is opened again
    let release!: () => void
    const held = new Promise<void>((resolve) => (release = resolve))
    server.use(
      http.get('*/api/v1/acme/billing/staging/annotations/sbt.northwind', async () => {
        await held
        return undefined
      }),
    )
    void view.queryClient.invalidateQueries()

    await eventually(() => expect(copyButton().disabled).toBe(true))
    release()
    await copyOn('Copy 1 to staging')
  })
})

describe('Effective documents that are the same, with a number beyond 2^53', () => {
  it('does not say that the effective documents differ', async () => {
    // an ancestor configures the same number in both views: the browser rounds it, so the texts are not exact
    for (const each of ['default', 'staging']) {
      await setConfiguration(org, project, each, 'sbt.northwind', { limit: 9007199254740992 })
    }
    await mountSelection(`kind=configurations&to=staging&${keysQuery('usg.northwind.invoicing')}`)

    await compared()
    expect(heading('usg.northwind.invoicing')).toBe('Usage usg.northwind.invoicing Identical')
  })
})
