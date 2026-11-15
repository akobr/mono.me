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

const isObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)

/** ASCII case folding (A-Z only), as the API matches the member names of project settings. */
const foldAscii = (value: string) => value.replace(/[A-Z]/g, (letter) => letter.toLowerCase())

/**
 * JSON Merge Patch (RFC 7396) as the API applies it to project settings (ProjectSettingsJson.ApplyMergePatch): null
 * removes a member, an object merges into an object, any other value replaces; a member name matches the target's in
 * any ASCII case and keeps the target's spelling. Neither input changes. The names of the patch are not checked (two
 * that differ only in case apply in their order).
 */
export function applyMergePatch(target: unknown, patch: unknown): unknown {
  if (!isObject(patch)) {
    return structuredClone(patch)
  }
  const result: Record<string, unknown> = isObject(target) ? structuredClone(target) : {}
  for (const [name, value] of Object.entries(patch)) {
    const matches = Object.keys(result).filter((key) => foldAscii(key) === foldAscii(name))
    if (value === null) {
      matches.forEach((match) => delete result[match])
      continue
    }
    const key = matches[0] ?? name
    matches.slice(1).forEach((match) => delete result[match])
    result[key] = applyMergePatch(result[key], value)
  }
  return result
}
