import { z } from 'zod'

/**
 * The delimiter of the invite dialog's tags input: a typed space, comma or semicolon commits the typed text. Anchored,
 * because reka tests it against the whole inserted text: a paste or an autofill is one insert and never commits a part
 * of a list.
 */
export const emailDelimiter = /^[\s,;]$/

/** A quoted display name: `"Lovelace, Ada" <…>` (Gmail) or `'Lovelace, Ada' <…>` (Outlook). */
const quotedName = /"[^"]*"(?=\s*<)|'[^']*'(?=\s*<)/g
const lineSeparators = /[;\r\n]+/
const hasAngleAddress = /<[^<>]*>/
/** A word that can only be a part of a display name: no `@`, no dot. */
const nameWord = /^\s*[^\s@.<>]+\s*$/
/** An address in angle brackets, or a word. Quotes and square brackets end a word (a CSV cell, a JSON array). */
const words = /<([^<>]*)>|[^\s<>"[\]]+/g

/**
 * The entries of a list: its parts between semicolons, line breaks and commas. Outlook does not quote a display name
 * such as `Lovelace, Ada <ada@example.com>`, so a single name word before a comma joins the entry with the address.
 */
function entriesOf(text: string): string[] {
  const entries: string[] = []
  for (const line of text.replace(quotedName, ' ').split(lineSeparators)) {
    const parts = line.split(',')
    for (let index = 0; index < parts.length; index++) {
      const part = parts[index]!
      const next = parts[index + 1]
      if (next !== undefined && nameWord.test(part) && hasAngleAddress.test(next)) {
        parts[index + 1] = `${part} ${next}`
      } else {
        entries.push(part)
      }
    }
  }
  return entries
}

/**
 * The addresses of a typed or pasted list (424-41), in lower case as the API stores them, without duplicates. Spaces,
 * tabs, line breaks, commas and semicolons separate them. A mail client's `Ada Lovelace <ada@example.com>` gives the
 * bare address, which is all the API accepts: in an entry with such a form, the words without `@` outside the
 * brackets are the display name and are dropped. Elsewhere every word is kept, so a typo shows as an invalid address
 * instead of disappearing.
 */
export function splitEmails(text: string): string[] {
  const emails = new Set<string>()
  for (const entry of entriesOf(text)) {
    const named = hasAngleAddress.test(entry)
    for (const [word, bracketed] of entry.matchAll(words)) {
      const email = (bracketed ?? word)
        .trim()
        .replace(/^'(.*)'$/, '$1')
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
