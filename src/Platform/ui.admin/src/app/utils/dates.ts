const units: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 3600],
  ['month', 30 * 24 * 3600],
  ['week', 7 * 24 * 3600],
  ['day', 24 * 3600],
  ['hour', 3600],
  ['minute', 60],
]

/** "3 hours ago", "in 2 days"; "just now" under a minute. Uses the browser locale. */
export function formatRelative(date: Date, now: Date = new Date()): string {
  const seconds = Math.round((date.getTime() - now.getTime()) / 1000)
  const format = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })

  for (const [unit, size] of units) {
    if (Math.abs(seconds) >= size) {
      return format.format(Math.trunc(seconds / size), unit)
    }
  }

  return 'just now'
}

/** Date and time in the given IANA zone (or the browser's), with the zone name. */
export function formatDateTime(date: Date, timeZone?: string): string {
  try {
    return new Intl.DateTimeFormat(undefined, {
      dateStyle: 'medium',
      timeStyle: 'short',
      timeZone,
      timeZoneName: 'short',
    }).format(date)
  } catch {
    return new Intl.DateTimeFormat(undefined, {
      dateStyle: 'medium',
      timeStyle: 'short',
      timeZoneName: 'short',
    }).format(date)
  }
}

export function formatDate(date: Date, timeZone?: string): string {
  try {
    return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeZone }).format(date)
  } catch {
    return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(date)
  }
}
