/* eslint-disable vue/one-component-per-file -- the dialog and the composable each get a small wrapper component */
import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, reactive } from 'vue'
import type { VueWrapper } from '@vue/test-utils'
import { QueryObserver, type QueryClient } from '@tanstack/vue-query'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { getAnnotation, setAnnotation, type View } from '@42for.net/2splatform.sdk'
import ViewCopyDialog from '@/components/views/ViewCopyDialog.vue'
import { useViewCopy } from '@/composables/useViewCopy'
import { mockHandlers } from '@/mock/all-handlers'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { useViewTransferStore } from '@/stores/view-transfer'
import { annotationStamp, type ViewCopyRequest, type ViewCopyResult } from '@/utils/view-copy'
import { annotationText, readAnnotation, readConfiguration, type ViewItemKind } from '@/utils/view-documents'
import { mountScreen, settle } from '../../helpers'

// The dialog that copies annotations or configurations to another view, and the composable that binds the run to the
// app (spec "Copy and Compare Annotations and Configurations Between Views", section 7), against the stateful mock
// API of the e2e mode, served by msw/node. The run itself is tested in view-copy.test.ts. In the default dataset
// acme/billing has the views default and staging, acme/shipping has one view.
const server = setupServer(...mockHandlers)
// unmounted before the body is cleared, so a closing dialog does not patch removed nodes
const mounted: { host: { unmount: () => void } }[] = []
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  server.events.removeAllListeners()
  server.resetHandlers()
  document.body.innerHTML = ''
})
afterAll(() => server.close())

const org = 'acme'
const project = 'billing'
const billing = { org, project, view: 'default' }
const staging = { org, project, view: 'staging' }
const root = '/api/v1/acme/billing'
const viewsUrl = `*${root}/views`

// Annotations: sbt.northwind differs between the views, sbt.initech is only in default, the unit of execution is the
// same in both, cnt.contoso.us is only in staging, and sbt.nobody is in neither.
const differs = 'sbt.northwind'
const onlyDefault = 'sbt.initech'
const same = 'uxe.northwind.notifications.staging.sms'
const onlyStaging = 'cnt.contoso.us'
const nowhere = 'sbt.nobody'
const annotations = [differs, onlyDefault, same, nowhere]
// Own configurations: sbt.contoso differs, usg.northwind.invoicing is the same in both, and the execution has one in
// default while staging has neither its annotation nor its context cnt.northwind.test.
const differing = 'sbt.contoso'
const equal = 'usg.northwind.invoicing'
const unannotated = 'exe.northwind.reporting.test'
const configurations = [differing, equal, unannotated]
// Four subjects that are copied in this order: two that differ, one that only default has, one that differs.
const subjects = ['sbt.contoso', 'sbt.fabrikam', onlyDefault, differs]

const stoppedMessage = 'Not copied, the copy was stopped.'
const comparedMessage = 'It changed in staging since it was compared.'
const goneMessage = 'It is no longer in default.'

const denied = () =>
  HttpResponse.json(
    { Message: 'The Contributor role on acme.billing is required.', ErrorCode: 'AccessDenied' },
    { status: 403 },
  )
const busy = () => HttpResponse.json({ Message: 'The database is busy.' }, { status: 500 })

/** The views the project lists from now on; the mock keeps data for any view name. */
const listViews = (...names: string[]) =>
  server.use(
    http.get(viewsUrl, () =>
      HttpResponse.json(names.map((name): View => ({ Name: name, IsDefault: name === 'default', IsRegistered: true }))),
    ),
  )

/** The requests from now on, as "POST /staging/annotations/sbt.initech", in the order they were sent. */
function recordRequests() {
  const lines: string[] = []
  server.events.on('request:start', ({ request }) => {
    const url = new URL(request.url)
    lines.push(`${request.method} ${url.pathname.replace(root, '')}`)
  })
  /** Everything that changes the API: every request but the GETs and the lookup of the deleted keys. */
  const writes = () => lines.filter((line) => !line.startsWith('GET ') && !line.endsWith('/annotations/deleted/lookup'))
  return { lines, writes }
}

/** Holds the first request to the path until it is released; the mock answers it then. */
function hold(method: 'get' | 'post' | 'patch', path: string) {
  let release!: () => void
  let reach!: () => void
  const released = new Promise<void>((resolve) => (release = resolve))
  const reached = new Promise<void>((resolve) => (reach = resolve))
  let held = false
  server.use(
    http[method](path, async () => {
      if (!held) {
        held = true
        reach()
        await released
      }
    }),
  )
  return { reached, release }
}

const stagingAnnotation = (key: string) => `*${root}/staging/annotations/${key}`
const inStaging = async (key: string) => !!(await readAnnotation(staging, key)).annotation
const textIn = async (view: string, key: string) =>
  annotationText(await getAnnotation(org, project, view, key, { includeInactive: true }))

interface DialogProps {
  open: boolean
  kind: ViewItemKind
  keys: readonly string[]
  target?: string
  expected?: Readonly<Record<string, string | number | null>>
  compare?: boolean
}

/**
 * The dialog as a place of the app mounts it: `v-model:open` and its props, which a test changes through `state`.
 * `copied` holds what the dialog emitted, and `closed` whether its place had it open each time it said that it has
 * left. A place that `keepsOpen` does not take the dialog's wish to close.
 */
async function openDialog(
  props: Partial<DialogProps> & Pick<DialogProps, 'keys'>,
  source = billing,
  place: { keepsOpen?: boolean } = {},
) {
  const state = reactive<DialogProps>({ open: true, kind: 'annotations', ...props })
  const copied: ViewCopyResult[] = []
  const closed: boolean[] = []
  const Place = defineComponent({
    setup: () => () =>
      h(ViewCopyDialog, {
        ...state,
        'onUpdate:open': (value: boolean) => (state.open = place.keepsOpen || value),
        onCopied: (result: ViewCopyResult) => copied.push(result),
        onClosed: () => closed.push(state.open),
      }),
  })
  const screen = await mountScreen(Place, {}, { scope: source })
  mounted.push(screen)
  await settle(6)
  return { ...screen, state, copied, closed }
}

type Dialog = Awaited<ReturnType<typeof openDialog>>

/** A slow machine takes longer than the second `vi.waitFor` waits by default. */
const eventually = <T>(assertion: () => T | Promise<T>) => vi.waitFor(assertion, { timeout: 5000 })

// The modal is teleported to the body.
const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')
const clean = (element: Element | null | undefined) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? ''
/** The texts of an element with a space between them: its parts are elements without white space in between. */
function spaced(element: Element | null | undefined): string {
  const texts: string[] = []
  const walk = (node: Node) => {
    if (node.nodeType === Node.TEXT_NODE) {
      texts.push(node.textContent?.replace(/\s+/g, ' ').trim() ?? '')
    } else {
      node.childNodes.forEach(walk)
    }
  }
  if (element) {
    walk(element)
  }
  return texts.filter(Boolean).join(' ')
}
const text = () => spaced(dialog())
const title = () => clean(dialog()?.querySelector('h2'))
const body = () => dialog()!.querySelector<HTMLElement>('[data-slot="body"]')!
const button = (label: string) =>
  [...(dialog()?.querySelectorAll<HTMLButtonElement>('button') ?? [])].find((candidate) => clean(candidate) === label)
/** The buttons of the footer, in their order. */
const footer = () => [...dialog()!.querySelectorAll('[data-slot="footer"] button')].map(clean)
const closeButton = () => dialog()!.querySelector('button[aria-label="Close"]')
const combobox = () => dialog()!.querySelector<HTMLButtonElement>('button[role="combobox"]')
const select = (view: { host: VueWrapper }) => view.host.findComponent({ name: 'Select' })
const radio = (value: string) => dialog()!.querySelector<HTMLButtonElement>(`button[role="radio"][value="${value}"]`)!
const checked = () => spaced(dialog()!.querySelector('button[role="radio"][aria-checked="true"]')?.closest('label'))
const checkbox = () => dialog()!.querySelector<HTMLButtonElement>('button[role="checkbox"]')
const status = () => dialog()!.querySelector<HTMLElement>('[role="status"]')!
/** The alerts of the dialog, as "title: description", with their colors. */
const alerts = (view: { host: VueWrapper }) =>
  view.host
    .findAllComponents({ name: 'Alert' })
    .map((alert) => [alert.props('color'), `${alert.props('title')}: ${alert.props('description') ?? ''}`])
const alertTitles = () => [...body().querySelectorAll('[data-slot="title"]')].map(clean)
/** The badges of the dialog with their colors: the outcomes of the documents, and "prod" beside the target. */
const badges = (view: { host: VueWrapper }) =>
  view.host.findAllComponents({ name: 'Badge' }).map((badge) => `${badge.props('label')}: ${badge.props('color')}`)
const region = (name: string) => dialog()!.querySelector<HTMLElement>(`[role="region"][aria-label="${name}"]`)
/** The cells of the table in a region: the kind, the key, the outcome and the reason. */
const rows = (name: string) =>
  [...(region(name)?.querySelectorAll('tbody tr') ?? [])].map((row) => [...row.querySelectorAll('td')].map(clean))
/** The links of the table in a region, as "key: address". */
const links = (name: string) =>
  [...(region(name)?.querySelectorAll('a') ?? [])].map((link) => `${clean(link)}: ${link.getAttribute('href')}`)

/** Focuses the button first, as a pointer or a key does, so the tests see where the focus goes afterwards. */
async function click(label: string) {
  const target = button(label)
  expect(target, `the button "${label}" of ${footer().join(', ')}`).toBeDefined()
  target!.focus()
  target!.click()
  await settle()
}

async function pick(view: Dialog, name: string) {
  select(view).vm.$emit('update:modelValue', name)
  await settle()
}

async function choose(element: HTMLElement) {
  element.click()
  await settle()
}

/** Runs a check and waits for its plan. */
async function check() {
  await click('Check')
  await eventually(() => expect(alertTitles()).toContain('Checked: nothing was written'))
}

/** Runs the copy with the button as it reads now, and waits for the result. */
async function copy(label = 'Copy') {
  await click(label)
  await eventually(() => expect(footer()).toContain('Done'))
}

describe('ViewCopyDialog: what is copied, and where to', () => {
  it('names the one key or counts several, per kind, and says where they come from', async () => {
    const view = await openDialog({ keys: [differs] })

    expect(title()).toBe('Copy sbt.northwind to another view')
    expect(clean(dialog()!.querySelector('[data-slot="description"]'))).toBe('From acme/billing, view default.')
    // the dialog is named by its title and described by its source
    const name = document.getElementById(dialog()!.getAttribute('aria-labelledby')!)
    expect(clean(name)).toBe('Copy sbt.northwind to another view')
    expect(clean(document.getElementById(dialog()!.getAttribute('aria-describedby')!))).toBe(
      'From acme/billing, view default.',
    )
    // the documents never go to session recordings
    expect(dialog()!.classList.contains('ph-no-capture')).toBe(true)

    view.state.keys = annotations
    await settle()
    expect(title()).toBe('Copy 4 annotations to another view')

    view.state.kind = 'configurations'
    view.state.keys = [differing]
    await settle()
    expect(title()).toBe('Copy sbt.contoso to another view')

    view.state.keys = configurations
    await settle()
    expect(title()).toBe('Copy 3 configurations to another view')
  })

  it('names the target in the title when the comparison fixed it, and the source view in the description', async () => {
    const view = await openDialog({ kind: 'configurations', keys: configurations, target: 'default' }, staging)

    expect(title()).toBe('Copy 3 configurations to default')
    expect(clean(dialog()!.querySelector('[data-slot="description"]'))).toBe('From acme/billing, view staging.')

    view.state.keys = [differing]
    view.state.kind = 'annotations'
    await settle()
    expect(title()).toBe('Copy sbt.contoso to default')
  })

  it('preselects the only other view', async () => {
    const view = await openDialog({ keys: [differs] })

    expect(select(view).props('items')).toEqual(['staging'])
    expect(select(view).props('modelValue')).toBe('staging')
    expect(clean(combobox())).toBe('staging')
    // staging is not the production view: no badge, no warning, and a copy needs no check
    expect(text()).not.toContain('prod')
    expect(alerts(view)).toEqual([])
    expect(button('Check')!.disabled).toBe(false)
    expect(button('Copy')!.disabled).toBe(false)
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])
  })

  it('preselects nothing among several other views, until one was used in the project', async () => {
    listViews('default', 'staging', 'qa')
    const view = await openDialog({ keys: [differs] })

    expect(select(view).props('items')).toEqual(['staging', 'qa'])
    expect(select(view).props('modelValue')).toBeUndefined()
    expect(clean(combobox())).toBe('Pick a view')
    expect(text()).toContain('When the other view already has it')
    expect(text()).toContain('Ancestors that the other view lacks are copied too.')
    // nothing runs without a target
    expect(button('Check')!.disabled).toBe(true)
    expect(button('Copy')!.disabled).toBe(true)

    await pick(view, 'qa')
    expect(clean(combobox())).toBe('qa')
    expect(text()).toContain('When qa already has it')
    expect(button('Check')!.disabled).toBe(false)
    expect(button('Copy')!.disabled).toBe(false)

    // a target that was only picked is not remembered
    await click('Cancel')
    expect(view.state.open).toBe(false)
    view.state.open = true
    await settle()
    expect(select(view).props('modelValue')).toBeUndefined()

    // one that a check or a copy went to is, per project
    await pick(view, 'qa')
    await check()
    expect(useViewTransferStore().lastTarget('acme.billing')).toBe('qa')
    await click('Cancel')
    view.state.open = true
    await settle()
    expect(select(view).props('modelValue')).toBe('qa')
    expect(clean(combobox())).toBe('qa')
  })

  it('takes the target last used from the store, and not the one of another project', async () => {
    listViews('default', 'staging', 'qa')
    const store = useViewTransferStore()
    store.rememberTarget('acme.shipping', 'qa')
    const view = await openDialog({ keys: [differs] })
    expect(select(view).props('modelValue')).toBeUndefined()

    await click('Cancel')
    store.rememberTarget('acme.billing', 'staging')
    view.state.open = true
    await settle()
    expect(select(view).props('modelValue')).toBe('staging')

    // a remembered view that the project does not list any more is no target
    await click('Cancel')
    store.rememberTarget('acme.billing', 'gone')
    view.state.open = true
    await settle()
    expect(select(view).props('modelValue')).toBeUndefined()
    expect(button('Check')!.disabled).toBe(true)
  })

  it('never preselects the production view from memory', async () => {
    listViews('default', 'staging', 'qa')
    useViewTransferStore().rememberTarget('acme.billing', 'default')
    const view = await openDialog({ keys: [differs] }, staging)

    expect(select(view).props('items')).toEqual(['default', 'qa'])
    expect(select(view).props('modelValue')).toBeUndefined()
    expect(alerts(view)).toEqual([])
    expect(button('Check')!.disabled).toBe(true)

    // also not after a check that went there from this dialog
    await pick(view, 'default')
    await check()
    expect(useViewTransferStore().lastTarget('acme.billing')).toBe('default')
    await click('Cancel')
    view.state.open = true
    await settle()
    expect(select(view).props('modelValue')).toBeUndefined()
    expect(clean(combobox())).toBe('Pick a view')
  })

  it('shows a fixed target as text, which cannot be changed', async () => {
    listViews('default', 'staging', 'qa')
    // the memory of the project does not move a fixed target
    useViewTransferStore().rememberTarget('acme.billing', 'staging')
    const view = await openDialog({ keys: [differs, onlyDefault], target: 'qa' })

    expect(combobox()).toBeNull()
    expect(select(view).exists()).toBe(false)
    expect(title()).toBe('Copy 2 annotations to qa')
    expect(text()).toContain('Copy to qa When qa already has it')
    expect(text()).toContain('Ancestors that qa lacks are copied too.')

    // and it is where the copy goes
    const requests = recordRequests()
    await copy()
    expect(alertTitles()).toEqual(['Copied to qa'])
    expect(requests.writes()).toEqual(['POST /qa/annotations/sbt.initech', 'POST /qa/annotations/sbt.northwind'])
    expect(await inStaging(onlyDefault)).toBe(false)
  })

  it('copies nowhere for a fixed target that is not another view of the project', async () => {
    // a mistyped ?to= of the comparison: what the comparison saw holds for that view only, so no other view takes it
    const view = await openDialog({ keys: [differs], target: 'stagin' })
    expect(title()).toBe('Copy sbt.northwind to another view')
    expect(text()).toContain('stagin is not another view of this project')
    expect(button('Check')!.disabled).toBe(true)
    expect(button('Copy')!.disabled).toBe(true)

    // the view of the route is no target either
    view.state.target = 'default'
    await settle()
    expect(text()).toContain('default is not another view of this project')
    expect(button('Copy')!.disabled).toBe(true)
  })

  it('says that a project with one view has no other, and offers nothing to run', async () => {
    const view = await openDialog({ keys: [differs] }, { org, project: 'shipping', view: 'default' })

    expect(alerts(view)).toEqual([
      ['neutral', 'The project has no other view: An Administrator can create another one on the Views page.'],
    ])
    expect(select(view).props('items')).toEqual([])
    expect(button('Check')!.disabled).toBe(true)
    expect(button('Copy')!.disabled).toBe(true)
    // no hint about a check that cannot run either
    expect(text()).not.toContain('Check first')
  })

  it('waits for the views: nothing runs before they are known, and then the target is preselected', async () => {
    const views = hold('get', viewsUrl)
    const view = await openDialog({ keys: [differs] })
    await views.reached

    // not known yet is not "no other view"
    expect(alerts(view)).toEqual([])
    expect(select(view).props('items')).toEqual([])
    expect(clean(combobox())).toBe('Pick a view')
    expect(button('Check')!.disabled).toBe(true)
    expect(button('Copy')!.disabled).toBe(true)

    views.release()
    await eventually(() => expect(select(view).props('modelValue')).toBe('staging'))
    expect(button('Check')!.disabled).toBe(false)
    expect(button('Copy')!.disabled).toBe(false)
  })

  it('takes a fixed target once the views are known', async () => {
    const views = hold('get', viewsUrl)
    const view = await openDialog({ keys: [differs], target: 'default' }, staging)
    await views.reached

    // until the project lists it, the given name is no target: nothing runs, and nothing warns
    expect(title()).toBe('Copy sbt.northwind to another view')
    expect(button('Check')!.disabled).toBe(true)
    expect(alerts(view)).toEqual([])

    views.release()
    await eventually(() => expect(title()).toBe('Copy sbt.northwind to default'))
    expect(combobox()).toBeNull()
    // the production view is marked and warned about as a fixed target too
    expect(text()).toContain('Copy to default prod default is the production view')
    expect(badges(view)).toEqual(['prod: primary'])
    expect(alertTitles()).toEqual(['default is the production view'])
    expect(button('Check')!.disabled).toBe(false)
    expect(button('Copy')!.disabled).toBe(true)
  })
})

describe('ViewCopyDialog: the production view', () => {
  const selection = [onlyStaging, differs, same]

  it('warns about the production view, and copies to it only after a check', async () => {
    const view = await openDialog({ keys: selection }, staging)

    // default is the only other view, so it is the target: by the rule of the one view, not by memory
    expect(select(view).props('modelValue')).toBe('default')
    expect(spaced(combobox()!.parentElement)).toBe('default prod')
    expect(badges(view)).toEqual(['prod: primary'])
    expect(alerts(view)).toEqual([
      ['warning', 'default is the production view: The copy changes what production reads.'],
    ])
    expect(text()).toContain('When default already has it')
    expect(text()).toContain('Replace its content with the content of staging.')

    // Copy is off, and says why to a pointer and to a screen reader
    const copyButton = button('Copy')!
    expect(copyButton.disabled).toBe(true)
    const hint = document.getElementById(copyButton.getAttribute('aria-describedby')!)
    expect(clean(hint)).toBe('Check first: default is the production view.')
    expect(button('Check')!.disabled).toBe(false)

    const requests = recordRequests()
    await check()

    expect(alerts(view)).toEqual([
      ['warning', 'default is the production view: The copy changes what production reads.'],
      ['info', 'Checked: nothing was written: To create 1 · To update 1 · Unchanged 1.'],
    ])
    expect(requests.writes()).toEqual([])
    expect(text()).not.toContain('Check first')
    const planned = button('Copy 2 to default')!
    expect(planned.disabled).toBe(false)
    expect(planned.hasAttribute('aria-describedby')).toBe(false)

    await copy('Copy 2 to default')
    expect(alerts(view)).toEqual([['success', 'Copied to default: 1 created, 1 updated, 1 unchanged.']])
    expect(requests.writes()).toEqual([
      'POST /default/annotations/sbt.northwind',
      'POST /default/annotations/cnt.contoso.us',
    ])
    expect(await textIn('default', differs)).toBe(await textIn('staging', differs))
  })

  it('asks for a new check of the production view after a change, and after a check that did not finish', async () => {
    await openDialog({ keys: selection }, staging)
    await check()
    expect(button('Copy 2 to default')!.disabled).toBe(false)

    // the plan held for Overwrite
    await choose(radio('skip'))
    expect(alertTitles()).toEqual(['default is the production view'])
    expect(button('Copy')!.disabled).toBe(true)
    expect(text()).toContain('Check first: default is the production view.')

    await check()
    // sbt.northwind is in default with another content, and stays
    expect(button('Copy 1 to default')!.disabled).toBe(false)

    // a check that ends as a whole is no plan
    await choose(radio('overwrite'))
    server.use(http.get(viewsUrl, busy))
    await click('Check')
    await eventually(() => expect(alertTitles()).toContain('The check did not finish'))
    expect(button('Copy')!.disabled).toBe(true)
    expect(text()).toContain('Check first: default is the production view.')
  })
})

describe('ViewCopyDialog: the options', () => {
  it('offers Overwrite and Skip, and says what is copied of annotations', async () => {
    await openDialog({ keys: annotations })

    const group = dialog()!.querySelector('[role="radiogroup"]')!
    expect([...group.querySelectorAll('label')].map(spaced)).toEqual([
      'Overwrite Replace its content with the content of default.',
      'Skip Leave it as it is.',
    ])
    expect(checked()).toBe('Overwrite Replace its content with the content of default.')
    // only configurations need an annotation
    expect(checkbox()).toBeNull()
    expect(text()).not.toContain('Copy missing annotations too')
    expect(text()).toContain(
      'The title, description, documentation link, labels, values, the enabled or disabled state and the validity. ' +
        'Ancestors that staging lacks are copied too. Configurations are not copied.',
    )

    await choose(radio('skip'))
    expect(checked()).toBe('Skip Leave it as it is.')
  })

  it('offers the missing annotations for configurations, and says what is copied of them', async () => {
    await openDialog({ kind: 'configurations', keys: configurations })

    expect(checked()).toBe('Overwrite Replace its content with the content of default.')
    expect(checkbox()!.getAttribute('aria-checked')).toBe('true')
    expect(spaced(checkbox()!.closest('[data-slot="root"]'))).toBe(
      'Copy missing annotations too A configuration needs its annotation. When staging does not have it, the ' +
        'annotation is copied first. Otherwise the configuration is skipped.',
    )
    expect(text()).toContain(
      'The own configuration, as it is stored. Inherited values, templates and schemas stay those of staging.',
    )
    expect(text()).not.toContain('Ancestors that staging lacks')

    await choose(checkbox()!)
    expect(checkbox()!.getAttribute('aria-checked')).toBe('false')
  })

  it('names every control for a screen reader', async () => {
    await openDialog({ kind: 'configurations', keys: configurations })

    // the select by its label
    const target = dialog()!.querySelector(`label[for="${combobox()!.id}"]`)
    expect(clean(target)).toBe('Copy to')
    // the group of the two choices by its question
    const group = dialog()!.querySelector('[role="radiogroup"]')!
    expect(group.getAttribute('aria-label')).toBe('When staging already has it')
    expect([...group.querySelectorAll('[role="radio"]')].map((item) => item.getAttribute('aria-label'))).toEqual([
      'Overwrite',
      'Skip',
    ])
    // the checkbox by its label
    expect(clean(dialog()!.querySelector(`label[for="${checkbox()!.id}"]`))).toBe('Copy missing annotations too')
    // the status line is there before anything runs, empty and polite, so its first text is read out
    expect(status().getAttribute('aria-live')).toBe('polite')
    expect(clean(status())).toBe('')
    expect(dialog()!.querySelector('[role="progressbar"]')).toBeNull()
  })
})

describe('ViewCopyDialog: Check', () => {
  it('shows what a copy of annotations would do, and writes nothing', async () => {
    const before = await textIn('staging', differs)
    const view = await openDialog({ keys: annotations })
    expect(button('Copy')).toBeDefined()
    const requests = recordRequests()

    await check()

    expect(alerts(view)).toEqual([
      ['info', 'Checked: nothing was written: To create 1 · To update 1 · Unchanged 1 · To skip 1.'],
    ])
    // the outcomes in the words of a dry run, in the order of the run
    expect(rows('Checked documents')).toEqual([
      ['Annotation', onlyDefault, 'To create', ''],
      ['Annotation', nowhere, 'To skip', goneMessage],
      ['Annotation', differs, 'To update', ''],
      ['Annotation', same, 'Unchanged', ''],
    ])
    expect([...region('Checked documents')!.querySelectorAll('th')].map(clean)).toEqual([
      'Kind',
      'Key',
      'Outcome',
      'Reason',
    ])
    // the colors of the outcomes, as in the result of an import
    expect(badges(view)).toEqual(['To create: success', 'To skip: warning', 'To update: info', 'Unchanged: neutral'])
    // nothing is there to open yet
    expect(links('Checked documents')).toEqual([])
    expect(region('Copied documents')).toBeNull()

    // the lookup of the deleted keys is the only request that is no GET
    expect(requests.lines.filter((line) => !line.startsWith('GET '))).toEqual([
      'POST /staging/annotations/deleted/lookup',
    ])
    expect(await inStaging(onlyDefault)).toBe(false)
    expect(await textIn('staging', differs)).toBe(before)

    // the options stay, and Copy says what it will write and where
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy 2 to staging'])
    expect(button('Copy 2 to staging')!.disabled).toBe(false)
    expect(checked()).toBe('Overwrite Replace its content with the content of default.')
    // a check is no copy
    expect(view.copied).toEqual([])
  })

  it('counts the annotations a configuration needs, and skips the configuration without them', async () => {
    const view = await openDialog({ kind: 'configurations', keys: configurations })

    await check()
    expect(alerts(view)).toEqual([['info', 'Checked: nothing was written: To create 3 · To update 1 · Unchanged 1.']])
    expect(rows('Checked documents')).toEqual([
      ['Configuration', differing, 'To update', ''],
      ['Configuration', equal, 'Unchanged', ''],
      ['Annotation', 'cnt.northwind.test', 'To create', `Missing ancestor of ${unannotated}.`],
      ['Annotation', unannotated, 'To create', 'Needed by its configuration.'],
      ['Configuration', unannotated, 'To create', ''],
    ])
    expect(button('Copy 4 to staging')!.disabled).toBe(false)

    // without the annotations the plan is another one
    await choose(checkbox()!)
    expect(alerts(view)).toEqual([])
    expect(region('Checked documents')).toBeNull()
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])

    await check()
    expect(alerts(view)).toEqual([['info', 'Checked: nothing was written: To update 1 · Unchanged 1 · To skip 1.']])
    expect(rows('Checked documents').at(-1)).toEqual([
      'Configuration',
      unannotated,
      'To skip',
      'Its annotation is not in staging.',
    ])
    expect(button('Copy 1 to staging')!.disabled).toBe(false)
  })

  it('has nothing to copy when the plan writes nothing', async () => {
    const view = await openDialog({ keys: [same, nowhere] })

    await check()

    expect(alerts(view)).toEqual([['info', 'Checked: nothing was written: Unchanged 1 · To skip 1.']])
    expect(footer()).toEqual(['Cancel', 'Check', 'Nothing to copy'])
    expect(button('Nothing to copy')!.disabled).toBe(true)
    // Check can run again
    expect(button('Check')!.disabled).toBe(false)
  })

  it('discards the plan when the target or an option changes', async () => {
    listViews('default', 'staging', 'qa')
    const view = await openDialog({ keys: annotations })
    const planned = () => region('Checked documents') !== null

    await pick(view, 'staging')
    await check()
    expect(planned()).toBe(true)
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy 2 to staging'])

    // another target: qa has none of the keys, so the plan of staging is not its plan
    await pick(view, 'qa')
    expect(planned()).toBe(false)
    expect(alerts(view)).toEqual([])
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])

    await check()
    expect(rows('Checked documents').map((row) => row[2])).not.toContain('To update')
    expect(footer().at(-1)).toMatch(/^Copy \d+ to qa$/)

    // picking the same target again changes nothing
    await pick(view, 'qa')
    expect(planned()).toBe(true)

    // another answer to what the target already has
    await pick(view, 'staging')
    await check()
    await choose(radio('skip'))
    expect(planned()).toBe(false)
    expect(alerts(view)).toEqual([])
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])

    await check()
    expect(rows('Checked documents')[2]).toEqual(['Annotation', differs, 'To skip', 'It is already in staging.'])
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy 1 to staging'])
  })

  it('discards the plan when the keys change', async () => {
    const view = await openDialog({ keys: annotations })
    await check()
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy 2 to staging'])

    // the same keys in a new list are the same selection
    view.state.keys = [...annotations]
    await settle()
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy 2 to staging'])

    view.state.keys = [differs]
    await settle()
    expect(region('Checked documents')).toBeNull()
    expect(alerts(view)).toEqual([])
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])
  })

  it('says why a check ended as a whole, and checks again', async () => {
    const view = await openDialog({ keys: annotations })
    // the views of the dialog are read; the run reads them itself
    server.use(http.get(viewsUrl, busy))

    await click('Check')
    await eventually(() => expect(alerts(view)).toEqual([['error', 'The check did not finish: The database is busy.']]))

    // an alert, so a screen reader reads it out wherever the focus is
    expect(spaced(dialog()!.querySelector('[role="alert"]'))).toBe('The check did not finish The database is busy.')
    expect(region('Checked documents')).toBeNull()
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])
    expect(clean(status())).toBe('')

    server.resetHandlers()
    await check()
    expect(alerts(view)).toEqual([
      ['info', 'Checked: nothing was written: To create 1 · To update 1 · Unchanged 1 · To skip 1.'],
    ])
    expect(dialog()!.querySelector('[role="alert"]')).toBeNull()
  })

  it('says how far a check is, and leaves no plan when it is stopped', async () => {
    const view = await openDialog({ keys: subjects })
    const second = hold('get', `*${root}/default/annotations/sbt.fabrikam`)

    await click('Check')
    await second.reached
    await eventually(() => expect(clean(status())).toBe('Checked 1 of 4…'))
    expect(footer()).toEqual(['Stop'])
    expect(closeButton()).toBeNull()
    // a check writes nothing, so the tab may go
    const leaving = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(leaving)
    expect(leaving.defaultPrevented).toBe(false)

    await click('Stop')
    second.release()
    await eventually(() => expect(footer()).toEqual(['Cancel', 'Check', 'Copy']))

    // half a check is no plan, and nothing failed
    expect(alerts(view)).toEqual([])
    expect(region('Checked documents')).toBeNull()
    expect(clean(status())).toBe('')
    expect(closeButton()).not.toBeNull()
  })
})

describe('ViewCopyDialog: Copy', () => {
  it('says how far it is, and stays until it is done', async () => {
    const view = await openDialog({ keys: subjects })
    const live = status()
    const second = hold('post', stagingAnnotation('sbt.fabrikam'))

    await click('Copy')
    await second.reached
    await eventually(() => expect(clean(status())).toBe('Copied 1 of 4…'))

    // the same element as before the run: a live region that appears with its text is often not read out
    expect(status()).toBe(live)
    const bar = dialog()!.querySelector('[role="progressbar"]')!
    expect(bar.getAttribute('aria-valuenow')).toBe('1')
    expect(bar.getAttribute('aria-valuemax')).toBe('4')
    // no way out but Stop: no close button, no Cancel, and Escape does nothing
    expect(closeButton()).toBeNull()
    expect(footer()).toEqual(['Stop'])
    // Stop stands at the other end of the footer than Copy did: the second click of a double click on Copy would
    // land on it, and stop the copy at its first key
    expect(button('Stop')!.classList.contains('me-auto')).toBe(true)
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))
    await settle()
    expect(view.state.open).toBe(true)
    expect(dialog()).not.toBeNull()
    // the options cannot change under the run
    expect(combobox()!.disabled).toBe(true)
    expect(radio('skip').disabled).toBe(true)
    // and the browser asks before the tab goes
    const leaving = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(leaving)
    expect(leaving.defaultPrevented).toBe(true)

    second.release()
    await eventually(() => expect(footer()).toEqual(['Compare with staging', 'Done']))
    expect(clean(status())).toBe('')
    expect(dialog()!.querySelector('[role="progressbar"]')).toBeNull()
    expect(closeButton()).not.toBeNull()
    const idle = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(idle)
    expect(idle.defaultPrevented).toBe(false)
  })

  it('closes with Escape while nothing runs', async () => {
    const view = await openDialog({ keys: subjects })

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))
    await settle()

    expect(view.state.open).toBe(false)
  })

  it('shows the result: its counts, every document and the links to what it wrote', async () => {
    const view = await openDialog({ keys: annotations })
    const requests = recordRequests()

    await copy()

    // something was skipped: a warning, not a success
    expect(alerts(view)).toEqual([['warning', 'Copied to staging: 1 created, 1 updated, 1 unchanged, 1 skipped.']])
    // the alert takes the focus, so it is read out
    expect(spaced(document.activeElement)).toBe('Copied to staging 1 created, 1 updated, 1 unchanged, 1 skipped.')
    expect(dialog()!.contains(document.activeElement)).toBe(true)
    expect(rows('Copied documents')).toEqual([
      ['Annotation', onlyDefault, 'Created', ''],
      ['Annotation', nowhere, 'Skipped', goneMessage],
      ['Annotation', differs, 'Updated', ''],
      ['Annotation', same, 'Unchanged', ''],
    ])
    expect(badges(view)).toEqual(['Created: success', 'Skipped: warning', 'Updated: info', 'Unchanged: neutral'])
    // only what was written is there to open, in the target view
    expect(links('Copied documents')).toEqual([
      'sbt.initech: /acme/billing/staging/annotations/sbt.initech',
      'sbt.northwind: /acme/billing/staging/annotations/sbt.northwind',
    ])
    expect(region('Checked documents')).toBeNull()
    // the options are gone
    expect(combobox()).toBeNull()
    expect(dialog()!.querySelector('[role="radiogroup"]')).toBeNull()
    expect(footer()).toEqual(['Compare with staging', 'Done'])

    expect(requests.writes()).toEqual([
      'POST /staging/annotations/sbt.initech',
      'POST /staging/annotations/sbt.northwind',
    ])
    expect(await textIn('staging', onlyDefault)).toBe(await textIn('default', onlyDefault))
    expect(await textIn('staging', differs)).toBe(await textIn('default', differs))

    // the place that opened the dialog hears of it once, and the project remembers the target
    expect(view.copied).toHaveLength(1)
    expect(view.copied[0]).toMatchObject({ target: 'staging', dryRun: false, stopped: false })
    expect(view.copied[0]!.items.map((item) => item.outcome)).toEqual(['Created', 'Skipped', 'Updated', 'Unchanged'])
    expect(useViewTransferStore().lastTarget('acme.billing')).toBe('staging')
  })

  it('is a success when nothing was skipped and nothing failed', async () => {
    const view = await openDialog({ keys: [differs, onlyDefault, same] })

    await copy()

    expect(alerts(view)).toEqual([['success', 'Copied to staging: 1 created, 1 updated, 1 unchanged.']])
  })

  it('is a warning when a document failed', async () => {
    server.use(http.post(stagingAnnotation(differs), busy))
    const view = await openDialog({ keys: [differs, onlyDefault] })

    await copy()

    expect(alerts(view)).toEqual([['warning', 'Copied to staging: 1 created, 1 failed.']])
    expect(rows('Copied documents')).toEqual([
      ['Annotation', onlyDefault, 'Created', ''],
      ['Annotation', differs, 'Failed', 'The database is busy.'],
    ])
    expect(badges(view)).toEqual(['Created: success', 'Failed: error'])
    // a failed document was not written: nothing to open
    expect(links('Copied documents')).toEqual(['sbt.initech: /acme/billing/staging/annotations/sbt.initech'])
    expect(footer()).toEqual(['Compare with staging', 'Done'])
  })

  it('links a configuration and the annotations that were created for it, each to its own page', async () => {
    const view = await openDialog({ kind: 'configurations', keys: configurations })

    await copy()

    expect(alerts(view)).toEqual([['success', 'Copied to staging: 3 created, 1 updated, 1 unchanged.']])
    expect(rows('Copied documents')).toEqual([
      ['Configuration', differing, 'Updated', ''],
      ['Configuration', equal, 'Unchanged', ''],
      ['Annotation', 'cnt.northwind.test', 'Created', `Missing ancestor of ${unannotated}.`],
      ['Annotation', unannotated, 'Created', 'Needed by its configuration.'],
      ['Configuration', unannotated, 'Created', ''],
    ])
    expect(links('Copied documents')).toEqual([
      'sbt.contoso: /acme/billing/staging/configurations/sbt.contoso',
      'cnt.northwind.test: /acme/billing/staging/annotations/cnt.northwind.test',
      `${unannotated}: /acme/billing/staging/annotations/${unannotated}`,
      `${unannotated}: /acme/billing/staging/configurations/${unannotated}`,
    ])
    expect((await readConfiguration(staging, unannotated)).own).toEqual(
      (await readConfiguration(billing, unannotated)).own,
    )
  })

  it('copies what the options say: Skip, and no missing annotations', async () => {
    const before = (await readConfiguration(staging, differing)).own
    const view = await openDialog({ kind: 'configurations', keys: configurations })
    await choose(radio('skip'))
    await choose(checkbox()!)
    const requests = recordRequests()

    await copy()

    expect(alerts(view)).toEqual([['warning', 'Copied to staging: 1 unchanged, 2 skipped.']])
    expect(rows('Copied documents')).toEqual([
      ['Configuration', differing, 'Skipped', 'staging has its own configuration.'],
      ['Configuration', equal, 'Unchanged', ''],
      ['Configuration', unannotated, 'Skipped', 'Its annotation is not in staging.'],
    ])
    expect(requests.writes()).toEqual([])
    expect((await readConfiguration(staging, differing)).own).toEqual(before)
    expect(await inStaging(unannotated)).toBe(false)
  })

  it('opens the comparison of the copied keys with the target, and closes', async () => {
    const view = await openDialog({ keys: annotations })
    await copy()

    await click('Compare with staging')

    expect(view.state.open).toBe(false)
    const route = view.router.currentRoute.value
    expect(route.path).toBe('/acme/billing/default/modeling/compare/selection')
    // the keys of the address are in key order
    expect(route.query).toEqual({ kind: 'annotations', to: 'staging', key: annotations.toSorted() })
  })

  it('stays open with its result when a guard cancels the way to the comparison', async () => {
    const view = await openDialog({ keys: annotations })
    await copy()
    // a form with unsaved changes asks before it is left, and the answer is to stay
    const remove = view.router.beforeEach(() => false)

    await click('Compare with staging')

    expect(view.state.open).toBe(true)
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default')
    expect(footer()).toEqual(['Compare with staging', 'Done'])
    expect(rows('Copied documents').length).toBeGreaterThan(0)
    remove()
  })

  it('keeps a long selection for the comparison, which has no room for it in its address', async () => {
    // none of them is in default, so each is one read
    const keys = Array.from({ length: 60 }, (_, index) => `sbt.customer-of-the-long-selection-${index}`)
    const view = await openDialog({ kind: 'configurations', keys })
    await copy()
    expect(alerts(view)).toEqual([['warning', 'Copied to staging: 60 skipped.']])

    await click('Compare with staging')

    const { path, query } = view.router.currentRoute.value
    expect(path).toBe('/acme/billing/default/modeling/compare/selection')
    expect(query).toEqual({ kind: 'configurations', to: 'staging', set: expect.any(String) })
    expect(useViewTransferStore().keysOf(query.set as string)).toEqual(keys.toSorted())
  })

  it('offers no comparison where the comparison itself opened it', async () => {
    const view = await openDialog({ keys: [differs], target: 'staging', compare: false })

    await copy()

    expect(footer()).toEqual(['Done'])
    await click('Done')
    expect(view.state.open).toBe(false)
    // it stays where it was
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default')
  })

  it('opens a copied document in the target view, and closes', async () => {
    const view = await openDialog({ keys: [onlyDefault] })
    await copy()

    region('Copied documents')!.querySelector('a')!.click()
    await settle()

    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/staging/annotations/sbt.initech')
    // The link itself closes nothing, so a link opened in a new tab leaves the result where it is. The dialog goes
    // with the view of the route, which the context guard of the app sets before the navigation lands.
    expect(view.state.open).toBe(true)
    useContextStore().set({ org: 'acme', project: 'billing', view: 'staging' })
    await settle()
    expect(view.state.open).toBe(false)
  })

  it('starts clean when it is opened again', async () => {
    const view = await openDialog({ kind: 'configurations', keys: configurations })
    await choose(radio('skip'))
    await choose(checkbox()!)
    await copy()
    expect(region('Copied documents')).not.toBeNull()

    await click('Done')
    expect(view.state.open).toBe(false)
    view.state.open = true
    await settle()

    // no result of the last copy, and the options as they start
    expect(alerts(view)).toEqual([])
    expect(region('Copied documents')).toBeNull()
    expect(select(view).props('modelValue')).toBe('staging')
    expect(checked()).toBe('Overwrite Replace its content with the content of default.')
    expect(checkbox()!.getAttribute('aria-checked')).toBe('true')
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])
    expect(clean(status())).toBe('')
  })

  it('forgets the plan and a failed check when it is opened again', async () => {
    const view = await openDialog({ keys: annotations })
    await check()
    await click('Cancel')
    view.state.open = true
    await settle()
    expect(region('Checked documents')).toBeNull()
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])

    server.use(http.get(viewsUrl, busy))
    await click('Check')
    await eventually(() => expect(alertTitles()).toEqual(['The check did not finish']))
    await click('Cancel')
    view.state.open = true
    await settle()
    expect(alerts(view)).toEqual([])
  })

  it('skips a key whose target is not what the comparison saw', async () => {
    // what the comparison page saw of the two keys in staging
    const compared = await Promise.all(
      [differs, 'sbt.contoso'].map((key) => getAnnotation(org, project, 'staging', key, { includeInactive: true })),
    )
    const expected = Object.fromEntries(compared.map((seen) => [seen.AnnotationKey, annotationStamp(seen)]))
    // and sbt.contoso changed there since, in another tab
    await setAnnotation(org, project, 'staging', 'sbt.contoso', {
      ...compared[1]!,
      Title: 'Changed since the comparison',
    })
    const view = await openDialog({ keys: [differs, 'sbt.contoso'], target: 'staging', compare: false, expected })
    const before = await textIn('staging', 'sbt.contoso')
    const requests = recordRequests()

    await check()
    expect(rows('Checked documents')).toEqual([
      ['Annotation', 'sbt.contoso', 'To skip', comparedMessage],
      ['Annotation', differs, 'To update', ''],
    ])

    await copy('Copy 1 to staging')
    expect(alerts(view)).toEqual([['warning', 'Copied to staging: 1 updated, 1 skipped.']])
    expect(rows('Copied documents')).toEqual([
      ['Annotation', 'sbt.contoso', 'Skipped', comparedMessage],
      ['Annotation', differs, 'Updated', ''],
    ])
    expect(requests.writes()).toEqual(['POST /staging/annotations/sbt.northwind'])
    expect(await textIn('staging', 'sbt.contoso')).toBe(before)
  })

  it('takes at most a hundred keys', async () => {
    const keys = Array.from({ length: 101 }, (_, index) => `sbt.bulk-${index}`)
    const view = await openDialog({ keys })

    expect(title()).toBe('Copy 101 annotations to another view')
    expect(alerts(view)).toEqual([['warning', 'Select up to 100 to compare or copy them at once: ']])
    expect(button('Check')!.disabled).toBe(true)
    expect(button('Copy')!.disabled).toBe(true)

    // exactly a hundred is allowed
    view.state.keys = keys.slice(1)
    await settle()
    expect(alerts(view)).toEqual([])
    expect(button('Check')!.disabled).toBe(false)
    expect(button('Copy')!.disabled).toBe(false)
  })
})

describe('ViewCopyDialog: a copy that does not finish', () => {
  it('stops after the key under way: the rest is skipped, and it can be tried again', async () => {
    const view = await openDialog({ keys: subjects })
    const second = hold('post', stagingAnnotation('sbt.fabrikam'))
    const requests = recordRequests()

    await click('Copy')
    await second.reached
    await click('Stop')
    // the request under way is not cut off
    expect(footer()).toEqual(['Stop'])
    second.release()
    await eventually(() => expect(footer()).toEqual(['Try again', 'Done']))

    expect(alerts(view)).toEqual([['warning', 'The copy to staging was stopped: 2 updated, 2 skipped.']])
    expect(spaced(document.activeElement)).toBe('The copy to staging was stopped 2 updated, 2 skipped.')
    expect(rows('Copied documents')).toEqual([
      ['Annotation', 'sbt.contoso', 'Updated', ''],
      ['Annotation', 'sbt.fabrikam', 'Updated', ''],
      ['Annotation', onlyDefault, 'Skipped', stoppedMessage],
      ['Annotation', differs, 'Skipped', stoppedMessage],
    ])
    expect(requests.writes()).toEqual([
      'POST /staging/annotations/sbt.contoso',
      'POST /staging/annotations/sbt.fabrikam',
    ])
    expect(await inStaging(onlyDefault)).toBe(false)
    expect(view.copied).toHaveLength(1)
    expect(view.copied[0]!.stopped).toBe(true)

    // Try again brings the options back as they were, and what was copied comes back unchanged
    await click('Try again')
    expect(region('Copied documents')).toBeNull()
    expect(alerts(view)).toEqual([])
    expect(select(view).props('modelValue')).toBe('staging')
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])

    await copy()
    expect(alerts(view)).toEqual([['success', 'Copied to staging: 1 created, 1 updated, 2 unchanged.']])
    expect(footer()).toEqual(['Compare with staging', 'Done'])
    expect(view.copied).toHaveLength(2)
  })

  it('shows why a copy ended as a whole above the rows so far, and tries again', async () => {
    server.use(http.post(stagingAnnotation(onlyDefault), denied))
    const view = await openDialog({ keys: subjects })
    await choose(radio('skip'))

    await copy()

    expect(alerts(view)).toEqual([
      ['error', 'The copy to staging did not finish: The Contributor role on acme.billing is required. 2 skipped.'],
    ])
    expect(spaced(document.activeElement)).toContain('The copy to staging did not finish')
    // the fourth key was not reached, and is no row
    expect(rows('Copied documents')).toEqual([
      ['Annotation', 'sbt.contoso', 'Skipped', 'It is already in staging.'],
      ['Annotation', 'sbt.fabrikam', 'Skipped', 'It is already in staging.'],
    ])
    expect(links('Copied documents')).toEqual([])
    expect(footer()).toEqual(['Try again', 'Done'])
    expect(view.copied[0]!.error).toMatchObject({ status: 403 })

    // Try again brings the options back as they were chosen
    server.resetHandlers()
    await click('Try again')
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])
    expect(checked()).toBe('Skip Leave it as it is.')
    expect(alerts(view)).toEqual([])

    await choose(radio('overwrite'))
    await copy()
    expect(alerts(view)).toEqual([['success', 'Copied to staging: 1 created, 3 updated.']])
  })

  it('asks for a new check of the production view after a copy that did not finish', async () => {
    server.use(http.post(`*${root}/default/annotations/${differs}`, denied))
    const view = await openDialog({ keys: [differs, onlyStaging] }, staging)
    await check()

    await copy('Copy 2 to default')
    expect(alerts(view)).toEqual([
      [
        'error',
        'The copy to default did not finish: The Contributor role on acme.billing is required. Nothing copied.',
      ],
    ])

    await click('Try again')
    expect(alertTitles()).toEqual(['default is the production view'])
    expect(button('Copy')!.disabled).toBe(true)
    expect(text()).toContain('Check first: default is the production view.')
  })

  it('creates no view: a copy to a target that is gone from the project ends as a whole', async () => {
    listViews('default', 'staging', 'qa')
    const view = await openDialog({ keys: [differs] })
    await pick(view, 'qa')
    // qa is unregistered behind the open dialog
    server.resetHandlers()
    const requests = recordRequests()

    await copy()

    expect(alerts(view)).toEqual([
      ['error', 'The copy to qa did not finish: qa is not a view of this project. Nothing copied.'],
    ])
    expect(requests.lines).toEqual(['GET /views'])
    expect(footer()).toEqual(['Try again', 'Done'])
  })

  it('has no target any more when the chosen view goes from the project', async () => {
    listViews('default', 'staging', 'qa')
    const view = await openDialog({ keys: [differs] })
    await pick(view, 'qa')
    await check()
    expect(footer().at(-1)).toMatch(/^Copy \d+ to qa$/)

    // the views are read again, and qa is not among them
    server.resetHandlers()
    await view.queryClient.invalidateQueries()
    await eventually(() => expect(select(view).props('items')).toEqual(['staging']))

    expect(region('Checked documents')).toBeNull()
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])
    expect(button('Check')!.disabled).toBe(true)
    expect(button('Copy')!.disabled).toBe(true)
    expect(text()).toContain('When the other view already has it')
  })

  it('shows a copy that ended before its first key without a table', async () => {
    const view = await openDialog({ keys: subjects })
    server.use(http.post(`*${root}/staging/annotations/deleted/lookup`, busy))

    await copy()

    expect(alerts(view)).toEqual([
      ['error', 'The copy to staging did not finish: The database is busy. Nothing copied.'],
    ])
    expect(region('Copied documents')).toBeNull()
    expect(footer()).toEqual(['Try again', 'Done'])
  })

  it('stops the copy when the dialog is unmounted while it runs', async () => {
    const view = await openDialog({ keys: subjects })
    const first = hold('post', stagingAnnotation('sbt.contoso'))
    const requests = recordRequests()
    await click('Copy')
    await first.reached

    // the page was left
    mounted.splice(0).forEach((screen) => screen.host.unmount())
    first.release()

    // the write under way is finished, and nothing follows it
    await eventually(async () =>
      expect(await textIn('staging', 'sbt.contoso')).toBe(await textIn('default', 'sbt.contoso')),
    )
    await new Promise((resolve) => setTimeout(resolve, 150))
    expect(requests.writes()).toEqual(['POST /staging/annotations/sbt.contoso'])
    expect(requests.lines.filter((line) => line.includes('sbt.fabrikam'))).toEqual([])
    expect(await inStaging(onlyDefault)).toBe(false)
    expect(view.copied).toEqual([])
  })
})

describe('ViewCopyDialog: what a hurried hand does', () => {
  it('runs one copy for two clicks on Copy', async () => {
    const view = await openDialog({ keys: [differs, onlyDefault] })
    const requests = recordRequests()

    const target = button('Copy')!
    target.click()
    target.click()
    await eventually(() => expect(footer()).toContain('Done'))

    expect(requests.writes()).toEqual([
      'POST /staging/annotations/sbt.initech',
      'POST /staging/annotations/sbt.northwind',
    ])
    expect(requests.lines.filter((line) => line === 'GET /views')).toHaveLength(1)
    expect(view.copied).toHaveLength(1)
    expect(alerts(view)).toEqual([['success', 'Copied to staging: 1 created, 1 updated.']])
  })

  it('runs one check for two clicks on Check', async () => {
    await openDialog({ keys: [differs, onlyDefault] })
    const requests = recordRequests()

    const target = button('Check')!
    target.click()
    target.click()
    await eventually(() => expect(alertTitles()).toEqual(['Checked: nothing was written']))
    await settle()

    expect(requests.lines.filter((line) => line === 'GET /views')).toHaveLength(1)
    expect(rows('Checked documents')).toHaveLength(2)
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy 2 to staging'])
  })

  it('stops once for two clicks on Stop', async () => {
    const view = await openDialog({ keys: subjects })
    const second = hold('post', stagingAnnotation('sbt.fabrikam'))
    await click('Copy')
    await second.reached

    const target = button('Stop')!
    target.click()
    target.click()
    second.release()
    await eventually(() => expect(footer()).toEqual(['Try again', 'Done']))

    expect(alerts(view)).toEqual([['warning', 'The copy to staging was stopped: 2 updated, 2 skipped.']])
    expect(view.copied).toHaveLength(1)
  })

  it('does not copy when Copy is clicked while a check runs', async () => {
    const view = await openDialog({ keys: [differs, onlyDefault] })
    const requests = recordRequests()

    const copyButton = button('Copy')!
    button('Check')!.click()
    copyButton.click()
    await eventually(() => expect(alertTitles()).toEqual(['Checked: nothing was written']))

    expect(requests.writes()).toEqual([])
    expect(view.copied).toEqual([])
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy 2 to staging'])
  })

  // Copy grows to where Check stood when the plan appears. In a low window the footer stays in place, and a quick
  // check is over between the two clicks of a double click on Check: the second one lands on Copy.
  it('does not copy by the second click of a double click, which was meant for Check', async () => {
    const view = await openDialog({ keys: [differs, onlyStaging] }, staging)
    await check()
    const requests = recordRequests()
    const planned = button('Copy 2 to default')!
    expect(planned.disabled).toBe(false)
    /** A click as the browser counts it: 1 for a single one, 2 for the second of a double click, 0 for a key. */
    const press = async (count: number) => {
      planned.dispatchEvent(new MouseEvent('click', { detail: count, bubbles: true, cancelable: true }))
      await settle()
    }

    await press(2)
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(requests.lines).toEqual([])
    expect(view.copied).toEqual([])
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy 2 to default'])
    expect(alertTitles()).toEqual(['default is the production view', 'Checked: nothing was written'])

    // a click of its own copies, as Enter and Space do
    await press(1)
    await eventually(() => expect(footer()).toContain('Done'))
    expect(alerts(view)).toEqual([['success', 'Copied to default: 1 created, 1 updated.']])
    expect(view.copied).toHaveLength(1)
  })

  it('copies by a key on Copy, whose click has no count', async () => {
    const view = await openDialog({ keys: [differs] })

    button('Copy')!.dispatchEvent(new MouseEvent('click', { detail: 0, bubbles: true, cancelable: true }))
    await eventually(() => expect(footer()).toContain('Done'))

    expect(alerts(view)).toEqual([['success', 'Copied to staging: 1 updated.']])
  })
})

describe('ViewCopyDialog: states that must not leak', () => {
  it('is mounted closed by its place, and opens with the target of that moment', async () => {
    listViews('default', 'staging', 'qa')
    const view = await openDialog({ keys: [differs], open: false })
    expect(dialog()).toBeNull()

    // what was used in the project while the dialog was closed counts when it opens
    useViewTransferStore().rememberTarget('acme.billing', 'qa')
    view.state.open = true
    await settle()

    expect(title()).toBe('Copy sbt.northwind to another view')
    expect(select(view).props('modelValue')).toBe('qa')
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])
    expect(button('Copy')!.disabled).toBe(false)
  })

  it('has nothing to run without a key', async () => {
    const view = await openDialog({ keys: [] })

    expect(title()).toBe('Copy 0 annotations to another view')
    expect(alerts(view)).toEqual([])
    expect(button('Check')!.disabled).toBe(true)
    expect(button('Copy')!.disabled).toBe(true)
  })

  it('takes the plan of a check only for the keys it checked', async () => {
    const view = await openDialog({ keys: annotations }, staging)
    const first = hold('get', `*${root}/staging/annotations/${differs}`)
    await click('Check')
    await first.reached

    // the selection behind the dialog changes while the check runs: a key went from the catalog
    view.state.keys = [same]
    await settle()
    first.release()
    await eventually(() => expect(footer()).toContain('Cancel'))
    await settle()

    // what was checked is not what a copy would take now: no plan, and the production view asks for its check
    expect(title()).toBe(`Copy ${same} to another view`)
    expect(region('Checked documents')).toBeNull()
    expect(alertTitles()).toEqual(['default is the production view'])
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])
    expect(button('Copy')!.disabled).toBe(true)
    expect(text()).toContain('Check first: default is the production view.')
  })

  it('compares the keys that were copied, whatever the selection is by then', async () => {
    const view = await openDialog({ keys: [differs, onlyDefault] })
    await copy()

    view.state.keys = [differs]
    await settle()
    // the result stays the result of the copy that ran
    expect(rows('Copied documents').map((row) => row[1])).toEqual([onlyDefault, differs])
    await click('Compare with staging')

    expect(view.router.currentRoute.value.query).toEqual({
      kind: 'annotations',
      to: 'staging',
      key: [onlyDefault, differs],
    })
  })

  it('stops the copy when the place that opened it closes it while it runs', async () => {
    const view = await openDialog({ keys: subjects })
    const first = hold('post', stagingAnnotation('sbt.contoso'))
    const requests = recordRequests()
    await click('Copy')
    await first.reached

    view.state.open = false
    await settle()
    first.release()

    await eventually(() => expect(view.copied).toHaveLength(1))
    expect(view.copied[0]!.stopped).toBe(true)
    expect(requests.writes()).toEqual(['POST /staging/annotations/sbt.contoso'])
    expect(await inStaging(onlyDefault)).toBe(false)
  })

  it('shows no result of a copy that a closed opening started in the next opening', async () => {
    const view = await openDialog({ keys: subjects })
    const first = hold('post', stagingAnnotation('sbt.contoso'))
    await click('Copy')
    await first.reached

    // closed by its place, and opened again before the copy is over
    view.state.open = false
    await settle()
    view.state.open = true
    await settle()
    first.release()
    await eventually(() => expect(footer()).toEqual(['Cancel', 'Check', 'Copy']))
    await settle()

    // the opening of now did not start it: no result, and no event that its place would take for its own
    expect(region('Copied documents')).toBeNull()
    expect(alerts(view)).toEqual([])
    expect(view.copied).toEqual([])
  })

  it('copies only what its check showed: a source changed since is skipped', async () => {
    const view = await openDialog({ keys: [differs] })
    await check()
    expect(rows('Checked documents')).toEqual([['Annotation', differs, 'To update', '']])

    // another client changes the source after the check
    const changed = await getAnnotation(org, project, 'default', differs, { includeInactive: true })
    await setAnnotation(org, project, 'default', differs, { ...changed, Title: 'Changed after the check' })
    await copy('Copy 1 to staging')

    expect(rows('Copied documents')).toEqual([
      ['Annotation', differs, 'Skipped', 'It changed in default since it was checked.'],
    ])
    expect(view.copied).toHaveLength(1)
  })

  it('copies only over what its check showed of the target: a document changed there since is skipped', async () => {
    const view = await openDialog({ keys: [differs] })
    await check()
    expect(rows('Checked documents')).toEqual([['Annotation', differs, 'To update', '']])

    // another client changes the target after the check
    const there = await getAnnotation(org, project, 'staging', differs, { includeInactive: true })
    await setAnnotation(org, project, 'staging', differs, { ...there, Title: 'Changed in staging after the check' })
    await copy('Copy 1 to staging')

    expect(rows('Copied documents')).toEqual([
      ['Annotation', differs, 'Skipped', 'It changed in staging since it was checked.'],
    ])
    expect(view.copied).toHaveLength(1)
  })

  it('leaves no plan of a check that it was closed under', async () => {
    // one key: a check is not stopped during its last key, so this one ends with a whole plan
    const view = await openDialog({ keys: [onlyDefault] })
    const only = hold('get', `*${root}/default/annotations/${onlyDefault}`)
    await click('Check')
    await only.reached

    // closed by its place, and opened again before the check is over
    view.state.open = false
    await settle()
    view.state.open = true
    await settle()
    only.release()
    await eventually(() => expect(footer()).toEqual(['Cancel', 'Check', 'Copy']))
    await settle()

    // the dialog that is open now did not ask for it
    expect(region('Checked documents')).toBeNull()
    expect(alerts(view)).toEqual([])
    expect(clean(status())).toBe('')
  })

  it('discards the plan when the kind changes: the same key is another document', async () => {
    // sbt.contoso differs between the views as an annotation, and as a configuration
    const view = await openDialog({ keys: [differing] }, staging)
    await check()
    expect(rows('Checked documents')).toEqual([['Annotation', differing, 'To update', '']])
    expect(button('Copy 1 to default')!.disabled).toBe(false)

    view.state.kind = 'configurations'
    await settle()

    // what was checked of the annotation is no plan for the configuration, and production asks for its check
    expect(region('Checked documents')).toBeNull()
    expect(alertTitles()).toEqual(['default is the production view'])
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])
    expect(button('Copy')!.disabled).toBe(true)
    expect(text()).toContain('Check first: default is the production view.')

    await check()
    expect(rows('Checked documents')).toEqual([['Configuration', differing, 'To update', '']])
  })

  // The view of the route changes under the open dialog by the browser's Back, or by a link of a result: a detail
  // page keeps its instance then, and the dialog with it.
  it('closes when the view of the route changes under it, and has no plan when it is opened again', async () => {
    listViews('default', 'staging', 'qa')
    const view = await openDialog({ keys: [differs], target: 'default' }, staging)
    await check()
    expect(button('Copy 1 to default')!.disabled).toBe(false)

    // to a third view of the project: the target stays another view of it, so only the source changes
    useContextStore().set({ org, project, view: 'qa' })
    await settle()

    expect(view.state.open).toBe(false)
    await eventually(() => expect(dialog()).toBeNull())

    view.state.open = true
    await settle()
    // the source is the view of the route; the plan was the one of a copy from staging
    expect(title()).toBe('Copy sbt.northwind to default')
    expect(clean(dialog()!.querySelector('[data-slot="description"]'))).toBe('From acme/billing, view qa.')
    expect(region('Checked documents')).toBeNull()
    expect(alertTitles()).toEqual(['default is the production view'])
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])
    expect(button('Copy')!.disabled).toBe(true)
    expect(text()).toContain('Check first: default is the production view.')
  })

  it('drops the plan with the view of the route also where its place does not let it close', async () => {
    listViews('default', 'staging', 'qa')
    const view = await openDialog({ keys: [differs], target: 'default' }, staging, { keepsOpen: true })
    await check()
    expect(button('Copy 1 to default')!.disabled).toBe(false)

    useContextStore().set({ org, project, view: 'qa' })
    await settle()

    // it stays, as a copy from qa: Copy must not say what the copy from staging would have written
    expect(view.state.open).toBe(true)
    expect(clean(dialog()!.querySelector('[data-slot="description"]'))).toBe('From acme/billing, view qa.')
    expect(region('Checked documents')).toBeNull()
    expect(alertTitles()).toEqual(['default is the production view'])
    expect(footer()).toEqual(['Cancel', 'Check', 'Copy'])
    expect(button('Copy')!.disabled).toBe(true)
    expect(text()).toContain('Check first: default is the production view.')
  })

  // The place that opened the dialog gives the focus a home then: not before the dialog has returned it.
  it('says that it is gone once it has left the screen, however it was closed, and not before', async () => {
    const view = await openDialog({ keys: [differs] })
    await check()
    expect(view.closed).toEqual([])

    await click('Cancel')
    await eventually(() => expect(view.closed).toHaveLength(1))

    view.state.open = true
    await settle()
    await copy()
    // opening it again, a copy and its result are no leave
    expect(view.closed).toHaveLength(1)
    await click('Done')
    await eventually(() => expect(view.closed).toHaveLength(2))

    view.state.open = true
    await settle()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))
    await eventually(() => expect(view.closed).toHaveLength(3))

    // and when its place closes it, as the comparison page does when the comparison changes
    view.state.open = true
    await settle()
    view.state.open = false
    await eventually(() => expect(view.closed).toHaveLength(4))
    await settle()

    // once per leave, each time closed for its place, and nothing of it is left on the screen
    expect(view.closed).toEqual([false, false, false, false])
    expect(dialog()).toBeNull()
  })

  it('keeps the focus in reach after a check: on the plan, as after a copy on the result', async () => {
    await openDialog({ keys: annotations })

    await check()

    expect(spaced(document.activeElement)).toBe(
      'Checked: nothing was written To create 1 · To update 1 · Unchanged 1 · To skip 1.',
    )
    expect(dialog()!.contains(document.activeElement)).toBe(true)
  })

  it('never loses the focus out of the dialog when the button that had it goes', async () => {
    await openDialog({ keys: subjects })
    const inside = () => dialog()!.contains(document.activeElement)
    const second = hold('post', stagingAnnotation('sbt.fabrikam'))

    // Copy goes when the run starts
    await click('Copy')
    await second.reached
    expect(inside()).toBe(true)
    // Stop goes when it ends
    await click('Stop')
    second.release()
    await eventually(() => expect(footer()).toEqual(['Try again', 'Done']))
    expect(inside()).toBe(true)
    // and Try again with the result
    await click('Try again')
    expect(inside()).toBe(true)
  })

  it('lets the keys scroll the documents: the table is a named region that takes the focus', async () => {
    await openDialog({ keys: annotations })
    await check()
    const plan = region('Checked documents')!
    expect(plan.tabIndex).toBe(0)
    expect(plan.querySelector('table')).not.toBeNull()

    await copy('Copy 2 to staging')
    const result = region('Copied documents')!
    expect(result.tabIndex).toBe(0)
    // one region at a time
    expect(dialog()!.querySelectorAll('[role="region"]')).toHaveLength(1)
  })
})

interface Harness {
  progress: { done: number; total: number } | undefined
  run: (request: ViewCopyRequest, signal?: AbortSignal) => Promise<ViewCopyResult>
}

/** The composable in a component of the app: with its query client, the stores and the context. */
async function mountCopy(source = billing) {
  const Place = defineComponent({
    setup(_, { expose }) {
      const { progress, run } = useViewCopy()
      expose({ progress, run })
      return () => null
    },
  })
  const screen = await mountScreen(Place, {}, { scope: source })
  mounted.push(screen)
  return { harness: screen.inner().vm as unknown as Harness, queryClient: screen.queryClient as QueryClient }
}

const request = (changes: Partial<ViewCopyRequest> & Pick<ViewCopyRequest, 'keys'>): ViewCopyRequest => ({
  source: billing,
  target: 'staging',
  kind: 'annotations',
  existing: 'overwrite',
  missingAnnotations: true,
  ...changes,
})

describe('useViewCopy', () => {
  it('runs the copy and says how far it is while it runs', async () => {
    const { harness } = await mountCopy()
    expect(harness.progress).toBeUndefined()
    const second = hold('post', stagingAnnotation('sbt.fabrikam'))

    // a key that is given twice is one key
    const run = harness.run(request({ keys: [...subjects, differs] }))
    expect(harness.progress).toEqual({ done: 0, total: 4 })
    await second.reached
    expect(harness.progress).toEqual({ done: 1, total: 4 })
    second.release()
    const result = await run

    expect(result).toMatchObject({ target: 'staging', dryRun: false, stopped: false })
    expect(result.items.map((item) => `${item.key} ${item.outcome}`)).toEqual([
      'sbt.contoso Updated',
      'sbt.fabrikam Updated',
      'sbt.initech Created',
      'sbt.northwind Updated',
    ])
    expect(harness.progress).toBeUndefined()
    expect(await inStaging(onlyDefault)).toBe(true)
  })

  it('checks without writing, and stops by its signal', async () => {
    const { harness } = await mountCopy()
    const requests = recordRequests()

    const plan = await harness.run(request({ keys: subjects, dryRun: true }))
    expect(plan.dryRun).toBe(true)
    expect(plan.items.map((item) => item.outcome)).toEqual(['Updated', 'Updated', 'Created', 'Updated'])
    expect(requests.writes()).toEqual([])
    expect(harness.progress).toBeUndefined()

    const stopped = await harness.run(request({ keys: subjects }), AbortSignal.abort())
    expect(stopped.stopped).toBe(true)
    expect(stopped.items.map((item) => item.message)).toEqual(subjects.map(() => stoppedMessage))
    expect(requests.writes()).toEqual([])
    expect(harness.progress).toBeUndefined()
  })

  it('has no progress left after a run that ended as a whole', async () => {
    const { harness } = await mountCopy()
    server.use(http.get(viewsUrl, busy))

    const result = await harness.run(request({ keys: subjects }))

    expect(result.error).toMatchObject({ status: 500 })
    expect(result.items).toEqual([])
    expect(harness.progress).toBeUndefined()
  })

  it('refreshes the open queries of what it wrote in the target', async () => {
    const { harness, queryClient } = await mountCopy()
    let reads = 0
    // an open detail of the annotation in the target view
    const observer = new QueryObserver(queryClient, {
      queryKey: ['v1', org, project, 'staging', 'annotations', differs],
      queryFn: () => {
        reads++
        return textIn('staging', differs)
      },
    })
    const stop = observer.subscribe(() => {})
    await eventually(() => expect(observer.getCurrentResult().isSuccess).toBe(true))
    expect(reads).toBe(1)

    await harness.run(request({ keys: [differs], dryRun: true }))
    await settle()
    expect(reads).toBe(1)

    await harness.run(request({ keys: [differs] }))
    await eventually(() => expect(reads).toBe(2))
    await eventually(async () => expect(observer.getCurrentResult().data).toBe(await textIn('default', differs)))
    stop()
  })

  it('reloads the catalog when it holds the target by the end of the run', async () => {
    const catalog = useCatalogStore()
    await catalog.forgetSnapshot(staging)
    // the user went to the target view while the copy ran
    await catalog.load(staging)
    expect(catalog.byKey.has(onlyDefault)).toBe(false)
    const { harness } = await mountCopy()
    const load = vi.spyOn(catalog, 'load')

    // a check, and a copy that writes nothing, leave it alone
    await harness.run(request({ keys: [onlyDefault], dryRun: true }))
    await harness.run(request({ keys: [same] }))
    expect(load).not.toHaveBeenCalled()

    await harness.run(request({ keys: [onlyDefault] }))
    expect(load).toHaveBeenCalledExactlyOnceWith(staging, { force: true })
    await eventually(() => expect(catalog.byKey.has(onlyDefault)).toBe(true))
  })

  it('forgets the snapshot of the target when it wrote there: the next visit must not show the old list', async () => {
    const catalog = useCatalogStore()
    await catalog.load(billing)
    const forget = vi.spyOn(catalog, 'forgetSnapshot')
    const { harness } = await mountCopy()

    await harness.run(request({ keys: [onlyDefault], dryRun: true }))
    expect(forget).not.toHaveBeenCalled()

    await harness.run(request({ keys: [onlyDefault] }))
    expect(forget).toHaveBeenCalledWith(staging)

    // a copy that writes nothing leaves it
    forget.mockClear()
    await harness.run(request({ keys: [onlyDefault] }))
    expect(forget).not.toHaveBeenCalled()
  })

  it('leaves the catalog alone while it holds the source: a copy does not change it', async () => {
    const catalog = useCatalogStore()
    await catalog.forgetSnapshot(billing)
    await catalog.load(billing)
    const { harness } = await mountCopy()
    const load = vi.spyOn(catalog, 'load')

    const result = await harness.run(request({ keys: [onlyDefault, differs] }))

    expect(result.items.map((item) => item.outcome)).toEqual(['Created', 'Updated'])
    expect(load).not.toHaveBeenCalled()
    expect(catalog.scope).toEqual(billing)
  })

  it('leaves the catalog alone when nothing was loaded into it', async () => {
    const catalog = useCatalogStore()
    const { harness } = await mountCopy()
    const load = vi.spyOn(catalog, 'load')

    await harness.run(request({ keys: [onlyDefault] }))

    expect(load).not.toHaveBeenCalled()
    expect(catalog.scope).toBeNull()
  })
})
