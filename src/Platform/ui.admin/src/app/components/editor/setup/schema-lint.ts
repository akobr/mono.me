import { linter, type Diagnostic } from '@codemirror/lint'
import type { Extension } from '@codemirror/state'
import { findNodeAtLocation, parseTree } from 'jsonc-parser'
import { isMap, isScalar, isSeq, parseDocument as parseYamlDocument, type Node as YamlNode } from 'yaml'
import { parsePointer } from '@/utils/json-pointer'
import { parseDocument, type DocumentFormat } from '@/utils/json-yaml'
import { validateAgainst } from '@/utils/schema-validation'

/** Text range of a pointer in a JSON document (the value, or the parent when the value is missing). */
function jsonRange(text: string, segments: string[]): { from: number; to: number } | undefined {
  const tree = parseTree(text)
  if (!tree) {
    return undefined
  }
  for (let length = segments.length; length >= 0; length--) {
    const path = segments.slice(0, length).map((segment) => (/^\d+$/.test(segment) ? Number(segment) : segment))
    const node = findNodeAtLocation(tree, path)
    if (node) {
      // A property value: underline the key and the value.
      const start = node.parent?.type === 'property' ? node.parent.offset : node.offset
      return { from: start, to: node.offset + Math.min(node.length, 200) }
    }
  }
  return undefined
}

/** Text range of a pointer in a YAML document, from the CST node ranges. */
function yamlRange(text: string, segments: string[]): { from: number; to: number } | undefined {
  const document = parseYamlDocument(text, { keepSourceTokens: true })
  for (let length = segments.length; length >= 0; length--) {
    const node = document.getIn(segments.slice(0, length), true) as YamlNode | undefined
    if (node && (isScalar(node) || isMap(node) || isSeq(node)) && node.range) {
      return { from: node.range[0], to: Math.min(node.range[1], node.range[0] + 200) }
    }
  }
  return undefined
}

/**
 * Ajv-based linting against a JSON Schema (spec section 9.3, the fallback that replaces codemirror-json-schema):
 * the document is parsed, validated, and each error is mapped from its JSON Pointer to a text range with
 * jsonc-parser (JSON) or the yaml CST (YAML). Errors on binding values are not reported.
 */
export function schemaLinter(schema: object, format: DocumentFormat): Extension {
  return linter(
    (view) => {
      const text = view.state.doc.toString()
      const parsed = parseDocument(text, format)
      if (!parsed.ok) {
        return []
      }

      return validateAgainst(schema, parsed.value).map((issue): Diagnostic => {
        const segments = parsePointer(issue.pointer)
        const range = (format === 'json' ? jsonRange(text, segments) : yamlRange(text, segments)) ?? { from: 0, to: 0 }
        return {
          from: Math.min(range.from, text.length),
          to: Math.min(Math.max(range.to, range.from), text.length),
          severity: 'error',
          message: `${issue.pointer || '/'}: ${issue.message}`,
          source: 'schema',
        }
      })
    },
    { delay: 400 },
  )
}
