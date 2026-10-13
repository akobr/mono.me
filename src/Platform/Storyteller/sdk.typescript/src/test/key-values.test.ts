import { describe, expect, it } from 'vitest'
import {
  detectKeyValueFormat,
  flattenJson,
  formatSecretReference,
  fromEnvKey,
  isSecretProtection,
  isSecretReference,
  isValidKeyValueKey,
  normalizeKeyValueKey,
  parseDotEnv,
  parseSecretReference,
  serializeDotEnv,
  toEnvKey,
  toFlatJson,
  toKeyValueDocument,
  unflattenJson,
} from '../lib/key-values'

describe('keys', () => {
  it.each(['db', 'db.password', 'Db.Password', '_private', 'a1.b2.c3', 'x'.repeat(128)])('accepts %s', (key) => {
    expect(isValidKeyValueKey(key)).toBe(true)
  })

  it.each(['', '1db', 'db.', '.db', 'db..password', 'db-password', 'db password', 'db.1x', 'x'.repeat(129), null, undefined])(
    'rejects %j',
    (key) => {
      expect(isValidKeyValueKey(key)).toBe(false)
    },
  )

  it('normalizes like the server', () => {
    expect(normalizeKeyValueKey(' Db.Password ')).toBe('db.password')
    expect(isSecretProtection('Public')).toBe(false)
    expect(isSecretProtection('Standard')).toBe(true)
    expect(isSecretProtection(undefined)).toBe(false)
  })
})

describe('.env', () => {
  it('parses comments, export, quotes and escapes', () => {
    const text = [
      '# database',
      'DB__HOST=sql.internal   # inline comment',
      'export DB__PORT=5432',
      'DB__PASSWORD="p#ss \\"word\\"\\nline2"',
      "API__KEY='keep $this #raw'",
      '',
      'EMPTY=',
    ].join('\n')

    expect(parseDotEnv(text)).toEqual([
      { key: 'db.host', value: 'sql.internal' },
      { key: 'db.port', value: '5432' },
      { key: 'db.password', value: 'p#ss "word"\nline2' },
      { key: 'api.key', value: 'keep $this #raw' },
      { key: 'empty', value: '' },
    ])
  })

  it('keeps the keys when asked, and rejects lines without =', () => {
    expect(parseDotEnv('Db_Host=x', { normalizeKeys: false })).toEqual([{ key: 'Db_Host', value: 'x' }])
    expect(() => parseDotEnv('just text')).toThrow(/Line 1/)
    expect(() => parseDotEnv('A="open')).toThrow(/unterminated/)
  })

  it('serializes with quoting where needed, and round-trips', () => {
    const entries = [
      { key: 'db.host', value: 'sql.internal' },
      { key: 'db.password', value: 'p#ss "word"\nline2' },
      { key: 'note', value: 'two words' },
      { key: 'empty', value: '' },
    ]
    const text = serializeDotEnv(entries)

    expect(text.split('\n')).toEqual([
      'DB__HOST=sql.internal',
      'DB__PASSWORD="p#ss \\"word\\"\\nline2"',
      'NOTE="two words"',
      'EMPTY=""',
    ])
    expect(parseDotEnv(text)).toEqual(entries)
    expect(toEnvKey('db.password')).toBe('DB__PASSWORD')
    expect(fromEnvKey('DB__PASSWORD')).toBe('db.password')
  })
})

describe('JSON', () => {
  it('flattens and unflattens', () => {
    const nested = { db: { host: 'sql.internal', port: 5432, ssl: true, tags: ['a', 'b'] }, name: 'billing', empty: null }
    const flat = flattenJson(nested)

    expect(flat).toEqual({
      'db.host': 'sql.internal',
      'db.port': '5432',
      'db.ssl': 'true',
      'db.tags': '["a","b"]',
      name: 'billing',
      empty: '',
    })
    expect(unflattenJson({ 'db.host': 'x', 'db.port': '1', name: 'n' })).toEqual({ db: { host: 'x', port: '1' }, name: 'n' })
    expect(() => unflattenJson({ db: 'x', 'db.host': 'y' })).toThrow(/conflicts/)
    expect(() => flattenJson('text')).toThrow(/object/)
  })

  it('builds and reads documents', () => {
    const document = toKeyValueDocument({ 'db.host': 'x' })

    expect(document).toEqual({ Format: '2s.key-values.v1', Items: [{ Key: 'db.host', Protection: 'Public', Value: 'x', ContentType: undefined }] })
    expect(toKeyValueDocument([{ key: 'k', value: 'v' }], { protection: 'Standard', contentType: 'text/plain' }).Items?.[0]).toMatchObject({
      Protection: 'Standard',
      ContentType: 'text/plain',
    })
    expect(toFlatJson({ Format: '2s.key-values.v1', Items: [{ Key: 'a', Protection: 'Public', Value: '1' }, { Key: 's', Protection: 'Standard' }] })).toEqual({ a: '1' })
  })

  it('detects formats', () => {
    expect(detectKeyValueFormat('{ "Format": "2s.key-values.v1", "Items": [] }')).toBe('document')
    expect(detectKeyValueFormat('{ "db.host": "x", "n": 1 }')).toBe('flat-json')
    expect(detectKeyValueFormat('{ "db": { "host": "x" } }')).toBe('json')
    expect(detectKeyValueFormat('# c\nDB__HOST=x\nexport A=1')).toBe('dotenv')
    expect(detectKeyValueFormat('{ broken')).toBe('unknown')
    expect(detectKeyValueFormat('just words')).toBe('unknown')
    expect(detectKeyValueFormat('   ')).toBe('unknown')
  })
})

describe('references', () => {
  it('parses the shorthand, the URI and the App Configuration form', () => {
    expect(parseSecretReference('kv://my-vault/db-password')).toEqual({
      vault: 'my-vault',
      name: 'db-password',
      version: undefined,
      uri: 'https://my-vault.vault.azure.net/secrets/db-password',
    })
    expect(parseSecretReference('https://My-Vault.vault.azure.net/secrets/db-password/0123456789abcdef0123456789abcdef')?.version).toBe(
      '0123456789abcdef0123456789abcdef',
    )
    expect(parseSecretReference('{"uri":"https://v.vault.azure.net/secrets/s"}', 'application/vnd.microsoft.appconfig.keyvaultref+json;charset=utf-8')).toMatchObject({
      vault: 'v',
      name: 's',
    })
  })

  it.each(['https://evil.example.com/secrets/s', 'kv://vault', 'db-password', '{"uri": 5}', ''])('rejects %j', (value) => {
    expect(parseSecretReference(value)).toBeUndefined()
  })

  it('knows the content types and formats a URI', () => {
    expect(isSecretReference('application/vnd.2s.secret-reference')).toBe(true)
    expect(isSecretReference('application/vnd.microsoft.appconfig.keyvaultref+json;charset=utf-8')).toBe(true)
    expect(isSecretReference('text/plain')).toBe(false)
    expect(formatSecretReference({ vault: 'V', name: 'n', version: 'ABC' })).toBe('https://v.vault.azure.net/secrets/n/abc')
  })
})
