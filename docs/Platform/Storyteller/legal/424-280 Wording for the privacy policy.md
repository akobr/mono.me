# 424-280: Wording for the privacy policy

The changes that [424-280](https://linear.app/424fun/issue/424-280/switch-42fornet-from-google-analytics-to-cookieless-analytics) needs in the legal drafts of `docs/42for.software/legal/`. They replace the corrected text in section 5 of the review [Cookieless Analytics on 42for.net](../../../specs/reviews/2026-11-17%20Cookieless%20Analytics%20on%2042for.net.md). Each change below gives the current text and its replacement. Apply them as they are: they reflect what the site does on branch `feat/424-280`.

## What the site does now

The wording rests on these facts. If one of them changes, recheck the text.

- **Analytics.** Page views go to PostHog's EU cloud (`eu.i.posthog.com`). PostHog keeps a random id in memory only, so a reload or a new tab gets a new one.
  - No cookie is set, and nothing stays in local or session storage.
  - Nothing is sent when Do Not Track or Global Privacy Control is on.
  - Events carry the page without its query, the referring site (its origin only), browser and device details, and the IP address, from which PostHog derives an approximate location.
  - PostHog keeps the data for 30 days, as set in the project.
- **Start-up probes.** When PostHog starts, it writes a test entry to local storage and deletes it at once. It also reads a few of its own keys and reads the cookies once. Nothing stays behind.
- **The theme setting.** VitePress keeps the light or dark theme in local storage (`vitepress-theme-appearance`). It writes `auto` on the first visit, before the visitor chooses anything.
- **The search.** Once the search is used, VitePress keeps the last query (session storage) and the view of the results (local storage).
- **Videos.** The videos on the `mrepo` page load nothing from YouTube until the visitor clicks one. The click starts YouTube's privacy-enhanced player (`youtube-nocookie.com`). From then on, YouTube (Google, USA) receives the visitor's IP address and may store data in the browser.
- **Google Analytics** is gone. Its property `G-HDDVP9E098` still has to be deleted.

## privacy-policy.md

### In short (line 13)

No change: "Your data stays in the EU, except sign-in, which runs through WorkOS in the USA." This is true again. A video you choose to play is covered in sections 4 and 6.

### Section 2, the website row (line 33)

Current:

```markdown
| **Website visits** to 42for.net: the pages visited, browser and device details | To see which documentation helps | [TBD: your consent] | [TBD] |
```

New:

```markdown
| **Website visits** to 42for.net: the pages visited (without their query), the website that linked to them, browser and device details, and the IP address with the approximate location derived from it | To see which documentation helps | Legitimate interest | 30 days |
```

### Section 4, the last bullet (line 49)

Current:

```markdown
- The 42for.net website uses Google Analytics, which sets cookies. [TBD: consent banner, or a switch to analytics without cookies]
```

New (three bullets):

```markdown
- The 42for.net website sets no cookies. Its analytics keep nothing in your browser's storage: they use a random id held in memory, which lasts only until you reload or close the page, and they send nothing when your browser has Do Not Track or Global Privacy Control turned on.
- The website remembers your choice of a light or dark theme in your browser's local storage, and while you use its search, your last query and how the results are shown.
- The videos on the website load nothing from YouTube until you click one. When you start a video, YouTube (Google, USA) receives your IP address and may store data in your browser, under [YouTube's privacy policy](https://policies.google.com/privacy).
```

### Section 5, the table (lines 57-61)

Remove the Google row and widen PostHog's.

Current:

```markdown
| PostHog | Usage analytics | EU (Germany) |
| Google | Website analytics of 42for.net | USA |
```

New:

```markdown
| PostHog | Usage analytics, and website analytics of 42for.net | EU (Germany) |
```

Google isn't added back for YouTube. YouTube is not our processor and has no data processing agreement with us, and this table lists only companies that have one. Section 4 already covers the videos, and section 6 covers the transfer.

### Section 6, Outside the EU (line 71)

Current:

```markdown
WorkOS and Google are in the USA. Both are certified under the EU-U.S. Data Privacy Framework, which the EU recognizes as giving adequate protection, and their data processing agreements also include the EU Standard Contractual Clauses.
```

New:

```markdown
WorkOS is in the USA. It is certified under the EU-U.S. Data Privacy Framework, which the EU recognizes as giving adequate protection, and its data processing agreement also includes the EU Standard Contractual Clauses.

When you start a video on our website, YouTube (Google, USA) receives your data as its own controller, not on our behalf. Google is certified under the EU-U.S. Data Privacy Framework too.
```

### Section 7 (line 82)

No change. "Withdraw your consent at any time, where we asked for it" can stay, though the website no longer asks for consent. Ask the lawyer whether to drop it.

## README.md

### The numbers (line 41)

Current:

```markdown
| Error reports, usage analytics and security logs | 30 days | Privacy 2, Sub-processors |
```

New:

```markdown
| Error reports, usage analytics, website analytics and security logs | 30 days | Privacy 2, Sub-processors |
```

### To fill in before publishing (line 55)

Current:

```markdown
- [ ] Google Analytics on 42for.net: a consent banner, or a switch to analytics without cookies (Privacy 2 and 4).
```

New:

```markdown
- [x] Analytics without cookies on 42for.net: PostHog EU with the id in memory, and the YouTube videos load only on click (Privacy 2 and 4; [424-280](https://linear.app/424fun/issue/424-280)).
- [ ] Delete the Google Analytics property `G-HDDVP9E098` once page views arrive in PostHog.
```

### For the lawyer, storage on the device (line 66)

Current:

```markdown
- **Storage on the device** (the Swedish Electronic Communications Act, chapter 9, section 28): the admin UI keeps a pseudonymous PostHog id in local storage without asking (it honours Do Not Track and Global Privacy Control), and 42for.net sets Google Analytics cookies. Storage that isn't strictly necessary normally needs consent.
```

New:

```markdown
- **Storage on the device** (the Swedish Electronic Communications Act, chapter 9, section 28). Storage that isn't strictly necessary normally needs consent. Nothing on our websites asks for it:
  - The admin UI keeps a pseudonymous PostHog id in local storage (it honours Do Not Track and Global Privacy Control).
  - 42for.net keeps its analytics id in memory only, but at start-up PostHog writes a test entry to local storage and deletes it at once, reads a few of its own keys, and reads the cookies once. Does that transient access count?
  - 42for.net writes the theme setting (`vitepress-theme-appearance`, `auto`) on the first visit, before the visitor chooses a theme, and its search keeps the last query and view once used. Are these strictly necessary, or should the theme key wait for a choice?
  - The YouTube videos load only on click, and the click is the visitor's request. Is a note beside the video enough?
```

## sub-processors.md

No change. Website analytics are our own data (we are the controller), not customer data, and PostHog is already listed. The sentence "Sentry, PostHog and WorkOS also process data for which we are the controller" already covers them.

## Version

The privacy policy states its version and date ("Version 0.1, draft of 16 November 2026"). Update the date when these changes go in, or raise the version if 0.1 has been shared already.
