import { mockUser, type MockDb } from '@/mock/db'
import { showcaseScope } from '@/mock/showcase'

/**
 * A showcase database without the seed: catalogs and documents come from the project definitions alone, so a test
 * of one project builds that project only. Access, views and the seeded deletions need resetDb('showcase').
 */
export const unseededShowcaseDb = (): MockDb => ({
  scenario: 'showcase',
  user: mockUser,
  account: null,
  accessPoints: {},
  views: {},
  annotations: {},
})

/** The helpers of one project's test. */
export function showcaseOf(org: string, project: string) {
  const scope = (view: string) => showcaseScope(org, project, view)!
  return {
    scope: (view: string) => ({ org, project, view }),
    /** The active annotations of the view, by key. */
    annotations: (view: string) =>
      new Map(scope(view).annotations.map((annotation) => [annotation.AnnotationKey, annotation])),
    keys: (view: string) => new Set(scope(view).annotations.map((annotation) => annotation.AnnotationKey)),
    /** The keys in Deleted, by the root of their deletion. */
    trash: (view: string) =>
      new Map(
        scope(view).trash.map((tree) => [tree.rootKey, tree.annotations.map((annotation) => annotation.AnnotationKey)]),
      ),
    /** The latest content of a document (`configuration:<key>`, `template:<code>`, `schema:…`). */
    latest: (view: string, id: string) =>
      scope(view)
        .documents.get(id as never)
        ?.versions.at(-1)?.Content as Record<string, unknown> | undefined,
    /** The latest own configuration of a key. */
    own: (view: string, key: string) =>
      scope(view).documents.get(`configuration:${key}`)?.versions.at(-1)?.Content as
        Record<string, unknown> | undefined,
    versions: (view: string, key: string) => scope(view).documents.get(`configuration:${key}`)?.versions.length ?? 0,
  }
}
