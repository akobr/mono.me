import { describe, expect, it } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { useScrollMemory } from '@/composables/useScrollMemory'
import { settle } from '../../helpers'

describe('scroll memory', () => {
  it('restores the offset once the list has rows and records every scroll after that', async () => {
    const offset = ref(120)
    const ready = ref(false)
    const List = defineComponent({
      setup() {
        const element = ref<HTMLElement>()
        useScrollMemory(element, offset, () => ready.value)
        return () => h('div', { ref: element, style: 'height: 100px; overflow: auto' }, [h('div')])
      },
    })

    const wrapper = mount(List, { attachTo: document.body })
    const element = wrapper.element as HTMLElement
    await settle()

    // While loading, a scroll of the empty list does not overwrite the memory.
    element.dispatchEvent(new Event('scroll'))
    expect(element.scrollTop).toBe(0)
    expect(offset.value).toBe(120)

    ready.value = true
    await settle()
    expect(element.scrollTop).toBe(120)

    element.scrollTop = 300
    element.dispatchEvent(new Event('scroll'))
    expect(offset.value).toBe(300)
    wrapper.unmount()
  })
})
