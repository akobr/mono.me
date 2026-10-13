import { describe, expect, it } from 'vitest'
import { safeReturnTo } from '@/utils/return-to'
import { groupMemberships, hasOrganization } from '@/utils/memberships'
import { currentSection, projectTarget, viewTarget } from '@/utils/context-paths'
import { buildNavigation } from '@/utils/navigation'
import { clampDetailWidth, detailMode } from '@/utils/detail-panel'
import { describeError } from '@/utils/errors'
import { StorytellerError } from '@42for.net/2splatform.sdk'

describe('safeReturnTo', () => {
  it.each([
    ['/orgs', '/orgs'],
    ['/acme/billing/default/annotations?q=type:exe#top', '/acme/billing/default/annotations?q=type:exe#top'],
  ])('keeps the same-origin path %s', (value, expected) => {
    expect(safeReturnTo(value, 'https://admin.test')).toBe(expected)
  })

  it.each(['https://evil.test/', '//evil.test/x', '/\\evil.test', 'javascript:alert(1)', 'orgs', 42, undefined])(
    'rejects %s',
    (value) => {
      expect(safeReturnTo(value, 'https://admin.test')).toBeUndefined()
    },
  )
})

describe('memberships', () => {
  const accessMap = {
    'acme.shipping': 'Contributor',
    acme: 'Owner',
    'acme.billing': 'Owner',
    'northwind.portal': 'Reader',
    bogus: 'Superuser',
  }

  it('groups organizations and projects from the AccessMap', () => {
    expect(groupMemberships(accessMap)).toEqual([
      {
        name: 'acme',
        role: 'Owner',
        projects: [
          { org: 'acme', name: 'billing', key: 'acme.billing', role: 'Owner' },
          { org: 'acme', name: 'shipping', key: 'acme.shipping', role: 'Contributor' },
        ],
      },
      { name: 'bogus', role: 'None', projects: [] },
      { name: 'northwind', projects: [{ org: 'northwind', name: 'portal', key: 'northwind.portal', role: 'Reader' }] },
    ])
  })

  it('knows organizations reached through a project only', () => {
    expect(hasOrganization(accessMap, 'northwind')).toBe(true)
    expect(hasOrganization(accessMap, 'north')).toBe(false)
    expect(hasOrganization(undefined, 'acme')).toBe(false)
  })
})

describe('context paths', () => {
  it('detects the section of a path', () => {
    expect(currentSection('/acme/billing/default/configurations/exe.a.b.c')).toEqual({
      kind: 'view',
      name: 'configurations',
    })
    expect(currentSection('/acme/billing/members')).toEqual({ kind: 'project', name: 'members' })
    expect(currentSection('/acme/members')).toEqual({ kind: 'org', name: 'members' })
    expect(currentSection('/orgs')).toEqual({ kind: 'none' })
  })

  it('keeps the section when switching the project or the view', () => {
    expect(projectTarget('/acme/billing/staging/configurations/exe.a.b.c', 'acme', 'shipping')).toBe(
      '/acme/shipping/default/configurations',
    )
    expect(projectTarget('/acme/billing/machines', 'acme', 'shipping')).toBe('/acme/shipping/machines')
    expect(projectTarget('/acme', 'acme', 'shipping')).toBe('/acme/shipping/default/annotations')
    expect(viewTarget('/acme/billing/default/graph/rst.x', 'acme', 'billing', 'staging')).toBe(
      '/acme/billing/staging/graph',
    )
    expect(viewTarget('/acme/billing/members', 'acme', 'billing', 'staging')).toBe('/acme/billing/staging/annotations')
  })
})

describe('navigation', () => {
  it('builds the sections of the current context and locks read-only entries', () => {
    const sections = buildNavigation({
      org: 'acme',
      project: 'billing',
      view: 'default',
      projectRole: 'Reader',
      orgRole: 'None',
    })

    expect(sections.map((section) => section.id)).toEqual(['view', 'project', 'org', 'global'])
    expect(sections[0]!.items[0]).toMatchObject({
      label: 'Annotations',
      to: '/acme/billing/default/annotations',
      locked: true,
    })
  })

  it('shows only the global entries outside an organization', () => {
    expect(buildNavigation({ projectRole: 'None', orgRole: 'None' }).map((section) => section.id)).toEqual(['global'])
  })
})

describe('describeError', () => {
  it('shows message and hint, never details', () => {
    const error = new StorytellerError({
      status: 403,
      message: 'No Administrator access.',
      hint: 'Ask an owner.',
      body: { Error: 'at Foo.Bar()' },
    })

    expect(describeError(error)).toEqual({
      title: 'No access',
      message: 'No Administrator access.',
      hint: 'Ask an owner.',
      status: 403,
    })
    expect(describeError(new Error('boom'), 'Failed')).toEqual({ title: 'Failed', message: 'boom' })
  })
})

describe('detail panel', () => {
  const phone = { medium: false, large: false }
  const tablet = { medium: true, large: false }
  const desktop = { medium: true, large: true }

  it.each([
    [phone, false, false, 'full'],
    [phone, true, false, 'full'],
    [tablet, false, false, 'overlay'],
    [tablet, true, false, 'overlay'],
    [tablet, true, true, 'full'],
    [desktop, false, false, 'overlay'],
    [desktop, true, false, 'pinned'],
    [desktop, false, true, 'full'],
    [desktop, true, true, 'full'],
  ] as const)('opens on %o with pinned %s and full screen %s as %s', (screen, pinned, fullscreen, mode) => {
    expect(detailMode({ ...screen, pinned, fullscreen })).toBe(mode)
  })

  it('keeps the width within 25-70 %', () => {
    expect(clampDetailWidth(10)).toBe(25)
    expect(clampDetailWidth(42.5)).toBe(42.5)
    expect(clampDetailWidth(90)).toBe(70)
    expect(clampDetailWidth(Number.NaN)).toBe(40)
  })
})
