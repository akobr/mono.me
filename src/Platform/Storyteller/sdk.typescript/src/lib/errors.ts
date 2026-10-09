// Errors of the Storyteller API, normalized from ErrorResponse and SchemaValidationErrorResponse bodies.
import type { SchemaValidationErrorDetail } from '../generated/model'

export interface StorytellerErrorInit {
  status: number
  message: string
  hint?: string
  errorCode?: string
  schemaErrors?: SchemaValidationErrorDetail[]
  body?: unknown
  cause?: unknown
}

export class StorytellerError extends Error {
  /** HTTP status; 0 for a network failure or an aborted request. */
  readonly status: number

  readonly hint?: string

  /** Stable code from ErrorResponse.ErrorCode, for example AccessDenied, LastOwner or PatchTestFailed. */
  readonly errorCode?: string

  /** Present on 409 responses of schema compliance checks. */
  readonly schemaErrors?: SchemaValidationErrorDetail[]

  /** The parsed error body, when there was one. */
  readonly body?: unknown

  constructor(init: StorytellerErrorInit) {
    super(init.message, { cause: init.cause })
    this.name = 'StorytellerError'
    this.status = init.status
    this.hint = init.hint
    this.errorCode = init.errorCode
    this.schemaErrors = init.schemaErrors
    this.body = init.body
  }

  /** Builds the error from a response status and its (possibly empty or non-JSON) body. */
  static fromResponse(status: number, statusText: string, body: unknown): StorytellerError {
    const record = isRecord(body) ? body : undefined
    const message = readString(record, 'Message')
      ?? (typeof body === 'string' && body.trim().length > 0 ? body.trim() : undefined)
      ?? `Storyteller API returned ${status}${statusText ? ` ${statusText}` : ''}.`
    const errors = record?.Errors

    return new StorytellerError({
      status,
      message,
      hint: readString(record, 'Hint'),
      errorCode: readString(record, 'ErrorCode'),
      schemaErrors: Array.isArray(errors) ? (errors as SchemaValidationErrorDetail[]) : undefined,
      body,
    })
  }
}

export const isStorytellerError = (error: unknown): error is StorytellerError => error instanceof StorytellerError

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

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function readString(record: Record<string, unknown> | undefined, key: string): string | undefined {
  const value = record?.[key]
  return typeof value === 'string' && value.length > 0 ? value : undefined
}
