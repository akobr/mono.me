<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import {
  canDescend,
  descendantTypeCodes,
  isValidAnnotationKey,
  typeCodes,
  typeMeta,
  type AnnotationTypeCode,
} from '@/utils/annotation-key'
import { schemaRoute, type SchemaTarget } from '@/utils/schema-targets'

/** Picks the kind, type and key of a new schema, then opens its editor with a starter document. */
const open = defineModel<boolean>('open', { default: false })

const router = useRouter()
const context = useContextStore()
const catalog = useCatalogStore()

const kind = ref<SchemaTarget['kind']>('type')
/** Undefined only while no type can descend from the key of a descendant-type schema. */
const type = ref<AnnotationTypeCode | undefined>('exe')
const key = ref('')

const kinds = [
  { label: 'Type schema', value: 'type', description: 'Every configuration of one annotation type.' },
  { label: 'Annotation schema', value: 'annotation', description: 'The configuration of one annotation.' },
  {
    label: 'Descendant-type schema',
    value: 'descendant',
    description: 'Configurations of a type below one annotation.',
  },
]
// A descendant-type schema exists only for a type that can descend from its key; until the key is valid, any type.
const codes = computed(() =>
  kind.value === 'descendant' && isValidAnnotationKey(key.value) ? descendantTypeCodes(key.value) : typeCodes,
)
const types = computed(() => codes.value.map((code) => ({ label: typeMeta[code].label, value: code })))
const keys = computed(() => [...catalog.byKey.keys()].toSorted())
const nothingDescends = computed(() => !codes.value.length)

// The selected type cannot descend from the new key: take the first that can.
watch(codes, (next) => {
  if (!type.value || !next.includes(type.value)) {
    type.value = next[0]
  }
})

const target = computed<SchemaTarget | undefined>(() => {
  if (kind.value === 'type') {
    return type.value ? { kind: 'type', type: type.value } : undefined
  }
  if (!isValidAnnotationKey(key.value)) {
    return undefined
  }
  if (kind.value === 'annotation') {
    return { kind: 'annotation', key: key.value }
  }
  return type.value && canDescend(key.value, type.value)
    ? { kind: 'descendant', key: key.value, type: type.value }
    : undefined
})

async function go() {
  if (target.value && context.scope) {
    open.value = false
    await router.push(schemaRoute(context.scope, target.value))
  }
}
</script>

<template>
  <UModal v-model:open="open" title="New schema">
    <template #body>
      <div class="space-y-4">
        <URadioGroup v-model="kind" :items="kinds" />
        <UFormField
          v-if="kind !== 'annotation'"
          label="Annotation type"
          :help="nothingDescends ? `No annotation type descends from ${key}.` : undefined"
        >
          <USelect v-model="type" :items="types" :disabled="nothingDescends" class="w-full" />
        </UFormField>
        <UFormField v-if="kind !== 'type'" label="Annotation key">
          <UInputMenu
            v-model="key"
            :items="keys"
            class="w-full"
            :ui="{ base: 'font-mono' }"
            placeholder="exe.subject.responsibility.context"
            virtualize
          />
        </UFormField>
        <div class="flex justify-end gap-2">
          <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
          <UButton label="Open editor" :disabled="!target" @click="go" />
        </div>
      </div>
    </template>
  </UModal>
</template>
