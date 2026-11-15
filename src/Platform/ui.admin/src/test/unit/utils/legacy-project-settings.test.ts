import { describe, expect, it, vi } from 'vitest'
import {
  legacyProjectSettingsDraft,
  legacyProjectSettingsKey,
  readLegacyProjectSettings,
  removeLegacyProjectSettings,
} from '@/utils/legacy-project-settings'

// The project settings the admin UI kept in each browser before the API stored them (424-266, spec section 4): read
// sanitized, only for the offer to save them for the project, and removed.

const store = (entries: unknown) => localStorage.setItem(legacyProjectSettingsKey, JSON.stringify(entries))
const stored = () => JSON.parse(localStorage.getItem(legacyProjectSettingsKey) ?? 'null') as unknown

describe('the project settings of this browser', () => {
  it('reads the entry of a project sanitized', () => {
    store({
      'acme.billing': {
        types: { exe: { label: '  Job  ', color: 'red', icon: 'i-lucide-unknown' }, xyz: { label: 'Nothing' } },
        defaultTimeZone: 'Asia/Tokyo',
        dateFormat: 'dd.mm.yyyy',
      },
      'acme.shipping': { dateFormat: 'mm/dd/yyyy' },
    })

    expect(readLegacyProjectSettings('acme.billing')).toEqual({
      types: { exe: { label: 'Job', color: 'red' } },
      defaultTimeZone: 'Asia/Tokyo',
      dateFormat: 'dd.mm.yyyy',
    })
    expect(readLegacyProjectSettings('acme.shipping')).toEqual({ dateFormat: 'mm/dd/yyyy' })
    expect(readLegacyProjectSettings('northwind.portal')).toBeUndefined()
  })

  it('finds nothing to save in an entry of defaults, unknown values or the wrong shape', () => {
    store({
      'acme.billing': { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' },
      'acme.shipping': { types: { exe: { color: 'mauve' } }, defaultTimeZone: '+01:00', dateFormat: 'yy/m/d' },
      'acme.returns': 'Asia/Tokyo',
    })

    expect(readLegacyProjectSettings('acme.billing')).toBeUndefined()
    expect(readLegacyProjectSettings('acme.shipping')).toBeUndefined()
    expect(readLegacyProjectSettings('acme.returns')).toBeUndefined()
  })

  it('reads nothing from a key that is not JSON or not an object, or from blocked storage', () => {
    localStorage.setItem(legacyProjectSettingsKey, '{ "acme.billing": ')
    expect(readLegacyProjectSettings('acme.billing')).toBeUndefined()
    localStorage.setItem(legacyProjectSettingsKey, '["acme.billing"]')
    expect(readLegacyProjectSettings('acme.billing')).toBeUndefined()

    const blocked = {
      getItem: () => {
        throw new DOMException('Blocked', 'SecurityError')
      },
    } as unknown as Storage
    expect(readLegacyProjectSettings('acme.billing', blocked)).toBeUndefined()
    expect(() => removeLegacyProjectSettings('acme.billing', blocked)).not.toThrow()
  })

  it('removes the entry of a project, and the key with the last one', () => {
    store({ 'acme.billing': { dateFormat: 'dd.mm.yyyy' }, 'acme.shipping': { dateFormat: 'mm/dd/yyyy' } })

    removeLegacyProjectSettings('northwind.portal')
    expect(stored()).toEqual({
      'acme.billing': { dateFormat: 'dd.mm.yyyy' },
      'acme.shipping': { dateFormat: 'mm/dd/yyyy' },
    })

    removeLegacyProjectSettings('acme.billing')
    expect(stored()).toEqual({ 'acme.shipping': { dateFormat: 'mm/dd/yyyy' } })

    removeLegacyProjectSettings('acme.shipping')
    expect(localStorage.getItem(legacyProjectSettingsKey)).toBeNull()
  })

  it('writes nothing without an entry to remove', () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem')
    const removeItem = vi.spyOn(Storage.prototype, 'removeItem')

    removeLegacyProjectSettings('acme.billing')
    expect(setItem).not.toHaveBeenCalled()
    expect(removeItem).not.toHaveBeenCalled()
    expect(readLegacyProjectSettings('acme.billing')).toBeUndefined()
    expect(localStorage.getItem(legacyProjectSettingsKey)).toBeNull()
  })

  it('makes a draft of the old values with the defaults for the rest', () => {
    expect(legacyProjectSettingsDraft({ dateFormat: 'dd.mm.yyyy' })).toEqual({
      types: {},
      defaultTimeZone: 'UTC',
      dateFormat: 'dd.mm.yyyy',
    })
    expect(legacyProjectSettingsDraft({ types: { exe: { label: 'Job' } }, defaultTimeZone: 'Asia/Tokyo' })).toEqual({
      types: { exe: { label: 'Job' } },
      defaultTimeZone: 'Asia/Tokyo',
      dateFormat: 'yyyy-mm-dd',
    })
  })
})
