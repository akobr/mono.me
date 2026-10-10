/**
 * Binding expressions (docs/Platform/Storyteller/binding.md): a string starting with "@" references another
 * value, and an object with $jsonlogic, $jsone or $definition is an envelope the server evaluates. The UI does not
 * evaluate them; it marks them and skips client-side schema checks on them.
 */
export const envelopeKeys = ['$jsonlogic', '$jsone', '$definition'] as const

export function isBindingString(value: unknown): value is string {
  return typeof value === 'string' && value.startsWith('@')
}

export function isEnvelope(value: unknown): value is Record<string, unknown> {
  return (
    value !== null && typeof value === 'object' && !Array.isArray(value) && envelopeKeys.some((key) => key in value)
  )
}

export function isBinding(value: unknown): boolean {
  return isBindingString(value) || isEnvelope(value)
}
