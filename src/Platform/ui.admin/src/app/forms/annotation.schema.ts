import { z } from 'zod'
import {
  SetAnnotationBody,
  setAnnotationBodyDescriptionMax,
  setAnnotationBodyDocumentationLinkMax,
  setAnnotationBodyTitleMax,
} from '@42for.net/2splatform.sdk/zod'
import type { Annotation } from '@42for.net/2splatform.sdk'
import { isUnitAnnotation } from '@/utils/unit-types'

export function isValidTimeZone(timeZone: string): boolean {
  try {
    return new Intl.DateTimeFormat('en', { timeZone }).resolvedOptions().timeZone.length > 0
  } catch {
    return false
  }
}

const isHttpUrl = (value: string) => /^https?:\/\/\S+$/i.test(value) && URL.canParse(value)

/** The limits of the API (AnnotationRules), published as maxLength in the OpenAPI document. */
export const annotationLimits = {
  Title: setAnnotationBodyTitleMax,
  Description: setAnnotationBodyDescriptionMax,
  DocumentationLink: setAnnotationBodyDocumentationLinkMax,
} as const

const count = new Intl.NumberFormat('en-US')

/**
 * One schema for the Form, JSON and YAML modes (spec section 11.1): the generated SetAnnotationBody with the UI
 * rules: the API's length limits (title 200, description 10,000, link 2048), an absolute http(s) link, an IANA time
 * zone, unique non-empty labels, non-empty value keys, and ValidFrom before ExpiresAt.
 */
export const annotationFormSchema = SetAnnotationBody.extend({
  Title: z
    .string()
    .trim()
    .max(annotationLimits.Title, `At most ${count.format(annotationLimits.Title)} characters.`)
    .optional(),
  Description: z
    .string()
    .max(annotationLimits.Description, `At most ${count.format(annotationLimits.Description)} characters.`)
    .optional(),
  DocumentationLink: z
    .string()
    .trim()
    .max(annotationLimits.DocumentationLink, `At most ${annotationLimits.DocumentationLink} characters.`)
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
  UnitType: z.string().optional(),
  UnitDefinition: z.string().optional(),
}).superRefine((value, context) => {
  if (value.ValidFrom && value.ExpiresAt && Date.parse(value.ValidFrom) >= Date.parse(value.ExpiresAt)) {
    context.addIssue({ code: 'custom', path: ['ExpiresAt'], message: 'Expires must be after Valid from.' })
  }
  if (!isUnitAnnotation(value)) {
    return
  }
  if (!value.UnitType?.trim()) {
    context.addIssue({ code: 'custom', path: ['UnitType'], message: 'Enter a unit type.' })
  }
  if (!value.UnitDefinition?.trim()) {
    context.addIssue({ code: 'custom', path: ['UnitDefinition'], message: 'Enter a unit definition.' })
  }
})

export type AnnotationForm = z.input<typeof annotationFormSchema>

/** Fields that are part of the key and cannot change through the editor. */
export const readOnlyAnnotationFields = ['AnnotationKey', 'Name', 'AnnotationType', 'ProjectName', 'ViewName'] as const

/** The editable document of an annotation. A unit also carries its trigger; other type-specific members stay on the loaded annotation. */
export function toAnnotationDraft(annotation: Annotation): AnnotationForm {
  const draft: AnnotationForm = {
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

  if (!isUnitAnnotation(annotation)) {
    return draft
  }

  const unit = annotation as Annotation & { UnitType?: string; UnitDefinition?: string }
  return { ...draft, UnitType: unit.UnitType ?? '', UnitDefinition: unit.UnitDefinition ?? '' }
}

/**
 * The body for SetAnnotation: empty strings and collections become absent. SetAnnotation replaces the whole
 * document and requires the type-specific members (SubjectKey, ContextNames, …), which the form does not edit,
 * so they are taken from the loaded `original`. A unit's trigger is edited, and the trimmed draft replaces it.
 */
export function toAnnotationBody(draft: AnnotationForm, original?: Annotation): Annotation {
  const blank = (value: string | undefined) => (value && value.trim() ? value.trim() : undefined)
  const body: Annotation & { UnitType?: string; UnitDefinition?: string } = {
    ...original,
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

  if (isUnitAnnotation(draft)) {
    body.UnitType = draft.UnitType?.trim() ?? ''
    body.UnitDefinition = draft.UnitDefinition?.trim() ?? ''
  }

  return body
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

/**
 * The fields named in a 400 InvalidAnnotation of the API ("The annotation 'x' is not valid: Title has 201 characters,
 * at most 200 are allowed. DocumentationLink must be an absolute http or https URL."), each with its sentence.
 */
export function invalidAnnotationFields(message: string): Partial<Record<keyof typeof annotationLimits, string>> {
  const fields: Partial<Record<keyof typeof annotationLimits, string>> = {}
  const separator = message.indexOf(': ')
  const problems = separator >= 0 ? message.slice(separator + 2) : message
  for (const sentence of problems.split(/(?<=\.)\s+/)) {
    const field = (Object.keys(annotationLimits) as (keyof typeof annotationLimits)[]).find((name) =>
      sentence.startsWith(`${name} `),
    )
    if (field) {
      fields[field] = sentence.trim()
    }
  }
  return fields
}
