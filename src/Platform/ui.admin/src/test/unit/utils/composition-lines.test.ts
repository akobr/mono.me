import { describe, expect, it } from 'vitest'
import { compositionLines, type CompositionLine } from '@/utils/composition-lines'
import { toJson } from '@/utils/json-yaml'

// The blame lines of the Composition view (424-36): the text of toJson, with the step of every line by pointer.
const of = (content: unknown, origins: [string, number][]) =>
  compositionLines(
    content,
    origins.map(([Path, Step]) => ({ Path, Step })),
  )
const text = (lines: CompositionLine[]) => lines.map((line) => line.text).join('\n')
const steps = (lines: CompositionLine[]) => lines.map((line) => [line.text.trim(), line.step])

describe('compositionLines', () => {
  it('gives each leaf line the step of its value and none to the root braces', () => {
    expect(
      of({ retries: 3, mode: 'batch' }, [
        ['/retries', 6],
        ['/mode', 3],
      ]),
    ).toEqual([
      { text: '{', pointer: '' },
      { text: '  "retries": 3,', pointer: '/retries', step: 6 },
      { text: '  "mode": "batch"', pointer: '/mode', step: 3 },
      { text: '}', pointer: '' },
    ])
  })

  it('gives the opening and the closing line of a container the step of the container', () => {
    const lines = of({ database: { hosts: ['a', 'b'], port: 5432 } }, [
      ['/database', 1],
      ['/database/hosts', 1],
      ['/database/hosts/0', 1],
      ['/database/hosts/1', 2],
      ['/database/port', 3],
    ])

    expect(steps(lines)).toEqual([
      ['{', undefined],
      ['"database": {', 1],
      ['"hosts": [', 1],
      ['"a",', 1],
      ['"b"', 2],
      ['],', 1],
      ['"port": 5432', 3],
      ['}', 1],
      ['}', undefined],
    ])
    expect(lines.map((line) => line.pointer)).toEqual([
      '',
      '/database',
      '/database/hosts',
      '/database/hosts/0',
      '/database/hosts/1',
      '/database/hosts',
      '/database/port',
      '/database',
      '',
    ])
  })

  it('writes an empty container on one line with its step', () => {
    expect(
      steps(
        of({ a: {}, b: [], c: [{}] }, [
          ['/a', 1],
          ['/b', 2],
          ['/c', 3],
          ['/c/0', 4],
        ]),
      ),
    ).toEqual([
      ['{', undefined],
      ['"a": {},', 1],
      ['"b": [],', 2],
      ['"c": [', 3],
      ['{}', 4],
      [']', 3],
      ['}', undefined],
    ])
  })

  it('escapes the names in the text and in the pointers', () => {
    const lines = of({ 'a/b': 1, 'c~d': { 'e"f': 'x\ny' } }, [
      ['/a~1b', 1],
      ['/c~0d', 2],
      ['/c~0d/e"f', 3],
    ])

    expect(lines.map((line) => [line.text, line.pointer, line.step])).toEqual([
      ['{', '', undefined],
      ['  "a/b": 1,', '/a~1b', 1],
      ['  "c~d": {', '/c~0d', 2],
      ['    "e\\"f": "x\\ny"', '/c~0d/e"f', 3],
      ['  }', '/c~0d', 2],
      ['}', '', undefined],
    ])
  })

  it('looks the steps up by pointer: JSON.parse moves integer-like keys first', () => {
    const content = JSON.parse('{ "b": 1, "10": 2, "2": 3 }') as unknown
    // in the server's order, which is the order of the stored document
    const lines = of(content, [
      ['/b', 1],
      ['/10', 2],
      ['/2', 3],
    ])

    expect(steps(lines)).toEqual([
      ['{', undefined],
      ['"2": 3,', 3],
      ['"10": 2,', 2],
      ['"b": 1', 1],
      ['}', undefined],
    ])
  })

  it('writes the text of toJson', () => {
    const documents: unknown[] = [
      {},
      [],
      null,
      undefined,
      { a: null, b: true, c: false, d: -0, e: 1.5e-7, f: 'žluťoučký 🐎', g: [[1, [2]], [], {}] },
      { nested: { deeper: { deepest: [{ x: 1 }, { y: [null] }] } }, '': 'empty name', '7': 'seven' },
    ]

    for (const document of documents) {
      expect(text(compositionLines(document, []))).toBe(toJson(document))
    }
  })

  it('leaves a line without a step when no origin names its value', () => {
    expect(steps(of({ a: 1, b: 2 }, [['/a', 1]]))).toEqual([
      ['{', undefined],
      ['"a": 1,', 1],
      ['"b": 2', undefined],
      ['}', undefined],
    ])
  })
})
