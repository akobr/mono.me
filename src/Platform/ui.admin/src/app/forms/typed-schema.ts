import type { TypedSchema, TypedSchemaError } from 'vee-validate'
import type { z } from 'zod'

/** Joins a Zod issue path the way VeeValidate names fields: `values[0].name`. */
export function toFieldPath(path: readonly PropertyKey[]): string {
  return path.reduce<string>((joined, segment) => {
    if (typeof segment === 'number') {
      return `${joined}[${segment}]`
    }

    return joined ? `${joined}.${String(segment)}` : String(segment)
  }, '')
}

/**
 * VeeValidate 4 adapter for Zod 4 schemas. @vee-validate/zod 4.15 supports Zod 3 only, and VeeValidate 5
 * (Standard Schema) is still in beta, so the app ships this small equivalent.
 */
export function toTypedSchema<TSchema extends z.ZodType>(
  schema: TSchema,
): TypedSchema<z.input<TSchema>, z.output<TSchema>> {
  return {
    __type: 'VVTypedSchema',
    async parse(values) {
      const result = await schema.safeParseAsync(values)

      if (result.success) {
        return { value: result.data, errors: [] }
      }

      const byPath = new Map<string, string[]>()

      for (const issue of result.error.issues) {
        const path = toFieldPath(issue.path)
        byPath.set(path, [...(byPath.get(path) ?? []), issue.message])
      }

      return { errors: [...byPath].map(([path, errors]): TypedSchemaError => ({ path, errors })) }
    },
  }
}
