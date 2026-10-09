// Orval input transformer: corrects the OpenAPI document before generation.
//
// JSON Patch bodies (application/json-patch+json) are documented by the API as "type: object", because the
// functions declare JArray. They are arrays of RFC 6902 operations, so the generated types, Zod schemas and
// mocks use JsonPatchOperation[] instead. Remove this once the API documents the array itself.
//
// Grant and revoke stay in the API for the CLI (sform); browser clients set exact roles through the members
// endpoints (setMemberRole, removeMember). They are marked deprecated here only, so NSwag emits no [Obsolete].

const jsonPatchContentType = 'application/json-patch+json'
const deprecatedOperations = new Set(['GrantUserAccess', 'RevokeUserAccess'])

const jsonPatchOperation = {
  type: 'object',
  description: 'One RFC 6902 JSON Patch operation.',
  required: ['op', 'path'],
  properties: {
    op: { type: 'string', enum: ['add', 'remove', 'replace', 'move', 'copy', 'test'] },
    path: { type: 'string', description: 'JSON Pointer (RFC 6901) of the target location, for example /retries.' },
    from: { type: 'string', description: 'JSON Pointer of the source location, for move and copy.' },
    value: { description: 'The value for add, replace and test.' },
  },
}

export default function transform(document) {
  document.components ??= {}
  document.components.schemas ??= {}
  document.components.schemas.JsonPatchOperation = jsonPatchOperation

  for (const pathItem of Object.values(document.paths ?? {})) {
    for (const operation of Object.values(pathItem ?? {})) {
      const requestBody = operation?.requestBody
      const content = requestBody?.content?.[jsonPatchContentType]

      if (content) {
        content.schema = { type: 'array', items: { $ref: '#/components/schemas/JsonPatchOperation' } }
        requestBody.required = true
      }

      if (deprecatedOperations.has(operation?.operationId)) {
        operation.deprecated = true
      }
    }
  }

  return document
}
