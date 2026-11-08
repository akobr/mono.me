// Versioned reads and writes of annotations: the strong ETag of the version read or written, If-Match and If-None-Match
// on a write, and a PATCH in both formats. The generated getAnnotation, setAnnotation and patchAnnotation drop the
// headers, take no condition arguments, and patchAnnotation sends a merge patch only.
import { getGetAnnotationUrl, getPatchAnnotationUrl, getSetAnnotationUrl } from '../generated/client/annotations/annotations'
import type { Annotation, AnnotationMergePatch, JsonPatchOperation } from '../generated/model'
import { StorytellerError } from './errors'
import { readBody, runRequest, type StorytellerRequestInit } from './fetcher'

/** A value and the ETag of its version. */
export interface VersionedResult<T> {
  value: T
  /** The strong ETag as sent ("a1.…"), to pass back in ifMatch; undefined when the header is missing or not readable (cross-origin without exposure). */
  etag?: string
}

/** The options of storytellerFetch (RequestInit, timeoutMs) and the query of the read. */
export type AnnotationReadOptions = StorytellerRequestInit & {
  /** Also an annotation that is not active now: disabled, scheduled or expired. Default false. */
  includeInactive?: boolean
}

/** The options of storytellerFetch and the conditions of a write; each overrides a header of the same name in headers, and '' removes that one. */
export type AnnotationWriteOptions = StorytellerRequestInit & {
  /** The ETag of an earlier read or write: writes only that version, and never re-creates a deleted key. '*': any existing annotation. */
  ifMatch?: string
  /** '*': creates only, a missing or deleted key. */
  ifNoneMatch?: string
}

/** The options of storytellerFetch and the condition of a patch; ifMatch overrides an If-Match in headers, and '' removes that one. */
export type AnnotationPatchOptions = StorytellerRequestInit & {
  /** The ETag of an earlier read or write: patches only that version. */
  ifMatch?: string
}

/**
 * A JSON Patch (RFC 6902) of the editable document, an array of operations sent as application/json-patch+json; or a
 * JSON Merge Patch (RFC 7396), an object sent as application/merge-patch+json with its explicit nulls (an undefined
 * member is left out, so it keeps its value).
 */
export type AnnotationPatchBody = AnnotationMergePatch | Record<string, unknown> | readonly JsonPatchOperation[]

const annotationMediaType = 'application/json'
const mergePatchMediaType = 'application/merge-patch+json'
const jsonPatchMediaType = 'application/json-patch+json'

/** The annotation with the ETag of its version; a deleted annotation is a 404, an inactive one too without includeInactive. */
export async function getAnnotationVersioned(
  organization: string,
  project: string,
  view: string,
  key: string,
  options: AnnotationReadOptions = {},
): Promise<VersionedResult<Annotation>> {
  const { includeInactive, ...init } = options
  // The query of the generated getAnnotation: includeInactive=true or false, none while undefined.
  const url = getGetAnnotationUrl(organization, project, view, key, { includeInactive })
  return await sendVersioned(url, 'GET', init, undefined, {})
}

/**
 * Writes the annotation (POST): creates it, updates it or re-creates it, under ifMatch or ifNoneMatch. The body
 * replaces the editable content. Returns the stored annotation with the ETag of the written version.
 */
export async function setAnnotationVersioned(
  organization: string,
  project: string,
  view: string,
  key: string,
  annotation: Annotation,
  options: AnnotationWriteOptions = {},
): Promise<VersionedResult<Annotation>> {
  const { ifMatch, ifNoneMatch, ...init } = options
  const body = { content: JSON.stringify(annotation), contentType: annotationMediaType }
  return await sendVersioned(getSetAnnotationUrl(organization, project, view, key), 'POST', init, body, {
    'If-Match': ifMatch,
    'If-None-Match': ifNoneMatch,
  })
}

/**
 * Patches the annotation (PATCH), merged on the server against its current state: an array as a JSON Patch, an object
 * as a JSON Merge Patch. A patch never creates or re-creates a key (404 AnnotationNotFound or AnnotationDeleted).
 * Returns the stored annotation with the ETag of the written version.
 */
export async function patchAnnotationVersioned(
  organization: string,
  project: string,
  view: string,
  key: string,
  patch: AnnotationPatchBody,
  options: AnnotationPatchOptions = {},
): Promise<VersionedResult<Annotation>> {
  const { ifMatch, ...init } = options
  const body = { content: JSON.stringify(patch), contentType: patchMediaTypeOf(patch) }
  return await sendVersioned(getPatchAnnotationUrl(organization, project, view, key), 'PATCH', init, body, { 'If-Match': ifMatch })
}

function patchMediaTypeOf(patch: unknown): string {
  if (Array.isArray(patch)) {
    return jsonPatchMediaType
  }

  if (typeof patch === 'object' && patch !== null) {
    return mergePatchMediaType
  }

  // Before any request: a JavaScript caller can pass anything.
  throw new TypeError('A patch is a JSON Patch (an array of operations) or a JSON Merge Patch (an object).')
}

async function sendVersioned(
  url: string,
  method: string,
  init: StorytellerRequestInit,
  body: { content: string; contentType: string } | undefined,
  conditions: Record<string, string | undefined>,
): Promise<VersionedResult<Annotation>> {
  const headers = new Headers(init.headers)

  for (const [name, tag] of Object.entries(conditions)) {
    // As the caller got it: a tag must not be reformatted. An empty tag clears the caller's header as well, so no stale
    // tag goes out.
    if (tag) {
      headers.set(name, tag)
    } else if (tag !== undefined) {
      headers.delete(name)
    }
  }

  // The helper serializes the body, so it names its media type.
  if (body) {
    headers.set('Content-Type', body.contentType)
  }

  // The body is read within the request's deadline, as storytellerFetch reads it.
  return await runRequest(url, { ...init, method, headers, body: body?.content }, async (response): Promise<VersionedResult<Annotation>> => {
    if (!response.ok) {
      throw StorytellerError.fromResponse(response.status, response.statusText, await readBody(response))
    }

    // An empty header is no tag.
    return { value: await readAnnotation(response), etag: response.headers.get('etag') || undefined }
  })
}

async function readAnnotation(response: Response): Promise<Annotation> {
  const body = await readBody(response)

  // An annotation is a JSON object; anything else is not the API's answer.
  if (body === null || typeof body !== 'object' || Array.isArray(body) || body instanceof Blob) {
    throw new StorytellerError({
      status: response.status,
      message: 'The Storyteller API answered without an annotation object.',
      body,
    })
  }

  return body as Annotation
}
