// Orval input transformer: corrects the OpenAPI document before generation.
//
// JSON Patch bodies (application/json-patch+json) are documented by the API as "type: object", because the
// functions declare JArray. They are arrays of RFC 6902 operations, so the generated types, Zod schemas and
// mocks use JsonPatchOperation[] instead. Remove this once the API documents the array itself.
//
// Grant and revoke stay in the API for the CLI (sform); browser clients set exact roles through the members
// endpoints (setMemberRole, removeMember). They are marked deprecated here only, so NSwag emits no [Obsolete].
//
// Values of annotations and configurations are IReadOnlyDictionary<string, object> in C#, so each value can be any
// JSON (a number, a string, an array). The document says "additionalProperties: { type: object }", which would make
// the generated Zod schemas reject primitive values; the transformer widens them to any JSON value.
//
// The effective and the resolved configuration read take ?format=flat|properties|env|yaml, which answers the Content alone
// as text/plain, application/yaml or a JSON object of strings. Orval would add a params argument before options and turn
// the return type into Configuration | string, so the transformer removes the parameter and the other media types of the
// 200: the generated functions, hooks, schemas and mocks keep the envelope, and lib/conditional.ts reads the formats.

const jsonPatchContentType = 'application/json-patch+json'
const deprecatedOperations = new Set(['GrantUserAccess', 'RevokeUserAccess'])
const formattedOperations = new Set(['GetConfiguration', 'GetResolvedConfiguration'])
const jsonContentType = 'application/json'

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

  for (const schema of Object.values(document.components.schemas)) {
    const values = schema?.properties?.Values

    if (values?.type === 'object' && values.additionalProperties?.type === 'object') {
      values.additionalProperties = {}
    }
  }

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

      if (formattedOperations.has(operation?.operationId)) {
        keepEnvelope(operation)
      }
    }
  }

  return document
}

// Only the Configuration of application/json stays: no format parameter, and no text bodies of the 200.
function keepEnvelope(operation) {
  operation.parameters = operation.parameters?.filter((parameter) => !(parameter?.in === 'query' && parameter?.name === 'format'))

  const content = operation.responses?.['200']?.content

  for (const contentType of Object.keys(content ?? {})) {
    if (contentType !== jsonContentType) {
      delete content[contentType]
    }
  }
}
