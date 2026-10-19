/* eslint-disable vue/one-component-per-file -- test helpers define small wrapper components */
import { defineComponent, h, nextTick, type Component } from 'vue'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ui from '@nuxt/ui/vue-plugin'
import { createMemoryHistory, createRouter, type Router, type RouteRecordRaw } from 'vue-router'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import type { AccountAccessMap } from '@42for.net/2splatform.sdk'
import { useContextStore } from '@/stores/context'
import { useSessionStore } from '@/stores/session'
import UApp from '@nuxt/ui/components/App.vue'

// CodeMirror needs layout APIs happy-dom does not have; a textarea stands in for the text editor.
export const CodeEditorStub = defineComponent({
  name: 'CodeEditor',
  props: { modelValue: { type: String, required: true }, language: { type: String, required: true } },
  emits: ['update:modelValue', 'save'],
  setup(props, { emit }) {
    return () =>
      h('textarea', {
        'data-language': props.language,
        value: props.modelValue,
        onInput: (event: Event) => emit('update:modelValue', (event.target as HTMLTextAreaElement).value),
      })
  },
})

/** MergeView needs layout APIs too; the stub shows both sides as text. */
export const DiffEditorStub = defineComponent({
  name: 'DiffEditor',
  props: { original: { type: String, default: '' }, modified: { type: String, default: '' } },
  setup(props) {
    return () =>
      h(
        'pre',
        { 'data-diff': 'true' },
        `${props.original}
---
${props.modified}`,
      )
  },
})

/**
 * Mounts a component inside <UApp> (tooltip and toast providers) with the Nuxt UI plugin. Props are reactive
 * through `setProps` on the returned wrapper of the inner component.
 */
export function mountInApp<T extends Component>(
  component: T,
  props: Record<string, unknown> = {},
  plugins: unknown[] = [],
) {
  const Host = defineComponent({
    setup(_, { attrs }) {
      return () => h(UApp, null, { default: () => h(component as Component, attrs) })
    },
  })

  const host = mount(Host, {
    attrs: props,
    attachTo: document.body,
    global: { plugins: [ui, ...(plugins as [])], stubs: { CodeEditor: CodeEditorStub, DiffEditor: DiffEditorStub } },
  })

  return {
    host,
    inner: () => host.findComponent(component as never) as VueWrapper,
    setProps: async (next: Record<string, unknown>) => {
      await host.setProps(next)
      await settle()
    },
  }
}

export async function settle(rounds = 4): Promise<void> {
  for (let index = 0; index < rounds; index++) {
    await flushPromises()
    await nextTick()
  }
}

export interface AppMountOptions {
  path?: string
  scope?: { org: string; project: string; view: string }
  accessMap?: Record<string, string>
  /** The routes of the router; one catch-all route (no params) by default. */
  routes?: RouteRecordRaw[]
}

/**
 * Mounts a screen component with everything it expects: a memory router at `path`, Vue Query (no retries), the
 * context store set to `scope` and an account with `accessMap`.
 */
export async function mountScreen<T extends Component>(
  component: T,
  props: Record<string, unknown>,
  options: AppMountOptions = {},
) {
  const scope = options.scope ?? { org: 'acme', project: 'billing', view: 'default' }
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: options.routes ?? [{ path: '/:pathMatch(.*)*', component: { render: () => null } }],
  })
  await router.push(options.path ?? `/${scope.org}/${scope.project}/${scope.view}`)
  await router.isReady()

  useContextStore().set(scope)
  useSessionStore().setAccount({
    Id: 'user_1',
    AccessMap: (options.accessMap ?? { [`${scope.org}.${scope.project}`]: 'Owner' }) as AccountAccessMap,
  })

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const view = mountInApp(component, props, [router, [VueQueryPlugin, { queryClient }]])
  return { ...view, router, queryClient }
}
