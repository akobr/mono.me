import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import type { VueWrapper } from '@vue/test-utils'
import { RouterView, type RouteRecordRaw } from 'vue-router'
import { useUnsavedChanges } from '@/composables/useUnsavedChanges'
import { installPageMemoryGuard } from '@/router/guards/page-memory'
import { usePageMemoryStore } from '@/stores/page-memory'
import { mountScreen } from '../../helpers'

// A dirty editor asks "Discard changes?" once per navigation, also when the page memory redirects the navigation to
// where a section was left: Vue Router runs the leave guards again on the redirected pass (424-67).

const confirm = vi.fn<() => Promise<boolean>>()
vi.mock('@/composables/useConfirm', () => ({ useConfirm: () => confirm }))

const base = '/acme/billing/default'
const scope = { org: 'acme', project: 'billing', view: 'default' }

const Editor = defineComponent({
  setup() {
    useUnsavedChanges(() => true)
    return () => h('form', { 'aria-label': 'Editor' })
  },
})

const routes: RouteRecordRaw[] = [
  { path: '/:org/:project/:view/annotations/:key', component: Editor },
  // as modeling/index.vue: the plain Modeling URL opens the first tab
  { path: '/:org/:project/:view/modeling', redirect: (to) => `${to.path.replace(/\/$/, '')}/schemas` },
  { path: '/:pathMatch(.*)*', component: { render: () => null } },
]

describe('useUnsavedChanges', () => {
  const mounted: VueWrapper[] = []
  beforeEach(() => confirm.mockReset())
  afterEach(() => mounted.splice(0).forEach((host) => host.unmount()))

  async function mountEditor() {
    const view = await mountScreen(RouterView, {}, { path: `${base}/annotations/sbt.northwind`, routes })
    mounted.push(view.host)
    installPageMemoryGuard(view.router)
    const memory = usePageMemoryStore()
    memory.rememberLocation(scope, 'modeling', `${base}/modeling/deleted?cascaded=1`)
    memory.rememberLocation(scope, 'configurations', `${base}/configurations/sbt.contoso/effective`)
    return { router: view.router, current: () => view.router.currentRoute.value.fullPath }
  }

  it('asks once when the page memory returns to where Modeling was left', async () => {
    const { router, current } = await mountEditor()
    confirm.mockResolvedValue(true)

    await router.push(`${base}/modeling`)

    expect(confirm).toHaveBeenCalledTimes(1)
    expect(current()).toBe(`${base}/modeling/deleted?cascaded=1`)
  })

  it('asks once when the page memory returns to where another section was left', async () => {
    const { router, current } = await mountEditor()
    confirm.mockResolvedValue(true)

    await router.push(`${base}/configurations`)

    expect(confirm).toHaveBeenCalledTimes(1)
    expect(current()).toBe(`${base}/configurations/sbt.contoso/effective`)
  })

  it('keeps the editor with Keep editing', async () => {
    const { router, current } = await mountEditor()
    confirm.mockResolvedValue(false)

    await router.push(`${base}/modeling`)

    expect(confirm).toHaveBeenCalledTimes(1)
    expect(current()).toBe(`${base}/annotations/sbt.northwind`)
  })

  it('asks again after a navigation that another guard cancelled after Discard', async () => {
    const { router, current } = await mountEditor()
    confirm.mockResolvedValue(true)
    const cancel = router.beforeEach(() => false)

    await router.push(`${base}/modeling`)
    expect(confirm).toHaveBeenCalledTimes(1)
    expect(current()).toBe(`${base}/annotations/sbt.northwind`)

    cancel()
    await router.push(`${base}/modeling`)
    expect(confirm).toHaveBeenCalledTimes(2)
    expect(current()).toBe(`${base}/modeling/deleted?cascaded=1`)
  })

  it('asks once for a navigation without a redirect', async () => {
    const { router, current } = await mountEditor()
    confirm.mockResolvedValue(true)

    await router.push('/acme/billing/settings')

    expect(confirm).toHaveBeenCalledTimes(1)
    expect(current()).toBe('/acme/billing/settings')
  })
})
