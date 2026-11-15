import {
  isStepUpRequired,
  isStorytellerError,
  stepUpMaxAge,
  StorytellerError,
  type AccountRoleName,
} from '@42for.net/2splatform.sdk'

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
  // A recent sign-in helps, unlike any other 401 or 403 (424-264).
  if (isStepUpRequired(error)) {
    return { title: stepUpTitle, message: stepUpText(stepUpMaxAge(error)), status: error.status }
  }

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

/** The title of an operation that needs a recent sign-in (401 StepUpRequired). */
export const stepUpTitle = 'Sign in again to continue'

/** The text of an operation that needs a recent sign-in, with the window of the API when it is known. */
export function stepUpText(maxAge?: number): string {
  const window = maxAge === undefined ? '' : ` A sign-in counts for ${formatWindow(maxAge)}.`
  return `For your security, this needs a recent sign-in. Sign in again, then repeat it.${window}`
}

/** A window in seconds written out as the API writes it: "10 minutes", "1 hour", "90 seconds". */
export function formatWindow(seconds: number): string {
  const [amount, unit] =
    seconds > 0 && seconds % 3600 === 0
      ? [seconds / 3600, 'hour']
      : seconds > 0 && seconds % 60 === 0
        ? [seconds / 60, 'minute']
        : [seconds, 'second']
  return `${amount} ${unit}${amount === 1 ? '' : 's'}`
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

/**
 * The title of a failed write's toast: a write that ran out of time may still be applied by the API, so it does not say
 * "not saved" (the SDK's hint, in the description, says to read it again before repeating it).
 */
export function writeFailureTitle(error: unknown, title: string): string {
  return isStorytellerError(error) && error.timedOut
    ? 'The API did not answer in time; the change may have been saved'
    : title
}

/** The message of an error for a form field or a toast. */
export function errorMessage(error: unknown): string {
  const description = describeError(error)
  return description.hint ? `${description.message} ${description.hint}` : description.message
}
