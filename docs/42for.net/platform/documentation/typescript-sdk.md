---
pageClass: dev-guide
---

# TypeScript SDK

**`@42for.net/2splatform.sdk`** is the whole Storyteller API for TypeScript: plain `fetch` functions, TanStack Query composables for Vue, Zod schemas and MSW mocks. The admin UI is built on it, so it gets a workout every day.

> Typed all the way down. Your editor will autocomplete things you didn't know the platform could do.

## Why you'll like it

- **Zero runtime dependencies** at its root: it runs on the global `fetch`, in browsers and Node 22+.
- **Pick what you need.** Vue Query, Zod and MSW are optional peers, behind their own entry points.
- **Errors you can reason about.** One `StorytellerError` with `status`, `errorCode` and a `hint`, plus helpers like `isForbidden` and `isTransientError`.
- **Mocks for free.** Every operation has an MSW handler with Faker data, so your tests run without a platform.
- **Same rules as .NET.** Flattening, formats and client-side encryption match the .NET packages byte for byte.

## Install

```bash
npm install @42for.net/2splatform.sdk
```

| Entry point | What | Needs |
| --- | --- | --- |
| `@42for.net/2splatform.sdk` | Request functions, models, errors, keys, paging, conditional reads and writes, key/value helpers | nothing |
| `@42for.net/2splatform.sdk/vue-query` | Composables: `useGetAnnotation`, `usePatchConfiguration`, … | `@tanstack/vue-query`, `vue` |
| `@42for.net/2splatform.sdk/zod` | Zod 4 schemas of parameters, bodies and responses | `zod` |
| `@42for.net/2splatform.sdk/msw` | MSW handlers with Faker data | `msw`, `@faker-js/faker` |
| `@42for.net/2splatform.sdk/hyok` | Client-side encryption of secrets, on WebCrypto | nothing |

## Configure once, call anywhere

```ts
import { configureStoryteller, getAccount, isForbidden } from '@42for.net/2splatform.sdk'

configureStoryteller({
  baseUrl: 'https://api.2s.42for.net/api',
  getAccessToken: ({ forceRefresh }) => auth.getAccessToken({ forceRefresh }),   // a bearer token of your sign-in
  onUnauthorized: () => auth.signIn(),                                          // a 401 survived one token refresh
  timeoutMs: 30_000,
})

try {
  const account = await getAccount()
} catch (error) {
  if (isForbidden(error)) {
    // signed in, but the role on the project is too low
  }
}
```

After a `401` the fetcher asks for a fresh token once (`forceRefresh: true`) before it gives up.

## Vue, the comfy way

```ts
import { useGetConfiguration, usePatchConfiguration, invalidateByPath } from '@42for.net/2splatform.sdk/vue-query'

const configuration = useGetConfiguration(org, project, view, key)   // refs, getters or strings
const patch = usePatchConfiguration({
  mutation: {
    onSuccess: () => invalidateByPath(queryClient, `/v1/${org.value}/${project.value}/${view.value}/configuration/${key.value}`),
  },
})
```

Query keys are the path segments of the request, so invalidating by path just works.

## Tests without a platform

```ts
import { setupServer } from 'msw/node'
import { getStorytellerMock, getGetMembersMockHandler } from '@42for.net/2splatform.sdk/msw'

const server = setupServer(...getStorytellerMock())
server.use(getGetMembersMockHandler([{ AccountId: 'user_01', Role: 'Owner' }]))
```

The handlers match `*/api/v1/…`, so keep a `baseUrl` that ends with `/api`.

## Machines, without a single secret

A server-side workload (GitHub Actions, Kubernetes, Vercel, a cloud identity) can trade its platform's token for a short-lived Storyteller token:

```ts
import { configureStoryteller, createFederatedTokenProvider, getAnnotations } from '@42for.net/2splatform.sdk'

const baseUrl = 'https://api.2s.42for.net/api'
const tokenEndpoint = `${baseUrl}/v1/house/main/access/token`   // also the audience of the machine's rule

configureStoryteller({
  baseUrl,
  getAccessToken: createFederatedTokenProvider({
    baseUrl,
    organization: 'house',
    project: 'main',
    machineId: '<the machine id>',
    getSubjectToken: () => readPlatformToken(tokenEndpoint),   // e.g. core.getIDToken(tokenEndpoint) in GitHub Actions
  }),
})

const page = await getAnnotations('house', 'main', 'default')
```

The token is cached until 60 s before it expires, and concurrent requests share one exchange.

## Polling and formats

```ts
import { getResolvedConfigurationConditional, formatConfiguration, isTransientError } from '@42for.net/2splatform.sdk'

let etag: string | undefined

const result = await getResolvedConfigurationConditional('house', 'main', 'default', 'exe.northwind.invoicing.production', { ifNoneMatch: etag })

if (!result.notModified) {
  etag = result.etag
  const dotenv = formatConfiguration(result.value?.Content, 'env')   // 'INVOICING__CURRENCY=NOK\n…'
}
```

`isTransientError(error)` tells "try again later" from "fix something", with the same rules as the .NET provider.

::: warning In a browser
Script can't read the `ETag` of a cross-origin response, so a browser gets a `200` every time. The conditional helpers shine in Node and behind a same-origin proxy.
:::

## Key/value pairs and secrets you keep

Helpers for the `.env` you have lying around:

```ts
import { importKeyValues, parseDotEnv, toKeyValueDocument } from '@42for.net/2splatform.sdk'

const document = toKeyValueDocument(parseDotEnv(text))   // every pair Public
await importKeyValues('house', 'main', 'default', document, { mode: 'Merge', dryRun: true })
```

And `./hyok` encrypts a secret in the browser or Node, for the project's recipients, so the platform stores what it can't read:

```ts
import { getRecipients, setKeyValue } from '@42for.net/2splatform.sdk'
import { encryptForRecipients, getHyokPlace } from '@42for.net/2splatform.sdk/hyok'

const recipients = await getRecipients('house', 'main')
const value = await encryptForRecipients('s3cret', recipients, getHyokPlace('house', 'main', 'default', 'db.password'))
await setKeyValue('house', 'main', 'default', 'db.password', { Protection: 'Hyok', Value: value })
```

## Reference

The [SDK readme](https://github.com/akobr/mono.me/blob/main/src/Platform/Storyteller/sdk.typescript/README.md) on GitHub: timeouts, every helper, conditional writes, view exports, federation per platform.
