import { configureStoryteller } from '@42for.net/2splatform.sdk'
import type { AppConfig } from './app-config'
import { getAuthService } from './state'

/**
 * Configures the SDK fetcher (companion spec 1.4). The token is read from AuthKit for every request;
 * a 401 that survives one forced refresh calls onUnauthorized, which signs in again.
 */
export function configureApi(config: AppConfig, onUnauthorized: () => void): void {
  configureStoryteller({
    baseUrl: config.apiBaseUrl,
    getAccessToken: (options) => getAuthService()?.getAccessToken(options),
    onUnauthorized,
  })
}
