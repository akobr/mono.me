import type {
  Annotation,
  AnnotationAuditEntry,
  AnnotationAuditEntryAction,
  AnnotationFieldChange,
  AnnotationLabelChanges,
  AnnotationValueChange,
  AuditActor,
  AuditStamp,
} from '@42for.net/2splatform.sdk'
import { overridesOf, type MockDb } from './db'

// The change history of the mock API, in the shapes of the API (annotation-lifecycle.md, "Audit"): one list of
// entries per key, written by every annotation write, delete, restore and configuration save.

export interface MockScope {
  org: string
  project: string
  view: string
}

/** Days a deleted annotation stays restorable (Annotations:DeletedRetentionDays of the API). */
export const retentionDays = 90

const maxValueLength = 256
let sequence = 0

export const mockActor = (db: MockDb): AuditActor => ({ Kind: 'Account', Id: db.user.id, Name: db.user.name })

export const stamp = (db: MockDb, at = new Date()): AuditStamp => ({ At: at.toISOString(), By: mockActor(db) })

export function newEntry(
  db: MockDb,
  action: AnnotationAuditEntryAction,
  extra: Partial<AnnotationAuditEntry> = {},
): AnnotationAuditEntry {
  sequence += 1
  return {
    Id: `adt_${Date.now().toString(36)}_${sequence.toString(36)}`,
    At: new Date().toISOString(),
    Action: action,
    Actor: mockActor(db),
    ...extra,
  }
}

export function auditOf(db: MockDb, scope: MockScope, key: string): AnnotationAuditEntry[] | undefined {
  return overridesOf(db, scope.org, scope.project, scope.view).audit?.[key]
}

export function appendAudit(db: MockDb, scope: MockScope, key: string, entry: AnnotationAuditEntry): void {
  const overrides = overridesOf(db, scope.org, scope.project, scope.view)
  const audit = (overrides.audit ??= {})
  ;(audit[key] ??= []).push(entry)
}

export function dropAudit(db: MockDb, scope: MockScope, key: string): void {
  const audit = overridesOf(db, scope.org, scope.project, scope.view).audit
  if (audit) {
    delete audit[key]
  }
}

// Fields with their previous and new value; the description only flags a change (AnnotationAuditDiff).
const valueFields = [
  'Title',
  'DocumentationLink',
  'IsDisabled',
  'ValidFrom',
  'ExpiresAt',
  'TimeZone',
  'UnitType',
] as const
const flaggedFields = ['Description', 'UnitDefinition'] as const

type ValueKind = NonNullable<AnnotationValueChange['ValueKind']>

function kindOf(value: unknown): ValueKind {
  if (value === null || value === undefined) {
    return 'Null'
  }
  if (Array.isArray(value)) {
    return 'Array'
  }
  switch (typeof value) {
    case 'string':
      return 'String'
    case 'number':
      return 'Number'
    case 'boolean':
      return 'Boolean'
    default:
      return 'Object'
  }
}

function cut(value: unknown): { value: unknown; truncated: boolean } {
  return typeof value === 'string' && value.length > maxValueLength
    ? { value: value.slice(0, maxValueLength), truncated: true }
    : { value, truncated: false }
}

const normalized = (value: unknown) => (value === '' || value === false || value === undefined ? null : value)

/** The differences of an update, or undefined when nothing changed (an update without changes writes nothing). */
export function diffAnnotations(
  before: Annotation,
  after: Annotation,
): Pick<AnnotationAuditEntry, 'Fields' | 'Labels' | 'Values'> | undefined {
  const record = (annotation: Annotation) => annotation as unknown as Record<string, unknown>
  const fields: AnnotationFieldChange[] = []

  for (const field of valueFields) {
    const previous = normalized(record(before)[field])
    const value = normalized(record(after)[field])
    if (JSON.stringify(previous) !== JSON.stringify(value)) {
      const previousCut = cut(previous)
      const valueCut = cut(value)
      fields.push({
        Field: field,
        Previous: previousCut.value as never,
        IsPreviousTruncated: previousCut.truncated,
        Value: valueCut.value as never,
        IsValueTruncated: valueCut.truncated,
      })
    }
  }

  for (const field of flaggedFields) {
    if ((normalized(record(before)[field]) ?? null) !== (normalized(record(after)[field]) ?? null)) {
      fields.push({ Field: field })
    }
  }

  const beforeLabels = new Set(before.Labels ?? [])
  const afterLabels = new Set(after.Labels ?? [])
  const labels: AnnotationLabelChanges = {
    Added: [...afterLabels].filter((label) => !beforeLabels.has(label)),
    Removed: [...beforeLabels].filter((label) => !afterLabels.has(label)),
  }

  const beforeValues = (before.Values ?? {}) as Record<string, unknown>
  const afterValues = (after.Values ?? {}) as Record<string, unknown>
  const values: AnnotationValueChange[] = []

  for (const key of new Set([...Object.keys(beforeValues), ...Object.keys(afterValues)])) {
    const inBefore = key in beforeValues
    const inAfter = key in afterValues
    if (inBefore && inAfter && JSON.stringify(beforeValues[key]) === JSON.stringify(afterValues[key])) {
      continue
    }

    const primitive = (value: unknown) => {
      const kind = kindOf(value)
      return kind === 'Object' || kind === 'Array' ? { value: undefined, truncated: false } : cut(value)
    }
    const previous = primitive(beforeValues[key])
    const value = primitive(afterValues[key])
    values.push({
      Key: key,
      Change: !inBefore ? 'Added' : !inAfter ? 'Removed' : 'Changed',
      PreviousKind: inBefore ? kindOf(beforeValues[key]) : undefined,
      Previous: previous.value as never,
      IsPreviousTruncated: previous.truncated,
      ValueKind: inAfter ? kindOf(afterValues[key]) : undefined,
      Value: value.value as never,
      IsValueTruncated: value.truncated,
    })
  }

  const hasLabels = labels.Added!.length > 0 || labels.Removed!.length > 0
  if (!fields.length && !hasLabels && !values.length) {
    return undefined
  }

  return {
    Fields: fields.length ? fields : undefined,
    Labels: hasLabels ? labels : undefined,
    Values: values.length ? values : undefined,
  }
}
