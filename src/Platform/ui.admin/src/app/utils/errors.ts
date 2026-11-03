import { isStorytellerError, StorytellerError, type AccountRoleName } from '@42for.net/2splatform.sdk'

export interface ErrorDescription {
  title: string
  message: string
  hint?: string
  status?: number
  /** True only when the API did not answer in time (status 0 too, but not a network failure). */
  timedOut?: true
}

/**
 * A user-facing description of an error. Only Message and Hint of the API's ErrorResponse are shown, never
 * exception details (ErrorResponse.Error can contain stack traces).
 */
export function describeError(error: unknown, fallbackTitle = 'Something went wrong'): ErrorDescription {
  if (isStorytellerError(error)) {
    const title = error.timedOut
      ? 'The API did not answer in time'
      : error.status === 0
        ? 'The API is not reachable'
        : error.status === 403
          ? 'No access'
          : error.status === 404
            ? 'Not found'
            : error.status >= 500
              ? 'The API failed'
              : fallbackTitle

    return {
      title,
      message: error.message,
      hint: error.hint,
      status: error.status,
      ...(error.timedOut ? { timedOut: true as const } : {}),
    }
  }

  return { title: fallbackTitle, message: error instanceof Error ? error.message : String(error ?? 'Unknown error') }
}

/**
 * The refusal of a screen that knows, before it asks, that the caller's role is below `role`: the 403 AccessDenied of
 * the API, so `ErrorState` shows it as "No access" with Retry. Screens check first because a Debug API (DEV_AUTH)
 * skips its role checks of users and would answer (424-164).
 */
export function roleRequiredError(role: AccountRoleName, subject: string): StorytellerError {
  return new StorytellerError({
    status: 403,
    errorCode: 'AccessDenied',
    message: `${subject} need the ${role} role or a higher one in this project.`,
  })
}

/** The message of an error for a form field or a toast. */
export function errorMessage(error: unknown): string {
  const description = describeError(error)
  return description.hint ? `${description.message} ${description.hint}` : description.message
}
