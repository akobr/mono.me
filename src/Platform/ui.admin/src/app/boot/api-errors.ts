import { isStorytellerError } from '@42for.net/2splatform.sdk'

export interface ApiErrorNotice {
  title: string
  description?: string
}

type Listener = (notice: ApiErrorNotice) => void

const listeners = new Set<Listener>()

/** Subscribes to global API failures (5xx and network); returns the unsubscribe function. */
export function onApiError(listener: Listener): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/**
 * Only failures a screen cannot handle itself are reported globally: server errors and an unreachable API.
 * 4xx responses (400, 403, 404, 409, 412, 422) belong to the screen that made the call.
 */
export function shouldReport(error: unknown): boolean {
  if (!isStorytellerError(error)) {
    return !(error instanceof DOMException && error.name === 'AbortError')
  }

  return !error.aborted && (error.status === 0 || error.status >= 500)
}

export function reportApiError(error: unknown): void {
  if (!shouldReport(error)) {
    return
  }

  const notice: ApiErrorNotice = isStorytellerError(error)
    ? {
        title: error.status === 0 ? 'The API is not reachable' : 'The API failed',
        description: error.hint ?? error.message,
      }
    : { title: 'Unexpected error', description: error instanceof Error ? error.message : String(error) }

  for (const listener of listeners) {
    listener(notice)
  }
}
