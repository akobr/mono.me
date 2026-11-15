import { HttpResponse } from 'msw'
import type { KeyValueBulkItemResult } from '@42for.net/2splatform.sdk'

/**
 * A stale sign-in in the mock API (424-264): set by window.storytellerMock.staleSignIn(), cleared by the mock sign-in
 * with maxAge. Under it the guarded handlers answer the API's step-up 401, as the API does for a sign-in older than its
 * window. Off by default, so the mock behaves as before.
 */
const staleKey = '2s-admin:mock-stale-sign-in'

/** The window of the mock API, as the API's default Auth:StepUp:MaxAge (10 minutes). */
export const mockStepUpMaxAge = 600

export const stepUpMessage =
  'This operation needs a recent sign-in: sign in again, then repeat it. A sign-in counts for 10 minutes.'

export const isStaleSignIn = (): boolean => sessionStorage.getItem(staleKey) === 'true'

export function setStaleSignIn(stale: boolean): void {
  if (stale) {
    sessionStorage.setItem(staleKey, 'true')
  } else {
    sessionStorage.removeItem(staleKey)
  }
}

/** The step-up of GET v1/auth/configuration. */
export const mockStepUpConfiguration = { Enabled: true, MaxAge: mockStepUpMaxAge }

/** The API's 401 StepUpRequired: the ErrorResponse with Details.MaxAge and the RFC 9470 challenge. */
export const stepUpResponse = () =>
  HttpResponse.json(
    { Message: stepUpMessage, ErrorCode: 'StepUpRequired', Details: { MaxAge: mockStepUpMaxAge } },
    {
      status: 401,
      headers: {
        'WWW-Authenticate': `Bearer error="insufficient_user_authentication", error_description="A recent sign-in is required", max_age="${mockStepUpMaxAge}"`,
      },
    },
  )

/** The step-up 401 when the sign-in is stale, otherwise nothing: the guard of a handler, after its role checks. */
export const requireRecentSignIn = (): Response | undefined => (isStaleSignIn() ? stepUpResponse() : undefined)

/** A bulk item the API refused for the step-up (restore, purge). */
export const stepUpItem = (key: string): KeyValueBulkItemResult => ({
  Key: key,
  Outcome: 'Forbidden',
  ErrorCode: 'StepUpRequired',
  Message: stepUpMessage,
})
