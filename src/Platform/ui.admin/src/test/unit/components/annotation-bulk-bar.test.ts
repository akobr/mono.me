import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount, type VueWrapper } from '@vue/test-utils'
import ui from '@nuxt/ui/vue-plugin'
import UApp from '@nuxt/ui/components/App.vue'
import type { AnnotationDeletionResult } from '@42for.net/2splatform.sdk'
import AnnotationBulkBar from '@/components/annotations/AnnotationBulkBar.vue'
import ConfirmDialog from '@/components/common/ConfirmDialog.vue'
import { settle } from '../../helpers'

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

vi.mock('@/composables/useAnnotationActions', () => ({
  useAnnotationActions: () => ({ removeMany, progress: ref(undefined) }),
}))
vi.mock('@/composables/useDeletionToast', () => ({ useDeletionToast: () => notifyDeleted }))

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
