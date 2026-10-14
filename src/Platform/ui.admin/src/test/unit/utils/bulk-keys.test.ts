import { afterEach, describe, expect, it } from 'vitest'
import { describeBulkProblem, readBulkLine, readBulkLines, withArticle } from '@/utils/bulk-keys'
import { applyTypeAppearance } from '@/utils/type-appearance'

const problemOf = (text: string) => describeBulkProblem(readBulkLine(text, 1))

describe('readBulkLines', () => {
  it('splits on line breaks, trims the lines and the parts, and numbers the lines as typed', () => {
    expect(readBulkLines('  sbt.northwind \r\n\r\nnorthwind.invoicing\n   \n rst . invoicing ')).toEqual([
      { line: 1, text: 'sbt.northwind', code: 'sbt', key: 'sbt.northwind' },
      { line: 3, text: 'northwind.invoicing', key: 'usg.northwind.invoicing' },
      { line: 5, text: 'rst . invoicing', code: 'rst', key: 'rst.invoicing' },
    ])
    expect(readBulkLines('')).toEqual([])
    expect(readBulkLines('\n \r\n\t\n')).toEqual([])
  })

  it('keeps spaces and commas inside a name, because only line breaks separate the keys', () => {
    expect(readBulkLines('northwind traders, inc.invoicing').map((line) => line.key)).toEqual([
      'usg.northwind traders, inc.invoicing',
    ])
  })
})

describe('readBulkLine', () => {
  it('reads a line that starts with a type code as that key, with the code in lower case', () => {
    expect(readBulkLine('rst.invoicing', 1)).toEqual({
      line: 1,
      text: 'rst.invoicing',
      code: 'rst',
      key: 'rst.invoicing',
    })
    expect(readBulkLine('unt.invoicing.pdf', 1).key).toBe('unt.invoicing.pdf')
    expect(readBulkLine('cnt.northwind.prod', 1).key).toBe('cnt.northwind.prod')
    expect(readBulkLine('uxe.northwind.invoicing.prod.pdf', 1).key).toBe('uxe.northwind.invoicing.prod.pdf')
    // the case of the names is kept, only the code is written in lower case
    expect(readBulkLine('EXE.Northwind.invoicing.prod', 2)).toEqual({
      line: 2,
      text: 'EXE.Northwind.invoicing.prod',
      code: 'exe',
      key: 'exe.Northwind.invoicing.prod',
    })
  })

  it('reads a compact line by its number of parts, 1 to 4', () => {
    expect(readBulkLine('northwind', 1)).toEqual({ line: 1, text: 'northwind', key: 'sbt.northwind' })
    expect(readBulkLine('northwind.invoicing', 1).key).toBe('usg.northwind.invoicing')
    expect(readBulkLine('northwind.invoicing.prod', 1).key).toBe('exe.northwind.invoicing.prod')
    expect(readBulkLine('northwind.invoicing.prod.pdf', 1).key).toBe('uxe.northwind.invoicing.prod.pdf')
  })

  it('reads a one-part type code as a subject, and a type name as a name', () => {
    expect(readBulkLine('exe', 1)).toEqual({ line: 1, text: 'exe', key: 'sbt.exe' })
    expect(readBulkLine('RST', 1).key).toBe('sbt.RST')
    // getAnnotationTypeInfo also knows the type names; only the three-letter codes start a key
    expect(readBulkLine('Subject.northwind', 1)).toEqual({
      line: 1,
      text: 'Subject.northwind',
      key: 'usg.Subject.northwind',
    })
  })

  it('finds the problems the API would read with another meaning or drop', () => {
    expect(readBulkLine('a.b.c.d.e', 1)).toEqual({ line: 1, text: 'a.b.c.d.e', problem: 'too-many-parts' })
    // the API would create the Execution exe.exe.a.b
    expect(readBulkLine('exe.a.b', 1)).toEqual({ line: 1, text: 'exe.a.b', code: 'exe', problem: 'wrong-parts' })
    expect(readBulkLine('sbt.a.b', 1).problem).toBe('wrong-parts')
    expect(readBulkLine('uxe.a.b.c', 1).problem).toBe('wrong-parts')
    // the API would drop the empty part and create usg.a.b
    expect(readBulkLine('a..b', 1)).toEqual({ line: 1, text: 'a..b', problem: 'empty-part' })
    expect(readBulkLine('.a', 1).problem).toBe('empty-part')
    expect(readBulkLine('a.', 1).problem).toBe('empty-part')
    expect(readBulkLine('exe.a. .c', 1)).toEqual({ line: 1, text: 'exe.a. .c', code: 'exe', problem: 'empty-part' })
  })

  it('rejects names with a control character or a character the item id cannot hold', () => {
    expect(readBulkLine('northwind\tinvoicing', 1)).toEqual({
      line: 1,
      text: 'northwind\tinvoicing',
      problem: 'invalid-name',
    })
    for (const text of ['a/b', 'sbt.a\\b', 'a.b?c', 'exe.a.b.#c', 'a\u0001b', 'a\u007fb']) {
      expect(readBulkLine(text, 1).problem, text).toBe('invalid-name')
      expect(readBulkLine(text, 1).key, text).toBeUndefined()
    }
  })
})

describe('describeBulkProblem', () => {
  afterEach(() => applyTypeAppearance({}))

  it('names the parts a key of the code has', () => {
    expect(problemOf('exe.a.b')).toBe('An Execution key has 4 parts: exe.subject.responsibility.context')
    expect(problemOf('usg.a')).toBe('A Usage key has 3 parts: usg.subject.responsibility')
    expect(problemOf('uxe.a.b.c')).toBe('A Unit of execution key has 5 parts: uxe.subject.responsibility.context.unit')
    expect(problemOf('rst.a.b')).toBe('A Responsibility key has 2 parts: rst.responsibility')
  })

  it('follows the type names of the project settings', () => {
    applyTypeAppearance({ exe: { label: 'Job' }, sbt: { label: 'Tenant' } })
    expect(problemOf('exe.a.b')).toBe('A Job key has 4 parts: exe.tenant.responsibility.context')
  })

  it('explains the other problems, the tab of a spreadsheet paste first', () => {
    expect(problemOf('a.b.c.d.e')).toBe(
      'Has 5 parts: a line without a type code has at most 4 (subject.responsibility.context.unit).',
    )
    expect(problemOf('a..b')).toBe('Has an empty part: two dots in a row, or a dot at the start or the end.')
    expect(problemOf('northwind\tinvoicing')).toBe(
      'Contains a tab: cells copied from a spreadsheet? Join them with dots.',
    )
    expect(problemOf('a/b')).toBe('Contains "/", which a name cannot hold.')
    expect(problemOf('a\u0001b')).toBe('Contains a control character, which a name cannot hold.')
    expect(problemOf('northwind.invoicing')).toBe('')
  })
})

describe('withArticle', () => {
  it('takes "an" before a vowel sound and "a" before the "you" of Unit and Usage', () => {
    expect(withArticle('Execution')).toBe('an Execution')
    expect(withArticle('Order')).toBe('an Order')
    expect(withArticle('Umbrella')).toBe('an Umbrella')
    expect(withArticle('Subject')).toBe('a Subject')
    expect(withArticle('Usage')).toBe('a Usage')
    expect(withArticle('Unit of execution')).toBe('a Unit of execution')
  })
})
