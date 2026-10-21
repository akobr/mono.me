import { describe, expect, it } from 'vitest'
import { createSeed, type MockScenario } from '@/mock/db'

// The legacy datasets (--legacy, --large and the new-user and invited scenarios) share the access points of db.ts.

const legacyScenarios: MockScenario[] = ['default', 'large', 'new-user', 'invited']

const descriptionsOf = (scenario: MockScenario) =>
  Object.fromEntries(Object.values(createSeed(scenario).accessPoints).map((point) => [point.Key, point.Description]))

describe.each(legacyScenarios)('the %s dataset', (scenario) => {
  it('describes its three projects in at most 500 characters, the same in every scenario, and no organization', () => {
    const descriptions = descriptionsOf(scenario)

    expect(Object.keys(descriptions).filter((key) => key.includes('.'))).toEqual([
      'acme.billing',
      'acme.shipping',
      'northwind.portal',
    ])
    for (const key of ['acme.billing', 'acme.shipping', 'northwind.portal']) {
      expect(descriptions[key]?.trim(), key).toBeTruthy()
      expect(descriptions[key]?.length, key).toBeLessThanOrEqual(500)
    }
    expect(descriptions.acme).toBeUndefined()
    expect(descriptions.northwind).toBeUndefined()
    expect(descriptions).toEqual(descriptionsOf('default'))
  })
})
