import { afterEach, describe, expect, it } from 'vitest'
import { computed } from 'vue'
import {
  applyDateFormat,
  dateFormat,
  dateFormatItems,
  dateFormats,
  dateInputLocale,
  defaultDateFormat,
  formatDate,
  formatDateParts,
  formatDateTime,
  isDateFormat,
  type DateFormat,
} from '@/utils/dates'

// 2026-10-03 15:04 UTC: a day and a month below 10 show the padding of each format.
const instant = new Date('2026-10-03T15:04:00Z')

const expected: Record<DateFormat, string> = {
  'yyyy-mm-dd': '2026-10-03',
  'dd.mm.yyyy': '03.10.2026',
  'dd/mm/yyyy': '03/10/2026',
  'mm/dd/yyyy': '10/03/2026',
  'd mmm yyyy': '3 Oct 2026',
}

describe('date formats (424-23)', () => {
  afterEach(() => applyDateFormat(defaultDateFormat))

  it('shows yyyy-mm-dd by default', () => {
    expect(dateFormat.value).toBe('yyyy-mm-dd')
    expect(formatDate(instant, 'UTC')).toBe('2026-10-03')
    expect(formatDateTime(instant, 'UTC')).toBe('2026-10-03 15:04 UTC')
  })

  it.each(Object.entries(expected))('formats %s', (format, date) => {
    expect(formatDate(instant, 'UTC', format as DateFormat)).toBe(date)
    expect(formatDateTime(instant, 'UTC', format as DateFormat)).toBe(`${date} 15:04 UTC`)
    expect(formatDateParts(2026, 10, 3, format as DateFormat)).toBe(date)
  })

  it('shows the date and the 24-hour time in the given zone', () => {
    expect(formatDate(instant, 'Asia/Tokyo')).toBe('2026-10-04')
    expect(formatDateTime(instant, 'Asia/Tokyo')).toBe('2026-10-04 00:04 GMT+9')
    expect(formatDateTime(new Date('2026-01-15T23:30:00Z'), 'America/New_York')).toBe('2026-01-15 18:30 EST')
  })

  it('falls back to the browser zone for a zone Intl does not know', () => {
    expect(formatDateTime(instant, 'Not/AZone')).toBe(formatDateTime(instant))
    expect(formatDate(instant, 'Not/AZone')).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })

  it('pads the year of a calendar date', () => {
    expect(formatDateParts(987, 1, 2)).toBe('0987-01-02')
  })

  it('knows its formats only', () => {
    expect(dateFormats.map((format) => format.value)).toEqual(Object.keys(expected))
    expect(isDateFormat('dd.mm.yyyy')).toBe(true)
    expect(isDateFormat('yyyy/mm/dd')).toBe(false)
    expect(isDateFormat(undefined)).toBe(false)
  })

  it('gives the date inputs a locale with the same order of segments', () => {
    const order = (locale: string) =>
      new Intl.DateTimeFormat(locale, { year: 'numeric', month: '2-digit', day: '2-digit', timeZone: 'UTC' })
        .formatToParts(instant)
        .map((part) => (part.type === 'literal' ? part.value : part.type))
        .join('')

    expect(order(dateInputLocale('yyyy-mm-dd'))).toBe('year-month-day')
    expect(order(dateInputLocale('dd.mm.yyyy'))).toBe('day.month.year')
    expect(order(dateInputLocale('dd/mm/yyyy'))).toBe('day/month/year')
    expect(order(dateInputLocale('mm/dd/yyyy'))).toBe('month/day/year')
    expect(order(dateInputLocale('d mmm yyyy'))).toBe('day/month/year')
  })

  it('makes the formatters follow the format in effect', () => {
    const shown = computed(() => formatDate(instant, 'UTC'))
    expect(shown.value).toBe('2026-10-03')

    applyDateFormat('dd.mm.yyyy')

    expect(shown.value).toBe('03.10.2026')
    expect(dateInputLocale()).toBe('en-CH')
  })

  it('lists the formats with the date in each', () => {
    expect(dateFormatItems(instant)[0]).toEqual({
      label: expect.stringMatching(/^2026-10-0\d \(yyyy-mm-dd\)$/),
      value: 'yyyy-mm-dd',
    })
    expect(dateFormatItems(instant)).toHaveLength(dateFormats.length)
  })
})
