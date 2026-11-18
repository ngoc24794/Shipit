import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount as baseMount, flushPromises, type VueWrapper } from '@vue/test-utils'
import CredentialsView from '../../src/views/config/CredentialsView.vue'
import CredentialFormView from '../../src/views/config/CredentialFormView.vue'
import { useConfigCredentialsStore } from '../../src/stores/configCredentials'
import { useAuthStore } from '../../src/stores/auth'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'

describe('Credentials View, Form & Store', () => {
  let mockApi: MockShipitApi
  let pinia: ReturnType<typeof createPinia>
  const mountedWrappers: VueWrapper[] = []

  const mount: typeof baseMount = ((component: any, options?: any) => {
    const w = baseMount(component, options)
    mountedWrappers.push(w)
    return w
  }) as typeof baseMount

  afterEach(() => {
    while (mountedWrappers.length > 0) {
      mountedWrappers.pop()?.unmount()
    }
    vi.restoreAllMocks()
  })

  beforeEach(async () => {
    pinia = createPinia()
    setActivePinia(pinia)
    mockApi = new MockShipitApi()
    setApi(mockApi)
    vi.spyOn(window, 'confirm').mockReturnValue(true)

    const authStore = useAuthStore()
    authStore.user = { username: 'admin' }
    authStore.initialized = true
  })

  it('loads credentials list without exposing secret values (tạo không lộ lại giá trị)', async () => {
    const store = useConfigCredentialsStore()
    const creds = await store.fetchCredentials()

    expect(creds.length).toBeGreaterThan(0)
    for (const cred of creds) {
      expect(cred.hasValue).toBe(true)
      expect((cred as any).value).toBeUndefined()
    }

    const created = await store.createCredential({
      name: 'test-new-secret',
      kind: 'git_token',
      value: 'super-sensitive-token-123'
    })

    expect(created.id).toBeDefined()
    expect(created.hasValue).toBe(true)
    expect((created as any).value).toBeUndefined()

    const found = store.credentials.find((c) => c.name === 'test-new-secret')
    expect(found).toBeDefined()
    expect((found as any)?.value).toBeUndefined()
  })

  it('renders credentials list and navigates to /credentials/new and /credentials/:id/edit', async () => {
    await router.push('/credentials')
    await router.isReady()
    const pushSpy = vi.spyOn(router, 'push')

    const wrapper = mount(CredentialsView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    expect(wrapper.text()).toContain('Thông tin xác thực')
    expect(wrapper.text()).toContain('Khoá SSH')
    expect(wrapper.text()).toContain('Token Git')

    const addButton = wrapper.findAll('button').find((b) => b.text().includes('Thêm thông tin xác thực'))
    expect(addButton).toBeDefined()
    await addButton!.trigger('click')
    expect(pushSpy).toHaveBeenCalledWith('/credentials/new')

    const editButton = wrapper.find('button[aria-label="Sửa thông tin xác thực"]')
    expect(editButton.exists()).toBe(true)
    await editButton.trigger('click')
    expect(pushSpy).toHaveBeenCalledWith('/credentials/1/edit')
  })

  it('displays username field ONLY when kind is Registry on CredentialFormView', async () => {
    await router.push('/credentials/new')
    await router.isReady()

    const wrapper = mount(CredentialFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await flushPromises()

    // In default 'ssh_key' mode: username field should not be present
    expect(wrapper.text()).not.toContain('Tên đăng nhập (Username)')

    // Change kind to 'registry'
    const select = wrapper.find('select')
    expect(select.exists()).toBe(true)
    await select.setValue('registry')
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Tên đăng nhập (Username)')
  })

  it('loads credential by :id on edit page and shows not-found for unknown id', async () => {
    await router.push('/credentials/1/edit')
    await router.isReady()

    const wrapper = mount(CredentialFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    expect(wrapper.text()).toContain('Sửa thông tin xác thực')
    const nameInput = wrapper.find('input[placeholder="Ví dụ: gitlab-deploy-key"]')
    expect((nameInput.element as HTMLInputElement).value).toBe('devops-ssh-key')

    // Unknown id
    await router.push('/credentials/99999/edit')
    await router.isReady()

    const notFoundWrapper = mount(CredentialFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    expect(notFoundWrapper.text()).toContain('Không tìm thấy thông tin xác thực')
    expect(notFoundWrapper.text()).toContain('Quay lại danh sách')
  })

  it('warns on unsaved changes when leaving CredentialFormView', async () => {
    await router.push('/credentials/new')
    await router.isReady()

    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValueOnce(false)

    const wrapper = mount(CredentialFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await flushPromises()

    const nameInput = wrapper.find('input[placeholder="Ví dụ: gitlab-deploy-key"]')
    await nameInput.setValue('unsaved-cred')

    await router.push('/credentials')
    expect(confirmSpy).toHaveBeenCalled()
    expect(router.currentRoute.value.path).toBe('/credentials/new')
  })

  it('preserves existing value when editing without entering new secret (sửa để trống giữ giá trị)', async () => {
    const store = useConfigCredentialsStore()
    const updateSpy = vi.spyOn(mockApi, 'updateCredential')

    const cred = await mockApi.createCredential({
      name: 'original-token',
      kind: 'git_token',
      value: 'original-secret-val'
    })

    await store.updateCredential(cred.id, {
      name: 'renamed-token',
      value: undefined
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
    await store.fetchCredentials()
    await expect(store.deleteCredential(1)).rejects.toThrow()

    expect(store.error).toBe('Thông tin xác thực đang được dùng bởi slave/pipeline')
    expect(store.credentials.some((c) => c.id === 1)).toBe(true)
  })
})
