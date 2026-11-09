import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount } from '@vue/test-utils'
import CredentialsView from '../../src/views/config/CredentialsView.vue'
import { useConfigCredentialsStore } from '../../src/stores/configCredentials'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'
import { useToast } from '../../src/composables/useToast'

describe('Credentials View & Store', () => {
  let mockApi: MockShipitApi

  beforeEach(async () => {
    setActivePinia(createPinia())
    mockApi = new MockShipitApi()
    setApi(mockApi)
  })

  it('loads credentials list without exposing secret values (tạo không lộ lại giá trị)', async () => {
    const store = useConfigCredentialsStore()
    const creds = await store.fetchCredentials()

    expect(creds.length).toBeGreaterThan(0)
    for (const cred of creds) {
      expect(cred.hasValue).toBe(true)
      expect((cred as any).value).toBeUndefined()
    }

    // Create a new credential
    const created = await store.createCredential({
      name: 'test-new-secret',
      kind: 'git_token',
      value: 'super-sensitive-token-123'
    })

    expect(created.id).toBeDefined()
    expect(created.hasValue).toBe(true)
    // Secret value should never be present in returned credential
    expect((created as any).value).toBeUndefined()

    // Assert that the newly fetched item in the list also doesn't contain secret value
    const found = store.credentials.find((c) => c.name === 'test-new-secret')
    expect(found).toBeDefined()
    expect((found as any)?.value).toBeUndefined()
  })

  it('renders credentials view and mounts dialog properly', async () => {
    const wrapper = mount(CredentialsView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(wrapper.text()).toContain('Thông tin xác thực')
    expect(wrapper.text()).toContain('Khoá SSH')
    expect(wrapper.text()).toContain('Token Git')
  })

  it('displays username field ONLY when kind is Registry (trường username chỉ khi Registry)', async () => {
    const store = useConfigCredentialsStore()
    await store.fetchCredentials()

    const wrapper = mount(CredentialsView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    // Open add dialog
    const addButton = wrapper.findAll('button').find((b) => b.text().includes('Thêm thông tin xác thực'))
    expect(addButton).toBeDefined()
    await addButton!.trigger('click')

    // In default 'ssh_key' mode: username field should not be present
    expect(document.body.innerHTML).not.toContain('Tên đăng nhập (Username)')

    // Find select and change kind to 'registry'
    const select = document.body.querySelector('select') as HTMLSelectElement
    expect(select).toBeDefined()
    select.value = 'registry'
    select.dispatchEvent(new Event('change'))
    await wrapper.vm.$nextTick()
    await new Promise((resolve) => setTimeout(resolve, 50))

    // Now username field should be visible
    expect(document.body.innerHTML).toContain('Tên đăng nhập (Username)')
  })

  it('preserves existing value when editing without entering new secret (sửa để trống giữ giá trị)', async () => {
    const store = useConfigCredentialsStore()
    const updateSpy = vi.spyOn(mockApi, 'updateCredential')

    const cred = await mockApi.createCredential({
      name: 'original-token',
      kind: 'git_token',
      value: 'original-secret-val'
    })

    // Update credential without providing value
    await store.updateCredential(cred.id, {
      name: 'renamed-token',
      value: undefined // empty / omitted
    })

    expect(updateSpy).toHaveBeenCalledWith(cred.id, {
      name: 'renamed-token',
      value: undefined
    })

    const updated = store.credentials.find((c) => c.id === cred.id)
    expect(updated?.name).toBe('renamed-token')
    expect(updated?.hasValue).toBe(true)
    expect((updated as any)?.value).toBeUndefined()
  })

  it('handles 409 conflict error when deleting an in-use credential (409 khi xoá)', async () => {
    const store = useConfigCredentialsStore()
    const toast = useToast()
    const toastErrorSpy = vi.spyOn(toast, 'error')

    await store.fetchCredentials()
    // Credential id 1 is used by slave 1 in mock
    await expect(store.deleteCredential(1)).rejects.toThrow()

    expect(store.error).toBe('Thông tin xác thực đang được dùng bởi slave/pipeline')
    expect(store.credentials.some((c) => c.id === 1)).toBe(true)
  })
})
