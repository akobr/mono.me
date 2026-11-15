import { describe, expect, it, vi } from 'vitest'
import type { VueWrapper } from '@vue/test-utils'
import TimeZoneSelect from '@/components/annotations/TimeZoneSelect.vue'
import { mountInApp, settle } from '../../helpers'

// The time zones of TimeZoneSelect (spec section 11.1): the zones the browser knows that the catalog of project settings
// lists, the zones the API accepts (424-266), UTC first.

describe('TimeZoneSelect', () => {
  it('offers the zones of the browser that the catalog lists, UTC first', async () => {
    vi.spyOn(Intl, 'supportedValuesOf').mockReturnValue([
      'Europe/Prague',
      '+01:00',
      'Mars/Olympus',
      'Asia/Calcutta',
      'UTC',
    ])
    const view = mountInApp(TimeZoneSelect, { modelValue: 'UTC' })
    await settle()

    const menu = view.host.findComponent({ name: 'SelectMenu' }) as VueWrapper
    const zones = (menu.props() as { items: { value: string }[] }).items.map((item) => item.value)
    expect(zones).toEqual(['UTC', 'Europe/Prague', 'Asia/Calcutta'])

    view.host.unmount()
  })

  it('offers a zone of the catalog the browser does not list while it is the value, so it shows as chosen', async () => {
    vi.spyOn(Intl, 'supportedValuesOf').mockReturnValue(['Europe/Prague', 'UTC'])
    const view = mountInApp(TimeZoneSelect, { modelValue: 'America/Coyhaique' })
    await settle()

    const menu = view.host.findComponent({ name: 'SelectMenu' }) as VueWrapper
    const zones = () => (menu.props() as { items: { value: string }[] }).items.map((item) => item.value)
    expect(zones()).toEqual(['UTC', 'Europe/Prague', 'America/Coyhaique'])
    expect(view.host.text()).toContain('America/Coyhaique')

    // Not a value outside the catalog.
    await view.setProps({ modelValue: 'Mars/Olympus' })
    expect(zones()).toEqual(['UTC', 'Europe/Prague'])

    view.host.unmount()
  })
})
