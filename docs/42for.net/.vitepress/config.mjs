import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'
import { tabsMarkdownPlugin } from 'vitepress-plugin-tabs'

// https://vitepress.dev/reference/site-config
export default withMermaid({
  title: '42for.net',
  description: 'simple and clean .net',
  head: [
    ['link', { rel: 'icon', type: 'image/png', sizes: '32x32', href: '/favicon-32x32.png'}],
    ['link', { rel: 'icon', type: 'image/png', sizes: '16x16', href: '/favicon-16x16.png'}],
    [
      'script',
      {
        async: true,
        src: 'https://www.googletagmanager.com/gtag/js?id=G-HDDVP9E098',
      },
    ],
    [
      'script',
      {},
      "window.dataLayer = window.dataLayer || [];\nfunction gtag(){dataLayer.push(arguments);}\ngtag('js', new Date());\ngtag('config', 'G-HDDVP9E098');",
    ],
    [
      'script',
      {
        async: true,
        src: '/tooltips.js',
      }
    ]
  ],
  markdown: {
    config(md) {
      md.use(tabsMarkdownPlugin)
    }
  },
  themeConfig: {
    // https://vitepress.dev/reference/default-theme-config
    logo: '/42-logo.png',
    lastUpdated: true,
    search: {
      provider: 'local'
    },
    nav: [
      { text: 'Home', link: '/' },
      { text: 'Documentation', link: '/introduction' },
      { text: 'Platform', link: '/platform/introduction'}
    ],
    sidebar: [
      {
        text: 'Getting started',
        items: [
          { text: 'Introduction', link: '/introduction' },
          { text: 'Motivation', link: '/motivation' },
          { text: 'Install', link: '/install' },
        ]
      },
      {
        text: 'Architecture',
        items: [
          { text: 'Modulith', link: '/architecture/modulith' },
          { text: 'No microservices', link: '/architecture/no-microservices' },
          { text: 'Which one to pick?', link: '/architecture/which-one-to-pick' },
        ]
      },
      {
        text: 'Monorepo',
        items: [
          { text: 'Introduction', link: '/monorepo/introduction' },
          { text: 'Why monorepo?', link: '/monorepo/why-monorepo' },
          { text: 'Road map', link: '/monorepo/road-map' },
        ]
      },
      {
        text: 'Platform',
        items: [
          { text: 'Introduction', link: '/platform/introduction' },
          { text: 'Overview', link: '/platform/overview' },
          { text: 'Live demo', link: '/platform/live-demo' },
          { text: 'Road map', link: '/platform/road-map' },
        ]
      },
      {
        text: 'CLI',
        items: [
          { text: 'mrepo', link: '/cli/mrepo' },
          { text: 'sform', link: '/cli/sform' }
        ]
      }/*,
      {
        text: 'Other talks',
        items: [
          { text: 'Automation testing', link: '/articles/automation-testing' },
          { text: 'Hell of dependencies', link: '/articles/dependency-hell' },
          { text: 'Infrastructure', link: '/articles/infrastructure' },
          { text: 'Technical decisions', link: '/articles/technical-decisions' },
          { text: 'Scarecrow of contracting', link: '/articles/contractors' }
        ]
      }*/
    ],
    socialLinks: [
      { icon: 'github', link: 'https://github.com/akobr/mono.me' }
    ],
    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2024 Ales Kobr'
    }
  },
  mermaid: {
    // refer https://mermaid.js.org/config/setup/modules/mermaidAPI.html#mermaidapi-configuration-defaults for options
  },
  mermaidPlugin: {
    class: "mermaid", // set additional css classes for parent container 
  },
})
