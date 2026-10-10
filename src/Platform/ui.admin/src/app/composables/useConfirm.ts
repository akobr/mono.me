import ConfirmModal from '@/components/common/ConfirmModal.vue'

export interface ConfirmOptions {
  title: string
  description?: string
  confirmLabel?: string
  cancelLabel?: string
  color?: 'error' | 'warning' | 'primary'
}

/** `await confirm({ title })` resolves to true when the user confirms. */
export function useConfirm() {
  const overlay = useOverlay()
  const modal = overlay.create(ConfirmModal)

  return async (options: ConfirmOptions): Promise<boolean> => {
    const instance = modal.open(options)
    return (await instance.result) === true
  }
}
