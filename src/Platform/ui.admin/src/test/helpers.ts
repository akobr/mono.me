/* eslint-disable vue/one-component-per-file -- test helpers define small wrapper components */
import { defineComponent, h, nextTick, type Component } from 'vue'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ui from '@nuxt/ui/vue-plugin'
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
    global: { plugins: [ui, ...(plugins as [])], stubs: { CodeEditor: CodeEditorStub } },
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
