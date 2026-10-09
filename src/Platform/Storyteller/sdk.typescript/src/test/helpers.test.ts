import { QueryClient } from '@tanstack/vue-query'
import { describe, expect, it } from 'vitest'
import { invalidateByPath, pathToQueryKey, queryKeyStartsWith, removeByPath } from '../lib/invalidation'
import { collectAll, paginate } from '../lib/paging'
import { getRole, hasRole, roleOrder, roleRank } from '../lib/roles'
import { getGetConfigurationQueryKey, getGetConfigurationsQueryKey, getGetResolvedConfigurationQueryKey } from '../generated/vue-query'

describe('invalidation', () => {
  it('turns paths into the generated key prefixes', () => {
    expect(pathToQueryKey('/v1/acme/billing/default/configuration/exe.a.b.c?format=unified')).toEqual([
      'v1', 'acme', 'billing', 'default', 'configuration', 'exe.a.b.c',
    ])
    expect(getGetConfigurationQueryKey('acme', 'billing', 'default', 'exe.a.b.c')).toEqual(
      pathToQueryKey('/v1/acme/billing/default/configuration/exe.a.b.c'),
    )
  })

  it('matches a configuration and everything below it, but not the listing', () => {
    const path = '/v1/acme/billing/default/configuration/exe.a.b.c'

    expect(queryKeyStartsWith(getGetConfigurationQueryKey('acme', 'billing', 'default', 'exe.a.b.c'), path)).toBe(true)
    expect(queryKeyStartsWith(getGetResolvedConfigurationQueryKey('acme', 'billing', 'default', 'exe.a.b.c'), path)).toBe(true)
    expect(queryKeyStartsWith(getGetConfigurationsQueryKey('acme', 'billing', 'default'), path)).toBe(false)
    expect(queryKeyStartsWith(getGetConfigurationQueryKey('acme', 'billing', 'default', 'exe.a.b.d'), path)).toBe(false)
  })

  it('invalidates and removes cached queries by path prefix', async () => {
    const client = new QueryClient()
    const configuration = getGetConfigurationQueryKey('acme', 'billing', 'default', 'exe.a.b.c')
    const resolved = getGetResolvedConfigurationQueryKey('acme', 'billing', 'default', 'exe.a.b.c')
    const listing = getGetConfigurationsQueryKey('acme', 'billing', 'default')
    client.setQueryData(configuration, { Version: 1 })
    client.setQueryData(resolved, { Version: 1 })
    client.setQueryData(listing, { Count: 1 })

    await invalidateByPath(client, '/v1/acme/billing/default/configuration/exe.a.b.c')

    expect(client.getQueryState(configuration)?.isInvalidated).toBe(true)
    expect(client.getQueryState(resolved)?.isInvalidated).toBe(true)
    expect(client.getQueryState(listing)?.isInvalidated).toBe(false)

    removeByPath(client, '/v1/acme/billing/default/configuration/exe.a.b.c/resolved')
    expect(client.getQueryData(resolved)).toBeUndefined()
    expect(client.getQueryData(configuration)).toEqual({ Version: 1 })
  })
})

describe('paging', () => {
  const pages = [
    { Items: [1, 2], ContinuationToken: 'second' },
    { Items: [3], ContinuationToken: 'third' },
    { Items: [4], ContinuationToken: null },
  ]

  it('follows continuation tokens until there is none', async () => {
    const tokens: (string | undefined)[] = []
    const items = await collectAll(
      async (token) => {
        tokens.push(token)
        return pages[tokens.length - 1]!
      },
      (page) => page.Items,
    )

    expect(items).toEqual([1, 2, 3, 4])
    expect(tokens).toEqual([undefined, 'second', 'third'])
  })

  it('stops when aborted', async () => {
    const controller = new AbortController()
    const seen: number[] = []

    await expect(async () => {
      for await (const page of paginate(async () => pages[0]!, controller.signal)) {
        seen.push(page.Items.length)
        controller.abort()
      }
    }).rejects.toThrow()
    expect(seen).toEqual([2])
  })
})

describe('roles', () => {
  it('orders roles and compares them', () => {
    expect(roleOrder[0]).toBe('None')
    expect(roleRank('Owner')).toBeGreaterThan(roleRank('Administrator'))
    expect(hasRole('ContributorWithSecrets', 'Contributor')).toBe(true)
    expect(hasRole('Reader', 'Contributor')).toBe(false)
    expect(hasRole(undefined, 'Reader')).toBe(false)
    expect(hasRole('Unknown', 'None')).toBe(true)
  })

  it('reads the role of an access point from an AccessMap', () => {
    const accessMap = { acme: 'Owner', 'acme.billing': 'Reader', 'acme.weird': 'Superuser' }

    expect(getRole(accessMap, 'acme.billing')).toBe('Reader')
    expect(getRole(accessMap, 'acme.other')).toBe('None')
    expect(getRole(accessMap, 'acme.weird')).toBe('None')
    expect(getRole(undefined, 'acme')).toBe('None')
  })
})
