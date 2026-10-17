import { describe, expect, it } from 'vitest'
import {
  canDescend,
  describe as describeDocument,
  getAnnotationFileName,
  getAnnotationSchemaFileName,
  getArchiveFileName,
  getConfigurationFileName,
  getContent,
  getDescendantTypeSchemaFileName,
  getFileName,
  getFolder,
  getPath,
  getTemplateFileName,
  getTypeSchemaFileName,
  identityOf,
  isSkippedPath,
  parseArchiveName,
  type ViewDocumentKind,
} from '@/mock/archive-names'

// The cases of ViewArchiveNamesTests (Backend.Core), so the mock names and reads files like the API.

type Parsed = [kind: ViewDocumentKind, type: string | undefined, key: string | undefined, version: number | undefined]

/** The document of a name that must parse without an error. */
function parse(fileName: string): Parsed {
  const name = parseArchiveName(fileName)
  expect(name, fileName).toBeDefined()
  expect(name!.errorCode, fileName).toBeUndefined()
  return [name!.kind, name!.type, name!.key, name!.version]
}

describe('file names of the documents', () => {
  it.each([
    ['rst.payments', 'rst.payments.json'],
    ['usg.billing.payments', 'usg.billing.payments.json'],
    ['exe.northwind.payments.prod', 'exe.northwind.payments.prod.json'],
    ['uxe.billing.payments.prod.nightly', 'uxe.billing.payments.prod.nightly.json'],
  ])('names the annotation %s by its key', (key, expected) => {
    expect(getAnnotationFileName(key)).toBe(expected)
  })

  it('gives every kind its suffix and the version before .json', () => {
    expect(getConfigurationFileName('rst.payments')).toBe('rst.payments.conf.json')
    expect(getConfigurationFileName('rst.payments', 3)).toBe('rst.payments.conf.3.json')
    expect(getTemplateFileName('exe')).toBe('exe.template.json')
    expect(getTemplateFileName('exe', 4)).toBe('exe.template.4.json')
    expect(getTemplateFileName('EXE')).toBe('exe.template.json')
    expect(getTypeSchemaFileName('exe')).toBe('exe.schema.json')
    expect(getTypeSchemaFileName('exe', 1)).toBe('exe.schema.1.json')
    expect(getAnnotationSchemaFileName('rst.payments')).toBe('rst.payments.schema.json')
    expect(getAnnotationSchemaFileName('rst.payments', 2)).toBe('rst.payments.schema.2.json')
    expect(getDescendantTypeSchemaFileName('rst.payments', 'exe')).toBe('rst.payments.exe.schema.json')
    expect(getDescendantTypeSchemaFileName('sbt.northwind', 'EXE', 2)).toBe('sbt.northwind.exe.schema.2.json')
  })

  it.each<[ViewDocumentKind, string | undefined, string | undefined, number | undefined, string]>([
    ['Annotation', undefined, 'rst.payments', undefined, 'rst.payments.json'],
    ['Configuration', undefined, 'rst.payments', 3, 'rst.payments.conf.3.json'],
    ['Template', 'exe', undefined, 4, 'exe.template.4.json'],
    ['TypeSchema', 'exe', undefined, undefined, 'exe.schema.json'],
    ['AnnotationSchema', undefined, 'rst.payments', undefined, 'rst.payments.schema.json'],
    ['DescendantTypeSchema', 'exe', 'sbt.northwind', 2, 'sbt.northwind.exe.schema.2.json'],
  ])('names a document of the kind %s', (kind, type, key, version, expected) => {
    expect(getFileName(kind, type, key, version)).toBe(expected)
  })

  it('rejects a version of an annotation and missing parts', () => {
    expect(() => getFileName('Annotation', undefined, 'rst.payments', 1)).toThrow(RangeError)
    expect(() => getFileName('Configuration', undefined, undefined)).toThrow(TypeError)
    expect(() => getFileName('DescendantTypeSchema', undefined, 'rst.payments')).toThrow(TypeError)
    expect(() => getTemplateFileName('xyz')).toThrow(RangeError)
  })

  it.each([
    ['rst.a:b', 'rst.a%3Ab.json'],
    ['rst.50%', 'rst.50%25.json'],
    ['sbt.<x>|y?*"', 'sbt.%3Cx%3E%7Cy%3F%2A%22.json'],
    ['rst.tab\there', 'rst.tab%09here.json'],
    ['rst.café', 'rst.café.json'],
  ])('encodes the characters that file systems reject in %s', (key, expected) => {
    expect(getAnnotationFileName(key)).toBe(expected)
  })
})

describe('parsing file names', () => {
  // the documents of the file-name tables (1.3) and of the archive layout (1.4)
  it.each<[string, ...Parsed]>([
    ['rst.payments.json', 'Annotation', undefined, 'rst.payments', undefined],
    ['usg.northwind.payments.json', 'Annotation', undefined, 'usg.northwind.payments', undefined],
    ['exe.northwind.payments.prod.json', 'Annotation', undefined, 'exe.northwind.payments.prod', undefined],
    ['rst.payments.conf.json', 'Configuration', undefined, 'rst.payments', undefined],
    ['rst.payments.conf.3.json', 'Configuration', undefined, 'rst.payments', 3],
    ['rst.payments.conf.1.json', 'Configuration', undefined, 'rst.payments', 1],
    ['exe.northwind.payments.prod.conf.json', 'Configuration', undefined, 'exe.northwind.payments.prod', undefined],
    ['exe.template.json', 'Template', 'exe', undefined, undefined],
    ['exe.template.4.json', 'Template', 'exe', undefined, 4],
    ['exe.template.3.json', 'Template', 'exe', undefined, 3],
    ['exe.schema.json', 'TypeSchema', 'exe', undefined, undefined],
    ['exe.schema.1.json', 'TypeSchema', 'exe', undefined, 1],
    ['rst.payments.schema.json', 'AnnotationSchema', undefined, 'rst.payments', undefined],
    ['rst.payments.schema.2.json', 'AnnotationSchema', undefined, 'rst.payments', 2],
    ['rst.payments.exe.schema.json', 'DescendantTypeSchema', 'exe', 'rst.payments', undefined],
    ['sbt.northwind.exe.schema.json', 'DescendantTypeSchema', 'exe', 'sbt.northwind', undefined],
    ['sbt.northwind.exe.schema.2.json', 'DescendantTypeSchema', 'exe', 'sbt.northwind', 2],
    ['rst.%73chema.json', 'Annotation', undefined, 'rst.schema', undefined],
  ])('tells the document of %s', (fileName, ...expected) => {
    expect(parse(fileName)).toEqual(expected)
  })

  it('takes no version of an annotation', () => {
    // a version file of PR #88: annotations have no versions
    expect(parseArchiveName('rst.payments.3.json')).toBeUndefined()
    // a usage has three segments, so "3" is the responsibility's name
    expect(parse('usg.payments.3.json')).toEqual(['Annotation', undefined, 'usg.payments.3', undefined])
  })

  it.each([
    ['rst.schema', 'rst.%73chema.json'],
    ['rst.template', 'rst.%74emplate.json'],
    ['sbt.Schema', 'sbt.%53chema.json'],
    ['sbt.TEMPLATE', 'sbt.%54EMPLATE.json'],
    ['unt.template.3', 'unt.%74emplate.3.json'],
    ['usg.schema.payments', 'usg.%73chema.payments.json'],
  ])('encodes and decodes the reserved second segment of %s', (key, fileName) => {
    expect(getAnnotationFileName(key)).toBe(fileName)
    expect(parse(fileName)).toEqual(['Annotation', undefined, key, undefined])
  })

  it('encodes the reserved second segment for every kind of the key', () => {
    expect(getConfigurationFileName('rst.schema', 2)).toBe('rst.%73chema.conf.2.json')
    expect(getAnnotationSchemaFileName('rst.template')).toBe('rst.%74emplate.schema.json')
    expect(getDescendantTypeSchemaFileName('sbt.schema', 'exe')).toBe('sbt.%73chema.exe.schema.json')

    expect(parse('rst.%73chema.conf.2.json')).toEqual(['Configuration', undefined, 'rst.schema', 2])
    expect(parse('sbt.%73chema.exe.schema.json')).toEqual(['DescendantTypeSchema', 'exe', 'sbt.schema', undefined])
  })

  it.each([
    ['rst.schemas', 'rst.schemas.json'],
    ['rst.myschema', 'rst.myschema.json'],
    ['usg.payments.schema', 'usg.payments.schema.json'],
    ['exe.s.template.c', 'exe.s.template.c.json'],
  ])('reserves only the second segment: %s', (key, fileName) => {
    expect(getAnnotationFileName(key)).toBe(fileName)
    expect(parse(fileName)).toEqual(['Annotation', undefined, key, undefined])
  })

  it('reads a literal reserved segment as a file of the type', () => {
    // not encoded, so these are the type's files, not the annotations rst.schema and unt.template.3
    expect(parse('rst.schema.json')).toEqual(['TypeSchema', 'rst', undefined, undefined])
    expect(parse('unt.template.3.json')).toEqual(['Template', 'unt', undefined, 3])
  })

  it.each([
    'rst.a:b',
    'rst.50%',
    'sbt.<x>|y?*"',
    'cnt.subject.prod',
    'exe.s.r.c',
    'uxe.s.r.c.u',
    'unt.r.u%25',
    'rst.schema',
    'unt.Template.3',
  ])('reverses the builders of the key %s', (key) => {
    expect(parse(getAnnotationFileName(key))).toEqual(['Annotation', undefined, key, undefined])
    expect(parse(getConfigurationFileName(key))).toEqual(['Configuration', undefined, key, undefined])
    expect(parse(getConfigurationFileName(key, 7))).toEqual(['Configuration', undefined, key, 7])
    expect(parse(getAnnotationSchemaFileName(key))).toEqual(['AnnotationSchema', undefined, key, undefined])
    expect(parse(getAnnotationSchemaFileName(key, 2))).toEqual(['AnnotationSchema', undefined, key, 2])
  })

  it.each(['rst', 'sbt', 'usg', 'cnt', 'exe', 'unt', 'uxe'])('reverses the builders of the type %s', (type) => {
    expect(parse(getTemplateFileName(type))).toEqual(['Template', type, undefined, undefined])
    expect(parse(getTemplateFileName(type, 5))).toEqual(['Template', type, undefined, 5])
    expect(parse(getTypeSchemaFileName(type))).toEqual(['TypeSchema', type, undefined, undefined])
    expect(parse(getTypeSchemaFileName(type, 9))).toEqual(['TypeSchema', type, undefined, 9])
  })

  it.each([
    ['rst.payments', 'unt'],
    ['rst.payments', 'usg'],
    ['rst.payments', 'exe'],
    ['sbt.shop', 'cnt'],
    ['sbt.shop', 'uxe'],
    ['usg.shop.payments', 'exe'],
    ['cnt.shop.prod', 'uxe'],
    ['exe.shop.payments.prod', 'uxe'],
    ['unt.payments.nightly', 'uxe'],
  ])('reverses the builder of the %s schema of %s', (key, type) => {
    expect(parse(getDescendantTypeSchemaFileName(key, type))).toEqual(['DescendantTypeSchema', type, key, undefined])
    expect(parse(getDescendantTypeSchemaFileName(key, type, 3))).toEqual(['DescendantTypeSchema', type, key, 3])
  })

  it.each([
    'export.json',
    'README.md',
    '.json',
    'rst.json',
    'rst.payments.extra.json',
    'rst.payments.extra.more.json',
    'rst.payments.template.json',
    'rst.payments.conf.x.json',
    'rst.payments.conf.1.2.json',
    'rst.payments.conf.-1.json',
    'rst.payments.schema.x.json',
    'rst.payments.exe.conf.json',
    'rst.payments.xyz.schema.json',
    'rst.payments.exe.schema.x.json',
    'rst.payments.exe.schema.1.2.json',
    'exe.template.x.json',
    'exe.template.1.2.json',
    'exe.schema.abc.json',
    'exe.s.r.json',
    'xyz.payments.json',
    'xyz.template.json',
    'rst..json',
    'rst.payments..json',
    'rst.payments.-1.json',
    'rst.payments.json.bak',
    'rst.bad%2.json',
    'rst.bad%ZZ.json',
    'rst.a%2Eb.json',
  ])('rejects %s, which is not a document', (fileName) => {
    expect(parseArchiveName(fileName)).toBeUndefined()
  })

  it.each([
    ['exe.s.r.c.rst.schema.json', 'rst', 'exe.s.r.c'],
    ['rst.payments.rst.schema.json', 'rst', 'rst.payments'],
    ['rst.payments.cnt.schema.json', 'cnt', 'rst.payments'],
    ['unt.payments.nightly.exe.schema.3.json', 'exe', 'unt.payments.nightly'],
    ['uxe.s.r.c.u.uxe.schema.json', 'uxe', 'uxe.s.r.c.u'],
  ])('reports %s, a descendant type that cannot descend from the key', (fileName, type, key) => {
    // still a document, so the import reports it instead of ignoring it
    const name = parseArchiveName(fileName)

    expect(name).toMatchObject({ kind: 'DescendantTypeSchema', type, key, errorCode: 'InvalidKey' })
    expect(name!.message).toBeTruthy()
  })

  it('follows the annotation hierarchy for descendant types', () => {
    expect(canDescend('rst.payments', 'unt')).toBe(true)
    expect(canDescend('rst.payments', 'UXE')).toBe(true)
    expect(canDescend('sbt.shop', 'cnt')).toBe(true)
    expect(canDescend('sbt.shop', 'unt')).toBe(false)
    expect(canDescend('cnt.shop.prod', 'exe')).toBe(true)
    expect(canDescend('cnt.shop.prod', 'usg')).toBe(false)
    expect(canDescend('exe.s.r.c', 'exe')).toBe(false)
    expect(canDescend('uxe.s.r.c.u', 'uxe')).toBe(false)
  })

  it.each<[string, ...Parsed]>([
    ['RST.payments.json', 'Annotation', undefined, 'rst.payments', undefined],
    ['rst.payments.JSON', 'Annotation', undefined, 'rst.payments', undefined],
    ['rst.payments.CONF.json', 'Configuration', undefined, 'rst.payments', undefined],
    ['Rst.payments.Conf.2.Json', 'Configuration', undefined, 'rst.payments', 2],
    ['EXE.Template.JSON', 'Template', 'exe', undefined, undefined],
    ['exe.SCHEMA.2.json', 'TypeSchema', 'exe', undefined, 2],
    ['rst.payments.Schema.json', 'AnnotationSchema', undefined, 'rst.payments', undefined],
    ['rst.payments.EXE.Schema.json', 'DescendantTypeSchema', 'exe', 'rst.payments', undefined],
    ['rst.Payments.json', 'Annotation', undefined, 'rst.Payments', undefined],
  ])('takes type codes and suffixes in any case: %s', (fileName, ...expected) => {
    // type codes are lower-cased; the names of a key keep their case
    expect(parse(fileName)).toEqual(expected)
  })

  it('gives encoded and plain spellings of a document the same identity', () => {
    const identity = (fileName: string) => identityOf(parseArchiveName(fileName)!)
    const plain = identity('rst.payments.json')

    expect(identity('rst.pay%6Dents.json')).toBe(plain)
    expect(identity('RST.payments.json')).toBe(plain)
    expect(identity('rst.payments.conf.json')).not.toBe(plain)
    expect(identity('rst.payments.conf.4.json')).toBe(identity('rst.payments.conf.json'))
    expect(identity('exe.template.json')).not.toBe(identity('exe.schema.json'))
  })
})

describe('archive layout', () => {
  it.each<[ViewDocumentKind, string]>([
    ['Annotation', 'Annotations'],
    ['TypeSchema', 'Schemas'],
    ['DescendantTypeSchema', 'Schemas'],
    ['AnnotationSchema', 'Schemas'],
    ['Template', 'Templates'],
    ['Configuration', 'Configurations'],
  ])('groups the kind %s into %s', (kind, content) => {
    expect(getContent(kind)).toBe(content)
  })

  it.each([
    '.git/HEAD',
    '.git/rst.inside.json',
    'repo/.git/config',
    '.github/workflows/rst.payments.json',
    'export/.vscode/settings.json',
    'annotations/.DS_Store',
    '._rst.payments.json',
    'annotations/._rst.payments.json',
    '.hidden.json',
    '__MACOSX/annotations/rst.payments.json',
    '',
  ])('skips the hidden or Mac path %j', (path) => {
    expect(isSkippedPath(path)).toBe(true)
  })

  it.each([
    'rst.payments.json',
    'annotations/rst.payments.json',
    'export/configurations/rst.payments.conf.json',
    './rst.payments.json',
    'a.b/rst.payments.json',
  ])('keeps the path %s', (path) => {
    expect(isSkippedPath(path)).toBe(false)
  })

  it.each<[ViewDocumentKind, string | undefined, string | undefined, number | undefined, string]>([
    ['Annotation', undefined, 'rst.payments', undefined, 'annotations/rst.payments.json'],
    ['TypeSchema', 'exe', undefined, 1, 'schemas/exe.schema.1.json'],
    ['DescendantTypeSchema', 'exe', 'sbt.northwind', undefined, 'schemas/sbt.northwind.exe.schema.json'],
    ['AnnotationSchema', undefined, 'rst.schema', undefined, 'schemas/rst.%73chema.schema.json'],
    ['Template', 'EXE', undefined, 3, 'templates/exe.template.3.json'],
    ['Configuration', undefined, 'rst.payments', undefined, 'configurations/rst.payments.conf.json'],
  ])('puts a document of the kind %s into its folder', (kind, type, key, version, expected) => {
    expect(getPath(kind, type, key, version)).toBe(expected)
  })

  it('names the folders after the kinds in lower case', () => {
    expect(
      ['Annotations', 'Schemas', 'Templates', 'Configurations'].map((content) => getFolder(content as never)),
    ).toEqual(['annotations', 'schemas', 'templates', 'configurations'])
  })

  it.each<[ViewDocumentKind, string | undefined, string | undefined, string]>([
    ['Annotation', undefined, 'rst.payments', 'annotation rst.payments'],
    ['TypeSchema', 'exe', undefined, 'type schema exe'],
    ['DescendantTypeSchema', 'exe', 'sbt.northwind', 'exe schema below sbt.northwind'],
    ['AnnotationSchema', undefined, 'rst.payments', 'schema of rst.payments'],
    ['Template', 'exe', undefined, 'template exe'],
    ['Configuration', undefined, 'rst.payments', 'configuration of rst.payments'],
  ])('describes a document of the kind %s', (kind, type, key, expected) => {
    expect(describeDocument(kind, type, key)).toBe(expected)
  })

  it('names an export with its time in UTC', () => {
    const createdAt = new Date('2026-10-14T03:30:12+02:00')

    expect(getArchiveFileName('acme', 'main', 'default', 'Folder', createdAt)).toBe(
      'export-acme-main-default-20261014-013012Z.zip',
    )
    expect(getArchiveFileName('acme', 'main', 'default', 'Git', createdAt)).toBe(
      'export-acme-main-default-git-20261014-013012Z.zip',
    )
  })
})
