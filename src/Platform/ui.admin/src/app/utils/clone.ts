/**
 * A deep copy of JSON data. Unlike structuredClone it also copies Vue reactive proxies (query data, props), which
 * structuredClone rejects; configuration documents are plain JSON anyway.
 */
export function cloneJson<T>(value: T): T {
  return value === undefined ? value : (JSON.parse(JSON.stringify(value)) as T)
}
