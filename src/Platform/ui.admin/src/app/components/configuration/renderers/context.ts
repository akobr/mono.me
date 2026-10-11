import { inject, provide, type InjectionKey, type Ref } from 'vue'

export interface SchemaFormContext {
  /** The effective document (ancestors, template and own content merged), for placeholders and the "inherited" chip. */
  effective: Ref<unknown>
  /** The own content as saved. The effective document contains it too, so a value counts as inherited only where the own content has none. */
  own: Ref<unknown>
  readonly: Ref<boolean>
  /** Copies the inherited value into the own content. */
  override: (pointer: string, value: unknown) => void
  /** Removes the own value, so the inherited value applies again. */
  reset: (pointer: string) => void
}

const key: InjectionKey<SchemaFormContext> = Symbol('schema-form')

export function provideSchemaForm(context: SchemaFormContext): void {
  provide(key, context)
}

export function useSchemaForm(): SchemaFormContext | undefined {
  return inject(key, undefined)
}
