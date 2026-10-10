import { markRaw } from 'vue'
import {
  and,
  isAnyOfControl,
  isBooleanControl,
  isControl,
  isDateControl,
  isDateTimeControl,
  isEnumControl,
  isIntegerControl,
  isNumberControl,
  isObjectArrayControl,
  isObjectControl,
  isOneOfControl,
  isOneOfEnumControl,
  isPrimitiveArrayControl,
  isStringControl,
  isTimeControl,
  or,
  rankWith,
  schemaMatches,
  uiTypeIs,
  type JsonFormsRendererRegistryEntry,
  type JsonSchema,
} from '@jsonforms/core'
import ArrayLayout from './ArrayLayout.vue'
import BooleanControl from './BooleanControl.vue'
import CombinatorRenderer from './CombinatorRenderer.vue'
import DateTimeControl from './DateTimeControl.vue'
import EnumControl from './EnumControl.vue'
import FallbackJsonControl from './FallbackJsonControl.vue'
import LayoutRenderer from './LayoutRenderer.vue'
import MapControl from './MapControl.vue'
import NumberControl from './NumberControl.vue'
import ObjectRenderer from './ObjectRenderer.vue'
import PrimitiveArrayControl from './PrimitiveArrayControl.vue'
import TextControl from './TextControl.vue'

type Schema = JsonSchema & Record<string, unknown>

const isSpecialString = schemaMatches((schema) => {
  const value = schema as Schema
  return (
    value.format === 'password' ||
    value.format === 'textarea' ||
    value.format === 'uri' ||
    value.format === 'url' ||
    value['x-secret'] === true ||
    value['x-multiline'] === true ||
    Number(value.maxLength ?? 0) > 200
  )
})

const isMap = schemaMatches((schema) => {
  const value = schema as Schema
  return !value.properties && typeof value.additionalProperties === 'object' && value.additionalProperties !== null
})

/** Keywords the form cannot express; such subtrees get the inline JSON editor. */
const isUnsupported = schemaMatches((schema) => {
  const value = schema as Schema
  return (
    'not' in value ||
    'if' in value ||
    'patternProperties' in value ||
    Array.isArray(value.items) ||
    'prefixItems' in value
  )
})

const entry = (
  rank: number,
  tester: Parameters<typeof rankWith>[1],
  renderer: object,
): JsonFormsRendererRegistryEntry => ({
  tester: rankWith(rank, tester),
  renderer: markRaw(renderer),
})

/**
 * The Nuxt UI renderer set for JSON Forms (spec section 10), ranked so specific renderers win: special strings
 * over plain strings, oneOf enums over oneOf objects, maps over plain objects, and the JSON fallback last
 * (but first for keywords the form cannot express).
 */
export const nuxtUiRenderers: JsonFormsRendererRegistryEntry[] = [
  entry(1, or(uiTypeIs('VerticalLayout'), uiTypeIs('HorizontalLayout'), uiTypeIs('Group')), LayoutRenderer),
  entry(1, isControl, FallbackJsonControl),
  entry(2, isStringControl, TextControl),
  entry(3, and(isStringControl, isSpecialString), TextControl),
  entry(2, or(isNumberControl, isIntegerControl), NumberControl),
  entry(2, isBooleanControl, BooleanControl),
  entry(3, isEnumControl, EnumControl),
  entry(5, isOneOfEnumControl, EnumControl),
  entry(4, or(isDateControl, isTimeControl, isDateTimeControl), DateTimeControl),
  entry(4, isPrimitiveArrayControl, PrimitiveArrayControl),
  entry(4, isObjectArrayControl, ArrayLayout),
  entry(2, isObjectControl, ObjectRenderer),
  entry(4, and(isObjectControl, isMap), MapControl),
  entry(4, or(isOneOfControl, isAnyOfControl), CombinatorRenderer),
  entry(50, and(isControl, isUnsupported), FallbackJsonControl),
]
