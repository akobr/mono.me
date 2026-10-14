import { ref } from 'vue'

const open = ref(false)

/** Open state of the "Bulk create annotations" dialog, shared by the annotations page and the command palette. */
export function useAnnotationBulkCreate() {
  return { open, show: () => (open.value = true) }
}
