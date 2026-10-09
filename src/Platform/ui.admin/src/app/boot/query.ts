import { MutationCache, QueryCache, QueryClient } from '@tanstack/vue-query'
import { isStorytellerError } from '@42for.net/2splatform.sdk'
import { reportApiError } from './api-errors'

/** One retry for failed reads, but never for 4xx: those answers do not change on a second try. */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (failureCount >= 1) {
    return false
  }

  return !(isStorytellerError(error) && error.status >= 400 && error.status < 500)
}

export function createQueryClient(): QueryClient {
  return new QueryClient({
    queryCache: new QueryCache({
      onError: (error, query) => {
        if (!query.meta?.silent) {
          reportApiError(error)
        }
      },
    }),
    mutationCache: new MutationCache({
      onError: (error) => reportApiError(error),
    }),
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        gcTime: 10 * 60_000,
        refetchOnWindowFocus: false,
        retry: shouldRetry,
      },
      mutations: { retry: false },
    },
  })
}
