import { describe, expect, it } from 'vitest'
import { QueryClient, type QueryKey } from '@tanstack/vue-query'
import type { KeyValuePolicy } from '@42for.net/2splatform.sdk'
import { invalidateKeyValues } from '@/queries/invalidation'

// A key/value write refreshes the view's pairs, and the project's policy only while the cached one has no Standard key:
// the API records the key with the project's first Standard secret, so Settings shows it then (424-167).

const scope = { org: 'acme', project: 'billing', view: 'default' }
const pairsKey = ['v1', 'acme', 'billing', 'default', 'key-values']
const pairKey = ['v1', 'acme', 'billing', 'default', 'key-values', 'db.password']
const policyKey = ['v1', 'acme', 'billing', 'key-values', 'policy']
const otherPolicyKey = ['v1', 'acme', 'shipping', 'key-values', 'policy']
const standard = { KeyId: 'https://st-keys.vault.azure.net/keys/kek-billing', CurrentKeyVersion: 'v1' }
const withoutKey: KeyValuePolicy = { AllowedProtections: ['Public', 'Standard'], Version: 0 }
const withKey: KeyValuePolicy = { ...withoutKey, Standard: standard, Version: 1 }

function cached(entries: [QueryKey, unknown][]) {
  const client = new QueryClient()
  for (const [key, data] of entries) {
    client.setQueryData(key, data)
  }
  return client
}

const invalidated = (client: QueryClient, key: QueryKey) => client.getQueryState(key)?.isInvalidated ?? false

describe('invalidateKeyValues', () => {
  it('refreshes the policy while it has no Standard key', async () => {
    const client = cached([
      [pairKey, { Key: 'db.password' }],
      [policyKey, withoutKey],
      [otherPolicyKey, withoutKey],
    ])

    await invalidateKeyValues(client, scope)

    expect(invalidated(client, pairKey)).toBe(true)
    expect(invalidated(client, policyKey)).toBe(true)
    expect(invalidated(client, otherPolicyKey)).toBe(false)
  })

  it('leaves a policy that knows the Standard key alone', async () => {
    const client = cached([
      [pairsKey, { Items: [] }],
      [policyKey, withKey],
    ])

    await invalidateKeyValues(client, scope)

    expect(invalidated(client, pairsKey)).toBe(true)
    expect(invalidated(client, policyKey)).toBe(false)
  })

  it('needs no cached policy', async () => {
    const client = cached([[pairsKey, { Items: [] }]])

    await invalidateKeyValues(client, scope)

    expect(invalidated(client, pairsKey)).toBe(true)
    expect(client.getQueryState(policyKey)).toBeUndefined()
  })
})
