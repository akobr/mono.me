import { describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import type { AuthService } from '@/boot/auth'
import { setBootState } from '@/boot/state'
import { useContextStore } from '@/stores/context'
import { useProjectSettingsStore } from '@/stores/project-settings'
import { useSessionStore } from '@/stores/session'

// The settings of the projects as the API serves them (424-266): received from the settings query, per project.

// A zone of the catalog newer than the browser's Intl (an older Safari or Chrome): isValidTimeZone refuses the zones in
// this set.
const unknownZones = vi.hoisted(() => new Set<string>())
vi.mock('@/forms/annotation.schema', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/forms/annotation.schema')>()
  return { ...original, isValidTimeZone: (zone: string) => !unknownZones.has(zone) && original.isValidTimeZone(zone) }
})

const defaults = { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd', version: 0 }

describe('project settings store', () => {
  it('shows the defaults until the answer of a project arrives', () => {
    const settings = useProjectSettingsStore()

    expect(settings.settingsOf('acme.billing')).toEqual(defaults)
    expect(settings.settingsOf(undefined)).toEqual(defaults)
    expect(settings.loaded('acme.billing')).toBe(false)
    expect(settings.version('acme.billing')).toBe(0)
    expect(settings.defaultTimeZone).toBe('UTC')
    expect(settings.dateFormat).toBe('yyyy-mm-dd')
  })

  it('keeps the answer of each project, with its version, and nothing in localStorage', async () => {
    const settings = useProjectSettingsStore()
    const modified = { At: '2026-11-15T10:00:00Z', By: { Kind: 'Account' as const, Id: 'user_1', Name: 'Ada' } }

    settings.receive('acme.billing', {
      Types: { sbt: { Label: 'Company', Plural: 'Companies', Color: 'pink', Icon: 'i-lucide-building-2' } },
      DefaultTimeZone: 'Europe/Prague',
      DateFormat: 'dd.mm.yyyy',
      Version: 3,
      Modified: modified,
    })
    settings.receive('acme.shipping', { Types: {}, Version: 0 })
    await nextTick()

    expect(settings.settingsOf('acme.billing')).toEqual({
      types: { sbt: { label: 'Company', plural: 'Companies', color: 'pink', icon: 'i-lucide-building-2' } },
      defaultTimeZone: 'Europe/Prague',
      dateFormat: 'dd.mm.yyyy',
      version: 3,
      modified,
      storedTimeZone: 'Europe/Prague',
      storedDateFormat: 'dd.mm.yyyy',
    })
    expect(settings.version('acme.billing')).toBe(3)
    expect(settings.loaded('acme.shipping')).toBe(true)
    expect(settings.settingsOf('acme.shipping')).toEqual(defaults)
    expect(settings.loaded('northwind.portal')).toBe(false)
    expect(localStorage.length).toBe(0)
  })

  it('follows the project of the context', () => {
    const context = useContextStore()
    const settings = useProjectSettingsStore()
    settings.receive('acme.billing', { Types: {}, DefaultTimeZone: 'Asia/Tokyo', DateFormat: 'd mmm yyyy', Version: 1 })

    context.set({ org: 'acme', project: 'billing', view: 'default' })
    expect(settings.defaultTimeZone).toBe('Asia/Tokyo')
    expect(settings.dateFormat).toBe('d mmm yyyy')

    context.set({ org: 'acme', project: 'shipping', view: 'default' })
    expect(settings.defaultTimeZone).toBe('UTC')
    expect(settings.dateFormat).toBe('yyyy-mm-dd')
    expect(settings.current).toEqual(defaults)
  })

  it('takes a newer answer of a project, and keeps its entry for the same answer', () => {
    const settings = useProjectSettingsStore()
    settings.receive('acme.billing', { Types: {}, DateFormat: 'dd/mm/yyyy', Version: 1 })
    const first = settings.settingsOf('acme.billing')

    settings.receive('acme.billing', { Types: {}, DateFormat: 'dd/mm/yyyy', Version: 1 })
    expect(settings.settingsOf('acme.billing')).toBe(first)

    settings.receive('acme.billing', { Types: {}, Version: 2 })
    expect(settings.settingsOf('acme.billing')).toMatchObject({ dateFormat: 'yyyy-mm-dd', version: 2 })
  })

  it('ignores an answer older than the settings it has, such as a slow read after the answer of a write', () => {
    const settings = useProjectSettingsStore()
    settings.receive('acme.billing', { Types: {}, DateFormat: 'dd/mm/yyyy', Version: 5 })
    const latest = settings.settingsOf('acme.billing')

    settings.receive('acme.billing', { Types: {}, DateFormat: 'mm/dd/yyyy', Version: 4 })
    expect(settings.settingsOf('acme.billing')).toBe(latest)

    // an older answer does not count as the source either: the answer of version 5 again changes nothing
    settings.receive('acme.billing', { Types: {}, DateFormat: 'dd/mm/yyyy', Version: 5 })
    expect(settings.settingsOf('acme.billing')).toBe(latest)

    // another answer of the same version is taken, the first answer of another project too
    settings.receive('acme.billing', { Types: {}, DateFormat: 'd mmm yyyy', Version: 5 })
    expect(settings.settingsOf('acme.billing')).toMatchObject({ dateFormat: 'd mmm yyyy', version: 5 })
    settings.receive('acme.shipping', { Types: {}, Version: 0 })
    expect(settings.loaded('acme.shipping')).toBe(true)

    // after clear() the first answer is taken whatever its version
    settings.clear()
    settings.receive('acme.billing', { Types: {}, Version: 1 })
    expect(settings.settingsOf('acme.billing')).toMatchObject({ dateFormat: 'yyyy-mm-dd', version: 1 })
  })

  it('shows only what the UI can: unknown zones, formats, codes, colors and icons fall back', () => {
    const settings = useProjectSettingsStore()

    settings.receive('acme.billing', {
      Types: {
        EXE: { Label: ' Job ', Color: 'red', Icon: 'i-lucide-unknown' },
        sbt: { Plural: 'Companies', Color: 'chartreuse' },
        xyz: { Label: 'Nothing' },
      },
      DefaultTimeZone: 'Mars/Olympus',
      DateFormat: 'yy.m.d',
      Version: -1,
    })

    expect(settings.settingsOf('acme.billing')).toMatchObject({
      types: { exe: { label: 'Job', color: 'red' } },
      defaultTimeZone: 'UTC',
      dateFormat: 'yyyy-mm-dd',
      version: 0,
    })
  })

  it('keeps a zone of the catalog that this browser does not know, and starts new annotations in UTC', () => {
    unknownZones.add('America/Coyhaique')
    try {
      useContextStore().set({ org: 'acme', project: 'billing', view: 'default' })
      const settings = useProjectSettingsStore()
      settings.receive('acme.billing', { Types: {}, DefaultTimeZone: 'America/Coyhaique', Version: 2 })

      expect(settings.current.defaultTimeZone).toBe('America/Coyhaique')
      expect(settings.defaultTimeZone).toBe('UTC')
    } finally {
      unknownZones.clear()
    }
  })

  it('forgets every project on sign-out', async () => {
    const service: AuthService = {
      kind: 'mock',
      getUser: () => ({ id: 'user_1', email: 'ada@example.com', name: 'Ada' }),
      getAccessToken: async () => 'token',
      signIn: vi.fn(async () => {}),
      signOut: vi.fn(async () => {}),
      consumeReturnTo: () => undefined,
    }
    setBootState({ status: 'ready', auth: service })
    const settings = useProjectSettingsStore()
    settings.receive('acme.billing', { Types: { exe: { Label: 'Job' } }, Version: 4 })

    await useSessionStore().signOut()

    expect(settings.loaded('acme.billing')).toBe(false)
    expect(settings.settingsOf('acme.billing')).toEqual(defaults)
  })
})
