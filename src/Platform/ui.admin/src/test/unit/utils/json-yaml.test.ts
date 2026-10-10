import { describe, expect, it } from 'vitest'
import { hasUnsafeInteger, parseDocument, serialize, toJson, toYaml } from '@/utils/json-yaml'

describe('json-yaml', () => {
  const value = { zeta: 1, alpha: { list: [1, 'two', true, null], text: 'a: b' }, empty: {} }

  it('round-trips JSON and YAML and keeps the key order', () => {
    const yaml = toYaml(value)
    const json = toJson(value)

    expect(Object.keys(JSON.parse(json))).toEqual(['zeta', 'alpha', 'empty'])
    expect(yaml.split('\n')[0]).toBe('zeta: 1')
    expect(parseDocument(yaml, 'yaml')).toEqual({ ok: true, value, warning: undefined })
    expect(parseDocument(json, 'json')).toEqual({ ok: true, value, warning: undefined })
  })

  it('serializes JSON with two spaces and YAML without line wrapping', () => {
    expect(serialize({ a: [1] }, 'json')).toBe('{\n  "a": [\n    1\n  ]\n}')
    expect(serialize({ long: 'x'.repeat(200) }, 'yaml')).toBe(`long: ${'x'.repeat(200)}\n`)
  })

  it('reports syntax errors with a line', () => {
    const json = parseDocument('{\n  "a": 1,\n  "b": \n}', 'json')
    const yaml = parseDocument('a: 1\na: 2\n', 'yaml')

    expect(json).toMatchObject({ ok: false, line: 4 })
    expect(yaml).toMatchObject({ ok: false, line: 2 })
    expect(parseDocument('  ', 'json')).toMatchObject({ ok: false, message: 'The document is empty.' })
  })

  it('warns about integers beyond 2^53', () => {
    expect(hasUnsafeInteger('{"id": 12345678901234567890}')).toBe(true)
    expect(hasUnsafeInteger('{"id": 9007199254740991, "text": "12345678901234567890"}')).toBe(false)
    expect(parseDocument('id: 12345678901234567890', 'yaml')).toMatchObject({
      ok: true,
      warning: expect.stringContaining('2^53'),
    })
  })
})
