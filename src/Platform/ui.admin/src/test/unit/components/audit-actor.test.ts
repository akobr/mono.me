import { describe, expect, it } from 'vitest'
import type { ConfigurationVersion } from '@42for.net/2splatform.sdk'
import AuditActorLink from '@/components/common/AuditActorLink.vue'
import VersionTimeline from '@/components/versions/VersionTimeline.vue'
import { mountScreen, settle } from '../../helpers'

describe('audit actors', () => {
  it('name the author of every version from its stamp', async () => {
    const versions: ConfigurationVersion[] = [
      { Version: 1, Modified: { At: '2026-10-01T08:00:00Z', By: { Kind: 'Machine', Id: 'mach-sync' } } },
      {
        Version: 2,
        Modified: { At: '2026-10-02T08:00:00Z', By: { Kind: 'Account', Id: 'user_01MOCKADA', Name: 'Ada Lovelace' } },
      },
      { Version: 3, Modified: { At: '2026-10-03T08:00:00Z', By: { Kind: 'System', Id: 'system' } } },
    ]

    const screen = await mountScreen(VersionTimeline, { versions, current: 3 })
    await settle()
    const rows = screen.host.findAll('li').map((row) => row.text())

    expect(rows[0]).toContain('System')
    expect(rows[1]).toContain('Ada Lovelace')
    expect(rows[2]).toContain('machine:mach-sync')
    screen.host.unmount()
  })

  it('link an account to the members for an Administrator, muted when subtle', async () => {
    const actor = { Kind: 'Account' as const, Id: 'user_01MOCKADA', Name: 'Ada Lovelace' }

    const screen = await mountScreen(AuditActorLink, { actor, subtle: true })
    await settle()
    const link = screen.host.find('a')

    expect(link.text()).toBe('Ada Lovelace')
    expect(link.attributes('href')).toBe('/acme/billing/members?highlight=user_01MOCKADA')
    expect(link.classes()).toContain('text-muted')
    expect(link.classes()).not.toContain('text-highlighted')
    screen.host.unmount()
  })

  it('keep the name out of PostHog clicks, as a link and as text', async () => {
    const name = (screen: Awaited<ReturnType<typeof mountScreen>>) =>
      screen.host.findAll('a, span').find((element) => element.text() === 'Ada Lovelace')
    const actor = { Kind: 'Account' as const, Id: 'user_01MOCKADA', Name: 'Ada Lovelace' }

    const admin = await mountScreen(AuditActorLink, { actor })
    await settle()
    expect(name(admin)?.element.tagName).toBe('A')
    expect(name(admin)?.classes()).toContain('ph-no-capture')
    admin.host.unmount()

    const reader = await mountScreen(AuditActorLink, { actor }, { accessMap: { 'acme.billing': 'Reader' } })
    await settle()
    expect(reader.host.find('a').exists()).toBe(false)
    expect(name(reader)?.classes()).toContain('ph-no-capture')
    reader.host.unmount()
  })
})
