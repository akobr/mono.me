import { describe, expect, it } from 'vitest'
import { effectiveContent } from '@/mock/configuration-handlers'
import { showcaseProject, showcaseScope } from '@/mock/showcase'
import { getAllAncestors } from '@/utils/annotation-key'
import { annotationStatus } from '@/utils/annotation-status'
import { showcaseOf, unseededShowcaseDb } from './support'

// gatherly/conferences follows example 11 of the Miro board: the DevConf series, its editions and the event services
// booked for them, extended with two more series, past and coming editions, and more services and jobs.

const showcase = showcaseOf('gatherly', 'conferences')
const views = showcaseProject('gatherly', 'conferences')!.views.map((view) => view.name)

const statusOf = (view: string, key: string) => annotationStatus(showcase.annotations(view).get(key)!)

/** Every string of a document, at any depth. */
const stringsOf = (value: unknown): string[] => {
  if (typeof value === 'string') {
    return [value]
  }
  if (Array.isArray(value)) {
    return value.flatMap(stringsOf)
  }
  return value && typeof value === 'object' ? Object.values(value).flatMap(stringsOf) : []
}

describe('gatherly/conferences', () => {
  it('has the catalog of the board’s default frame', () => {
    const keys = showcase.keys('default')
    for (const key of [
      'sbt.devconf',
      'cnt.devconf.online-2026',
      'cnt.devconf.prague-2026',
      'rst.ticketing',
      'rst.streaming',
      'rst.badges',
      'unt.ticketing.reminder-mail',
      'usg.devconf.ticketing',
      'usg.devconf.streaming',
      'usg.devconf.badges',
      'exe.devconf.ticketing.prague-2026',
      'exe.devconf.ticketing.online-2026',
      'exe.devconf.streaming.prague-2026',
      'exe.devconf.streaming.online-2026',
      'exe.devconf.badges.prague-2026',
      'uxe.devconf.ticketing.prague-2026.reminder-mail',
      'uxe.devconf.ticketing.online-2026.reminder-mail',
    ]) {
      expect(keys.has(key), key).toBe(true)
    }
    // The board's slice: badges for the Prague edition only, and the reminder mail as the only job of ticketing.
    expect(
      [...keys].filter((key) => /^exe\.devconf\.(ticketing|streaming|badges)\.(online|prague)-2026$/.test(key)),
    ).toHaveLength(5)
    expect(keys.has('exe.devconf.badges.online-2026')).toBe(false)
    expect([...keys].filter((key) => /^uxe\.devconf\.ticketing\.(online|prague)-2026\./.test(key)).toSorted()).toEqual([
      'uxe.devconf.ticketing.online-2026.reminder-mail',
      'uxe.devconf.ticketing.prague-2026.reminder-mail',
    ])
  })

  it('extends it with the series, editions, services and jobs of the model', () => {
    const keys = showcase.keys('default')
    expect([...keys].filter((key) => key.startsWith('sbt.')).toSorted()).toEqual([
      'sbt.data-summit',
      'sbt.design-week',
      'sbt.devconf',
    ])
    for (const key of [
      'cnt.devconf.prague-2025',
      'cnt.devconf.berlin-2027',
      'rst.call-for-papers',
      'rst.sponsorship',
      'rst.venue-wifi',
      'unt.call-for-papers.review-reminder',
    ]) {
      expect(keys.has(key), key).toBe(true)
    }
    expect(showcase.annotations('default').get('unt.ticketing.reminder-mail')).toMatchObject({
      UnitType: 'time',
      UnitDefinition: '0 9 * * *',
    })
    // The new services belong to other views.
    expect(keys.has('rst.matchmaking')).toBe(false)
    expect(keys.has('rst.event-app')).toBe(false)
  })

  it('stores the ticket tiers and prices, the stream ingest, the badge printers and the CFP deadline', () => {
    expect(showcase.own('default', 'exe.data-summit.ticketing.london-2026')).toMatchObject({
      currency: 'GBP',
      vatRate: 20,
      tiers: expect.arrayContaining([{ name: 'regular', price: 450, quota: 840 }]),
    })
    expect(showcase.own('default', 'exe.devconf.ticketing.online-2026')?.tiers).toEqual([
      { name: 'stream-pass', price: 0, quota: 4000 },
      { name: 'pro-pass', price: 79, quota: 1000 },
    ])
    expect(showcase.own('default', 'exe.devconf.streaming.prague-2026')).toMatchObject({
      ingest: { protocol: 'srt', host: 'ingest.eu-central-1.stream.gatherly.example', port: 9000 },
      streamKey: '@(devconf.prague-2026.mux-stream-key, primaryVault)',
    })
    expect(showcase.own('default', 'exe.devconf.badges.prague-2026')).toMatchObject({
      printers: expect.arrayContaining(['badge-printer-a.prague-2026.devconf.onsite.gatherly.example']),
      model: 'zebra-zd621',
    })
    expect(showcase.own('default', 'exe.devconf.badges.prague-2026')?.printers).toHaveLength(3)
    expect(showcase.own('default', 'exe.devconf.call-for-papers.berlin-2027')).toMatchObject({
      deadline: '2027-02-28T23:59:00+01:00',
    })
  })

  it('sells the Prague editions in Czech crowns and gives the online edition a sponsor lobby', () => {
    expect(showcase.own('default', 'exe.devconf.ticketing.prague-2025')).toMatchObject({
      currency: 'CZK',
      vatRate: 21,
      tiers: [
        { name: 'early-bird', price: 6950, quota: 320 },
        { name: 'regular', price: 9900, quota: 1120 },
        { name: 'student', price: 2950, quota: 160 },
      ],
    })
    const online = showcase.own('default', 'exe.devconf.sponsorship.online-2026')
    expect(online).toEqual({
      packages: { platinum: 2, gold: 4, silver: 8 },
      virtualBooths: true,
      sponsorLobby: 'https://devconf.example/online-2026/sponsors',
    })
    expect(showcase.own('default', 'exe.devconf.sponsorship.prague-2026')).toMatchObject({
      packages: { platinum: 2, gold: 6, silver: 12 },
      boothPlan: 'https://files.gatherly.example/devconf/prague-2026/booth-plan.pdf',
    })
  })

  it('keeps the long history of the ticketing defaults and of the flagship’s ticket sales', () => {
    expect(showcase.versions('default', 'rst.ticketing')).toBeGreaterThanOrEqual(15)
    expect(showcase.versions('default', 'exe.devconf.ticketing.prague-2026')).toBe(6)
    // The early birds sold out: the flagship sells regular, student and workshop-day tickets now.
    expect(showcase.own('default', 'exe.devconf.ticketing.prague-2026')).toMatchObject({
      currency: 'CZK',
      tiers: [
        expect.objectContaining({ name: 'regular', price: 11500 }),
        expect.objectContaining({ name: 'student' }),
        expect.objectContaining({ name: 'workshop-day' }),
      ],
    })
    // The visible recent versions of the defaults tune fraud checks, refunds and the waitlist, one change at a time.
    expect(showcase.own('default', 'rst.ticketing')).toMatchObject({
      maxTicketsPerOrder: 20,
      fraudCheck: { provider: 'stripe-radar', blockScore: 65 },
      waitlist: { enabled: true, offerHours: 24 },
    })
  })

  it('requires the currency and the tiers on every edition’s ticketing', () => {
    expect(showcase.latest('default', 'schema:descendant:rst.ticketing:exe')).toMatchObject({
      required: ['currency', 'tiers'],
    })
  })

  it('mails every job run as Gatherly, answering to the organizer of the series', () => {
    const run = 'uxe.devconf.ticketing.berlin-2027.reminder-mail'
    expect(showcase.own('default', run)).toBeUndefined()
    expect(effectiveContent(unseededShowcaseDb(), showcase.scope('default'), run)).toMatchObject({
      sender: { name: 'Gatherly', address: 'no-reply@gatherly.example' },
      replyTo: '@annotation("/organizerEmail", "Subject")',
      template: 'ticket-reminder-v2',
    })
    expect(showcase.annotations('default').get('sbt.devconf')?.Values).toMatchObject({
      organizerEmail: 'team@devconf.example',
    })
  })

  it('follows the calendar: past editions expired, coming ones scheduled, services off where they do not run', () => {
    expect(statusOf('default', 'cnt.devconf.prague-2025')).toBe('expired')
    expect(statusOf('default', 'exe.devconf.ticketing.prague-2025')).toBe('expired')
    expect(statusOf('default', 'cnt.devconf.berlin-2027')).toBe('scheduled')
    expect(statusOf('default', 'uxe.devconf.ticketing.berlin-2027.reminder-mail')).toBe('scheduled')
    expect(statusOf('default', 'cnt.devconf.prague-2026')).toBe('active')
    expect(showcase.annotations('default').get('cnt.devconf.prague-2026')?.ExpiresAt).toBeDefined()
    // Copenhagen is in person only, so its streaming is switched off.
    expect(statusOf('default', 'exe.design-week.streaming.copenhagen-2026')).toBe('disabled')
    // The programme of Prague 2026 is final, so its review reminders are off.
    expect(statusOf('default', 'uxe.devconf.call-for-papers.prague-2026.review-reminder')).toBe('disabled')
    expect(statusOf('default', 'uxe.devconf.call-for-papers.online-2026.review-reminder')).toBe('active')
    // Design Week gave notice on its streaming booking: it ends with the year.
    const ending = showcase.annotations('default').get('usg.design-week.streaming')!
    expect(ending.Labels).toContain('notice-given')
    expect(ending.ExpiresAt).toBeDefined()
    expect(annotationStatus(ending)).toBe('active')
  })

  it('creates every annotation after its ancestors and before its own configuration', () => {
    for (const view of views) {
      const { annotations, documents, trash } = showcaseScope('gatherly', 'conferences', view)!
      const all = new Map(
        [...annotations, ...trash.flatMap((tree) => tree.annotations)].map((annotation) => [
          annotation.AnnotationKey,
          annotation,
        ]),
      )
      const createdAt = (key: string) => Date.parse(all.get(key)!.Created!.At!)
      for (const key of all.keys()) {
        for (const ancestor of getAllAncestors(key)) {
          expect(createdAt(ancestor), `${view}: ${key} after ${ancestor}`).toBeLessThanOrEqual(createdAt(key))
        }
        const first = documents.get(`configuration:${key}`)?.versions[0]
        if (first) {
          expect(Date.parse(first.Modified!.At!), `${view}: ${key} before its configuration`).toBeGreaterThanOrEqual(
            createdAt(key),
          )
        }
      }
    }
  })

  it('resolves every @config and @annotation binding of the effective configurations', () => {
    const resolved = new Set<string>()
    for (const view of views) {
      const annotations = showcase.annotations(view)
      for (const key of annotations.keys()) {
        const effective = effectiveContent(unseededShowcaseDb(), showcase.scope(view), key)
        for (const value of stringsOf(effective).filter((text) => /^@(config|annotation)\(/.test(text))) {
          const config = /^@config\("\/(\w+)"\)$/.exec(value)
          const annotation = /^@annotation\("\/(\w+)", "(\w+)"\)$/.exec(value)
          expect(config ?? annotation, `${view}: ${key} has an unexpected binding ${value}`).toBeTruthy()
          if (config) {
            expect(effective[config[1]!], `${view}: ${key} ${value}`).toBeDefined()
            resolved.add(value)
          } else if (annotation) {
            const [, name, type] = annotation
            const ancestor = getAllAncestors(key)
              .map((candidate) => annotations.get(candidate)!)
              .find((candidate) => candidate.AnnotationType === type)
            expect(ancestor?.Values?.[name!], `${view}: ${key} ${value}`).toBeDefined()
            resolved.add(value)
          }
        }
      }
    }
    expect([...resolved].toSorted()).toEqual([
      '@annotation("/eventCode", "Context")',
      '@annotation("/organizerEmail", "Subject")',
      '@config("/website")',
    ])
  })

  it('starts with the cancelled Lisbon edition and a mistaken Wi-Fi setup in Deleted', () => {
    expect(showcase.trash('default').get('cnt.design-week.lisbon-2026')).toEqual(
      expect.arrayContaining([
        'exe.design-week.ticketing.lisbon-2026',
        'uxe.design-week.ticketing.lisbon-2026.reminder-mail',
        'cnt.design-week.lisbon-2026',
      ]),
    )
    expect(showcase.trash('default').get('exe.devconf.venue-wifi.online-2026')).toEqual([
      'exe.devconf.venue-wifi.online-2026',
    ])
    expect(showcase.keys('default').has('cnt.design-week.lisbon-2026')).toBe(false)
  })

  it('follows the board in feature-delivery1: Prague 2026 with ticketing and the new matchmaking', () => {
    expect([...showcase.keys('feature-delivery1')].toSorted()).toEqual(
      [
        'sbt.devconf',
        'cnt.devconf.prague-2026',
        'rst.ticketing',
        'rst.matchmaking',
        'unt.matchmaking.intro-mail',
        'usg.devconf.ticketing',
        'usg.devconf.matchmaking',
        'exe.devconf.ticketing.prague-2026',
        'exe.devconf.matchmaking.prague-2026',
        'uxe.devconf.matchmaking.prague-2026.intro-mail',
      ].toSorted(),
    )
    // A copy of production's current ticket sales, without their history.
    expect(showcase.versions('feature-delivery1', 'exe.devconf.ticketing.prague-2026')).toBe(1)
    expect(showcase.own('feature-delivery1', 'exe.devconf.ticketing.prague-2026')).toEqual(
      showcase.own('default', 'exe.devconf.ticketing.prague-2026'),
    )
    // The job sends no more intros per mail than the edition allows per day.
    const run = 'uxe.devconf.matchmaking.prague-2026.intro-mail'
    expect(effectiveContent(unseededShowcaseDb(), showcase.scope('feature-delivery1'), run)).toMatchObject({
      introsPerMail: 2,
      maxIntrosPerDay: 2,
    })
    expect(showcase.own('feature-delivery1', 'usg.devconf.matchmaking')).toMatchObject({ plan: 'pilot' })
  })

  it('brings wallet passes, a stricter ticketing schema and the event app in the release candidate', () => {
    expect(showcase.own('release-candidate', 'rst.ticketing')?.walletPasses).toEqual(['apple', 'google'])
    expect(showcase.latest('release-candidate', 'schema:descendant:rst.ticketing:exe')).toMatchObject({
      required: ['currency', 'tiers', 'vatRate'],
    })
    const keys = showcase.keys('release-candidate')
    expect(keys.has('exe.devconf.event-app.prague-2026')).toBe(true)
    expect(keys.has('cnt.design-week.lisbon-2026')).toBe(false)
    expect(showcase.versions('release-candidate', 'exe.devconf.ticketing.prague-2026')).toBe(7)
  })

  it('runs development on DevConf’s test editions with test payments and debug logging', () => {
    const keys = showcase.keys('development')
    expect([...keys].filter((key) => key.startsWith('cnt.')).toSorted()).toEqual(['cnt.devconf.dev', 'cnt.devconf.qa'])
    expect([...keys].filter((key) => key.startsWith('sbt.'))).toEqual(['sbt.devconf'])
    expect(showcase.own('development', 'exe.devconf.ticketing.qa')).toMatchObject({ paymentMode: 'test' })
    expect(showcase.own('development', 'cnt.devconf.qa')).toMatchObject({ logLevel: 'debug' })
    // The QA rig prints on a real badge printer, as its purpose says.
    expect(showcase.own('development', 'exe.devconf.badges.qa')?.printers).toEqual([
      'badge-printer-qa.rig.gatherly.example',
    ])
    expect(showcase.annotations('development').get('cnt.devconf.qa')?.Values?.purpose).toContain('real badge printer')
  })

  it('copies the editions on sale to staging', () => {
    expect([...showcase.keys('staging')].filter((key) => key.startsWith('cnt.')).toSorted()).toEqual([
      'cnt.data-summit.london-2026',
      'cnt.design-week.copenhagen-2026',
      'cnt.devconf.online-2026',
      'cnt.devconf.prague-2026',
    ])
    expect(showcase.own('staging', 'cnt.devconf.prague-2026')?.website).toBe(
      'https://staging.devconf.example/prague-2026',
    )
    expect(showcase.own('staging', 'exe.devconf.ticketing.prague-2026')).toMatchObject({
      paymentMode: 'test',
      tiers: expect.arrayContaining([expect.objectContaining({ name: 'regular', price: 11500 })]),
    })
  })
})
