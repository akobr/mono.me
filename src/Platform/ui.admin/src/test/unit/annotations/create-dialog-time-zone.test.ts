import { describe, expect, it } from 'vitest'
import AnnotationCreateDialog from '@/components/annotations/AnnotationCreateDialog.vue'
import { useProjectSettingsStore } from '@/stores/project-settings'
import { mountScreen, settle } from '../../helpers'

// The time zone of New annotation starts as the project's default (424-12), which arrives from the API (424-266): a
// dialog opened before the answer moves to the project's zone until the user picks one.

async function mountDialog() {
  const view = await mountScreen(
    AnnotationCreateDialog,
    { open: true },
    { accessMap: { 'acme.billing': 'Contributor' } },
  )
  await settle()
  const zone = () => view.host.findComponent({ name: 'TimeZoneSelect' })
  return { ...view, zone }
}

describe('New annotation: the time zone', () => {
  it('follows a late answer of the project settings while the zone is untouched', async () => {
    const { host, zone } = await mountDialog()
    expect(zone().props('modelValue')).toBe('UTC')

    useProjectSettingsStore().receive('acme.billing', { Types: {}, DefaultTimeZone: 'Asia/Tokyo', Version: 1 })
    await settle()
    expect(zone().props('modelValue')).toBe('Asia/Tokyo')

    host.unmount()
  })

  it('keeps a zone the user picked, and starts from the project again when it opens again', async () => {
    const { host, zone, setProps } = await mountDialog()

    zone().vm.$emit('update:modelValue', 'Europe/Prague')
    await settle()
    useProjectSettingsStore().receive('acme.billing', { Types: {}, DefaultTimeZone: 'Asia/Tokyo', Version: 1 })
    await settle()
    expect(zone().props('modelValue')).toBe('Europe/Prague')

    await setProps({ open: false })
    await setProps({ open: true })
    expect(zone().props('modelValue')).toBe('Asia/Tokyo')

    useProjectSettingsStore().receive('acme.billing', { Types: {}, DefaultTimeZone: 'America/New_York', Version: 2 })
    await settle()
    expect(zone().props('modelValue')).toBe('America/New_York')

    host.unmount()
  })
})
