// JSON helpers shared by the configuration formats and the project settings; not part of the package's API.

const plainName = /^[A-Za-z_][A-Za-z0-9_-]*$/

// The JSON path of a member, as JsonPaths of the .NET library ConfigurationFormats prints it: .name for a plain name,
// ['name'] for any other: \ and ' escaped, C0 controls and DEL as \n, \r, \t or \uXXXX, and # as #, so a path never
// holds a line break or a #.
export function toPropertyPath(parent: string, name: string): string {
  if (plainName.test(name)) {
    return `${parent}.${name}`
  }

  let escaped = ''

  for (const unit of name.split('')) {
    const code = unit.charCodeAt(0)

    if (unit === '\\' || unit === '\'') {
      escaped += `\\${unit}`
    } else if (unit === '\n') {
      escaped += '\\n'
    } else if (unit === '\r') {
      escaped += '\\r'
    } else if (unit === '\t') {
      escaped += '\\t'
    } else if (code < 0x20 || code === 0x7f || unit === '#') {
      escaped += `\\u${code.toString(16).toUpperCase().padStart(4, '0')}`
    } else {
      escaped += unit
    }
  }

  return `${parent}['${escaped}']`
}

// An object of JSON.parse, or one with a null prototype; a Date or a Map is not JSON data.
export function isPlainObject(value: unknown): value is Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return false
  }

  const prototype = Object.getPrototypeOf(value) as unknown
  return prototype === Object.prototype || prototype === null
}
