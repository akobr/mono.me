import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import {
  deleteAnnotation,
  getAnnotation,
  getAnnotations,
  getSubjects,
  setAnnotation,
  type Annotation,
} from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const [org, project, view] = ['acme', 'billing', 'default']
const day = 24 * 3600 * 1000

const subject = (name: string, fields: Partial<Annotation> = {}): Annotation => ({
  ProjectName: project,
  ViewName: view,
  AnnotationKey: `sbt.${name}`,
  Name: name,
  AnnotationType: 'Subject',
  ...fields,
})

const keysOf = (response: { Annotations?: Annotation[] }, prefix: string) =>
  (response.Annotations ?? []).map((annotation) => annotation.AnnotationKey).filter((key) => key.startsWith(prefix))

async function seed(): Promise<void> {
  await setAnnotation(org, project, view, 'sbt.aa-on', subject('aa-on'))
  await setAnnotation(org, project, view, 'sbt.aa-off', subject('aa-off', { IsDisabled: true }))
  await setAnnotation(
    org,
    project,
    view,
    'sbt.aa-later',
    subject('aa-later', { ValidFrom: new Date(Date.now() + day).toISOString() }),
  )
  await setAnnotation(
    org,
    project,
    view,
    'sbt.aa-over',
    subject('aa-over', { ExpiresAt: new Date(Date.now() - day).toISOString() }),
  )
  await setAnnotation(org, project, view, 'sbt.aa-gone', subject('aa-gone'))
  await deleteAnnotation(org, project, view, 'sbt.aa-gone')
}

describe('the mock API reads like the API', () => {
  it('lists only active annotations unless asked for the inactive and deleted ones', async () => {
    await seed()

    expect(keysOf(await getSubjects(org, project, view), 'sbt.aa-')).toEqual(['sbt.aa-on'])
    expect(keysOf(await getSubjects(org, project, view, { includeInactive: true }), 'sbt.aa-').toSorted()).toEqual([
      'sbt.aa-later',
      'sbt.aa-off',
      'sbt.aa-on',
      'sbt.aa-over',
    ])
    expect(keysOf(await getAnnotations(org, project, view, { includeDeleted: true }), 'sbt.aa-').toSorted()).toEqual([
      'sbt.aa-gone',
      'sbt.aa-on',
    ])
  })

  it('answers 404 AnnotationInactive for an inactive key unless asked for it', async () => {
    await seed()

    await expect(getAnnotation(org, project, view, 'sbt.aa-off')).rejects.toMatchObject({
      status: 404,
      errorCode: 'AnnotationInactive',
    })
    expect((await getAnnotation(org, project, view, 'sbt.aa-off', { includeInactive: true })).IsDisabled).toBe(true)
    await expect(getAnnotation(org, project, view, 'sbt.aa-gone')).rejects.toMatchObject({ status: 404 })
    expect((await getAnnotation(org, project, view, 'sbt.aa-gone', { includeDeleted: true })).Deletion).toBeTruthy()
  })
})
