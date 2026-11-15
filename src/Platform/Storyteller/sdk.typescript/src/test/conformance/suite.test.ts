import { describe, expect, it } from 'vitest'
import { suite } from './conformance'

// The kinds this SDK runs, and the ones it cannot: a kind the suite adds fails here until the SDK decides.
const run = new Set(['flattening', 'formats', 'errors'])
const notApplicable = new Map([
  ['refresh', 'the TypeScript SDK has no configuration provider; the .NET provider runs refresh.json'],
  // Run in step 1c of 424-266, with validateProjectSettings and the catalog module.
  ['project-settings', 'run in step 1c: the SDK has no validateProjectSettings yet; the .NET reference runs project-settings.json'],
])

describe(`conformance suite ${suite.name} ${suite.version}`, () => {
  it('has a version', () => {
    expect(suite.version).toMatch(/^\d+\.\d+\.\d+$/)
  })

  it.each(suite.files)('runs $path or says why not', ({ kind }) => {
    expect(run.has(kind) || notApplicable.has(kind), `the kind ${kind} is neither run nor named as not applicable`).toBe(true)
  })
})
