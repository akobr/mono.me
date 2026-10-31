import { afterEach, describe, expect, it } from 'vitest'
import type { VueWrapper } from '@vue/test-utils'
import AnnotationCreateActions from '@/components/annotations/AnnotationCreateActions.vue'
import { useAnnotationBulkCreate } from '@/composables/useAnnotationBulkCreate'
import { useAnnotationCreate } from '@/composables/useAnnotationCreate'
import { mountScreen } from '../../helpers'

// New annotation and Bulk create, shared by the Annotations and Graph pages (424-130).
const mountActions = (variant: 'header' | 'empty', role = 'Contributor') =>
  mountScreen(AnnotationCreateActions, { variant }, { accessMap: { 'acme.billing': role } })

/** The accessible names of the buttons: the aria-label, or the label. */
const names = (host: VueWrapper) =>
  host.findAll('button').map((button) => button.attributes('aria-label') ?? button.text())

afterEach(() => {
  useAnnotationCreate().open.value = false
  useAnnotationBulkCreate().open.value = false
})

describe('create actions', () => {
  it('offers Bulk create, New and the icon button New annotation in the top bar', async () => {
    const { host } = await mountActions('header')
    expect(names(host)).toEqual(['Bulk create', 'New', 'New annotation'])
    host.unmount()
  })

  it('offers New annotation and Bulk create in the empty state', async () => {
    const { host } = await mountActions('empty')
    expect(names(host)).toEqual(['New annotation', 'Bulk create'])
    host.unmount()
  })

  it('opens the dialogs through their shared state', async () => {
    const { host } = await mountActions('header')
    await host.find('button[aria-label="New annotation"]').trigger('click')
    expect(useAnnotationCreate().open.value).toBe(true)
    await host.find('button[aria-label="Bulk create"]').trigger('click')
    expect(useAnnotationBulkCreate().open.value).toBe(true)
    host.unmount()

    useAnnotationCreate().open.value = false
    useAnnotationBulkCreate().open.value = false
    const empty = await mountActions('empty')
    const [create, bulk] = empty.host.findAll('button')
    await create!.trigger('click')
    expect(useAnnotationCreate().open.value).toBe(true)
    await bulk!.trigger('click')
    expect(useAnnotationBulkCreate().open.value).toBe(true)
    empty.host.unmount()
  })

  it('shows nothing to a Reader', async () => {
    for (const variant of ['header', 'empty'] as const) {
      const { host } = await mountActions(variant, 'Reader')
      expect(host.findAll('button')).toHaveLength(0)
      host.unmount()
    }
  })
})
