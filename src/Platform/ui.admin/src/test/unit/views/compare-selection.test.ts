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
  type View,
} from '@42for.net/2splatform.sdk'
import DiffEditor from '@/components/editor/DiffEditor.vue'
import { mockHandlers } from '@/mock/all-handlers'
import SelectionPage from '@/pages/[org]/[project]/[view]/modeling/compare.selection.vue'
import { invalidateAnnotation, invalidateConfiguration } from '@/queries/invalidation'
import { contextGuard } from '@/router/guards/context'
import { useViewTransferStore } from '@/stores/view-transfer'
import { mountInApp, mountScreen, settle } from '../../helpers'

// The comparison of a selection of annotations or configurations across two views (424-309), against the stateful
// mock API. In the default dataset acme/billing has the views default and staging, whose annotations and
// configurations differ: the keys below were picked for their states, and a change of the generator shows here.

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
// unmounted before the body is cleared, so a closing tooltip does not patch removed nodes
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

const annotationReads = (urls: string[]) => urls.filter((url) => /\/annotations\/[a-z]{3}\./.test(url))
const configurationReads = (urls: string[]) => urls.filter((url) => url.includes('/configuration/'))

async function mountSelection(query: string, source = { org, project, view: 'default' }) {
  const view = await mountScreen(
    SelectionPage,
    {},
    {
      path: `/${source.org}/${source.project}/${source.view}/modeling/compare/selection?${query}`,
      scope: source,
      accessMap: { 'acme.billing': 'Owner', 'acme.shipping': 'Owner' },
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

async function click(element: HTMLElement) {
  element.click()
  await settle()
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
    expect(text()).toContain('Only the selected keys are compared.')
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
    expect(text()).toContain('Only the selected keys are compared.')
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
    expect(text()).toContain('What staging and default read.')
    expect(text()).not.toContain('Only the selected keys are compared.')
    expect(heading('cnt.contoso.us')).toMatch(/^Context cnt\.contoso\.us Only in staging \+0 −\d+$/)
    expect(heading('exe.northwind.reporting.test')).toMatch(
      /^Execution exe\.northwind\.reporting\.test Only in default \+\d+ −0$/,
    )
    expect(heading('sbt.contoso')).toBe('Subject sbt.contoso Different +1 −1')
    // without an own document it still reads what it inherits, the same in both views
    expect(heading('sbt.northwind')).toBe('Subject sbt.northwind Identical')
    expect(heading(inherited)).toMatch(new RegExp(`^Unit of execution ${inherited} Different \\+\\d+ −\\d+$`))
    expect(notes(inherited)).toEqual([
      'The own configurations are the same. The difference is inherited (ancestors, the template, key values), and a copy does not change it.',
    ])
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
