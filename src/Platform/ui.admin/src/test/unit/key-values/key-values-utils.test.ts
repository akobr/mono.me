import { describe, expect, it, vi } from 'vitest'
import { StorytellerError } from '@42for.net/2splatform.sdk'
import { keyValueFormSchema } from '@/forms/key-value.schema'
import {
  bindingFor,
  daysUntil,
  keyMatchesGlob,
  markMatching,
  parseImportText,
  parseProtectionInUse,
  toDocument,
  valuePreview,
} from '@/utils/key-values'
import { useRevealedValue } from '@/composables/useRevealedValue'

describe('key/value helpers', () => {
  it('builds the bindings of the three sources', () => {
    expect(bindingFor('db.password')).toBe('@db.password')
    expect(bindingFor('db.password', 'public')).toBe('@public(db.password)')
    expect(bindingFor('db.password', 'secret')).toBe('@secret(db.password)')
  })

  it('previews the first line of a value', () => {
    expect(valuePreview('line one\nline two')).toBe('line one')
    expect(valuePreview('x'.repeat(80), 10)).toBe(`${'x'.repeat(10)}…`)
    expect(valuePreview(undefined)).toBe('')
  })

  it('counts the days until a deleted key goes', () => {
    const now = new Date('2026-10-13T12:00:00Z')
    expect(daysUntil('2026-10-20T12:00:00Z', now)).toBe(7)
    expect(daysUntil('2026-10-13T06:00:00Z', now)).toBe(0)
    expect(daysUntil(undefined, now)).toBeUndefined()
  })

  it('reads the counts of a ProtectionInUse error', () => {
    const error = new StorytellerError({
      status: 409,
      message: "The protection level 'Hsm' is still used by keys of the project (default: 3, staging: 1); change them first.",
      errorCode: 'ProtectionInUse',
    })

    expect(parseProtectionInUse(error)).toEqual({ level: 'Hsm', counts: [{ view: 'default', count: 3 }, { view: 'staging', count: 1 }] })
    expect(parseProtectionInUse(new Error('other'))).toBeUndefined()
  })
})

describe('import parsing', () => {
  it('parses .env, flat JSON and documents into rows', () => {
    const env = parseImportText('DB__HOST=sql\nDB__PASSWORD="s3cret"')
    expect(env.format).toBe('dotenv')
    expect(env.items.map((item) => [item.key, item.value, item.protection])).toEqual([
      ['db.host', 'sql', 'Public'],
      ['db.password', 's3cret', 'Public'],
    ])

    const flat = parseImportText('{ "db": { "port": 5432 }, "1bad": "x" }')
    expect(flat.format).toBe('json')
    expect(flat.items.find((item) => item.key === '1bad')?.invalid).toBe(true)
    expect(flat.items.find((item) => item.key === 'db.port')?.value).toBe('5432')

    const document = parseImportText('{ "Format": "2s.key-values.v1", "Items": [{ "Key": "a", "Protection": "Standard", "Value": "1" }] }')
    expect(document.format).toBe('document')
    expect(document.items[0]?.protection).toBe('Standard')

    expect(() => parseImportText('just text')).toThrow()
  })

  it('marks keys by glob and builds the document', () => {
    const items = parseImportText('DB__HOST=sql\nDB__PASSWORD=s\nAPI__TOKEN=t').items
    const marked = markMatching(markMatching(items, '*password*', 'Hsm'), 'api.*', 'Standard')

    expect(marked.map((item) => item.protection)).toEqual(['Public', 'Hsm', 'Standard'])
    expect(keyMatchesGlob('db.password', 'DB.*')).toBe(true)
    expect(keyMatchesGlob('db.pass', 'db.pas?')).toBe(true)
    expect(keyMatchesGlob('api.key', 'db.*')).toBe(false)

    const document = toDocument(marked)
    expect(document.Format).toBe('2s.key-values.v1')
    expect(document.Items?.[1]).toEqual({ Key: 'db.password', Protection: 'Hsm', Value: 's', ContentType: undefined })
  })
})

describe('the form rules', () => {
  it('accepts the API key rule and refuses the rest', () => {
    expect(keyValueFormSchema.safeParse({ key: 'db.password', protection: 'Standard', value: 'x', contentType: 'text/plain' }).success).toBe(true)
    expect(keyValueFormSchema.safeParse({ key: '1db', protection: 'Standard', value: 'x' }).success).toBe(false)
    expect(keyValueFormSchema.safeParse({ key: 'db..x', protection: 'Public', value: 'x' }).success).toBe(false)
    expect(keyValueFormSchema.safeParse({ key: 'db', protection: 'Vault', value: 'x' }).success).toBe(false)
    expect(keyValueFormSchema.safeParse({ key: 'db', protection: 'Public', value: 'x'.repeat(24_577) }).success).toBe(false)
  })
})

describe('useRevealedValue', () => {
  it('masks the value again after 60 seconds', async () => {
    vi.useFakeTimers()
    try {
      const revealed = useRevealedValue(async () => 's3cret')

      await revealed.reveal()
      expect(revealed.value.value).toBe('s3cret')

      vi.advanceTimersByTime(59_000)
      expect(revealed.value.value).toBe('s3cret')
      vi.advanceTimersByTime(1_001)
      expect(revealed.value.value).toBeUndefined()
    } finally {
      vi.useRealTimers()
    }
  })

  it('keeps the error of a refused reveal', async () => {
    const revealed = useRevealedValue(async () => {
      throw new Error('no rights')
    })

    await revealed.reveal()

    expect(revealed.value.value).toBeUndefined()
    expect((revealed.error.value as Error).message).toBe('no rights')
  })
})
