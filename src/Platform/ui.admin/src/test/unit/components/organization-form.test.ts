import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import ui from '@nuxt/ui/vue-plugin'
import { StorytellerError } from '@42for.net/2splatform.sdk'
import OrganizationForm from '@/components/orgs/OrganizationForm.vue'

function mountForm(submit = vi.fn(async () => {}), props: { autofocus?: boolean; disabled?: boolean } = {}) {
  const wrapper = mount(OrganizationForm, {
    props: { submit, ...props },
    global: { plugins: [ui] },
    attachTo: document.body,
  })
  const [organization, project] = wrapper.findAll('input')
  return { wrapper, organization: organization!, project: project!, description: wrapper.find('textarea'), submit }
}

// VeeValidate validates asynchronously, so the assertions wait for the outcome instead of counting ticks.
describe('OrganizationForm (VeeValidate + Zod)', () => {
  it('validates the names before submitting', async () => {
    const { wrapper, organization, project, submit } = mountForm()

    await organization.setValue('Acme Corp')
    await project.setValue('orgs')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => expect(wrapper.text()).toContain('Use 2 to 63 lower-case letters'))
    expect(submit).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('submits trimmed, valid names', async () => {
    const { wrapper, organization, project, submit } = mountForm()

    await organization.setValue(' acme ')
    await project.setValue('billing')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() =>
      expect(submit).toHaveBeenCalledWith({ organization: 'acme', project: 'billing', description: '' }),
    )
    wrapper.unmount()
  })

  it('submits the description of the first project, at most 500 characters', async () => {
    const { wrapper, organization, project, description, submit } = mountForm()

    await organization.setValue('acme')
    await project.setValue('billing')
    await description.setValue('x'.repeat(501))
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => expect(wrapper.text()).toContain('At most 500 characters.'))
    expect(submit).not.toHaveBeenCalled()

    // The dialogs trim it before they send it.
    await description.setValue(' Invoices of Acme. ')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() =>
      expect(submit).toHaveBeenCalledWith({
        organization: 'acme',
        project: 'billing',
        description: ' Invoices of Acme. ',
      }),
    )
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

    await vi.waitFor(() => expect(wrapper.text()).toContain('This organization already exists'))
    wrapper.unmount()
  })

  // The dialog of "New organization" opens with the cursor in the first field; the onboarding wizard moves the focus
  // itself and focuses nothing on its first load (424-267).
  it('focuses the first field by default', async () => {
    const { wrapper, organization } = mountForm()

    await vi.waitFor(() => expect(document.activeElement).toBe(organization.element))
    wrapper.unmount()
  })

  it('leaves the focus alone without autofocus', async () => {
    ;(document.activeElement as HTMLElement | null)?.blur()
    const { wrapper, organization } = mountForm(undefined, { autofocus: false })

    // The autofocus of the input waits for a timer.
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(document.activeElement).not.toBe(organization.element)
    expect(document.activeElement).toBe(document.body)
    wrapper.unmount()
  })

  it('submits with an enabled button by default', () => {
    const { wrapper } = mountForm()

    expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeUndefined()
    wrapper.unmount()
  })

  it('does not submit while it is disabled, however the submit comes', async () => {
    const { wrapper, organization, project, submit } = mountForm(undefined, { disabled: true })

    expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeDefined()
    await organization.setValue('acme')
    await project.setValue('billing')
    const event = new Event('submit', { bubbles: true, cancelable: true })
    wrapper.find('form').element.dispatchEvent(event)
    await new Promise((resolve) => setTimeout(resolve, 20))

    expect(submit).not.toHaveBeenCalled()
    // The page does not reload either.
    expect(event.defaultPrevented).toBe(true)

    await wrapper.setProps({ disabled: false })
    expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeUndefined()
    await wrapper.find('form').trigger('submit')
    await vi.waitFor(() =>
      expect(submit).toHaveBeenCalledWith({ organization: 'acme', project: 'billing', description: '' }),
    )
    wrapper.unmount()
  })
})
