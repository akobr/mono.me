import { ref } from 'vue'

const open = ref(false)

/** Open state of the "New annotation" dialog, shared by the annotations page and the command palette. */
export function useAnnotationCreate() {
  return { open, show: () => (open.value = true) }
}
