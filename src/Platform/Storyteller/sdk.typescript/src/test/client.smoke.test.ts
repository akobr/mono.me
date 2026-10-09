import { describe, expect, it } from 'vitest'
import { configureStoryteller, getAccount, getAuthConfiguration } from '../index'

// Optional check against a running API. Skipped unless both variables are set, for example:
//   STORYTELLER_SMOKE_URL=http://localhost:7071/api STORYTELLER_SMOKE_TOKEN=eyJ… npm test
const baseUrl = process.env.STORYTELLER_SMOKE_URL
const token = process.env.STORYTELLER_SMOKE_TOKEN

describe.skipIf(!baseUrl || !token)('smoke against a running Storyteller API', () => {
  it('reads the auth configuration and the signed-in account', async () => {
    configureStoryteller({ baseUrl: baseUrl!, getAccessToken: () => token })

    const authConfiguration = await getAuthConfiguration()
    const account = await getAccount()

    expect(authConfiguration.Provider).toBeTruthy()
    expect(account.Id).toBeTruthy()
  })
})
