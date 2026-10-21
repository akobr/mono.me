import { describe, expect, it } from 'vitest'
import { effectiveContent } from '@/mock/configuration-handlers'
import { annotationStatus } from '@/utils/annotation-status'
import { showcaseOf, unseededShowcaseDb } from './support'

// pixel-forge/live-ops, a new example of the showcase (spec section 9.12): games on platform regions, the backend
// services they buy, and the runs of the seasonal live events, whose windows make them expired or scheduled.

const showcase = showcaseOf('pixel-forge', 'live-ops')
const views = ['default', 'development', 'staging', 'release-candidate', 'feature-delivery1']
const statusOf = (view: string, key: string) => annotationStatus(showcase.annotations(view).get(key)!)
const ofType = (view: string, code: string) => [...showcase.keys(view)].filter((key) => key.startsWith(`${code}.`))
const runsOf = (event: string, game: string) =>
  ofType('default', 'uxe').filter((key) => key.startsWith(`uxe.${game}.`) && key.endsWith(`.${event}`))
const statusesOf = (keys: string[]) => new Set(keys.map((key) => statusOf('default', key)))

describe('pixel-forge/live-ops', () => {
  it('has the games, platform regions, backend services and live events of the model', () => {
    expect(ofType('default', 'sbt').toSorted()).toEqual(['sbt.dungeon-dash', 'sbt.kart-blitz', 'sbt.star-drift'])
    expect(new Set(ofType('default', 'cnt').map((key) => key.split('.')[2]))).toEqual(
      new Set(['steam-eu', 'steam-na', 'ps5-eu', 'ps5-na', 'switch-jp', 'xbox-eu', 'mobile-apac']),
    )
    expect(ofType('default', 'rst').toSorted()).toEqual([
      'rst.anti-cheat',
      'rst.cloud-saves',
      'rst.events',
      'rst.leaderboards',
      'rst.matchmaking',
      'rst.store',
    ])
    expect(ofType('default', 'unt').toSorted()).toEqual([
      'unt.events.anniversary',
      'unt.events.double-xp-weekend',
      'unt.events.spring-sale',
      'unt.events.winter-festival',
    ])
    // Live events belong to the events service, so every event run is a run of an events deployment.
    expect(ofType('default', 'uxe').every((key) => key.split('.')[2] === 'events')).toBe(true)
  })

  it('runs anti-cheat and cloud saves on PC and mobile only, and no anti-cheat for the co-op game', () => {
    const deployments = ofType('default', 'exe')
    const open = deployments.filter((key) => /^exe\.[a-z-]+\.(anti-cheat|cloud-saves)\./.test(key))
    expect(open.length).toBeGreaterThan(0)
    // Consoles are closed platforms and sync saves themselves.
    expect(open.filter((key) => /\.(ps5|xbox|switch)-[a-z]+$/.test(key))).toEqual([])
    expect(deployments).toContain('exe.star-drift.matchmaking.ps5-eu')
    expect(showcase.keys('default').has('usg.dungeon-dash.anti-cheat')).toBe(false)
    expect(deployments.some((key) => key.startsWith('exe.dungeon-dash.anti-cheat.'))).toBe(false)
  })

  it('stores queue sizes, region servers and the store currency on the deployments', () => {
    expect(showcase.own('default', 'exe.star-drift.store.steam-eu')).toMatchObject({
      currency: 'EUR',
      taxIncluded: true,
      serverKey: '@(store.star-drift-steam, primaryVault)',
    })
    expect(showcase.own('default', 'exe.kart-blitz.store.switch-jp')).toMatchObject({ currency: 'JPY' })
    expect(showcase.own('default', 'exe.kart-blitz.store.ps5-na')).toMatchObject({
      currency: 'USD',
      taxIncluded: false,
    })
    expect(showcase.latest('default', 'schema:descendant:rst.store:exe')).toMatchObject({ required: ['currency'] })
    expect(showcase.own('default', 'exe.star-drift.matchmaking.steam-eu')).toMatchObject({
      queues: { ranked: { timeoutSeconds: 120 }, raid: { timeoutSeconds: 60 } },
      dedicatedServers: { fleet: '@config("/gameServers")', region: '@config("/cloudRegion")' },
    })
    // The bindings read the platform region: its servers through the configuration, its store id from its Values.
    expect(
      effectiveContent(unseededShowcaseDb(), showcase.scope('default'), 'exe.star-drift.matchmaking.steam-eu'),
    ).toMatchObject({ cloudRegion: 'eu-central-1', gameServers: 'gs.steam-eu.star-drift.pixelforge.example' })
    for (const view of views) {
      for (const key of ofType(view, 'cnt')) {
        expect(showcase.annotations(view).get(key)?.Values?.storeAppId, `${view} ${key}`).toEqual(expect.any(String))
      }
    }
  })

  it('buys a service plan of every service a game needs', () => {
    for (const [game, count] of [
      ['star-drift', 6],
      ['dungeon-dash', 5],
      ['kart-blitz', 6],
    ] as const) {
      expect(ofType('default', 'usg').filter((key) => key.startsWith(`usg.${game}.`)).length, game).toBe(count)
    }
    for (const key of ofType('default', 'usg')) {
      expect(showcase.own('default', key), key).toBeDefined()
    }
    expect(showcase.own('default', 'usg.star-drift.matchmaking')).toMatchObject({
      tier: 'gold',
      sla: 99.95,
      peakCcu: 420000,
      monthlyBudgetEur: 24000,
      modes: ['ranked', 'casual', 'raid'],
    })
    expect(showcase.own('default', 'usg.dungeon-dash.leaderboards')).toMatchObject({
      tier: 'silver',
      sla: 99.9,
      boards: ['daily-run', 'deepest-floor'],
    })
  })

  it('requires rewards on every live event', () => {
    expect(showcase.latest('default', 'schema:type:unt')).toMatchObject({ required: ['rewards'] })
    for (const key of ofType('default', 'unt')) {
      expect(showcase.own('default', key)?.rewards, key).toEqual(
        expect.arrayContaining([expect.objectContaining({ item: expect.any(String), quantity: expect.any(Number) })]),
      )
    }
  })

  it('opens and closes the event runs with their windows: past events expired, coming events scheduled', () => {
    for (const game of ['star-drift', 'dungeon-dash', 'kart-blitz']) {
      expect(statusesOf(runsOf('spring-sale', game)), game).toEqual(new Set(['expired']))
    }
    expect(
      statusesOf([...runsOf('winter-festival', 'star-drift'), ...runsOf('winter-festival', 'kart-blitz')]),
    ).toEqual(new Set(['scheduled']))
    expect(statusesOf(runsOf('anniversary', 'star-drift'))).toEqual(new Set(['expired']))
    expect(statusesOf(runsOf('anniversary', 'kart-blitz'))).toEqual(new Set(['scheduled']))
    // Dungeon Dash's first anniversary is on now and ends in a few days.
    expect(statusesOf(runsOf('anniversary', 'dungeon-dash'))).toEqual(new Set(['active']))
    const running = showcase.annotations('default').get('uxe.dungeon-dash.events.steam-eu.anniversary')!
    expect(Date.parse(running.ExpiresAt!)).toBeGreaterThan(Date.now())
    // The CRONs of the live events are in UTC, the runs in the time zone of their platform region.
    expect(showcase.annotations('default').get('unt.events.double-xp-weekend')?.TimeZone).toBe('UTC')
    expect(showcase.annotations('default').get('uxe.star-drift.events.steam-na.double-xp-weekend')?.TimeZone).toBe(
      'America/New_York',
    )
    // The recurring weekend has no window; it is paused on mobile only.
    expect(statusOf('default', 'uxe.dungeon-dash.events.steam-eu.double-xp-weekend')).toBe('active')
    expect(statusOf('default', 'uxe.dungeon-dash.events.mobile-apac.double-xp-weekend')).toBe('disabled')
  })

  it('lets an event run inherit the rewards of its event and the claim rules of its type', () => {
    const run = 'uxe.star-drift.events.steam-eu.winter-festival'
    expect(showcase.own('default', run)).toBeUndefined()
    expect(effectiveContent(unseededShowcaseDb(), showcase.scope('default'), run)).toMatchObject({
      theme: 'snow',
      durationHours: 504,
      rewardDelivery: 'inbox',
      claimWindowDays: 21,
    })
    // Japan keeps the festival through the New Year holidays: its run lasts four days longer, with a reward of its own.
    const japan = 'uxe.kart-blitz.events.switch-jp.winter-festival'
    expect(showcase.own('default', japan)).toMatchObject({ durationHours: 600 })
    const expiresAt = (key: string) => Date.parse(showcase.annotations('default').get(key)!.ExpiresAt!)
    expect(expiresAt(japan) - expiresAt('uxe.kart-blitz.events.ps5-eu.winter-festival')).toBe(4 * 24 * 3600 * 1000)
    expect(showcase.own('default', 'uxe.star-drift.events.steam-na.double-xp-weekend')).toEqual({
      cron: '0 18 * * 5',
      timeZone: 'America/New_York',
    })
  })

  it('launches Kart Blitz on Xbox in Europe next month, with its deployments', () => {
    expect(statusOf('default', 'cnt.kart-blitz.xbox-eu')).toBe('scheduled')
    const xbox = ofType('default', 'exe').filter((key) => key.startsWith('exe.kart-blitz.') && key.endsWith('.xbox-eu'))
    expect(xbox.length).toBeGreaterThan(0)
    expect(statusesOf(xbox)).toEqual(new Set(['scheduled']))
    // The anniversary comes before the launch, the Winter Festival after it.
    expect(showcase.keys('default').has('uxe.kart-blitz.events.xbox-eu.anniversary')).toBe(false)
    expect(showcase.keys('default').has('uxe.kart-blitz.events.xbox-eu.winter-festival')).toBe(true)
  })

  it('starts with the cancelled Halloween Hunt and its runs, and a deployment deleted alone', () => {
    const trash = showcase.trash('default')
    expect(trash.get('unt.events.halloween-hunt')).toEqual(
      expect.arrayContaining(['uxe.dungeon-dash.events.steam-eu.halloween-hunt', 'unt.events.halloween-hunt']),
    )
    expect(trash.get('exe.dungeon-dash.cloud-saves.switch-jp')).toEqual(['exe.dungeon-dash.cloud-saves.switch-jp'])
    expect(ofType('default', 'uxe').some((key) => key.endsWith('.halloween-hunt'))).toBe(false)
  })

  it('keeps the client patch history of Star Drift', () => {
    expect(showcase.versions('default', 'sbt.star-drift')).toBeGreaterThanOrEqual(15)
    expect(showcase.own('default', 'sbt.star-drift')).toMatchObject({ minClientVersion: '3.25.0' })
  })

  it('brings season 12 in the release candidate', () => {
    expect(showcase.own('release-candidate', 'sbt.star-drift')).toMatchObject({ minClientVersion: '3.26.0' })
    expect(showcase.own('release-candidate', 'rst.matchmaking')).toMatchObject({
      maxSkillGap: 300,
      maxPingMs: 100,
      latencyBuckets: [30, 60, 100],
    })
    expect(showcase.own('release-candidate', 'unt.events.winter-festival')).toMatchObject({ theme: 'aurora' })
    expect(showcase.latest('release-candidate', 'schema:type:unt')).toMatchObject({
      required: ['rewards', 'durationHours'],
    })
    expect(showcase.keys('release-candidate').has('exe.star-drift.battle-pass.steam-eu')).toBe(true)
    expect(showcase.keys('default').has('rst.battle-pass')).toBe(false)
    // Client 2.4 fixes the XP exploit, so the paused weekend runs again on mobile.
    expect(showcase.own('release-candidate', 'sbt.dungeon-dash')).toMatchObject({ minClientVersion: '2.4.0' })
    expect(statusOf('release-candidate', 'uxe.dungeon-dash.events.mobile-apac.double-xp-weekend')).toBe('active')
    // The cancelled event is not part of the release.
    expect(showcase.keys('release-candidate').has('unt.events.halloween-hunt')).toBe(false)
  })

  it('develops on dev kits, the Steam beta branch and the QA rigs, with debug logging and sandboxes', () => {
    expect(new Set(ofType('development', 'cnt').map((key) => key.split('.')[2]))).toEqual(
      new Set(['steam-beta', 'ps5-devkit', 'qa-lab', 'switch-devkit', 'phone-farm']),
    )
    expect(showcase.own('development', 'cnt.star-drift.ps5-devkit')).toMatchObject({
      logLevel: 'debug',
      platformEnvironment: 'sp-int',
    })
    expect(showcase.annotations('development').get('cnt.star-drift.ps5-devkit')?.Values).toMatchObject({
      owner: 'barbara@example.com',
      devkitSerial: expect.stringMatching(/^DFI-T1000AA-\d{6}$/),
    })
    expect(showcase.own('development', 'exe.star-drift.store.steam-beta')).toMatchObject({ sandbox: true })
    expect(showcase.own('development', 'uxe.star-drift.events.qa-lab.double-xp-weekend')).toMatchObject({
      cron: '*/30 * * * *',
    })
  })

  it('rehearses the coming events on the certification environments in staging', () => {
    const runs = ofType('staging', 'uxe')
    expect(runs.length).toBeGreaterThan(0)
    expect(new Set(runs.map((key) => statusOf('staging', key)))).toEqual(new Set(['active']))
    // Only where the event will run: the Kart Blitz anniversary comes before the Xbox launch.
    expect(runs).toContain('uxe.kart-blitz.events.ps5-eu.anniversary')
    expect(runs).not.toContain('uxe.kart-blitz.events.xbox-eu.anniversary')
    expect(showcase.own('staging', 'cnt.kart-blitz.xbox-eu')).toMatchObject({ platformEnvironment: 'CERT' })
  })

  it('adds crossplay in feature-delivery1', () => {
    expect(showcase.own('feature-delivery1', 'exe.kart-blitz.crossplay.switch-jp')).toMatchObject({
      pool: 'console',
      voiceChat: 'off',
    })
    expect(showcase.own('feature-delivery1', 'exe.star-drift.crossplay.steam-eu')).toMatchObject({ pool: 'pc' })
    expect(showcase.latest('feature-delivery1', 'schema:descendant:rst.crossplay:exe')).toMatchObject({
      required: ['pool'],
    })
    expect(showcase.keys('default').has('rst.crossplay')).toBe(false)
  })
})
