import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount, type VueWrapper } from '@vue/test-utils'
import ui from '@nuxt/ui/vue-plugin'
import UApp from '@nuxt/ui/components/App.vue'
import { setupServer } from 'msw/node'
import type { AnnotationDeletionResult } from '@42for.net/2splatform.sdk'
import AnnotationBulkBar from '@/components/annotations/AnnotationBulkBar.vue'
import ConfirmDialog from '@/components/common/ConfirmDialog.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { useViewTransferStore } from '@/stores/view-transfer'
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

// The delete tests mount the bar without a router and a query client, so the comparison is a stand-in there; the
// tests of "Compare with view" take the composable itself.
let realTransfer = false

vi.mock('@/composables/useAnnotationActions', () => ({
  useAnnotationActions: () => ({ removeMany, progress: ref(undefined) }),
}))
vi.mock('@/composables/useDeletionToast', () => ({ useDeletionToast: () => notifyDeleted }))
vi.mock('@/composables/useViewTransfer', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/composables/useViewTransfer')>()
  return {
    useViewTransfer: (...parameters: Parameters<typeof original.useViewTransfer>) =>
      realTransfer ? original.useViewTransfer(...parameters) : { blocked: ref(undefined), openCompare: vi.fn() },
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
    { attachTo: document.body, global: { plugins: [ui] } },
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

// "Compare with view" (spec "Copy and Compare Annotations and Configurations Between Views", section 8) against the
// mock API: acme/billing has the views default and staging, acme/shipping only its default view.
describe('Compare with view in the annotations bulk bar', () => {
  const server = setupServer(...mockHandlers)
  const mounted: { host: { unmount: () => void } }[] = []
  beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
  beforeEach(() => {
    realTransfer = true
  })
  afterEach(() => {
    // unmounted before the body is cleared, so a closing tooltip does not patch removed nodes
    mounted.splice(0).forEach((view) => view.host.unmount())
    server.resetHandlers()
    document.body.innerHTML = ''
  })
  afterAll(() => server.close())

  const comparison = '/acme/billing/default/modeling/compare/selection'
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
    return { ...view, clear }
  }

  const toolbar = () => document.body.querySelector<HTMLElement>('[role="toolbar"][aria-label="Bulk actions"]')!
  const button = (label: string) =>
    [...toolbar().querySelectorAll('button')].find((candidate) => candidate.textContent?.trim() === label)!
  const labels = () => [...toolbar().querySelectorAll('button')].map((candidate) => candidate.textContent?.trim())

  it('sits after Label and before Delete', async () => {
    await mountCompareBar(['sbt.northwind'])

    expect(labels()).toEqual(['Enable', 'Disable', 'Label', 'Compare with view', 'Delete', 'Clear selection'])
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

  it('is disabled in a project without another view, with the reason beside it', async () => {
    // acme/shipping has only its default view, and the user is a Contributor there
    const view = await mountCompareBar(['sbt.northwind'], 'shipping')

    const compare = button('Compare with view')
    expect(compare.disabled).toBe(true)
    const reason = document.getElementById(compare.getAttribute('aria-describedby')!)
    expect(reason?.textContent).toBe('The project has no other view')
    expect(toolbar().contains(reason)).toBe(true)
    // and as the tooltip of the button
    compare.dispatchEvent(new PointerEvent('pointermove', { bubbles: true, pointerType: 'mouse' }))
    await vi.waitFor(() =>
      expect(document.body.querySelector('[role="tooltip"]')?.textContent?.trim()).toBe(
        'The project has no other view',
      ),
    )

    compare.click()
    await settle()
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/shipping/default')
    // the other actions stay on
    expect(button('Delete').disabled).toBe(false)
  })

  it('is disabled above 100 selected annotations, with the reason beside it', async () => {
    await mountCompareBar(keysOf(101))

    const compare = button('Compare with view')
    expect(compare.disabled).toBe(true)
    expect(document.getElementById(compare.getAttribute('aria-describedby')!)?.textContent).toBe(
      'Select up to 100 to compare or copy them at once',
    )
  })

  it('is enabled with exactly 100 selected annotations', async () => {
    await mountCompareBar(keysOf(100))

    expect(button('Compare with view').disabled).toBe(false)
    expect(toolbar().textContent).not.toContain('Select up to 100')
  })
})
