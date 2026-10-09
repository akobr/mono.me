import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ui from '@nuxt/ui/vue-plugin'
import { StorytellerError } from '@42for.net/2splatform.sdk'
import OrganizationForm from '@/components/orgs/OrganizationForm.vue'

async function settle() {
  // VeeValidate validates asynchronously; give it a few ticks.
  for (let index = 0; index < 5; index++) {
    await flushPromises()
  }
}

function mountForm(submit = vi.fn(async () => {})) {
  const wrapper = mount(OrganizationForm, { props: { submit }, global: { plugins: [ui] }, attachTo: document.body })
  const [organization, project] = wrapper.findAll('input')
  return { wrapper, organization: organization!, project: project!, submit }
}

describe('OrganizationForm (VeeValidate + Zod)', () => {
  it('validates the names before submitting', async () => {
    const { wrapper, organization, project, submit } = mountForm()

    await organization.setValue('Acme Corp')
    await project.setValue('orgs')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(submit).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Use 2 to 63 lower-case letters')
    wrapper.unmount()
  })

  it('submits trimmed, valid names', async () => {
    const { wrapper, organization, project, submit } = mountForm()

    await organization.setValue(' acme ')
    await project.setValue('billing')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(submit).toHaveBeenCalledWith({ organization: 'acme', project: 'billing' })
    wrapper.unmount()
  })

  it('maps a 409 from the API to the organization field', async () => {
    const submit = vi.fn(async () => {
      throw new StorytellerError({ status: 409, message: 'Exists', errorCode: 'AccessPointExists' })
    })
    const { wrapper, organization, project } = mountForm(submit)

    await organization.setValue('acme')
    await project.setValue('billing')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(wrapper.text()).toContain('This organization already exists')
    wrapper.unmount()
  })
})
