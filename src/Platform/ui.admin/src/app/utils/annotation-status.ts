import type { Annotation } from '@42for.net/2splatform.sdk'

export type AnnotationStatus = 'active' | 'disabled' | 'scheduled' | 'expired'

export const annotationStatuses: AnnotationStatus[] = ['active', 'disabled', 'scheduled', 'expired']

export const statusMeta: Record<
  AnnotationStatus,
  { label: string; color: 'success' | 'neutral' | 'info' | 'warning'; icon: string }
> = {
  active: { label: 'Active', color: 'success', icon: 'i-lucide-circle-check' },
  disabled: { label: 'Disabled', color: 'neutral', icon: 'i-lucide-circle-slash' },
  scheduled: { label: 'Scheduled', color: 'info', icon: 'i-lucide-calendar-clock' },
  expired: { label: 'Expired', color: 'warning', icon: 'i-lucide-calendar-x' },
}

type StatusFields = Pick<Annotation, 'IsDisabled' | 'ValidFrom' | 'ExpiresAt'>

/**
 * Spec section 7, with `now` in UTC: disabled wins, then a future ValidFrom (scheduled), then an
 * ExpiresAt at or before now (expired); otherwise active. Invalid dates are ignored.
 */
export function annotationStatus(annotation: StatusFields, now: number = Date.now()): AnnotationStatus {
  if (annotation.IsDisabled) {
    return 'disabled'
  }

  const validFrom = annotation.ValidFrom ? Date.parse(annotation.ValidFrom) : Number.NaN
  if (!Number.isNaN(validFrom) && validFrom > now) {
    return 'scheduled'
  }

  const expiresAt = annotation.ExpiresAt ? Date.parse(annotation.ExpiresAt) : Number.NaN
  if (!Number.isNaN(expiresAt) && expiresAt <= now) {
    return 'expired'
  }

  return 'active'
}
