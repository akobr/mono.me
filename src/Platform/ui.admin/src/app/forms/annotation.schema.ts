import { z } from 'zod'
import { SetAnnotationBody } from '@42for.net/2splatform.sdk/zod'
import type { Annotation } from '@42for.net/2splatform.sdk'

export function isValidTimeZone(timeZone: string): boolean {
  try {
    return new Intl.DateTimeFormat('en', { timeZone }).resolvedOptions().timeZone.length > 0
  } catch {
    return false
  }
}

const isHttpUrl = (value: string) => /^https?:\/\/\S+$/i.test(value) && URL.canParse(value)

/**
 * One schema for the Form, JSON and YAML modes (spec section 11.1): the generated SetAnnotationBody with the UI
 * rules: a title of at most 200 characters, an absolute http(s) link, an IANA time zone, unique non-empty labels,
 * non-empty value keys, and ValidFrom before ExpiresAt.
 */
export const annotationFormSchema = SetAnnotationBody.extend({
  Title: z.string().trim().max(200, 'At most 200 characters.').optional(),
  Description: z.string().optional(),
  DocumentationLink: z
    .string()
    .trim()
    .refine((value) => value === '' || isHttpUrl(value), 'Use an absolute http(s) URL.')
    .optional(),
  TimeZone: z
    .string()
    .trim()
    .refine((value) => value === '' || isValidTimeZone(value), 'Use an IANA time zone, for example Europe/Prague.')
    .optional(),
  Labels: z
    .array(z.string().trim().min(1, 'Labels cannot be empty.'))
    .refine(
      (labels) => new Set(labels.map((label) => label.toLowerCase())).size === labels.length,
      'Labels must be unique.',
    )
    .optional(),
  Values: z
    .record(z.string(), z.unknown())
    .refine((values) => Object.keys(values).every((key) => key.trim().length > 0), 'Value keys cannot be empty.')
    .optional(),
}).superRefine((value, context) => {
  if (value.ValidFrom && value.ExpiresAt && Date.parse(value.ValidFrom) >= Date.parse(value.ExpiresAt)) {
    context.addIssue({ code: 'custom', path: ['ExpiresAt'], message: 'Expires must be after Valid from.' })
  }
})

export type AnnotationForm = z.input<typeof annotationFormSchema>

/** Fields that are part of the key and cannot change through the editor. */
export const readOnlyAnnotationFields = ['AnnotationKey', 'Name', 'AnnotationType', 'ProjectName', 'ViewName'] as const

/** The editable document of an annotation: the base fields only (relation fields are derived by the API). */
export function toAnnotationDraft(annotation: Annotation): AnnotationForm {
  return {
    ProjectName: annotation.ProjectName,
    ViewName: annotation.ViewName,
    AnnotationKey: annotation.AnnotationKey,
    Name: annotation.Name,
    AnnotationType: annotation.AnnotationType,
    IsDisabled: annotation.IsDisabled ?? false,
    ValidFrom: annotation.ValidFrom ?? null,
    ExpiresAt: annotation.ExpiresAt ?? null,
    TimeZone: annotation.TimeZone ?? '',
    Title: annotation.Title ?? '',
    Description: annotation.Description ?? '',
    DocumentationLink: annotation.DocumentationLink ?? '',
    Labels: annotation.Labels ?? [],
    Values: annotation.Values ?? {},
  }
}

/** The body for SetAnnotation: empty strings and collections become absent. */
export function toAnnotationBody(draft: AnnotationForm): Annotation {
  const blank = (value: string | undefined) => (value && value.trim() ? value.trim() : undefined)
  return {
    ProjectName: draft.ProjectName,
    ViewName: draft.ViewName,
    AnnotationKey: draft.AnnotationKey,
    Name: draft.Name,
    AnnotationType: draft.AnnotationType ?? 'Responsibility',
    IsDisabled: draft.IsDisabled ? true : null,
    ValidFrom: draft.ValidFrom || null,
    ExpiresAt: draft.ExpiresAt || null,
    TimeZone: blank(draft.TimeZone),
    Title: blank(draft.Title),
    Description: blank(draft.Description),
    DocumentationLink: blank(draft.DocumentationLink),
    Labels: draft.Labels?.length ? draft.Labels : undefined,
    Values: draft.Values && Object.keys(draft.Values).length ? draft.Values : undefined,
  }
}

/** Issues of a document edited as JSON/YAML that the schema cannot express: changed key fields. */
export function readOnlyIssues(original: Annotation, edited: unknown): { path: string; message: string }[] {
  if (!edited || typeof edited !== 'object' || Array.isArray(edited)) {
    return [{ path: '', message: 'The annotation must be an object.' }]
  }

  const record = edited as Record<string, unknown>
  return readOnlyAnnotationFields
    .filter((field) => record[field] !== original[field])
    .map((field) => ({ path: field, message: 'is part of the key and cannot change.' }))
}
