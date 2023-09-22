import { defineConfig } from 'vitepress'

// https://vitepress.dev/reference/site-config
export default defineConfig({
  title: "42for.net",
  description: "simple and clean .net",
  themeConfig: {
    // https://vitepress.dev/reference/default-theme-config
    logo: "/42-for-net.svg",
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
      /*{
        text: 'Other concepts',
        items: [
          { text: 'Automated testing', link: '/concepts/automation-testing' },
          { text: 'Infrastructure', link: '/concepts/infrastructure' },
          { text: 'Hell of dependencies', link: '/concepts/dependency-hell' },
        ]
      },*/
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
      }
    ],

    socialLinks: [
      { icon: 'github', link: 'https://github.com/akobr/mono.me' }
    ]
  }
})
