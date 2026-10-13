import type { QueryClient } from '@tanstack/vue-query'
import { invalidateByPath, removeByPath } from '@42for.net/2splatform.sdk/vue-query'
import type { Scope } from '@/stores/context'

// Path-prefix invalidation after mutations (spec section 6). Query keys are the path segments of the request,
// so a prefix covers a resource and everything below it (versions, diffs, resolved).

const base = (scope: Scope) => `/v1/${scope.org}/${scope.project}/${scope.view}`

export function invalidateAnnotation(client: QueryClient, scope: Scope, key: string): Promise<void> {
  return invalidateByPath(client, `${base(scope)}/annotations/${key}`)
}

export function forgetAnnotation(client: QueryClient, scope: Scope, key: string): void {
  removeByPath(client, `${base(scope)}/annotations/${key}`)
}

/** The lists and the entries of deleted annotations (`/annotations/deleted…`). */
export function invalidateDeleted(client: QueryClient, scope: Scope): Promise<void> {
  return invalidateByPath(client, `${base(scope)}/annotations/deleted`)
}

export async function invalidateConfiguration(client: QueryClient, scope: Scope, key: string): Promise<void> {
  await Promise.all([
    invalidateByPath(client, `${base(scope)}/configuration/${key}`),
    invalidateByPath(client, `${base(scope)}/configurations`),
    // a configuration save is an entry in the annotation's history
    invalidateByPath(client, `${base(scope)}/annotations/${key}/history`),
  ])
}

/** Every configuration of the view (a template or schema change alters their effective documents). */
export async function invalidateAllConfigurations(client: QueryClient, scope: Scope): Promise<void> {
  await Promise.all([
    invalidateByPath(client, `${base(scope)}/configuration`),
    invalidateByPath(client, `${base(scope)}/configurations`),
  ])
}

export async function invalidateTemplate(client: QueryClient, scope: Scope, type: string): Promise<void> {
  await Promise.all([
    invalidateByPath(client, `${base(scope)}/template/${type}`),
    invalidateByPath(client, `${base(scope)}/templates`),
  ])
}

/** Every schema kind, and the combined schemas (`/configuration-schema/{key}/definition`) below the same prefix. */
export async function invalidateSchemas(client: QueryClient, scope: Scope): Promise<void> {
  await Promise.all([
    invalidateByPath(client, `${base(scope)}/configuration-schema`),
    invalidateByPath(client, `${base(scope)}/configuration-schemas`),
  ])
}

export async function invalidateAccess(
  client: QueryClient,
  accessPointKey: string,
  org?: string,
  project?: string,
): Promise<void> {
  await Promise.all([
    invalidateByPath(client, `/v1/access/points/${accessPointKey}`),
    org && project ? invalidateByPath(client, `/v1/${org}/${project}/access`) : Promise.resolve(),
  ])
}

/** Every pair of the view, their versions and diffs. */
export function invalidateKeyValues(client: QueryClient, scope: Scope): Promise<void> {
  return invalidateByPath(client, `${base(scope)}/key-values`)
}

/** The project's key/value policy, its recipients, and the lists (a level change shows up there). */
export async function invalidateKeyValuePolicy(client: QueryClient, org: string, project: string): Promise<void> {
  await invalidateByPath(client, `/v1/${org}/${project}/key-values`)
}
