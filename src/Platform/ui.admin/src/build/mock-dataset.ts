// The datasets of the mock API and the options `npm run dev:mock` takes to pick one (build/dev-mock.ts). The list
// mirrors `scenarios` of app/mock/db.ts, which Node cannot import (no `@/` alias); a unit test keeps them equal.

export const mockDatasets = ['default', 'new-user', 'invited', 'large', 'showcase'] as const

export type MockDataset = (typeof mockDatasets)[number]

/** The dataset without an option: the fourteen showcase projects (424-77). */
export const defaultMockDataset: MockDataset = 'showcase'

export type DevMockArguments = { dataset: MockDataset; viteArguments: string[] } | { error: string }

export const devMockUsage = [
  'Usage: npm run dev:mock [-- <option>] [vite options]',
  '  (none)             the showcase projects',
  '  --legacy           the previous standard dataset (acme, northwind)',
  '  --large            the large dataset (16,317 annotations in acme/billing)',
  `  --scenario=<name>  one of ${mockDatasets.join(', ')}`,
].join('\n')

const isDataset = (value: string): value is MockDataset => (mockDatasets as readonly string[]).includes(value)

/**
 * Takes the dataset options out of the arguments of `npm run dev:mock -- …`; every other argument is kept, in order,
 * for Vite. At most one dataset option is allowed.
 */
export function parseDevMockArguments(args: readonly string[]): DevMockArguments {
  const chosen: { option: string; dataset: string }[] = []
  const viteArguments: string[] = []

  for (let index = 0; index < args.length; index++) {
    const arg = args[index]!
    if (arg === '--legacy') {
      chosen.push({ option: arg, dataset: 'default' })
    } else if (arg === '--large') {
      chosen.push({ option: arg, dataset: 'large' })
    } else if (arg.startsWith('--scenario=')) {
      chosen.push({ option: arg, dataset: arg.slice('--scenario='.length) })
    } else if (arg === '--scenario') {
      const name = args[index + 1]
      if (name === undefined || name.startsWith('-')) {
        return { error: `--scenario needs a dataset name: ${mockDatasets.join(', ')}.` }
      }
      chosen.push({ option: `--scenario ${name}`, dataset: name })
      index++
    } else {
      viteArguments.push(arg)
    }
  }

  if (chosen.length > 1) {
    return { error: `Pick one dataset; got ${chosen.map((entry) => entry.option).join(' and ')}.` }
  }

  const dataset = chosen[0]?.dataset ?? defaultMockDataset
  if (!isDataset(dataset)) {
    return { error: `Unknown dataset '${dataset}'; use one of ${mockDatasets.join(', ')}.` }
  }
  return { dataset, viteArguments }
}
