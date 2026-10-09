import { isStorytellerError } from '@42for.net/2splatform.sdk'

export interface ErrorDescription {
  title: string
  message: string
  hint?: string
  status?: number
}

/**
 * A user-facing description of an error. Only Message and Hint of the API's ErrorResponse are shown, never
 * exception details (ErrorResponse.Error can contain stack traces).
 */
export function describeError(error: unknown, fallbackTitle = 'Something went wrong'): ErrorDescription {
  if (isStorytellerError(error)) {
    const title =
      error.status === 0
        ? 'The API is not reachable'
        : error.status === 403
          ? 'No access'
          : error.status === 404
            ? 'Not found'
            : error.status >= 500
              ? 'The API failed'
              : fallbackTitle

    return { title, message: error.message, hint: error.hint, status: error.status }
  }

  return { title: fallbackTitle, message: error instanceof Error ? error.message : String(error ?? 'Unknown error') }
}

/** The message of an error for a form field or a toast. */
export function errorMessage(error: unknown): string {
  const description = describeError(error)
  return description.hint ? `${description.message} ${description.hint}` : description.message
}
