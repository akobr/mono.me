import { describe, expect, it } from 'vitest'
import {
  agreementLinks,
  exhibitionChoiceDescription,
  onboardingSteps,
  onboardingTexts,
  residencyLinks,
  securityLinks,
  stepLabel,
  stepNames,
  subProcessors,
  welcomeLine,
  workosPurpose,
} from '@/utils/onboarding'

// The texts of the onboarding wizard (424-267, spec 3.3 to 3.6 and 8, reworked after the first review): one file
// holds every text, sub-processor and link of the five steps.

describe('the welcome step', () => {
  it('meets the user with the platform, its motto and its pricing', () => {
    expect(onboardingTexts.welcome.heading).toBe('Meet the 2S Platform')
    expect(onboardingTexts.welcome.motto).toBe(
      'Built to tame the configuration mayhem of complex systems and fast-growing application catalogs, AI-made ones included.',
    )
    expect(onboardingTexts.welcome.pricing).toBe(
      'The platform is free to use while we shape its pricing. When pricing arrives, it will apply to every customer, existing ones included, and you will hear from us well in advance. It will be based on usage, not on seats, with a generous free tier.',
    )
    expect(onboardingTexts.welcome.documentation).toEqual({
      label: 'Documentation',
      to: 'https://42for.net/platform/introduction',
    })
  })
})

describe('the residency step', () => {
  it('states where the data is in its heading and its first sentence', () => {
    expect(onboardingTexts.residency.heading).toBe("Your platform's data stays in the EU")
    expect(onboardingTexts.residency.lead).toBe(
      'All customer data, configuration, secrets and machine access are stored and processed only in the EU (Sweden).',
    )
    expect(onboardingTexts.residency.ai).toBe('The same applies to all AI data processing within the platform.')
  })

  it('names the four sub-processors of the deployment, with WorkOS as the only one outside the EU', () => {
    expect(workosPurpose).toBe(
      'Signs in the operators of the 2S Platform and sends their invitation emails. It processes only their name, email, IP address and session data, and the email address of a person they invite.',
    )
    expect(subProcessors).toEqual([
      { name: 'Microsoft Azure', purpose: 'Hosting, storage, secrets, logs', region: 'EU only' },
      { name: 'Sentry', purpose: 'Error reporting', region: 'EU only' },
      { name: 'PostHog', purpose: 'Product analytics', region: 'EU only' },
      {
        name: 'WorkOS',
        purpose: workosPurpose,
        note: 'Every authorization decision is made and recorded by 2S in the EU.',
        region: 'USA',
      },
    ])
  })
})

describe('the security step', () => {
  it('has a lead and four sections: secrets, machines, people and security testing', () => {
    const texts = onboardingTexts.security

    expect(texts.heading).toBe('Security first')
    expect(texts.lead).toBe("Security isn't a feature we added later. It is the ground the 2S Platform stands on.")
    expect([texts.secrets.title, texts.machines.title, texts.people.title, texts.testing.title]).toEqual([
      'Secrets',
      'Machines and pipelines',
      'People',
      'Security testing',
    ])
  })

  it('names the three safeguards and the way to an EU-only identity provider', () => {
    expect(onboardingTexts.security.people.text).toBe(
      'You sign in with WorkOS AuthKit, and 2S alone decides what you may do. Impersonation is blocked, secrets ask for a recent sign-in, and every change of access lands in an append-only audit log.',
    )
    expect(onboardingTexts.security.people.identityProvider).toBe(
      'Need an EU-only identity provider? Bring your own SSO provider, or host the platform yourself.',
    )
  })
})

describe('the links of the wizard', () => {
  it('lead to the hosting, the sub-processors, the security pages and the agreements', () => {
    expect(residencyLinks).toEqual([
      { label: 'Cloud hosting', to: 'https://42for.net/platform/cloud' },
      { label: 'Sub-processors list', to: 'https://42for.software/legal/sub-processors' },
    ])
    expect(securityLinks).toEqual([
      { label: 'Security details', to: 'https://42for.net/platform/security' },
      { label: 'Authentication of machines', to: 'https://42for.net/platform/auth-machine' },
      { label: 'Self-hosting option', to: 'https://42for.net/platform/self-hosting' },
    ])
    expect(agreementLinks).toEqual({
      license: { label: 'Customer License Agreement', to: 'https://42for.software/legal/customer-license-agreement' },
      dataProcessing: {
        label: 'Data Processing Agreement',
        to: 'https://42for.software/legal/data-processing-agreement',
      },
    })
  })

  it('are https addresses, each with its own label', () => {
    const links = [
      onboardingTexts.welcome.documentation,
      ...residencyLinks,
      ...securityLinks,
      agreementLinks.license,
      agreementLinks.dataProcessing,
    ]

    for (const link of links) {
      expect(new URL(link.to).protocol, link.label).toBe('https:')
    }
    expect(new Set(links.map((link) => link.label)).size).toBe(links.length)
    expect(new Set(links.map((link) => link.to)).size).toBe(links.length)
  })
})

describe('the steps and their names', () => {
  it('are welcome, residency, security, exhibition and organization', () => {
    expect(onboardingSteps).toEqual(['welcome', 'residency', 'security', 'exhibition', 'organization'])
    expect(onboardingSteps.map((step) => stepNames[step])).toEqual([
      'Welcome',
      'Residency',
      'Security',
      'Exhibition',
      'Organization',
    ])
  })

  it('are named by their number and name in the step list', () => {
    expect(stepLabel(1, 'welcome')).toBe('Step 1: Welcome')
    expect(stepLabel(2, 'residency')).toBe('Step 2: Residency')
    expect(stepLabel(4, 'organization')).toBe('Step 4: Organization')
  })
})

describe('the other texts', () => {
  it('greet the user by name, or nobody', () => {
    expect(welcomeLine('Ada Lovelace')).toBe('Welcome, Ada Lovelace')
    expect(welcomeLine()).toBe('Welcome')
    expect(welcomeLine('')).toBe('Welcome')
  })

  it('invite to the exhibition with its story and what it shows', () => {
    expect(onboardingTexts.exhibition.heading).toBe('Join the exhibition')
    expect(onboardingTexts.exhibition.text).toBe(
      'Step into Fourth Wall Software, a fictional software house whose customers include Acme Corp and Globex. See how the platform keeps their CRM, HR and CAD solutions configured.',
    )
    // a visitor is a Reader, who reads the schemas and templates on Modeling (424-272)
    expect(onboardingTexts.exhibition.promise).toBe(
      'Explore 30 annotations, configurations that resolve live, the graph that ties them together, and the schemas and templates behind them.',
    )
    expect(onboardingTexts.exhibition.choice).toBe('Show me the exhibition')
  })

  it('say what joining the exhibition does, the same for any project of the deployment', () => {
    const description =
      'Adds the demo project to your account as a Reader. Nothing you do there can break it: you join read-only.'

    expect(exhibitionChoiceDescription({ Organization: 'fourth-wall', Project: 'toons-and-tyrants' })).toBe(description)
    expect(exhibitionChoiceDescription()).toBe(description)
  })

  it('name the button of the last step in the note of the exhibition', () => {
    const { noteStart, noteEnd } = onboardingTexts.exhibition

    expect(`${noteStart} ${onboardingTexts.organization.exhibitionOnly} ${noteEnd}`).toBe(
      'This step is optional. To join only the exhibition and create your own organization later, check the box and choose Continue with the exhibition only in the last step.',
    )
  })
})
