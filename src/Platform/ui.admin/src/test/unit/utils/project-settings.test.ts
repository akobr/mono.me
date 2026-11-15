import { describe, expect, it } from 'vitest'
import { validateProjectSettings } from '@42for.net/2splatform.sdk'
import { defaultProjectSettings, fromApi, toApi } from '@/utils/project-settings'

// The mapping between the API's project settings (PascalCase) and the UI's (424-266, spec section 1).

describe('project settings mapping', () => {
  it('reads the defaults of a project that has none', () => {
    expect(fromApi({ Types: {}, Version: 0 })).toEqual(defaultProjectSettings)
    expect(fromApi(undefined)).toEqual(defaultProjectSettings)
  })

  it('maps the types, the zone, the format, the version and the stamp', () => {
    const modified = { At: '2026-11-15T10:00:00+00:00', By: { Kind: 'Account' as const, Id: 'user-1', Name: 'Ada' } }

    expect(
      fromApi({
        Types: {
          sbt: { Label: 'Company', Plural: 'Companies', Color: 'pink', Icon: 'i-lucide-building-2' },
          rst: { Label: 'Solution', Color: 'green' },
        },
        DefaultTimeZone: 'Europe/Prague',
        DateFormat: 'dd.mm.yyyy',
        Version: 3,
        Modified: modified,
      }),
    ).toEqual({
      types: {
        sbt: { label: 'Company', plural: 'Companies', color: 'pink', icon: 'i-lucide-building-2' },
        rst: { label: 'Solution', color: 'green' },
      },
      defaultTimeZone: 'Europe/Prague',
      dateFormat: 'dd.mm.yyyy',
      version: 3,
      modified,
      storedTimeZone: 'Europe/Prague',
      storedDateFormat: 'dd.mm.yyyy',
    })
  })

  it('writes the types in the order of the codes, without empty members, and the zone and format when set', () => {
    const body = toApi({
      types: { exe: { label: 'Job', icon: 'i-lucide-rocket' }, rst: { color: 'green' } },
      defaultTimeZone: 'Asia/Tokyo',
      dateFormat: 'd mmm yyyy',
    })

    expect(body).toEqual({
      Types: { rst: { Color: 'green' }, exe: { Label: 'Job', Icon: 'i-lucide-rocket' } },
      DefaultTimeZone: 'Asia/Tokyo',
      DateFormat: 'd mmm yyyy',
    })
    expect(Object.keys(body.Types!)).toEqual(['rst', 'exe'])
  })

  it('sends only what the API takes: no plural without a name, no unknown color', () => {
    const body = toApi({
      types: { sbt: { plural: 'Companies', color: 'chartreuse' as never }, exe: { label: '  Job  ', plural: 'Jobs' } },
      defaultTimeZone: 'UTC',
      dateFormat: 'yyyy-mm-dd',
    })

    expect(body).toEqual({ Types: { exe: { Label: 'Job', Plural: 'Jobs' } } })
    expect(validateProjectSettings(body).valid).toBe(true)
  })

  it('leaves out a default the server does not store, and keeps one it stores', () => {
    const untouched = fromApi({ Types: {}, Version: 2 })
    expect(toApi(untouched, untouched)).toEqual({ Types: {} })

    const explicit = fromApi({ Types: {}, DefaultTimeZone: 'UTC', DateFormat: 'yyyy-mm-dd', Version: 2 })
    expect(toApi(explicit, explicit)).toEqual({ Types: {}, DefaultTimeZone: 'UTC', DateFormat: 'yyyy-mm-dd' })

    // back to the default: sent as unset
    const set = fromApi({ Types: {}, DefaultTimeZone: 'Europe/Oslo', DateFormat: 'dd.mm.yyyy', Version: 2 })
    expect(toApi({ ...set, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' }, set)).toEqual({ Types: {} })
  })

  it('reads its own body back as the same settings', () => {
    const settings = fromApi({
      Types: { usg: { Label: 'Contract', Color: 'amber', Icon: 'i-lucide-file' } },
      DefaultTimeZone: 'America/New_York',
      Version: 1,
    })

    expect(fromApi({ ...toApi(settings, settings), Version: 1 })).toEqual(settings)
  })
})
