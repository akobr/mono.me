import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setupServer } from 'msw/node'
import {
  getAnnotationHistory,
  getConfigurationSchemas,
  getConfigurations,
  getDeletedAnnotations,
  getTemplates,
  getViews,
  restoreAnnotations,
  roleRank,
  type AccountRole,
  type Annotation,
} from '@42for.net/2splatform.sdk'
import { isValidTimeZone } from '@/forms/annotation.schema'
import { mockHandlers } from '@/mock/all-handlers'
import { combinedSchema, ownContent, schemaErrors } from '@/mock/configuration-handlers'
import { annotationsOf, loadDb, resetDb, scenarios, type MockDb } from '@/mock/db'
import { documentIds, getDocument } from '@/mock/documents'
import { buildShowcaseScope, showcaseOrganizations, showcaseProjects, showcaseScope } from '@/mock/showcase'
import type { ScopeBuild } from '@/mock/showcase/builder'
import { people, type PersonaId } from '@/mock/showcase/people'
import { writeShowcaseSettings } from '@/mock/showcase/settings'
import type { ShowcaseProject } from '@/mock/showcase/types'
import { environmentViewNames } from '@/mock/showcase/views'
import { codeOfKey, getAllAncestors, isValidAnnotationKey, typeCodes } from '@/utils/annotation-key'
import { withRelations } from '@/utils/annotation-relations'
import { annotationStatus, type AnnotationStatus } from '@/utils/annotation-status'
import { isDateFormat } from '@/utils/dates'
import { useProjectSettingsStore } from '@/stores/project-settings'
import { sanitizeTypeAppearances } from '@/utils/type-appearance'
import { mockDatasets } from '../../../build/mock-dataset.ts'
import { unseededShowcaseDb } from './showcase/support'

// The integrity of the showcase dataset (424-77, spec section 10): the rules every project follows. Each project
// also has its own test of what its model must contain (showcase/<project>.test.ts). The tests of one project read
// its scopes through a database without the seed, so they build that project only (`vitest -t <project>`).

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const documentationProjects = [
  'invoicing-suite',
  'money-suite',
  'streaming-platform',
  'erp',
  'vehicle-platform',
  'plant-fleet',
]
const sizeOf = (project: string): [number, number] =>
  project === 'finance-cloud'
    ? [170, 260]
    : project === 'fitness-cloud'
      ? [900, 1150]
      : documentationProjects.includes(project)
        ? [150, 400]
        : [60, 200]

const roles: AccountRole[] = ['Reader', 'Contributor', 'ContributorWithSecrets', 'Administrator', 'Owner']
const projectKeyOf = (project: ShowcaseProject) => `${project.org}.${project.project}`
const scopeOf = (project: ShowcaseProject, view: string) => ({ org: project.org, project: project.project, view })
const built = (project: ShowcaseProject, view: string) => showcaseScope(project.org, project.project, view)!

const configurationKeys = (build: ScopeBuild) =>
  [...build.documents.keys()].filter((id) => id.startsWith('configuration:')).map((id) => id.slice(14))

describe('the showcase dataset', () => {
  let db: MockDb
  beforeEach(() => {
    db = resetDb('showcase')
  })

  it('is one of the datasets of npm run dev:mock', () => {
    expect(scenarios).toEqual([...mockDatasets])
  })

  it('gives Ada a role in every project: Reader in exactly one, at least Contributor in the others', () => {
    const projectRoles = showcaseProjects.map(
      (project) => db.accessPoints[projectKeyOf(project)]?.AccessMap?.[people.ada.id],
    )
    expect(projectRoles.every((role) => role && roleRank(role) >= roleRank('Reader'))).toBe(true)
    expect(projectRoles.filter((role) => role === 'Reader')).toHaveLength(1)
    expect(new Set(projectRoles)).toEqual(new Set(roles))
  })

  it('uses every role among the other people, in organizations and in projects', () => {
    const others = (members: Partial<Record<PersonaId, AccountRole>>) =>
      Object.entries(members)
        .filter(([persona]) => persona !== 'ada')
        .map(([, role]) => role)
    expect(new Set(showcaseOrganizations.flatMap((organization) => others(organization.members)))).toEqual(
      new Set(roles),
    )
    expect(new Set(showcaseProjects.flatMap((project) => others(project.members)))).toEqual(new Set(roles))
  })

  it('has an Owner in every organization and project, and every project in a known organization', () => {
    for (const point of Object.values(db.accessPoints)) {
      expect(Object.values(point.AccessMap ?? {}), point.Key).toContain('Owner')
      expect(Object.values(point.AccessMap ?? {}), point.Key).not.toContain('None')
    }
    const organizations = new Set(showcaseOrganizations.map((organization) => organization.org))
    expect(showcaseProjects.every((project) => organizations.has(project.org))).toBe(true)
    expect(new Set(showcaseProjects.map(projectKeyOf)).size).toBe(showcaseProjects.length)
  })

  it('registers the views of every project and seeds the deletions of its default view', () => {
    for (const project of showcaseProjects) {
      expect(db.views[projectKeyOf(project)]?.map((view) => view.Name)).toEqual(project.views.map((view) => view.name))
      const trash = built(project, 'default').trash
      const overrides = db.annotations[`${project.org}/${project.project}/default`]
      expect(Object.keys(overrides?.trash ?? {}).toSorted(), project.project).toEqual(
        trash.flatMap((tree) => tree.annotations.map((annotation) => annotation.AnnotationKey)).toSorted(),
      )
    }
  })

  it('keeps the account map and the access points in agreement', () => {
    const derived = Object.fromEntries(
      Object.entries(db.accessPoints).flatMap(([key, point]) => {
        const role = point.AccessMap?.[db.user.id]
        return role ? [[key, role]] : []
      }),
    )
    expect(db.account?.AccessMap).toEqual(derived)
  })

  it('stays on the default dataset when no dataset is chosen', () => {
    sessionStorage.clear()
    expect(loadDb().scenario).toBe('default')
  })

  it('builds the same scope twice', () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-10-21T08:00:00Z'))
    try {
      for (const project of showcaseProjects) {
        expect(buildShowcaseScope(project.org, project.project, 'default')).toEqual(
          buildShowcaseScope(project.org, project.project, 'default'),
        )
      }
    } finally {
      vi.useRealTimers()
    }
  })

  it('serves a project through the mock API: views, lists, Deleted and a restore', async () => {
    const [org, project] = ['fabrikam', 'invoicing-suite']

    const views = await getViews(org, project)
    expect(views.map((view) => view.Name)).toEqual([...environmentViewNames])
    expect(views[0]).toMatchObject({ Name: 'default', IsDefault: true, Description: 'Production environment' })

    const configurations = await getConfigurations(org, project, 'default')
    expect(configurations.Configurations?.find((entry) => entry.AnnotationKey === 'rst.invoicing')?.Version).toBe(18)
    expect((await getTemplates(org, project, 'default')).map((entry) => entry.AnnotationType)).toContain('uxe')
    expect(await getConfigurationSchemas(org, project, 'default')).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ Kind: 'DescendantType', AnnotationKey: 'rst.invoicing', AnnotationType: 'usg' }),
        expect.objectContaining({ Kind: 'DescendantType', AnnotationKey: 'sbt.northwind', AnnotationType: 'cnt' }),
      ]),
    )

    const roots = await getDeletedAnnotations(org, project, 'default', { roots: true })
    expect(roots.Annotations?.map((annotation) => annotation.AnnotationKey)).toEqual(
      expect.arrayContaining(['cnt.trey-research.sandbox', 'exe.vanarsdel.portal.production']),
    )
    expect(
      (await getConfigurations(org, project, 'default')).Configurations?.some(
        (entry) => entry.AnnotationKey === 'exe.trey-research.invoicing.sandbox',
      ),
    ).toBe(false)

    await restoreAnnotations(org, project, 'default', { Keys: ['cnt.trey-research.sandbox'], Descendants: true })
    expect(annotationsOf(loadDb(), org, project, 'default').has('exe.trey-research.invoicing.sandbox')).toBe(true)
    expect(
      (await getConfigurations(org, project, 'default')).Configurations?.some(
        (entry) => entry.AnnotationKey === 'exe.trey-research.invoicing.sandbox',
      ),
    ).toBe(true)

    // The history of an annotation a person created starts with that person.
    const history = await getAnnotationHistory(org, project, 'default', 'sbt.munson-pickles')
    expect(history.Entries?.at(-1)).toMatchObject({
      Action: 'Created',
      Actor: { Kind: 'Account', Name: 'Grace Hopper' },
    })
  })

  it('writes the settings of the showcase projects, keeps the others, and updates an open page', () => {
    localStorage.setItem(
      '2s-admin:project-settings',
      JSON.stringify({ 'acme.billing': { defaultTimeZone: 'Asia/Tokyo' } }),
    )
    const settings = useProjectSettingsStore()
    writeShowcaseSettings()

    expect(settings.settingsOf('fabrikam.invoicing-suite').types.sbt?.label).toBe('Customer')
    expect(settings.settingsOf('acme.billing').defaultTimeZone).toBe('Asia/Tokyo')
  })
})

describe.each(showcaseProjects.map((project) => [project.project, project] as const))('%s', (name, project) => {
  const views = project.views.map((view) => view.name)

  const db = unseededShowcaseDb()

  it('registers the five environment views first, with default as production', () => {
    expect(views.slice(0, 5)).toEqual([...environmentViewNames])
    expect(project.views[0]?.description).toBe('Production environment')
    expect(new Set(views).size).toBe(views.length)
  })

  it('builds every view with annotations, and its default view in the size of its example', () => {
    for (const view of views) {
      expect(built(project, view).annotations.length, view).toBeGreaterThan(0)
    }
    const [min, max] = sizeOf(name)
    const { annotations, trash } = built(project, 'default')
    const size = annotations.length + trash.reduce((sum, tree) => sum + tree.annotations.length, 0)
    expect(size).toBeGreaterThanOrEqual(min)
    expect(size).toBeLessThanOrEqual(max)
  })

  it('stays below a page of 1,000 annotations per type', () => {
    for (const view of views) {
      for (const code of typeCodes) {
        const count = built(project, view).annotations.filter(
          (annotation) => codeOfKey(annotation.AnnotationKey) === code,
        )
        expect(count.length, `${view} ${code}`).toBeLessThan(1000)
      }
    }
  })

  it('has canonical keys, names and relation members, and every ancestor of an annotation', () => {
    for (const view of views) {
      const { annotations, trash } = built(project, view)
      const active = new Set(annotations.map((annotation) => annotation.AnnotationKey))
      const check = (annotation: Annotation, present: Set<string>) => {
        const key = annotation.AnnotationKey
        expect(isValidAnnotationKey(key), key).toBe(true)
        expect(withRelations(annotation), key).toEqual(annotation)
        expect(
          key
            .split('.')
            .slice(1)
            .every((part) => /^[a-z0-9][a-zA-Z0-9-]*$/.test(part)),
          key,
        ).toBe(true)
        expect(annotation).toMatchObject({ ProjectName: project.project, ViewName: view })
        expect(isValidTimeZone(annotation.TimeZone ?? ''), key).toBe(true)
        for (const ancestor of getAllAncestors(key)) {
          expect(present.has(ancestor), `${view}: ${key} needs ${ancestor}`).toBe(true)
        }
      }
      annotations.forEach((annotation) => check(annotation, active))
      for (const tree of trash) {
        const present = new Set([...active, ...tree.annotations.map((annotation) => annotation.AnnotationKey)])
        tree.annotations.forEach((annotation) => check(annotation, present))
        expect(tree.annotations.at(-1)?.AnnotationKey).toBe(tree.rootKey)
      }
    }
  })

  it('shows every status in production, and deletions with and without descendants', () => {
    const { annotations, trash } = built(project, 'default')
    const now = Date.now()
    const statuses = new Set(annotations.map((annotation) => annotationStatus(annotation, now)))
    expect(statuses).toEqual(new Set<AnnotationStatus>(['active', 'disabled', 'scheduled', 'expired']))
    expect(trash.some((tree) => tree.annotations.length > 1)).toBe(true)
    expect(trash.some((tree) => tree.annotations.length === 1)).toBe(true)
    expect(trash.every((tree) => Date.now() - Date.parse(tree.deletedAt) < 90 * 86_400_000)).toBe(true)
  })

  it('has histories of one, a few and many versions, templates and schemas', () => {
    const { documents } = built(project, 'default')
    const lengths = configurationKeys(built(project, 'default')).map(
      (key) => documents.get(`configuration:${key}`)!.versions.length,
    )
    expect(lengths).toContain(1)
    expect(lengths.some((length) => length >= 2 && length <= 5)).toBe(true)
    expect(lengths.some((length) => length >= 15)).toBe(true)
    expect([...documents.keys()].some((id) => id.startsWith('template:'))).toBe(true)
    expect([...documents.keys()].some((id) => id.startsWith('schema:'))).toBe(true)
    expect(configurationKeys(built(project, 'default')).length).toBeGreaterThanOrEqual(
      Math.floor(built(project, 'default').annotations.length / 4),
    )
  })

  it('keeps the content of every current version, which never expires', () => {
    for (const view of views) {
      for (const [id, document] of built(project, view).documents) {
        expect(document.versions.at(-1)?.Content, `${view} ${id}`).toBeDefined()
        expect(document.versions.at(-1)?.ExpirationTime, `${view} ${id}`).toBe('9999-12-31T23:59:59.999Z')
      }
    }
  })

  it('dates every configuration within the life of its annotation', () => {
    for (const view of views) {
      const { annotations, trash, documents } = built(project, view)
      const deletedAt = new Map(
        trash.flatMap((tree) => tree.annotations.map((annotation) => [annotation.AnnotationKey, tree.deletedAt])),
      )
      for (const annotation of [...annotations, ...trash.flatMap((tree) => tree.annotations)]) {
        const key = annotation.AnnotationKey
        const versions = documents.get(`configuration:${key}`)?.versions ?? []
        const end = deletedAt.get(key)
        if (versions[0]) {
          expect(Date.parse(versions[0].Modified.At!), `${view} ${key}`).toBeGreaterThanOrEqual(
            Date.parse(annotation.Created!.At!) - 1000,
          )
        }
        if (end) {
          expect(Date.parse(annotation.Modified!.At!), `${view} ${key}`).toBeLessThanOrEqual(Date.parse(end))
          for (const version of versions) {
            expect(Date.parse(version.Modified.At!), `${view} ${key} v${version.Version}`).toBeLessThan(Date.parse(end))
          }
        }
      }
    }
  })

  it('stores configurations that satisfy their combined schema, except the ones listed as non-compliant', () => {
    for (const view of views) {
      const scope = scopeOf(project, view)
      for (const id of documentIds(db, scope, 'configuration:')) {
        const key = id.slice('configuration:'.length)
        const errors = schemaErrors(combinedSchema(db, scope, key)?.MergedContent, ownContent(db, scope, key) ?? {})
        if (view === 'default' && project.nonCompliant?.includes(key)) {
          expect(errors, `${view} ${key}`).not.toEqual([])
        } else {
          expect(errors, `${view} ${key}`).toEqual([])
        }
        expect(getDocument(db, scope, id)).toBeDefined()
      }
    }
  })

  it('brings type names, colors and icons the UI can show, a time zone and a date format', () => {
    expect(sanitizeTypeAppearances(project.settings.types)).toEqual(project.settings.types)
    expect(Object.keys(project.settings.types).length).toBeGreaterThanOrEqual(5)
    expect(isValidTimeZone(project.settings.defaultTimeZone)).toBe(true)
    expect(isDateFormat(project.settings.dateFormat)).toBe(true)
  })
})
