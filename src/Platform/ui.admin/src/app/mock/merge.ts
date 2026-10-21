/** Deep merge: objects merge key by key, everything else (arrays included) is replaced. */
export function deepMerge(base: unknown, overlay: unknown): unknown {
  if (
    base &&
    overlay &&
    typeof base === 'object' &&
    typeof overlay === 'object' &&
    !Array.isArray(base) &&
    !Array.isArray(overlay)
  ) {
    const result: Record<string, unknown> = { ...(base as Record<string, unknown>) }
    for (const [key, value] of Object.entries(overlay as Record<string, unknown>)) {
      result[key] = key in result ? deepMerge(result[key], value) : structuredClone(value)
    }
    return result
  }
  return structuredClone(overlay)
}
