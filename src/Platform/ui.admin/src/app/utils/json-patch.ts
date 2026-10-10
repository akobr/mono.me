import { compare } from 'fast-json-patch'
import type { JsonPatchOperation } from '@42for.net/2splatform.sdk'
import { getAt, hasAt } from './json-pointer'

/**
 * The JSON Patch for a configuration save (spec section 11.2):
 * 1. `compare(original, edited)`; an empty diff means nothing to save;
 * 2. a `test` with the original value before every `replace` and `remove`, so a document changed by someone else
 *    since it was loaded fails with 412 instead of being overwritten (optimistic concurrency without ETags).
 */
export function buildPatch(original: unknown, edited: unknown): JsonPatchOperation[] {
  const base = (original ?? {}) as object
  const operations = compare(base, (edited ?? {}) as object) as JsonPatchOperation[]
  const guarded: JsonPatchOperation[] = []
  const tested = new Set<string>()

  for (const operation of operations) {
    if (
      (operation.op === 'replace' || operation.op === 'remove') &&
      !tested.has(operation.path) &&
      hasAt(base, operation.path)
    ) {
      guarded.push({ op: 'test', path: operation.path, value: getAt(base, operation.path) })
      tested.add(operation.path)
    }
    guarded.push(operation)
  }

  return guarded
}

/** Only the changing operations (no tests), for a summary like "3 changes". */
export function changeCount(patch: readonly JsonPatchOperation[]): number {
  return patch.filter((operation) => operation.op !== 'test').length
}
