import { z } from 'zod'

/**
 * The delimiter of the invite dialog's tags input: a typed space, comma or semicolon commits the typed text. Anchored,
 * because reka tests it against the whole inserted text: a paste or an autofill is one insert and never commits a part
 * of a list.
 */
export const emailDelimiter = /^[\s,;]$/

const quoted = /"[^"]*"/g
const listSeparators = /[,;\r\n]+/
const hasAngleAddress = /<[^<>]*>/
/** An address in angle brackets, or a word. */
const words = /<([^<>]*)>|[^\s<>]+/g

/**
 * The addresses of a typed or pasted list (424-41), in lower case as the API stores them, without duplicates. Spaces,
 * tabs, line breaks, commas and semicolons separate them. A mail client's `Ada Lovelace <ada@example.com>` gives the
 * bare address, which is all the API accepts: in an entry (between commas, semicolons or line breaks) with such a
 * form, the words without `@` outside the brackets are the display name and are dropped. Elsewhere every word is
 * kept, so a typo shows as an invalid address instead of disappearing.
 */
export function splitEmails(text: string): string[] {
  const emails = new Set<string>()
  for (const entry of text.replace(quoted, ' ').split(listSeparators)) {
    const named = hasAngleAddress.test(entry)
    for (const [word, bracketed] of entry.matchAll(words)) {
      const email = (bracketed ?? word)
        .trim()
        .replace(/^mailto:/i, '')
        .toLowerCase()
      if (email && (bracketed !== undefined || !named || email.includes('@'))) {
        emails.add(email)
      }
    }
  }
  return [...emails]
}

/** Each entry read by {@link splitEmails}, without duplicates across the entries. */
export function normalizeEmails(entries: readonly string[]): string[] {
  return [...new Set(entries.flatMap(splitEmails))]
}

const emailSchema = z.email()

export const isEmail = (value: string) => emailSchema.safeParse(value).success
