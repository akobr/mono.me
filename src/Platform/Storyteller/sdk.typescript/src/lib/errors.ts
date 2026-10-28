// Errors of the Storyteller API, normalized from ErrorResponse and SchemaValidationErrorResponse bodies.
import type { SchemaValidationErrorDetail } from '../generated/model'

export interface StorytellerErrorInit {
  status: number
  message: string
  hint?: string
  errorCode?: string
  schemaErrors?: SchemaValidationErrorDetail[]
  body?: unknown
  /** True when the request was cancelled through its AbortSignal (status 0). */
  aborted?: boolean
  cause?: unknown
}

export class StorytellerError extends Error {
  /** HTTP status; 0 for a network failure or an aborted request. */
  readonly status: number

  readonly hint?: string

  /**
   * Stable code from ErrorResponse.ErrorCode, for example AccessDenied, LastOwner or PatchTestFailed;
   * for the token endpoint the OAuth error (RFC 6749 §5.2), for example invalid_request or invalid_scope.
   */
  readonly errorCode?: string

  /** Present on 409 responses of schema compliance checks. */
  readonly schemaErrors?: SchemaValidationErrorDetail[]

  /** The parsed error body, when there was one. */
  readonly body?: unknown

  /** True when the request was cancelled through its AbortSignal; such errors are not failures to report. */
  readonly aborted: boolean

  constructor(init: StorytellerErrorInit) {
    super(init.message, { cause: init.cause })
    this.name = 'StorytellerError'
    this.status = init.status
    this.hint = init.hint
    this.errorCode = init.errorCode
    this.schemaErrors = init.schemaErrors
    this.body = init.body
    this.aborted = init.aborted ?? false
  }

  /** Builds the error from a response status and its (possibly empty or non-JSON) body. */
  static fromResponse(status: number, statusText: string, body: unknown): StorytellerError {
    const record = isRecord(body) ? body : undefined
    // The token endpoint answers with an OAuth error body ({ error, error_description }) instead of ErrorResponse.
    const oauthError = readString(record, 'error')
    const message = readString(record, 'Message')
      ?? (oauthError ? readString(record, 'error_description') : undefined)
      ?? (typeof body === 'string' && body.trim().length > 0 ? body.trim() : undefined)
      ?? `Storyteller API returned ${status}${statusText ? ` ${statusText}` : ''}.`
    const errors = record?.Errors

    // A blank ErrorCode is no code, as for the .NET SDK.
    const errorCode = readString(record, 'ErrorCode')

    return new StorytellerError({
      status,
      message,
      hint: readString(record, 'Hint'),
      errorCode: (errorCode?.trim() ? errorCode : undefined) ?? oauthError,
      schemaErrors: Array.isArray(errors) ? (errors as SchemaValidationErrorDetail[]) : undefined,
      body,
    })
  }
}

export const isStorytellerError = (error: unknown): error is StorytellerError => error instanceof StorytellerError

/** An aborted request (status 0, cancelled through its AbortSignal), as opposed to a network failure. */
export const isAborted = (error: unknown): error is StorytellerError => error instanceof StorytellerError && error.aborted

const hasStatus = (status: number) => (error: unknown): error is StorytellerError =>
  isStorytellerError(error) && error.status === status

export const isUnauthorized = hasStatus(401)
export const isForbidden = hasStatus(403)
export const isNotFound = hasStatus(404)
export const isConflict = hasStatus(409)
export const isPreconditionFailed = hasStatus(412)

/** True for errors that carry the given ErrorResponse.ErrorCode. */
export const hasErrorCode = (error: unknown, errorCode: string): error is StorytellerError =>
  isStorytellerError(error) && error.errorCode === errorCode

/**
 * True for a failure worth trying again later, as the .NET configuration provider classifies it: a network failure,
 * 408, 424, 429, a 5xx other than 501 and 505, and a 2xx that was not the answer the SDK expected. A poller keeps its
 * last document and polls again; any other status gets the same answer again. False for an aborted request and for
 * errors that are not StorytellerError, which the SDK cannot classify.
 */
export function isTransientError(error: unknown): boolean {
  if (!isStorytellerError(error) || error.aborted) {
    return false
  }

  const { status } = error

  if (status === 0 || status === 408 || status === 424 || status === 429) {
    return true
  }

  if (status === 501 || status === 505) {
    return false
  }

  return status >= 500 || (status >= 200 && status < 300)
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function readString(record: Record<string, unknown> | undefined, key: string): string | undefined {
  const value = record?.[key]
  return typeof value === 'string' && value.length > 0 ? value : undefined
}
