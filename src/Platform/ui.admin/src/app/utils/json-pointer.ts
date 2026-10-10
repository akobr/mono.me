import { cloneJson } from './clone'

/** RFC 6901 JSON Pointer helpers ("/endpoints/0/url"). */

export function escapeSegment(segment: string | number): string {
  return String(segment).replaceAll('~', '~0').replaceAll('/', '~1')
}

export function toPointer(segments: readonly (string | number)[]): string {
  return segments.map((segment) => `/${escapeSegment(segment)}`).join('')
}

export function parsePointer(pointer: string): string[] {
  const body = pointer.startsWith('#') ? pointer.slice(1) : pointer
  if (body === '' || body === '/') {
    return body === '/' ? [''] : []
  }
  return body
    .slice(1)
    .split('/')
    .map((segment) => decodeURIComponent(segment).replaceAll('~1', '/').replaceAll('~0', '~'))
}

/** JSON Forms data paths are dot paths ("endpoints.0.url"). */
export function fromDataPath(path: string): string {
  return path ? toPointer(path.split('.')) : ''
}

export function getAt(value: unknown, pointer: string | readonly string[]): unknown {
  const segments = typeof pointer === 'string' ? parsePointer(pointer) : pointer
  let current = value
  for (const segment of segments) {
    if (current === null || typeof current !== 'object') {
      return undefined
    }
    current = (current as Record<string, unknown>)[segment]
  }
  return current
}

export function hasAt(value: unknown, pointer: string): boolean {
  const segments = parsePointer(pointer)
  if (segments.length === 0) {
    return value !== undefined
  }
  const parent = getAt(value, segments.slice(0, -1))
  return parent !== null && typeof parent === 'object' && (segments.at(-1) as string) in (parent as object)
}

/** A copy of `value` with the value at `pointer` set (missing objects are created). */
export function setAt<T>(value: T, pointer: string, next: unknown): T {
  const segments = parsePointer(pointer)
  if (segments.length === 0) {
    return next as T
  }
  const root = cloneJson(value ?? {}) as Record<string, unknown>
  let current: Record<string, unknown> = root
  segments.slice(0, -1).forEach((segment, index) => {
    const existing = current[segment]
    if (existing === null || typeof existing !== 'object') {
      current[segment] = /^\d+$/.test(segments[index + 1]!) ? [] : {}
    }
    current = current[segment] as Record<string, unknown>
  })
  current[segments.at(-1)!] = next
  return root as T
}

/** A copy of `value` without the value at `pointer`. */
export function removeAt<T>(value: T, pointer: string): T {
  const segments = parsePointer(pointer)
  if (segments.length === 0 || value === null || typeof value !== 'object') {
    return value
  }
  const root = cloneJson(value) as Record<string, unknown>
  const parent = getAt(root, segments.slice(0, -1))
  if (Array.isArray(parent)) {
    parent.splice(Number(segments.at(-1)), 1)
  } else if (parent && typeof parent === 'object') {
    delete (parent as Record<string, unknown>)[segments.at(-1)!]
  }
  return root as T
}
