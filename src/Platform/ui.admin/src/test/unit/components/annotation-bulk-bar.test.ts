import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount, type VueWrapper } from '@vue/test-utils'
import ui from '@nuxt/ui/vue-plugin'
import UApp from '@nuxt/ui/components/App.vue'
import { setupServer } from 'msw/node'
import type { AnnotationDeletionResult } from '@42for.net/2splatform.sdk'
import AnnotationBulkBar from '@/components/annotations/AnnotationBulkBar.vue'
import ConfirmDialog from '@/components/common/ConfirmDialog.vue'
import ViewCopyDialog from '@/components/views/ViewCopyDialog.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { useViewTransferStore } from '@/stores/view-transfer'
import { annotationText, readAnnotation } from '@/utils/view-documents'
import { mountScreen, settle } from '../../helpers'

// The selection of the page: removeMany drops each deleted key from the catalog, so the keys prop shrinks meanwhile.
const selection = ref<string[]>([])
let failing: string[] = []

const removeMany = vi.fn(async (keys: string[]) => {
  const deletions: AnnotationDeletionResult[] = []
  for (const key of keys) {
    if (failing.includes(key)) {
      continue
    }
    selection.value = selection.value.filter((selected) => selected !== key)
    deletions.push({ DeletedKeys: [key] } as AnnotationDeletionResult)
    await Promise.resolve()
  }
  return { failed: keys.filter((key) => failing.includes(key)), deletions }
})
const notifyDeleted = vi.fn()

// The delete tests mount the bar without a router and a query client, so the comparison and the copy are stand-ins
// there; the tests of "Compare with view" and "Copy to view" take the composable itself.
let realTransfer = false

vi.mock('@/composables/useAnnotationActions', () => ({
  useAnnotationActions: () => ({ removeMany, progress: ref(undefined) }),
}))
vi.mock('@/composables/useDeletionToast', () => ({ useDeletionToast: () => notifyDeleted }))
vi.mock('@/composables/useViewTransfer', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/composables/useViewTransfer')>()
  return {
    useViewTransfer: (...parameters: Parameters<typeof original.useViewTransfer>) =>
      realTransfer
        ? original.useViewTransfer(...parameters)
        : { blocked: ref(undefined), openCompare: vi.fn(), copying: ref(false) },
  }
})

let wrapper: VueWrapper | undefined

/** The bar as the Annotations page renders it: only while something is selected. */
async function mountBar(keys: string[]) {
  selection.value = keys
  wrapper = mount(
    defineComponent({
      setup: () => () =>
        h(UApp, null, {
          default: () => (selection.value.length ? h(AnnotationBulkBar, { keys: selection.value }) : null),
        }),
    }),
    // the copy dialog reads the views of the project, which needs the query client
    { attachTo: document.body, global: { plugins: [ui], stubs: { ViewCopyDialog: true } } },
  )
  await settle()
}

async function confirmDelete() {
  const dialog = wrapper!.findComponent(ConfirmDialog)
  expect(dialog.props('title')).toBe(
    selection.value.length === 1 ? 'Delete 1 annotation?' : `Delete ${selection.value.length} annotations?`,
  )
  dialog.vm.$emit('confirm')
  await settle()
}

beforeEach(() => {
  failing = []
  realTransfer = false
  removeMany.mockClear()
  notifyDeleted.mockClear()
})

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
})

describe('AnnotationBulkBar', () => {
  it('counts every deleted annotation although the selection shrinks during the delete', async () => {
    await mountBar(['UNIT.a', 'UNIT.b'])

    await confirmDelete()

    expect(removeMany).toHaveBeenCalledWith(['UNIT.a', 'UNIT.b'])
    expect(notifyDeleted).toHaveBeenCalledTimes(1)
    expect(notifyDeleted.mock.calls[0]![0]).toBe('2 annotations deleted')
    expect(notifyDeleted.mock.calls[0]![1]).toHaveLength(2)
  })

  it('uses the singular for one annotation', async () => {
    await mountBar(['UNIT.a'])

    await confirmDelete()

    expect(notifyDeleted.mock.calls[0]![0]).toBe('1 annotation deleted')
  })

  it('reports the failed and the deleted annotations', async () => {
    failing = ['UNIT.b']
    await mountBar(['UNIT.a', 'UNIT.b'])

    await confirmDelete()

    expect(notifyDeleted.mock.calls[0]![0]).toBe('1 annotation deleted')
    expect(document.body.textContent).toContain('1 annotation could not be deleted')
  })
})

// "Compare with view" and "Copy to view" (spec "Copy and Compare Annotations and Configurations Between Views",
// section 8) against the mock API: acme/billing has the views default and staging, acme/shipping only its default
// view.
describe('Compare with view and Copy to view in the annotations bulk bar', () => {
  const server = setupServer(...mockHandlers)
  const mounted: { host: { unmount: () => void } }[] = []
  beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
  beforeEach(() => {
    realTransfer = true
  })
  afterEach(() => {
    // unmounted before the body is cleared, so a closing tooltip or dialog does not patch removed nodes
    mounted.splice(0).forEach((view) => view.host.unmount())
    server.resetHandlers()
    document.body.innerHTML = ''
  })
  afterAll(() => server.close())

  const comparison = '/acme/billing/default/modeling/compare/selection'
  const staging = { org: 'acme', project: 'billing', view: 'staging' }
  const keysOf = (count: number) =>
    Array.from({ length: count }, (_, index) => `exe.northwind.invoicing.job-${String(index).padStart(3, '0')}`)

  async function mountCompareBar(keys: string[], project = 'billing') {
    const clear = vi.fn()
    const view = await mountScreen(
      AnnotationBulkBar,
      { keys, onClear: clear },
      {
        scope: { org: 'acme', project, view: 'default' },
        accessMap: { 'acme.billing': 'Owner', 'acme.shipping': 'Contributor' },
      },
    )
    mounted.push(view)
    // the bar knows whether there is another view once the views of the project arrived
    await vi.waitFor(() =>
      expect(view.queryClient.getQueryState(['v1', 'acme', project, 'views'])?.status).toBe('success'),
    )
    await settle()
    return { ...view, clear, copyDialog: () => view.host.findComponent(ViewCopyDialog) }
  }

  const toolbar = () => document.body.querySelector<HTMLElement>('[role="toolbar"][aria-label="Bulk actions"]')!
  const button = (label: string) =>
    [...toolbar().querySelectorAll('button')].find((candidate) => candidate.textContent?.trim() === label)!
  const labels = () => [...toolbar().querySelectorAll('button')].map((candidate) => candidate.textContent?.trim())
  /** What the bar shows, in its order: the number of selected rows, the buttons, and the reason beside them. */
  const parts = () => [...toolbar().children].map((child) => child.textContent?.trim())
  const transfers = () => [button('Compare with view'), button('Copy to view')]

  // the copy dialog is teleported to the body
  const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')
  const dialogButton = (label: string) =>
    [...(dialog()?.querySelectorAll('button') ?? [])].find((candidate) => candidate.textContent?.trim() === label)
  /** A copy is several requests: a slow machine takes longer than the second `vi.waitFor` waits by default. */
  const eventually = (assertion: () => void) => vi.waitFor(assertion, { timeout: 3000 })

  /** Both buttons are off for one reason, which is one text beside them and the description of both. */
  function expectBlocked(reason: string) {
    const [compare, copy] = transfers()
    expect([compare!.disabled, copy!.disabled]).toEqual([true, true])
    const described = compare!.getAttribute('aria-describedby')
    expect(described).toBeTruthy()
    expect(copy!.getAttribute('aria-describedby')).toBe(described)
    const text = document.getElementById(described!)
    expect(text?.textContent).toBe(reason)
    expect(toolbar().contains(text)).toBe(true)
    // said once, not once per button
    expect(toolbar().textContent!.split(reason)).toHaveLength(2)
  }

  it('sit after Label and before Delete', async () => {
    await mountCompareBar(['sbt.northwind'])

    expect(labels()).toEqual([
      'Enable',
      'Disable',
      'Label',
      'Compare with view',
      'Copy to view',
      'Delete',
      'Clear selection',
    ])
    expect(parts()[0]).toBe('1 selected')
    // both are on, and nothing explains them
    for (const transfer of transfers()) {
      expect(transfer.disabled).toBe(false)
      expect(transfer.hasAttribute('aria-describedby')).toBe(false)
    }
  })

  it('opens the comparison of the selected annotations and keeps the selection', async () => {
    const view = await mountCompareBar(['sbt.northwind', 'sbt.initech'])

    const compare = button('Compare with view')
    expect(compare.disabled).toBe(false)
    expect(compare.hasAttribute('aria-describedby')).toBe(false)

    compare.click()
    await settle()

    // the keys of the address are sorted, and no target is picked yet
    expect(view.router.currentRoute.value.fullPath).toBe(
      `${comparison}?kind=annotations&key=sbt.initech&key=sbt.northwind`,
    )
    expect(view.clear).not.toHaveBeenCalled()
    // a selection that fits in the address is not kept in the browser
    expect(sessionStorage.getItem('2s-admin:view-transfer')).toBeNull()
  })

  it('keeps a selection that is too long for an address, and opens the comparison by its id', async () => {
    const keys = keysOf(60)
    const view = await mountCompareBar(keys)
    // nothing is kept while the bar is shown, only when the comparison is opened
    expect(sessionStorage.getItem('2s-admin:view-transfer')).toBeNull()

    button('Compare with view').click()
    await settle()

    const { path, query } = view.router.currentRoute.value
    expect(path).toBe(comparison)
    expect(query.kind).toBe('annotations')
    expect(query.key).toBeUndefined()
    expect(useViewTransferStore().keysOf(String(query.set))).toEqual(keys)
    expect(view.clear).not.toHaveBeenCalled()
  })

  it('opens the copy of the selected annotations with Copy to view', async () => {
    const view = await mountCompareBar(['sbt.northwind', 'sbt.initech'])
    expect(view.copyDialog().props('open')).toBe(false)
    expect(dialog()).toBeNull()

    button('Copy to view').click()
    await settle()

    // the dialog of annotations, for the selected keys; the target is picked in it
    expect(view.copyDialog().props()).toMatchObject({
      open: true,
      kind: 'annotations',
      keys: ['sbt.northwind', 'sbt.initech'],
      compare: true,
    })
    expect(view.copyDialog().props('target')).toBeUndefined()
    expect(view.copyDialog().props('expected')).toBeUndefined()
    await vi.waitFor(() => expect(dialog()?.textContent).toContain('Copy 2 annotations to another view'))
    // the page stays, with its selection
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default')
    expect(view.clear).not.toHaveBeenCalled()
  })

  it('copies the selected annotations to the other view and keeps the selection', async () => {
    // sbt.initech is only in default
    const key = 'sbt.initech'
    expect((await readAnnotation(staging, key)).annotation).toBeUndefined()
    const view = await mountCompareBar([key])

    button('Copy to view').click()
    // staging is the only other view, so it is the target
    await eventually(() => expect(dialogButton('Copy')?.disabled).toBe(false))
    dialogButton('Copy')!.click()
    await eventually(() => expect(dialog()?.textContent).toContain('Copied to staging'))

    const copied = (await readAnnotation(staging, key)).annotation
    const source = (await readAnnotation({ ...staging, view: 'default' }, key)).annotation
    expect(copied && annotationText(copied)).toBe(annotationText(source!))
    // from a list, the copy can be compared next
    expect(dialogButton('Compare with staging')).toBeDefined()

    dialogButton('Done')!.click()
    await eventually(() => expect(view.copyDialog().props('open')).toBe(false))
    // the rows of this view did not change: they stay selected
    expect(view.clear).not.toHaveBeenCalled()
    expect(parts()[0]).toBe('1 selected')
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default')
  })

  it('are disabled in a project without another view, with the reason once beside them', async () => {
    // acme/shipping has only its default view, and the user is a Contributor there
    const view = await mountCompareBar(['sbt.northwind'], 'shipping')

    expectBlocked('The project has no other view')
    // the reason follows the two buttons, before Delete
    expect(parts()).toEqual([
      '1 selected',
      'Enable',
      'Disable',
      'Label',
      'Compare with view',
      'Copy to view',
      'The project has no other view',
      'Delete',
      'Clear selection',
    ])

    for (const transfer of transfers()) {
      transfer.click()
    }
    await settle()
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/shipping/default')
    expect(view.copyDialog().props('open')).toBe(false)
    expect(dialog()).toBeNull()
    // the other actions stay on
    expect(button('Delete').disabled).toBe(false)
  })

  it.each(['Compare with view', 'Copy to view'])(
    'shows the reason as the tooltip of the disabled %s',
    async (label) => {
      await mountCompareBar(['sbt.northwind'], 'shipping')

      button(label).dispatchEvent(new PointerEvent('pointermove', { bubbles: true, pointerType: 'mouse' }))

      await vi.waitFor(() =>
        expect(document.body.querySelector('[role="tooltip"]')?.textContent?.trim()).toBe(
          'The project has no other view',
        ),
      )
    },
  )

  it('are disabled above 100 selected annotations, with the reason once beside them', async () => {
    const view = await mountCompareBar(keysOf(101))

    expectBlocked('Select up to 100 to compare or copy them at once')

    button('Copy to view').click()
    await settle()
    expect(view.copyDialog().props('open')).toBe(false)
  })

  it('are enabled with exactly 100 selected annotations', async () => {
    await mountCompareBar(keysOf(100))

    expect(transfers().map((transfer) => transfer.disabled)).toEqual([false, false])
    expect(toolbar().textContent).not.toContain('Select up to 100')
  })
})
