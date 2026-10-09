import { ref } from 'vue'

const open = ref(false)

export interface ShortcutGroup {
  title: string
  shortcuts: { keys: string[]; description: string }[]
}

/** Keyboard shortcuts listed in the help dialog (spec section 4). */
export const shortcutGroups: ShortcutGroup[] = [
  {
    title: 'General',
    shortcuts: [
      { keys: ['meta', 'K'], description: 'Command palette' },
      { keys: ['/'], description: 'Focus the search box' },
      { keys: ['['], description: 'Toggle the sidebar' },
      { keys: ['?'], description: 'Show this help' },
    ],
  },
  {
    title: 'Go to',
    shortcuts: [
      { keys: ['G', 'O'], description: 'Organizations' },
      { keys: ['G', 'A'], description: 'Annotations' },
      { keys: ['G', 'C'], description: 'Configurations' },
      { keys: ['G', 'G'], description: 'Graph' },
      { keys: ['G', 'T'], description: 'Templates' },
      { keys: ['G', 'S'], description: 'Schemas' },
    ],
  },
  {
    title: 'Lists and details',
    shortcuts: [
      { keys: ['J'], description: 'Next row' },
      { keys: ['K'], description: 'Previous row' },
      { keys: ['enter'], description: 'Open the detail' },
      { keys: ['escape'], description: 'Close the detail' },
      { keys: ['meta', 'S'], description: 'Save' },
      { keys: ['meta', 'shift', 'F'], description: 'Cycle Form, JSON and YAML' },
    ],
  },
]

export function useShortcutsHelp() {
  return { open, show: () => (open.value = true), hide: () => (open.value = false) }
}
