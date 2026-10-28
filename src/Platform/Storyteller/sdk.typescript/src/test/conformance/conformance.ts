// The conformance suite, src/Platform/Storyteller/conformance, read from the repository: suite.json and the files it
// lists, parsed with JSON.parse as the SDK parses a body. Not a test file; the runners next to it import it.
import { readFileSync } from 'node:fs'

const root = new URL('../../../../conformance/', import.meta.url)

export interface SuiteFile {
  path: string
  kind: string
}

export interface Suite {
  name: string
  version: string
  files: SuiteFile[]
}

export interface DocumentError {
  rule: string
  path: string
}

export interface FlatteningVector {
  name: string
  document: unknown
  keys?: Record<string, string>
  error?: DocumentError
}

export interface FlatteningFile {
  target: string
  vectors: FlatteningVector[]
}

export interface FormatVector {
  name: string
  document: unknown
  format: string
  text?: string
  error?: DocumentError
}

export interface FormatsFile {
  vectors: FormatVector[]
}

export interface VectorResponse {
  status: number
  headers?: Record<string, string>
  json?: unknown
  text?: string
}

export interface Failure {
  status: number | null
  errorCode: string | null
  transient: boolean
}

export interface ErrorVector {
  name: string
  read: 'effective' | 'resolved'
  response: VectorResponse
  error: Failure
}

export interface ErrorsFile {
  key: string
  vectors: ErrorVector[]
}

export function readConformance<T>(path: string): T {
  return JSON.parse(readFileSync(new URL(path, root), 'utf8')) as T
}

export const suite = readConformance<Suite>('suite.json')

export function filesOfKind(kind: string): string[] {
  return suite.files.filter(file => file.kind === kind).map(file => file.path)
}

/** A json body goes out as JSON, a text body as text/plain; the headers as written. A new response for every request. */
export function toResponse(response: VectorResponse): Response {
  const headers = new Headers(response.headers)
  let body: string | null = null

  if (response.json !== undefined) {
    body = JSON.stringify(response.json)
    headers.set('content-type', 'application/json; charset=utf-8')
  } else if (response.text !== undefined) {
    body = response.text
    headers.set('content-type', 'text/plain; charset=utf-8')
  }

  return new Response(body, { status: response.status, headers })
}
