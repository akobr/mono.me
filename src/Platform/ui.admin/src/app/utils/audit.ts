import type {
  AnnotationAuditCause,
  AnnotationAuditEntry,
  AnnotationAuditEntryAction,
  AuditActor,
} from '@42for.net/2splatform.sdk'

// Presentation of the audit entries of an annotation (spec 4.3). The entries carry previous and new values for the
// fields and primitive values, kinds only for objects and arrays, and only a flag for the description.

/** One change of an entry, rendered as a line or a chip. */
export type AuditChange =
  | { kind: 'field'; field: string; previous: string; value: string }
  | { kind: 'flag'; field: string }
  | { kind: 'labels'; added: string[]; removed: string[] }
  | { kind: 'value'; key: string; text: string }
  | { kind: 'configuration'; version: number }

const flaggedFields = new Set(['Description', 'UnitDefinition'])

export const actionLabels: Record<AnnotationAuditEntryAction, string> = {
  Created: 'Created',
  Recreated: 'Re-created',
  Updated: 'Updated',
  Deleted: 'Deleted',
  Restored: 'Restored',
  ConfigurationChanged: 'Configuration saved',
  ConfigurationDeleted: 'Configuration deleted',
}

export const actionColors: Record<AnnotationAuditEntryAction, 'success' | 'neutral' | 'error' | 'info' | 'warning'> = {
  Created: 'success',
  Recreated: 'warning',
  Updated: 'neutral',
  Deleted: 'error',
  Restored: 'info',
  ConfigurationChanged: 'neutral',
  ConfigurationDeleted: 'neutral',
}

export const actionIcons: Record<AnnotationAuditEntryAction, string> = {
  Created: 'i-lucide-plus',
  Recreated: 'i-lucide-rotate-ccw',
  Updated: 'i-lucide-pencil',
  Deleted: 'i-lucide-trash-2',
  Restored: 'i-lucide-archive-restore',
  ConfigurationChanged: 'i-lucide-settings-2',
  ConfigurationDeleted: 'i-lucide-settings-2',
}

/** Name of an account (or its id), `machine:<id>` for a machine, and System. */
export function actorName(actor: AuditActor | undefined): string {
  if (!actor) {
    return 'Unknown'
  }
  switch (actor.Kind) {
    case 'Machine':
      return `machine:${actor.Id ?? '?'}`
    case 'System':
      return 'System'
    default:
      return actor.Name?.trim() || actor.Id || 'Unknown'
  }
}

/** "with sbt.northwind" for a cascade, "for exe.a.b.c" for an ancestor created or restored for a key. */
export function causeText(cause: AnnotationAuditCause | undefined): string | undefined {
  if (!cause?.Key) {
    return undefined
  }
  return cause.Kind === 'Ancestor' ? `for ${cause.Key}` : `with ${cause.Key}`
}

/** A recorded value: strings quoted (with … when cut), objects and arrays by their kind. */
export function formatAuditValue(value: unknown, truncated = false, kind?: string | null): string {
  if (kind === 'Object') {
    return 'object'
  }
  if (kind === 'Array') {
    return 'array'
  }
  if (value === null || value === undefined) {
    return '–'
  }
  if (typeof value === 'string') {
    return `"${value}${truncated ? '…' : ''}"`
  }
  return typeof value === 'object' ? JSON.stringify(value) : String(value)
}

export function auditChanges(entry: AnnotationAuditEntry): AuditChange[] {
  const changes: AuditChange[] = []

  for (const field of entry.Fields ?? []) {
    const name = field.Field ?? ''
    changes.push(
      flaggedFields.has(name)
        ? { kind: 'flag', field: name }
        : {
            kind: 'field',
            field: name,
            previous: formatAuditValue(field.Previous, field.IsPreviousTruncated),
            value: formatAuditValue(field.Value, field.IsValueTruncated),
          },
    )
  }

  const added = entry.Labels?.Added ?? []
  const removed = entry.Labels?.Removed ?? []
  if (added.length || removed.length) {
    changes.push({ kind: 'labels', added, removed })
  }

  for (const value of entry.Values ?? []) {
    const key = value.Key ?? ''
    const previous = formatAuditValue(value.Previous, value.IsPreviousTruncated, value.PreviousKind)
    const next = formatAuditValue(value.Value, value.IsValueTruncated, value.ValueKind)
    const text =
      value.Change === 'Added'
        ? `${key} added: ${next}`
        : value.Change === 'Removed'
          ? `${key} removed`
          : value.PreviousKind === 'Object' && value.ValueKind === 'Object'
            ? `${key}: object changed`
            : value.PreviousKind === 'Array' && value.ValueKind === 'Array'
              ? `${key}: array changed`
              : `${key}: ${previous} → ${next}`
    changes.push({ kind: 'value', key, text })
  }

  if (entry.ConfigurationVersion) {
    changes.push({ kind: 'configuration', version: entry.ConfigurationVersion })
  }

  return changes
}
