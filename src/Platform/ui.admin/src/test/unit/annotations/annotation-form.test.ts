import { describe, expect, it } from 'vitest'
import type { Annotation } from '@42for.net/2splatform.sdk'
import {
  annotationFormSchema,
  isValidTimeZone,
  readOnlyIssues,
  toAnnotationBody,
  toAnnotationDraft,
} from '@/forms/annotation.schema'
import { chunk, runLimited } from '@/composables/useAnnotationActions'
import AnnotationForm from '@/components/annotations/AnnotationForm.vue'
import { mountScreen, settle } from '../../helpers'

const annotation: Annotation = {
  ProjectName: 'billing',
  ViewName: 'default',
  AnnotationKey: 'exe.northwind.invoicing.prod',
  Name: 'prod',
  AnnotationType: 'Execution',
  Title: 'Prod',
  Labels: ['eu'],
  Values: { retries: 3 },
  ...({ SubjectKey: 'sbt.northwind' } as object),
}

const issues = (value: unknown) =>
  annotationFormSchema.safeParse(value).error?.issues.map((issue) => `${issue.path.join('.')}: ${issue.message}`) ?? []

describe('annotation form schema', () => {
  it('accepts a draft of an annotation', () => {
    expect(issues(toAnnotationDraft(annotation))).toEqual([])
  })

  it('applies the UI rules', () => {
    const draft = toAnnotationDraft(annotation)

    expect(
      issues({
        ...draft,
        Title: 'x'.repeat(201),
        DocumentationLink: 'ftp://files.test/doc',
        TimeZone: 'Mars/Olympus',
        Labels: ['eu', 'EU', ' '],
        Values: { ' ': 1 },
        ValidFrom: '2026-11-01T00:00:00Z',
        ExpiresAt: '2026-10-01T00:00:00Z',
      }),
    ).toEqual([
      'TimeZone: Use an IANA time zone, for example Europe/Prague.',
      'Title: At most 200 characters.',
      'DocumentationLink: Use an absolute http(s) URL.',
      'Labels.2: Labels cannot be empty.',
      'Labels: Labels must be unique.',
      'Values: Value keys cannot be empty.',
      'ExpiresAt: Expires must be after Valid from.',
    ])
    expect(issues({ ...draft, ValidFrom: '2026-11-01T00:00:00Z', ExpiresAt: '2026-10-01T00:00:00Z' })).toEqual([
      'ExpiresAt: Expires must be after Valid from.',
    ])
  })

  it('accepts primitive values (Values is any JSON per key)', () => {
    expect(issues({ ...toAnnotationDraft(annotation), Values: { a: 1, b: 'x', c: [1], d: null } })).toEqual([])
  })

  it('validates time zones with Intl', () => {
    expect(isValidTimeZone('Europe/Prague')).toBe(true)
    expect(isValidTimeZone('UTC')).toBe(true)
    expect(isValidTimeZone('Nowhere/City')).toBe(false)
  })

  it('builds the body without derived or empty fields', () => {
    const body = toAnnotationBody({
      ...toAnnotationDraft(annotation),
      Title: '  ',
      Labels: [],
      Values: {},
      IsDisabled: false,
    })

    expect(body).toEqual({
      ProjectName: 'billing',
      ViewName: 'default',
      AnnotationKey: 'exe.northwind.invoicing.prod',
      Name: 'prod',
      AnnotationType: 'Execution',
      IsDisabled: null,
      ValidFrom: null,
      ExpiresAt: null,
      TimeZone: undefined,
      Title: undefined,
      Description: undefined,
      DocumentationLink: undefined,
      Labels: undefined,
      Values: undefined,
    })
  })

  it('keeps the type-specific members of the loaded annotation in the body', () => {
    const loaded = {
      ...annotation,
      SubjectKey: 'sbt.northwind',
      ResponsibilityKey: 'rst.invoicing',
      ContextKey: 'cnt.northwind.prod',
      UsageKey: 'usg.northwind.invoicing',
      SubjectName: 'northwind',
      ResponsibilityName: 'invoicing',
      ContextName: 'prod',
      Title: 'Old',
    } as Annotation

    const body = toAnnotationBody({ ...toAnnotationDraft(loaded), Title: 'New' }, loaded) as unknown as Record<string, unknown>

    expect(body.Title).toBe('New')
    expect(body.SubjectKey).toBe('sbt.northwind')
    expect(body.UsageKey).toBe('usg.northwind.invoicing')
    expect(body.ContextName).toBe('prod')
  })

  it('blocks changes of key fields in the text modes', () => {
    expect(readOnlyIssues(annotation, { ...toAnnotationDraft(annotation), Title: 'new' })).toEqual([])
    expect(
      readOnlyIssues(annotation, { ...toAnnotationDraft(annotation), Name: 'other', ViewName: 'staging' }),
    ).toEqual([
      { path: 'Name', message: 'is part of the key and cannot change.' },
      { path: 'ViewName', message: 'is part of the key and cannot change.' },
    ])
    expect(readOnlyIssues(annotation, [])).toHaveLength(1)
  })
})

describe('AnnotationForm', () => {
  it('shows the type, key and name as one line of text instead of inputs', async () => {
    const screen = await mountScreen(AnnotationForm, { annotation, fresh: true })
    await settle()

    const inputs = screen.host.findAll('input').map((input) => input.element.value)
    expect(inputs).not.toContain('prod')
    expect(inputs).not.toContain('Execution')
    expect(inputs).not.toContain('exe.northwind.invoicing.prod')

    // the type is its icon in the type color, named for assistive technology; the name is the bold last segment
    const identity = screen.host.find('[data-testid="annotation-identity"]')
    expect(identity.find('[role="img"]').attributes('aria-label')).toBe('Execution')
    expect(identity.find('[role="img"]').classes()).toContain('text-st-exe')
    expect(identity.find('code').text()).toBe('exe.northwind.invoicing.prod')
    expect(identity.find('code').classes()).toEqual(expect.arrayContaining(['text-sm', 'break-all']))
    expect(identity.find('.font-semibold').text()).toBe('prod')
    screen.host.unmount()
  })
})

describe('bulk helpers', () => {
  it('splits into batches of a size', () => {
    expect(chunk([1, 2, 3, 4, 5], 2)).toEqual([[1, 2], [3, 4], [5]])
  })

  it('runs at most the given number of workers at once', async () => {
    let running = 0
    let peak = 0
    const done: number[] = []

    await runLimited([1, 2, 3, 4, 5, 6, 7, 8, 9], 4, async (item) => {
      running++
      peak = Math.max(peak, running)
      await new Promise((resolve) => setTimeout(resolve, 5))
      done.push(item)
      running--
    })

    expect(peak).toBe(4)
    expect(done.toSorted()).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9])
  })
})
