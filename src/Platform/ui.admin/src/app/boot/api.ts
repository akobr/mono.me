import { configureStoryteller } from '@42for.net/2splatform.sdk'
import type { AppConfig } from './app-config'
import { getAuthService } from './state'

/** The longest a read may take (GET, HEAD): the .NET configuration provider's read timeout is 30 s as well. */
export const apiReadTimeoutMs = 30_000

/**
 * The longest any other request may take: Bulk create, restore, delete and purge of subtrees, the schema writes with
 * their compliance check, the bulk key-value actions and the key-value import grow with their input.
 */
export const apiWriteTimeoutMs = 120_000

/** The view import's upload and the export's download, which grow with the view (passed per call). */
export const apiTransferTimeoutMs = 10 * 60_000

/** The limit of a request by its method; the time a browser queues the request counts too. */
export function apiTimeoutMs({ method }: { method: string }): number {
  return method === 'GET' || method === 'HEAD' ? apiReadTimeoutMs : apiWriteTimeoutMs
}

/**
 * Configures the SDK fetcher (companion spec 1.4). The token is read from AuthKit for every request;
 * a 401 that survives one forced refresh calls onUnauthorized, which signs in again. Every request ends: one the API
 * does not answer in time fails with a timeout, which the screens show with Retry.
 */
export function configureApi(config: AppConfig, onUnauthorized: () => void): void {
  configureStoryteller({
    baseUrl: config.apiBaseUrl,
    getAccessToken: (options) => getAuthService()?.getAccessToken(options),
    onUnauthorized,
    timeoutMs: apiTimeoutMs,
  })
}
