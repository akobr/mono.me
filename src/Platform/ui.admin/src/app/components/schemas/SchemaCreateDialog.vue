<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { isValidAnnotationKey, typeCodes, typeMeta, type AnnotationTypeCode } from '@/utils/annotation-key'
import { schemaRoute, type SchemaTarget } from '@/utils/schema-targets'

/** Picks the kind, type and key of a new schema, then opens its editor with a starter document. */
const open = defineModel<boolean>('open', { default: false })

const router = useRouter()
const context = useContextStore()
const catalog = useCatalogStore()

const kind = ref<SchemaTarget['kind']>('type')
const type = ref<AnnotationTypeCode>('exe')
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
const types = computed(() => typeCodes.map((code) => ({ label: typeMeta[code].label, value: code })))
const keys = computed(() => [...catalog.byKey.keys()].toSorted())

const target = computed<SchemaTarget | undefined>(() => {
  if (kind.value === 'type') {
    return { kind: 'type', type: type.value }
  }
  if (!isValidAnnotationKey(key.value)) {
    return undefined
  }
  return kind.value === 'annotation'
    ? { kind: 'annotation', key: key.value }
    : { kind: 'descendant', key: key.value, type: type.value }
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
        <UFormField v-if="kind !== 'annotation'" label="Annotation type">
          <USelect v-model="type" :items="types" class="w-full" />
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
