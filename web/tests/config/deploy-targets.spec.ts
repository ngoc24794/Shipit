import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount, flushPromises } from '@vue/test-utils'
import DeployTargetsView from '../../src/views/config/DeployTargetsView.vue'
import { useConfigDeployTargetsStore } from '../../src/stores/configDeployTargets'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'

describe('DeployTargets View & Store', () => {
  let mockApi: MockShipitApi
  let pinia: ReturnType<typeof createPinia>

  beforeEach(() => {
    pinia = createPinia()
    setActivePinia(pinia)
    mockApi = new MockShipitApi()
    setApi(mockApi)
  })

  it('renders deploy targets view with fixed help text and existing items', async () => {
    const wrapper = mount(DeployTargetsView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    expect(wrapper.text()).toContain('Deployment được phép')
    expect(wrapper.text()).toContain(
      'Danh sách này giới hạn những deployment mà Shipit sẽ cập nhật. Quyền thực tế của Shipit trong cụm vẫn do RBAC của Kubernetes quyết định: thêm vào đây không cấp thêm quyền, nếu RBAC không cho phép thì lần deploy sẽ lỗi.'
    )
    expect(wrapper.text()).toContain('devops')
    expect(wrapper.text()).toContain('shipit-demo')
    expect(wrapper.text()).toContain('avina-workspace')
    expect(wrapper.text()).toContain('avina-catalog')
  })

  it('validates quick add form inputs (DNS-1123 format)', async () => {
    const wrapper = mount(DeployTargetsView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    // Submit empty form
    await wrapper.find('form').trigger('submit.prevent')
    expect(wrapper.text()).toContain('Vui lòng nhập Namespace')
    expect(wrapper.text()).toContain('Vui lòng nhập Deployment')

    // Submit invalid namespace
    const nsInput = wrapper.findAll('input')[0]
    const depInput = wrapper.findAll('input')[1]

    await nsInput.setValue('DevOps_Upper')
    await depInput.setValue('valid-app')
    await wrapper.find('form').trigger('submit.prevent')

    expect(wrapper.text()).toContain('Namespace phải tuân theo chuẩn DNS-1123 label')
  })

  it('adds a new deployment target successfully', async () => {
    const wrapper = mount(DeployTargetsView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const nsInput = wrapper.findAll('input')[0]
    const depInput = wrapper.findAll('input')[1]

    await nsInput.setValue('production')
    await depInput.setValue('payment-api')
    await wrapper.find('form').trigger('submit.prevent')

    await new Promise((resolve) => setTimeout(resolve, 100))
    await flushPromises()

    expect(wrapper.text()).toContain('production')
    expect(wrapper.text()).toContain('payment-api')
  })

  it('handles 409 conflict when adding duplicate target', async () => {
    const store = useConfigDeployTargetsStore()
    await store.fetchDeployTargets()

    // devops/shipit-demo already exists in mock
    await expect(
      store.createDeployTarget({
        namespace: 'devops',
        deployment: 'shipit-demo'
      })
    ).rejects.toThrow()

    expect(store.error).toBe('Deployment này đã tồn tại trong danh sách.')
  })

  it('handles deletion with confirmation and prevents deleting target in use', async () => {
    const wrapper = mount(DeployTargetsView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    // Try deleting first item (devops/shipit-demo which is in use by pipeline 1)
    const deleteBtn = wrapper.find('button[aria-label="Xoá deployment devops/shipit-demo"]')
    expect(deleteBtn.exists()).toBe(true)
    await deleteBtn.trigger('click')

    // Confirm dialog is opened (teleported to document.body)
    expect(document.body.textContent).toContain('Xoá deployment được phép')
    expect(document.body.textContent).toContain('devops/shipit-demo')

    // Click confirm delete in dialog
    const buttons = Array.from(document.body.querySelectorAll('button'))
    const confirmBtn = buttons.find((b) => b.textContent?.includes('Xoá deployment'))
    expect(confirmBtn).toBeDefined()
    if (confirmBtn) {
      confirmBtn.click()
      await new Promise((resolve) => setTimeout(resolve, 100))
      await flushPromises()
    }

    // Since it's in use by pipeline, error should be set
    const store = useConfigDeployTargetsStore()
    expect(store.error).toContain('pipeline')
  })
})
