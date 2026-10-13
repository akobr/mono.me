import { readonly, shallowRef } from 'vue'

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

export type DateFormat = 'yyyy-mm-dd' | 'dd.mm.yyyy' | 'dd/mm/yyyy' | 'mm/dd/yyyy' | 'd mmm yyyy'

export const defaultDateFormat: DateFormat = 'yyyy-mm-dd'

/**
 * The date formats offered in Settings and Preferences (424-23), in this order. `inputLocale` is an English locale
 * whose numeric date has the same order of year, month and day, so UInputDate shows its segments in that order with
 * the `yyyy`, `mm` and `dd` placeholders.
 */
export const dateFormats: readonly { value: DateFormat; inputLocale: string }[] = [
  { value: 'yyyy-mm-dd', inputLocale: 'en-CA' },
  { value: 'dd.mm.yyyy', inputLocale: 'en-CH' },
  { value: 'dd/mm/yyyy', inputLocale: 'en-GB' },
  { value: 'mm/dd/yyyy', inputLocale: 'en-US' },
  { value: 'd mmm yyyy', inputLocale: 'en-GB' },
]

export const isDateFormat = (value: unknown): value is DateFormat =>
  dateFormats.some((format) => format.value === value)

const current = shallowRef<DateFormat>(defaultDateFormat)

/**
 * The date format in effect. The formatters read it, so every template, computed or render function that calls them
 * follows a change. The date-format store sets it from the project settings and Preferences while it lives.
 */
export const dateFormat = readonly(current)

export function applyDateFormat(format: DateFormat): void {
  current.value = isDateFormat(format) ? format : defaultDateFormat
}

/** The locale that gives UInputDate the segment order of the format. */
export function dateInputLocale(format: DateFormat = current.value): string {
  return dateFormats.find((entry) => entry.value === format)?.inputLocale ?? 'en-CA'
}

const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

/** A calendar date without a time or a zone (month 1–12), such as a JSON Schema `date`. */
export function formatDateParts(year: number, month: number, day: number, format: DateFormat = current.value): string {
  const yyyy = String(year).padStart(4, '0')
  const mm = String(month).padStart(2, '0')
  const dd = String(day).padStart(2, '0')
  switch (format) {
    case 'dd.mm.yyyy':
      return `${dd}.${mm}.${yyyy}`
    case 'dd/mm/yyyy':
      return `${dd}/${mm}/${yyyy}`
    case 'mm/dd/yyyy':
      return `${mm}/${dd}/${yyyy}`
    case 'd mmm yyyy':
      return `${day} ${monthNames[month - 1]} ${yyyy}`
    default:
      return `${yyyy}-${mm}-${dd}`
  }
}

/**
 * The fields of an instant in the given IANA zone (or the browser's, when the zone is unknown). `en-US` keeps the
 * digits Latin and the zone names English; `h23` keeps midnight at 00.
 */
function partsOf(date: Date, timeZone?: string) {
  const options: Intl.DateTimeFormatOptions = {
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
    timeZoneName: 'short',
  }
  let formatter: Intl.DateTimeFormat
  try {
    formatter = new Intl.DateTimeFormat('en-US', { ...options, timeZone })
  } catch {
    formatter = new Intl.DateTimeFormat('en-US', options)
  }
  const parts = Object.fromEntries(formatter.formatToParts(date).map((part) => [part.type, part.value]))
  return {
    year: Number(parts.year),
    month: Number(parts.month),
    day: Number(parts.day),
    time: `${parts.hour}:${parts.minute}`,
    zone: parts.timeZoneName ?? '',
  }
}

/** The date in the given IANA zone (or the browser's), in the format in effect: "2026-10-13". */
export function formatDate(date: Date, timeZone?: string, format: DateFormat = current.value): string {
  const parts = partsOf(date, timeZone)
  return formatDateParts(parts.year, parts.month, parts.day, format)
}

/** Date and 24-hour time in the given IANA zone (or the browser's), with the zone name: "2026-10-13 15:04 UTC". */
export function formatDateTime(date: Date, timeZone?: string, format: DateFormat = current.value): string {
  const parts = partsOf(date, timeZone)
  return `${formatDateParts(parts.year, parts.month, parts.day, format)} ${parts.time} ${parts.zone}`.trimEnd()
}

/** Select items of the date formats, each with the date in that format: "2026-10-13 (yyyy-mm-dd)". */
export function dateFormatItems(today: Date = new Date()): { label: string; value: DateFormat }[] {
  return dateFormats.map(({ value }) => ({ label: `${formatDate(today, undefined, value)} (${value})`, value }))
}
