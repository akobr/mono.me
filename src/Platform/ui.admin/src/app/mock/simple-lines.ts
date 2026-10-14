import { getAnnotationTypeInfo } from '@/utils/annotation-key'

/** The type of a compact line by its number of segments. */
const compactCodes = ['sbt', 'usg', 'exe', 'uxe'] as const

/**
 * The key the API makes of one entry of SetAnnotationsSimple, read exactly as
 * CosmosAnnotationService.CreateAnnotationsFromStringAsync does: a blank entry is skipped, the entry is split on '.'
 * without trimming and empty segments are dropped. When the first segment is a type code (any case, kept as typed)
 * and the number of segments fits it, the entry is that key; otherwise it is read by its number of segments, 1 to 4
 * (sbt, usg, exe, uxe), and more is skipped. So `exe.a.b` is the Execution `exe.exe.a.b`, and `a..b` is `usg.a.b`.
 * The admin UI sends only canonical keys (utils/bulk-keys.ts); this mirrors the server for other inputs.
 */
export function keyOfSimpleLine(input: string): string | undefined {
  if (!input.trim()) {
    return undefined
  }

  const segments = input.split('.').filter((segment) => segment.length > 0)
  const info = segments[0]?.length === 3 ? getAnnotationTypeInfo(segments[0]) : undefined
  if (info && segments.length === info.segments) {
    return segments.join('.')
  }

  const code = compactCodes[segments.length - 1]
  return code ? [code, ...segments].join('.') : undefined
}
