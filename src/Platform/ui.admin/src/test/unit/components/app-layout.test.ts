import { afterEach, describe, expect, it } from 'vitest'
import { h, type SetupContext } from 'vue'
import { mount, type VueWrapper } from '@vue/test-utils'
import ui from '@nuxt/ui/vue-plugin'
import { createMemoryHistory, createRouter } from 'vue-router'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import App from '@/App.vue'
import { settle } from '../../helpers'

// The layout of a route (424-267): `wide` of its meta reaches the bare layout as a prop, and only the bare layout: the
// dashboard shell has no column to widen.

/** A layout that shows what App.vue gave it. */
const layout = (name: string) => ({
  name,
  inheritAttrs: false,
  setup:
    (_: unknown, { attrs, slots }: SetupContext) =>
    () =>
      h('div', { 'data-layout': name, 'data-given': JSON.stringify(attrs) }, slots.default?.()),
})

const page = { render: () => h('p', 'The page') }
let app: VueWrapper | undefined

afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.innerHTML = ''
})

async function mountApp(path: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/onboarding', component: page, meta: { layout: 'bare', wide: true } },
      { path: '/login', component: page, meta: { layout: 'bare' } },
      { path: '/orgs', component: page, meta: { wide: true } },
    ],
  })
  await router.push(path)
  await router.isReady()
  app = mount(App, {
    attachTo: document.body,
    global: {
      // The query devtools of a development build, which App.vue loads, need a query client.
      plugins: [ui, router, [VueQueryPlugin, { queryClient: new QueryClient() }]],
      stubs: { BareLayout: layout('bare'), DefaultLayout: layout('default'), ApiErrorToaster: true },
    },
  })
  await settle()
  const shown = () => {
    const element = document.body.querySelector<HTMLElement>('[data-layout]')!
    return { layout: element.dataset.layout, given: JSON.parse(element.dataset.given!), text: element.textContent }
  }
  return { router, shown }
}

describe('the layout of a route', () => {
  it('gives the bare layout the wide column of the route', async () => {
    const { router, shown } = await mountApp('/onboarding')
    expect(shown()).toEqual({ layout: 'bare', given: { wide: true }, text: 'The page' })

    await router.push('/login')
    await settle()
    expect(shown()).toEqual({ layout: 'bare', given: { wide: false }, text: 'The page' })
  })

  it('gives the dashboard shell nothing, whatever the route says', async () => {
    const { shown } = await mountApp('/orgs')

    expect(shown()).toEqual({ layout: 'default', given: {}, text: 'The page' })
  })
})
