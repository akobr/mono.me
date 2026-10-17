import { describe, expect, it } from 'vitest'
import { emailDelimiter, isEmail, normalizeEmails, splitEmails } from '@/utils/emails'

describe('splitEmails', () => {
  it('splits on spaces, tabs, line breaks, commas and semicolons, in any number', () => {
    expect(splitEmails('ada@example.com grace@example.com,alan@example.com')).toEqual([
      'ada@example.com',
      'grace@example.com',
      'alan@example.com',
    ])
    expect(splitEmails(' , ada@example.com;;\tgrace@example.com\r\n\r\nalan@example.com , ;')).toEqual([
      'ada@example.com',
      'grace@example.com',
      'alan@example.com',
    ])
  })

  it('reads nothing from empty text and from separators only', () => {
    expect(splitEmails('')).toEqual([])
    expect(splitEmails(' \t\r\n')).toEqual([])
    expect(splitEmails(' ,; ,')).toEqual([])
  })

  it('lower-cases the addresses, as the API stores them, and keeps the first of the duplicates', () => {
    expect(splitEmails('Ada@Example.com grace@example.com ada@example.com ADA@EXAMPLE.COM')).toEqual([
      'ada@example.com',
      'grace@example.com',
    ])
  })

  it('reads the bare address of the forms a mail client copies, without the display names', () => {
    expect(
      splitEmails('Ada Lovelace <ada@example.com>, "Hopper, Grace" <grace@example.com>; <alan@example.com>'),
    ).toEqual(['ada@example.com', 'grace@example.com', 'alan@example.com'])
    expect(splitEmails('Ada Lovelace <ada@example.com> grace@example.com')).toEqual([
      'ada@example.com',
      'grace@example.com',
    ])
  })

  it("reads Outlook's unquoted and single-quoted Last, First display names", () => {
    expect(splitEmails('Lovelace, Ada <ada@example.com>; Hopper, Grace <grace@example.com>')).toEqual([
      'ada@example.com',
      'grace@example.com',
    ])
    expect(splitEmails("O'Brien, Bob <bob@example.com>; Doe, Jane X. <jane@example.com>")).toEqual([
      'bob@example.com',
      'jane@example.com',
    ])
    expect(splitEmails("'Lovelace, Ada' <ada@example.com>; 'ada@example.com' <ada@example.com>")).toEqual([
      'ada@example.com',
    ])
    expect(splitEmails('alan@example.com, Lovelace, Ada <ada@example.com>')).toEqual([
      'alan@example.com',
      'ada@example.com',
    ])
  })

  it('reads quoted addresses of a CSV row or a JSON array', () => {
    expect(splitEmails('"ada@example.com","grace@example.com"')).toEqual(['ada@example.com', 'grace@example.com'])
    expect(splitEmails('["ada@example.com", "grace@example.com"]')).toEqual(['ada@example.com', 'grace@example.com'])
    expect(splitEmails("['ada@example.com', 'grace@example.com']")).toEqual(['ada@example.com', 'grace@example.com'])
    expect(splitEmails('ada@example.com "grace@example.com" alan@example.com')).toEqual([
      'ada@example.com',
      'grace@example.com',
      'alan@example.com',
    ])
    // a quoted name without an address in brackets stays visible
    expect(splitEmails('"Hopper, Grace","grace@example.com"')).toEqual(['hopper', 'grace', 'grace@example.com'])
    // a single quote inside an address stays
    expect(splitEmails("o'brien@example.com")).toEqual(["o'brien@example.com"])
  })

  it('removes a mailto: prefix', () => {
    expect(splitEmails('mailto:ada@example.com MAILTO:grace@example.com')).toEqual([
      'ada@example.com',
      'grace@example.com',
    ])
  })

  it('keeps a word without @ as a typo, unless its entry is in the mail client form', () => {
    expect(splitEmails('ada@example.com grace.example.com')).toEqual(['ada@example.com', 'grace.example.com'])
    // the typo is in its own entry, so it is not taken for a display name
    expect(splitEmails('Ada <ada@example.com>, grace.example.com')).toEqual(['ada@example.com', 'grace.example.com'])
    expect(splitEmails('grace.example.com, Ada <ada@example.com>')).toEqual(['grace.example.com', 'ada@example.com'])
    expect(splitEmails('ada@example.com <grace.example.com>')).toEqual(['ada@example.com', 'grace.example.com'])
  })
})

describe('normalizeEmails', () => {
  it('splits every entry and removes the duplicates across them', () => {
    expect(normalizeEmails(['ada@example.com', 'Grace@example.com alan@example.com,', 'ADA@example.com'])).toEqual([
      'ada@example.com',
      'grace@example.com',
      'alan@example.com',
    ])
    expect(normalizeEmails([' ', ''])).toEqual([])
  })

  it('reads each entry on its own, so a mail client entry does not drop a typo of another one', () => {
    expect(normalizeEmails(['grace.example.com', 'Ada <ada@example.com>'])).toEqual([
      'grace.example.com',
      'ada@example.com',
    ])
  })
})

describe('isEmail', () => {
  it('accepts an address and rejects the rest', () => {
    expect(isEmail('ada@example.com')).toBe(true)
    expect(isEmail('ada.lovelace+admin@mail.example.co.uk')).toBe(true)
    expect(isEmail('grace.example.com')).toBe(false)
    expect(isEmail('ada@')).toBe(false)
    expect(isEmail('ada@example')).toBe(false)
    expect(isEmail('')).toBe(false)
  })
})

describe('emailDelimiter', () => {
  it('matches one typed separator, never a longer insert', () => {
    for (const separator of [' ', ',', ';', '\t']) {
      expect(emailDelimiter.test(separator)).toBe(true)
    }
    for (const insert of ['a', '@', '.', ', ', 'ada@example.com grace@example.com', '']) {
      expect(emailDelimiter.test(insert)).toBe(false)
    }
  })
})
