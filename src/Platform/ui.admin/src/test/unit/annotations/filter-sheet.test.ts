import { describe, expect, it } from 'vitest'
import AnnotationFilterSheet from '@/components/annotations/AnnotationFilterSheet.vue'
import { mountInApp, settle } from '../../helpers'

describe('AnnotationFilterSheet', () => {
  it('keeps the active terms, whose labels are the filter text, out of PostHog clicks', async () => {
    const view = mountInApp(AnnotationFilterSheet, { query: 'subject:northwind label:falcon' })
    await view.host.find('button[aria-label="Filters"]').trigger('click')
    await settle()

    const buttons = [...document.body.querySelectorAll<HTMLButtonElement>('button')]
    const term = buttons.find((button) => button.textContent?.trim() === 'subject:northwind')
    const clearAll = buttons.find((button) => button.textContent?.trim() === 'Clear all')

    expect(term?.closest('.ph-no-capture')).toBeInstanceOf(HTMLElement)
    // The fixed labels around them are still counted.
    expect(clearAll).toBeInstanceOf(HTMLButtonElement)
    expect(clearAll?.closest('.ph-no-capture')).toBeNull()
    view.host.unmount()
  })
})
