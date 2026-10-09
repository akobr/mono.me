import { describe, expect, it } from 'vitest'
import { z } from 'zod'
import { organizationSchema, projectNameSchema, viewNameSchema } from '@/forms/names.schema'
import { toFieldPath, toTypedSchema } from '@/forms/typed-schema'

describe('name rules (mirror of NameRules)', () => {
  it.each(['acme', '42dotnet', 'billing-eu-2', 'ab'])('accepts %s', (name) => {
    expect(projectNameSchema.safeParse(name).success).toBe(true)
  })

  it.each(['', 'a', 'Acme', '-acme', 'acme.billing', 'a_b', 'x'.repeat(64)])('rejects %s', (name) => {
    expect(projectNameSchema.safeParse(name).success).toBe(false)
  })

  it('rejects reserved words per kind', () => {
    expect(organizationSchema.safeParse({ organization: 'orgs', project: 'billing' }).success).toBe(false)
    expect(organizationSchema.safeParse({ organization: 'views', project: 'billing' }).success).toBe(true)
    expect(viewNameSchema.safeParse('settings').success).toBe(false)
  })

  it('trims before validating', () => {
    expect(organizationSchema.parse({ organization: ' acme ', project: 'billing' })).toEqual({
      organization: 'acme',
      project: 'billing',
    })
  })
})

describe('toTypedSchema (VeeValidate adapter for Zod 4)', () => {
  it('joins paths the way VeeValidate names fields', () => {
    expect(toFieldPath(['values', 0, 'name'])).toBe('values[0].name')
    expect(toFieldPath(['title'])).toBe('title')
  })

  it('returns the parsed value or errors grouped by path', async () => {
    const schema = toTypedSchema(
      z.object({ title: z.string().max(3, 'Too long'), labels: z.array(z.string().min(1, 'Empty')) }),
    )

    await expect(schema.parse({ title: 'ok', labels: ['a'] })).resolves.toEqual({
      value: { title: 'ok', labels: ['a'] },
      errors: [],
    })
    await expect(schema.parse({ title: 'long', labels: ['', 'b'] })).resolves.toEqual({
      errors: [
        { path: 'title', errors: ['Too long'] },
        { path: 'labels[0]', errors: ['Empty'] },
      ],
    })
  })
})
