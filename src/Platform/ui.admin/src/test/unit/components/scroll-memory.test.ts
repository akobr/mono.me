import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref, type Ref } from 'vue'
import { mount } from '@vue/test-utils'
import { useScrollMemory } from '@/composables/useScrollMemory'
import { settle } from '../../helpers'

/** Stands in for the browser's ResizeObserver: `resize()` reports a size change of the observed element. */
function fakeResizeObserver() {
  let notify: (() => void) | undefined
  vi.stubGlobal(
    'ResizeObserver',
    class {
      constructor(callback: () => void) {
        notify = callback
      }
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
  return { resize: () => notify?.() }
}

function mountList(offset: Ref<number>, ready: () => boolean, style = 'height: 100px; overflow: auto') {
  const List = defineComponent({
    setup() {
      const element = ref<HTMLElement>()
      useScrollMemory(element, offset, ready)
      return () => h('div', { ref: element, style }, [h('div')])
    },
  })
  return mount(List, { attachTo: document.body })
}

const scrollTo = (element: HTMLElement, top: number) => {
  element.scrollTop = top
  element.dispatchEvent(new Event('scroll'))
}

afterEach(() => vi.unstubAllGlobals())

describe('scroll memory', () => {
  it('restores the offset once the list has rows and records every scroll after that', async () => {
    const offset = ref(120)
    const ready = ref(false)
    const wrapper = mountList(offset, () => ready.value)
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

  it('neither records the reset offset of a list hidden by a covering detail nor keeps it once the list shows', async () => {
    const observer = fakeResizeObserver()
    const offset = ref(0)
    const wrapper = mountList(offset, () => true)
    const element = wrapper.element as HTMLElement
    await settle()
    scrollTo(element, 300)
    expect(offset.value).toBe(300)

    // The detail covers the list: `display: none` drops its offset, which must not reach the memory.
    element.style.display = 'none'
    observer.resize()
    scrollTo(element, 0)
    expect(offset.value).toBe(300)

    // Shown again, the browser reports the reset offset before the size change; the offset comes back.
    element.style.display = ''
    scrollTo(element, 0)
    expect(offset.value).toBe(300)
    observer.resize()
    await settle()
    expect(element.scrollTop).toBe(300)

    scrollTo(element, 450)
    expect(offset.value).toBe(450)
    wrapper.unmount()
  })

  it('puts the offset back when a list opened behind a covering detail shows', async () => {
    const observer = fakeResizeObserver()
    const offset = ref(200)
    const wrapper = mountList(offset, () => true, 'display: none; height: 100px; overflow: auto')
    const element = wrapper.element as HTMLElement
    await settle()
    expect(element.scrollTop).toBe(0)

    element.style.display = ''
    scrollTo(element, 0)
    expect(offset.value).toBe(200)
    observer.resize()
    await settle()
    expect(element.scrollTop).toBe(200)
    wrapper.unmount()
  })
})
