import { describe, expect, it } from 'vitest'
import { safeReturnTo } from '@/utils/return-to'
import { groupMemberships, hasOrganization } from '@/utils/memberships'
import { currentSection, projectTarget, viewLocation, viewTarget } from '@/utils/context-paths'
import { accessibleContext, buildNavigation, mergeContext, navigationScope } from '@/utils/navigation'
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
    expect(currentSection('/acme/billing/monitor')).toEqual({ kind: 'project', name: 'monitor' })
    expect(projectTarget('/acme/billing/monitor', 'acme', 'shipping')).toBe('/acme/shipping/monitor')
    expect(projectTarget('/acme', 'acme', 'shipping')).toBe('/acme/shipping/default/annotations')
    expect(viewTarget('/acme/billing/default/graph/rst.x', 'acme', 'billing', 'staging')).toBe(
      '/acme/billing/staging/graph',
    )
    expect(viewTarget('/acme/billing/members', 'acme', 'billing', 'staging')).toBe('/acme/billing/staging/annotations')
  })

  // monitor is not a reserved view name; the Monitor page has no pages below it (424-35).
  it('tells the Monitor page from a view named monitor', () => {
    expect(currentSection('/acme/billing/monitor/graph/rst.x')).toEqual({ kind: 'view', name: 'graph' })
    expect(viewLocation('/acme/billing/monitor/annotations')).toEqual({
      scope: { org: 'acme', project: 'billing', view: 'monitor' },
      section: 'annotations',
      root: true,
    })
    expect(projectTarget('/acme/billing/monitor/graph/rst.x', 'acme', 'shipping')).toBe('/acme/shipping/default/graph')
    expect(viewTarget('/acme/billing/monitor/graph', 'acme', 'billing', 'default')).toBe('/acme/billing/default/graph')
    expect(currentSection('/acme/billing/machines/graph')).toEqual({ kind: 'project', name: 'machines' })
  })

  // Schemas, Templates, Export & Import, Deleted and Compare views are tabs of Modeling, each with its path (424-38).
  it('keeps the Modeling tab, without the detail, when switching the project or the view', () => {
    expect(currentSection('/acme/billing/default/modeling/templates')).toEqual({ kind: 'view', name: 'modeling' })
    expect(viewTarget('/acme/billing/default/modeling/templates', 'acme', 'billing', 'staging')).toBe(
      '/acme/billing/staging/modeling/templates',
    )
    expect(projectTarget('/acme/billing/staging/modeling/templates', 'acme', 'shipping')).toBe(
      '/acme/shipping/default/modeling/templates',
    )

    expect(currentSection('/acme/billing/default/modeling/schemas/type/exe')).toEqual({
      kind: 'view',
      name: 'modeling',
    })
    expect(viewTarget('/acme/billing/default/modeling/schemas/type/exe', 'acme', 'billing', 'staging')).toBe(
      '/acme/billing/staging/modeling/schemas',
    )
    expect(projectTarget('/acme/billing/staging/modeling/schemas/annotation/sbt.a/type/exe', 'acme', 'shipping')).toBe(
      '/acme/shipping/default/modeling/schemas',
    )
    // Deleted stays on Deleted instead of going to Annotations
    expect(viewTarget('/acme/billing/default/modeling/deleted', 'acme', 'billing', 'staging')).toBe(
      '/acme/billing/staging/modeling/deleted',
    )
  })

  it('opens Modeling on its first tab when the path names no tab', () => {
    expect(currentSection('/acme/billing/default/modeling')).toEqual({ kind: 'view', name: 'modeling' })
    expect(viewTarget('/acme/billing/default/modeling', 'acme', 'billing', 'staging')).toBe(
      '/acme/billing/staging/modeling',
    )
    expect(projectTarget('/acme/billing/default/modeling', 'acme', 'shipping')).toBe('/acme/shipping/default/modeling')
    expect(viewTarget('/acme/billing/default/modeling/foo', 'acme', 'billing', 'staging')).toBe(
      '/acme/billing/staging/modeling',
    )
  })

  // modeling is not a reserved view name: the Modeling page is the fourth segment, below the view (424-38).
  it('tells the Modeling page from a view named modeling', () => {
    expect(currentSection('/acme/billing/modeling/annotations')).toEqual({ kind: 'view', name: 'annotations' })
    expect(viewLocation('/acme/billing/modeling/graph/rst.x')).toEqual({
      scope: { org: 'acme', project: 'billing', view: 'modeling' },
      section: 'graph',
      root: false,
    })
    expect(currentSection('/acme/billing/modeling/modeling/schemas')).toEqual({ kind: 'view', name: 'modeling' })
    expect(viewTarget('/acme/billing/modeling/modeling/templates/exe', 'acme', 'billing', 'default')).toBe(
      '/acme/billing/default/modeling/templates',
    )
    expect(viewTarget('/acme/billing/default/modeling/compare', 'acme', 'billing', 'modeling')).toBe(
      '/acme/billing/modeling/modeling/compare',
    )
    expect(viewTarget('/acme/billing/modeling/graph/rst.x', 'acme', 'billing', 'default')).toBe(
      '/acme/billing/default/graph',
    )
    expect(projectTarget('/acme/billing/modeling/modeling/export-import', 'acme', 'shipping')).toBe(
      '/acme/shipping/default/modeling/export-import',
    )
    expect(projectTarget('/acme/billing/modeling/annotations', 'acme', 'shipping')).toBe(
      '/acme/shipping/default/annotations',
    )
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
    expect(sections.map((section) => section.label)).toEqual(['View · default', 'Project', 'Organization', undefined])
    expect(sections.map((section) => section.defaultView)).toEqual([true, false, false, false])
    expect(sections[0]!.items[0]).toMatchObject({
      label: 'Annotations',
      to: '/acme/billing/default/annotations',
      locked: true,
    })
  })

  it('names the selected view and marks only the default one', () => {
    const [section] = buildNavigation({
      org: 'acme',
      project: 'billing',
      view: 'staging',
      projectRole: 'Contributor',
      orgRole: 'None',
    })

    expect(section).toMatchObject({ id: 'view', label: 'View · staging', defaultView: false })
    expect(section!.items[0]).toMatchObject({ to: '/acme/billing/staging/annotations', locked: false })
  })

  it('marks the default view on a project page without a remembered view', () => {
    const scope = navigationScope({ org: 'acme', project: 'shipping' }, undefined, { 'acme.shipping': 'Contributor' })
    const [section] = buildNavigation({ ...scope, projectRole: 'Contributor', orgRole: 'None' })

    expect(section).toMatchObject({ id: 'view', label: 'View · default', defaultView: true })
  })

  it('shows only the global entries outside an organization', () => {
    expect(buildNavigation({ projectRole: 'None', orgRole: 'None' }).map((section) => section.id)).toEqual(['global'])
  })
})

describe('navigation context', () => {
  const remembered = { org: 'acme', project: 'billing', view: 'staging' }
  const accessMap = { 'acme.billing': 'Reader', 'acme.shipping': 'Contributor', globex: 'Owner' }

  it.each([
    ['a project page of the same project', { org: 'acme', project: 'billing' }, remembered],
    ['an organization page of the same organization', { org: 'acme' }, remembered],
    ['a page without an organization', {}, remembered],
    ['another view', { org: 'acme', project: 'billing', view: 'default' }, { ...remembered, view: 'default' }],
    ['another project', { org: 'acme', project: 'shipping' }, { org: 'acme', project: 'shipping' }],
    ['another organization', { org: 'globex' }, { org: 'globex' }],
  ])('merges the remembered context into %s', (_, current, expected) => {
    expect(mergeContext(current, remembered)).toEqual(expected)
  })

  it('starts from the current context when nothing is remembered', () => {
    expect(mergeContext({ org: 'acme', project: 'billing' }, undefined)).toEqual({ org: 'acme', project: 'billing' })
    expect(mergeContext({}, undefined)).toEqual({})
  })

  it('drops the remembered organization or project the caller is no longer a member of', () => {
    expect(accessibleContext(remembered, accessMap)).toEqual(remembered)
    expect(accessibleContext(remembered, { 'acme.shipping': 'Reader' })).toEqual({ org: 'acme' })
    expect(accessibleContext(remembered, { 'acme.billing': 'None', acme: 'Reader' })).toEqual({ org: 'acme' })
    expect(accessibleContext(remembered, { globex: 'Owner' })).toBeUndefined()
    expect(accessibleContext(undefined, accessMap)).toBeUndefined()
  })

  it('keeps the remembered view and project on pages of a less specific context', () => {
    expect(navigationScope({ org: 'acme', project: 'billing' }, remembered, accessMap)).toEqual(remembered)
    expect(navigationScope({ org: 'acme' }, remembered, accessMap)).toEqual(remembered)
    expect(navigationScope({}, remembered, accessMap)).toEqual(remembered)
    expect(navigationScope({}, remembered, { globex: 'Owner' })).toEqual({})
  })

  it('shows the default view of a project without a remembered view', () => {
    expect(navigationScope({ org: 'acme', project: 'shipping' }, remembered, accessMap)).toEqual({
      org: 'acme',
      project: 'shipping',
      view: 'default',
    })
    expect(navigationScope({}, { org: 'acme', project: 'billing' }, accessMap)).toEqual({
      org: 'acme',
      project: 'billing',
      view: 'default',
    })
    expect(navigationScope({ org: 'globex' }, remembered, accessMap)).toEqual({ org: 'globex' })
  })

  it('always keeps the parts of the route, also without access', () => {
    expect(navigationScope({ org: 'northwind', project: 'portal' }, remembered, accessMap)).toEqual({
      org: 'northwind',
      project: 'portal',
      view: 'default',
    })
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
