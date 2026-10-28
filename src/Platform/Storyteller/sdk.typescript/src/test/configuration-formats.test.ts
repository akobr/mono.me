import { describe, expect, it } from 'vitest'
import { flattenConfiguration, formatConfiguration, InvalidDocumentError, isTransientError, StorytellerError } from '../index'

// What the conformance vectors cannot hold: JavaScript values that JSON has no text for. test/conformance runs the vectors.
describe('flattenConfiguration', () => {
  it('has no keys for a null or an undefined content, as a version-0 configuration', () => {
    expect(flattenConfiguration(null)).toEqual({})
    expect(flattenConfiguration(undefined, 'env')).toEqual({})
    expect(formatConfiguration(null, 'flat')).toBe('{}\n')
    expect(formatConfiguration(undefined, 'env')).toBe('')
  })

  it('flattens to the flat keys by default', () => {
    expect(flattenConfiguration({ db: { hosts: ['a'] } })).toEqual({ 'db:hosts:0': 'a' })
  })

  it('keeps a property named __proto__ as a plain key', () => {
    const keys = flattenConfiguration(JSON.parse('{ "__proto__": { "a": "x" }, "b": "y" }'))

    expect(Object.entries(keys)).toEqual([['__proto__:a', 'x'], ['b', 'y']])
    expect(Object.getPrototypeOf(keys)).toBeNull()
  })

  it('treats undefined as null', () => {
    expect(flattenConfiguration({ a: undefined, b: [undefined, 'x'] })).toEqual({ 'b:1': 'x' })
  })

  it('rejects what is not JSON data', () => {
    expect(() => flattenConfiguration({ at: new Date(0) })).toThrow(TypeError)
    expect(() => flattenConfiguration(new Date(0))).toThrow(InvalidDocumentError)
    expect(() => formatConfiguration({ a: 'x' }, 'yaml' as 'flat')).toThrow(TypeError)
  })

  it('reports a rule and a path, never a value', () => {
    let error: unknown

    try {
      flattenConfiguration({ db: { 'pass:word': 's3cr3t' } })
    } catch (thrown) {
      error = thrown
    }

    expect(error).toBeInstanceOf(InvalidDocumentError)
    expect(error).toMatchObject({ name: 'InvalidDocumentError', rule: 'colon-in-name', path: "$.db['pass:word']" })
    expect((error as Error).message).not.toContain('s3cr3t')
  })
})

describe('isTransientError', () => {
  it('classifies a network failure as transient and an abort as not', () => {
    expect(isTransientError(new StorytellerError({ status: 0, message: 'The Storyteller API is not reachable.' }))).toBe(true)
    expect(isTransientError(new StorytellerError({ status: 0, message: 'The request was aborted.', aborted: true }))).toBe(false)
  })

  it('does not classify other errors', () => {
    expect(isTransientError(new Error('boom'))).toBe(false)
    expect(isTransientError(undefined)).toBe(false)
  })

  it('classifies a 2xx that was not the expected answer as transient', () => {
    expect(isTransientError(new StorytellerError({ status: 200, message: 'Not a configuration.' }))).toBe(true)
  })

  it('falls back to the OAuth error when ErrorCode is blank', () => {
    expect(StorytellerError.fromResponse(400, '', { ErrorCode: ' ', error: 'invalid_request' }).errorCode).toBe('invalid_request')
    expect(StorytellerError.fromResponse(403, '', { ErrorCode: ' ' }).errorCode).toBeUndefined()
  })
})
