// @vitest-environment node
import { execFile } from 'node:child_process'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'
import { describe, expect, it } from 'vitest'
import { mockDatasets, parseDevMockArguments } from '../../../build/mock-dataset.ts'

// `npm run dev:mock` runs build/dev-mock.ts with Node's type stripping, which neither Vitest nor vue-tsc uses; the
// error cases run it as npm does, without starting Vite.
const root = fileURLToPath(new URL('../../..', import.meta.url))
const script = path.join(root, 'build/dev-mock.ts')
const run = promisify(execFile)

describe('the options of npm run dev:mock', () => {
  it('starts the showcase without an option and keeps the arguments for Vite', () => {
    expect(parseDevMockArguments([])).toEqual({ dataset: 'showcase', viteArguments: [] })
    expect(parseDevMockArguments(['--open', '--host', '0.0.0.0'])).toEqual({
      dataset: 'showcase',
      viteArguments: ['--open', '--host', '0.0.0.0'],
    })
  })

  it('picks the previous datasets with --legacy and --large', () => {
    expect(parseDevMockArguments(['--legacy'])).toEqual({ dataset: 'default', viteArguments: [] })
    expect(parseDevMockArguments(['--open', '--large'])).toEqual({ dataset: 'large', viteArguments: ['--open'] })
  })

  it('picks any dataset with --scenario', () => {
    expect(parseDevMockArguments(['--scenario=new-user'])).toEqual({ dataset: 'new-user', viteArguments: [] })
    expect(parseDevMockArguments(['--scenario', 'invited', '--open'])).toEqual({
      dataset: 'invited',
      viteArguments: ['--open'],
    })
    for (const dataset of mockDatasets) {
      expect(parseDevMockArguments([`--scenario=${dataset}`])).toEqual({ dataset, viteArguments: [] })
    }
  })

  it('refuses two datasets, an unknown one and --scenario without a name', () => {
    expect(parseDevMockArguments(['--legacy', '--large'])).toEqual({
      error: 'Pick one dataset; got --legacy and --large.',
    })
    expect(parseDevMockArguments(['--legacy', '--legacy'])).toHaveProperty('error')
    expect(parseDevMockArguments(['--scenario=nope'])).toEqual({
      error: "Unknown dataset 'nope'; use one of default, new-user, invited, large, showcase.",
    })
    expect(parseDevMockArguments(['--scenario'])).toHaveProperty('error')
    expect(parseDevMockArguments(['--scenario', '--open'])).toHaveProperty('error')
  })

  it('runs with Node and stops with code 2 and the usage on a wrong option', async () => {
    for (const args of [['--legacy', '--large'], ['--scenario=nope']]) {
      const failure = await run(process.execPath, [script, ...args], { cwd: root }).then(
        () => undefined,
        (error: { code: number; stderr: string }) => error,
      )
      expect(failure?.code).toBe(2)
      expect(failure?.stderr).toContain('Usage: npm run dev:mock')
    }
  })
})
