import { parseAnnotationKey } from '@/utils/annotation-key'

/** A suggested unit type. The stored value is lowercase; the label is what the menu shows. */
export interface UnitTypeSuggestion {
  value: string
  label: string
  description: string
  definitionPlaceholder: string
  definitionHelp: string
}

export const unitTypeSuggestions = [
  {
    value: 'time',
    label: 'Time',
    description: 'A job that runs on a schedule.',
    definitionPlaceholder: '*/15 * * * *',
    definitionHelp: 'A Cron expression. The annotation time zone controls when it runs.',
  },
  {
    value: 'message',
    label: 'Message',
    description: 'A job that runs when a message arrives.',
    definitionPlaceholder: 'orders.created',
    definitionHelp: 'A message name rule, a message template, or a source name.',
  },
  {
    value: 'volume',
    label: 'Volume',
    description: 'A job that runs when a file or another source changes.',
    definitionPlaceholder: 'incoming/*.csv',
    definitionHelp:
      'A watched location or a source of entities (files, Azure storage, PostgreSQL, Cosmos DB, or a virtual source), with an optional path filter.',
  },
] as const satisfies readonly UnitTypeSuggestion[]

export const genericUnitDefinitionPlaceholder = 'Trigger definition'

export const genericUnitDefinitionHelp =
  'Any text that describes the trigger. Common types are Time, Message, and Volume.'

const cronHintText = 'A Cron expression usually has 5 or 6 fields, for example */15 * * * *.'

/** The key code `unt`, or an annotation whose type is Unit. The key rule matches `withRelations`. */
export function isUnitAnnotation(annotation: {
  AnnotationKey?: string | null
  AnnotationType?: string | null
}): boolean {
  return annotation.AnnotationType === 'Unit' || parseAnnotationKey(annotation.AnnotationKey ?? '')?.code === 'unt'
}

/** The suggestion for this stored value, ignoring case. Any other text has no suggestion. */
export function unitTypeInfo(value: string | null | undefined): UnitTypeSuggestion | undefined {
  const normalized = value?.trim().toLowerCase()
  return unitTypeSuggestions.find((suggestion) => suggestion.value === normalized)
}

/** Suggestions plus the current value when it is not one of them, so the menu can show `event`. */
export function unitTypeMenuItems(
  current: string | null | undefined,
): { label: string; value: string; description: string }[] {
  const items: { label: string; value: string; description: string }[] = unitTypeSuggestions.map((suggestion) => ({
    label: suggestion.label,
    value: suggestion.value,
    description: suggestion.description,
  }))
  const value = current?.trim()
  if (value && !items.some((item) => item.value === value)) {
    items.unshift({ label: value, value, description: 'Custom type' })
  }
  return items
}

/**
 * A hint when a Time definition does not have 5 or 6 fields. Empty text and a matching expression have no hint.
 * The hint never blocks save.
 */
export function cronFieldHint(definition: string | null | undefined): string | undefined {
  const trimmed = definition?.trim() ?? ''
  if (!trimmed) {
    return undefined
  }
  const fields = trimmed.split(/\s+/)
  return fields.length === 5 || fields.length === 6 ? undefined : cronHintText
}
