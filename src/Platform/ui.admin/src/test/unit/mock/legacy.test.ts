import { describe, expect, it } from 'vitest'
import { createSeed, scenarios, type MockScenario } from '@/mock/db'

// The legacy datasets (--legacy, --large and the new-user and invited scenarios) share the access points of db.ts,
// the exhibition among them (424-267; exhibition.test.ts).

const legacyScenarios = scenarios.filter((scenario) => scenario !== 'showcase')
const projects = ['acme.billing', 'acme.shipping', 'northwind.portal', 'exhibition.exhibition']

const descriptionsOf = (scenario: MockScenario) =>
  Object.fromEntries(Object.values(createSeed(scenario).accessPoints).map((point) => [point.Key, point.Description]))

describe.each(legacyScenarios)('the %s dataset', (scenario) => {
  it('describes its four projects in at most 500 characters, the same in every scenario, and no organization', () => {
    const descriptions = descriptionsOf(scenario)

    expect(Object.keys(descriptions).filter((key) => key.includes('.'))).toEqual(projects)
    for (const key of projects) {
      expect(descriptions[key]?.trim(), key).toBeTruthy()
      expect(descriptions[key]?.length, key).toBeLessThanOrEqual(500)
    }
    expect(descriptions.acme).toBeUndefined()
    expect(descriptions.northwind).toBeUndefined()
    expect(descriptions.exhibition).toBeUndefined()
    expect(descriptions).toEqual(descriptionsOf('default'))
  })
})
