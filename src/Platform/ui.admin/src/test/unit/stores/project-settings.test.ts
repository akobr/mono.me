import { describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { useContextStore } from '@/stores/context'
import { useProjectSettingsStore } from '@/stores/project-settings'

const storageKey = '2s-admin:project-settings'

describe('project settings store', () => {
  it('starts every project in UTC', () => {
    const settings = useProjectSettingsStore()

    expect(settings.settingsOf('acme.billing')).toEqual({ defaultTimeZone: 'UTC' })
    expect(settings.settingsOf(undefined)).toEqual({ defaultTimeZone: 'UTC' })
    expect(settings.defaultTimeZone).toBe('UTC')
  })

  it('keeps the default time zone per project in localStorage', async () => {
    const settings = useProjectSettingsStore()

    settings.update('acme.billing', { defaultTimeZone: 'Europe/Prague' })
    await nextTick()

    expect(settings.settingsOf('acme.billing').defaultTimeZone).toBe('Europe/Prague')
    expect(settings.settingsOf('acme.shipping').defaultTimeZone).toBe('UTC')
    expect(JSON.parse(localStorage.getItem(storageKey)!)).toEqual({
      'acme.billing': { defaultTimeZone: 'Europe/Prague' },
    })
  })

  it('follows the project of the context', () => {
    const context = useContextStore()
    const settings = useProjectSettingsStore()
    settings.update('acme.billing', { defaultTimeZone: 'Asia/Tokyo' })

    context.set({ org: 'acme', project: 'billing', view: 'default' })
    expect(settings.defaultTimeZone).toBe('Asia/Tokyo')

    context.set({ org: 'acme', project: 'shipping', view: 'default' })
    expect(settings.defaultTimeZone).toBe('UTC')
  })

  it('ignores time zones Intl does not know', () => {
    localStorage.setItem(storageKey, JSON.stringify({ 'acme.billing': { defaultTimeZone: 'Mars/Olympus' } }))
    const settings = useProjectSettingsStore()

    expect(settings.settingsOf('acme.billing').defaultTimeZone).toBe('UTC')

    settings.update('acme.shipping', { defaultTimeZone: 'America/New_York' })
    settings.update('acme.shipping', { defaultTimeZone: 'Nowhere/City' })
    expect(settings.settingsOf('acme.shipping').defaultTimeZone).toBe('America/New_York')
  })

  it('resets a project to the defaults', () => {
    const settings = useProjectSettingsStore()
    settings.update('acme.billing', { defaultTimeZone: 'Europe/London' })
    settings.update('acme.shipping', { defaultTimeZone: 'Europe/Prague' })

    settings.reset('acme.billing')

    expect(settings.settingsOf('acme.billing').defaultTimeZone).toBe('UTC')
    expect(settings.settingsOf('acme.shipping').defaultTimeZone).toBe('Europe/Prague')
  })

  it('keeps the defaults when localStorage is blocked', () => {
    const blocked = vi.spyOn(window, 'localStorage', 'get').mockImplementation(() => {
      throw new DOMException('Storage is disabled', 'SecurityError')
    })
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})

    try {
      const settings = useProjectSettingsStore()
      settings.update('acme.billing', { defaultTimeZone: 'Europe/Prague' })

      expect(warn).toHaveBeenCalled()
      expect(settings.settingsOf('acme.billing').defaultTimeZone).toBe('Europe/Prague')
      expect(settings.settingsOf('acme.shipping').defaultTimeZone).toBe('UTC')
    } finally {
      // Before the shared afterEach clears localStorage; restoreMocks would only run after it.
      blocked.mockRestore()
    }
  })
})
