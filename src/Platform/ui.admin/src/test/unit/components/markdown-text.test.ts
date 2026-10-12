import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import MarkdownText from '@/components/common/MarkdownText.vue'

describe('MarkdownText', () => {
  it('renders block Markdown inside its root', () => {
    const wrapper = mount(MarkdownText, {
      props: { source: '## Endpoints\n\n| Name | URL |\n| --- | --- |\n| api | `/api` |\n\n- one\n- two' },
    })

    expect(wrapper.classes()).toContain('markdown')
    expect(wrapper.find('h2').text()).toBe('Endpoints')
    expect(wrapper.find('.markdown-table > table td code').text()).toBe('/api')
    expect(wrapper.findAll('ul > li').map((item) => item.text())).toEqual(['one', 'two'])
  })

  it('shows the empty text for an empty source', () => {
    const wrapper = mount(MarkdownText, { props: { source: '', empty: 'Nothing to preview.' } })

    expect(wrapper.classes()).not.toContain('markdown')
    expect(wrapper.text()).toBe('Nothing to preview.')
  })
})
