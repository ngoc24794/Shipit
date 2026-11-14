import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount } from '@vue/test-utils'
import JobAllowListView from '../../src/views/jobs/JobAllowListView.vue'
import { useJobsAllowStore } from '../../src/stores/jobsAllow'
import { useToast } from '../../src/composables/useToast'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'
import { ApiError } from '../../src/api/client'

describe('JobAllowListView (Allowlist Job)', () => {
  let mockApi: MockShipitApi

  beforeEach(async () => {
    document.body.innerHTML = ''
    setActivePinia(createPinia())
    mockApi = new MockShipitApi()
    setApi(mockApi)
    await router.push('/settings/job-allow')
    await router.isReady()
  })

  it('renders allowlist table with namespace, kind badge, and resource name', async () => {
    const store = useJobsAllowStore()
    await store.fetchEntries()

    const wrapper = mount(JobAllowListView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(wrapper.text()).toContain('avina-workspace')
    expect(wrapper.text()).toContain('avina-files-config')
    expect(wrapper.text()).toContain('avina-users-config')
    expect(wrapper.text()).toContain('avina-files-secret')
    expect(wrapper.text()).toContain('avina-users-secret')
    expect(wrapper.text()).toContain('registry-secret')
  })

  it('adds a new allowlist entry successfully', async () => {
    const createSpy = vi.spyOn(mockApi, 'createJobAllowEntry')

    const wrapper = mount(JobAllowListView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    const inputs = wrapper.findAll('input')
    const nsInput = inputs.find((i) => i.attributes('placeholder')?.includes('VD: avina-workspace'))
    await nsInput!.setValue('test-ns')

    const nameInput = inputs.find((i) => i.attributes('placeholder')?.includes('VD: avina-files-config'))
    await nameInput!.setValue('test-config')

    const kindSelect = wrapper.find('select')
    await kindSelect.setValue('configmap')

    const submitBtn = wrapper.findAll('button').find((b) => b.text().includes('Thêm mục'))
    await wrapper.find('form').trigger('submit.prevent')
    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(createSpy).toHaveBeenCalledWith({
      namespace: 'test-ns',
      kind: 'configmap',
      name: 'test-config'
    })
  })

  it('handles 409 conflict when deleting an entry that is in use by a Job', async () => {
    // Delete entry 2: 'avina-files-config' which is used by Job 'avina-dbmigrator'
    const wrapper = mount(JobAllowListView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    // Find delete button on table
    const deleteButtons = wrapper.findAll('button').filter((b) => b.text().includes('Xoá'))
    expect(deleteButtons.length).toBeGreaterThan(0)
    await deleteButtons[0].trigger('click')
    await wrapper.vm.$nextTick()

    // Confirm dialog is open
    const confirmBtn = Array.from(document.body.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Xoá mục')
    )
    expect(confirmBtn).toBeDefined()
    await confirmBtn!.click()
    await new Promise((resolve) => setTimeout(resolve, 60))

    // Toast error should report that allowlist item is in use by Job
    const toast = useToast()
    expect(toast.toasts.value.some((t) => t.message.includes('đang được Job sử dụng'))).toBe(true)
  })
})
