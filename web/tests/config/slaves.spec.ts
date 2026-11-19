import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount as baseMount, flushPromises, type VueWrapper } from '@vue/test-utils'
import SlavesView from '../../src/views/config/SlavesView.vue'
import SlaveFormView from '../../src/views/config/SlaveFormView.vue'
import { useConfigSlavesStore } from '../../src/stores/configSlaves'
import { useConfigCredentialsStore } from '../../src/stores/configCredentials'
import { useAuthStore } from '../../src/stores/auth'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'

describe('Slaves View, Form & Store', () => {
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

  it('navigates from SlavesView to /slaves/new and /slaves/:id/edit', async () => {
    await router.push('/slaves')
    await router.isReady()
    const pushSpy = vi.spyOn(router, 'push')

    const wrapper = mount(SlavesView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const addButton = wrapper.findAll('button').find((b) => b.text().includes('Thêm máy build'))
    expect(addButton).toBeDefined()
    await addButton!.trigger('click')
    expect(pushSpy).toHaveBeenCalledWith('/slaves/new')

    const editButton = wrapper.find('button[aria-label="Sửa máy build"]')
    expect(editButton.exists()).toBe(true)
    await editButton.trigger('click')
    expect(pushSpy).toHaveBeenCalledWith('/slaves/1/edit')
  })

  it('filters SSH credential select to strictly kind=ssh_key on SlaveFormView', async () => {
    const credStore = useConfigCredentialsStore()
    await credStore.fetchCredentials()

    expect(credStore.credentials.length).toBe(3)
    const sshCreds = credStore.credentials.filter((c) => c.kind === 'ssh_key')
    expect(sshCreds.length).toBe(1)

    await router.push('/slaves/new')
    await router.isReady()

    const wrapper = mount(SlaveFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const sshSelect = wrapper.find('[role="combobox"]')
    expect(sshSelect.exists()).toBe(true)
    await sshSelect.trigger('click')
    const opts = Array.from(document.body.querySelectorAll('[role="option"]')).filter(
      (o) => !o.textContent?.includes('Chọn')
    )
    expect(opts.length).toBe(1)
    expect(opts[0].textContent?.trim()).toContain('devops-ssh-key')
  })

  it('loads slave by :id on edit page and shows not-found for unknown id', async () => {
    await router.push('/slaves/1/edit')
    await router.isReady()

    const wrapper = mount(SlaveFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    expect(wrapper.text()).toContain('Sửa máy build')
    const nameInput = wrapper.find('input[placeholder="Ví dụ: slave-staging-01"]')
    expect((nameInput.element as HTMLInputElement).value.length).toBeGreaterThan(0)

    await router.push('/slaves/99999/edit')
    await router.isReady()

    const notFoundWrapper = mount(SlaveFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 150))
    await flushPromises()

    expect(notFoundWrapper.text()).toContain('Không tìm thấy máy build')
  })

  it('tests connection to a slave and displays result (kiểm tra kết nối hiện kết quả)', async () => {
    const store = useConfigSlavesStore()
    const testSpy = vi.spyOn(mockApi, 'testSlaveConnection')

    await store.fetchSlaves()
    expect(store.slaves.length).toBeGreaterThan(0)
    const slaveId = store.slaves[0].id

    const result = await store.testSlave(slaveId)
    expect(testSpy).toHaveBeenCalledWith(slaveId)
    expect(result.ok).toBe(true)
    expect(result.message).toContain('thành công')
    expect(store.testResults[slaveId]).toEqual(result)

    const wrapper = mount(SlavesView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))

    const testBtn = wrapper.findAll('button').find((b) => b.text().includes('Kiểm tra kết nối'))
    expect(testBtn).toBeDefined()

    await testBtn!.trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 150))

    expect(wrapper.text()).toContain('thành công')
  })

  it('creates and updates a slave properly', async () => {
    const store = useConfigSlavesStore()

    const created = await store.createSlave({
      name: 'worker-node-03',
      host: '192.168.1.100',
      port: 2222,
      user: 'shipit-runner',
      sshCredentialId: 1
    })

    expect(created.id).toBeDefined()
    expect(created.name).toBe('worker-node-03')
    expect(created.port).toBe(2222)

    const updated = await store.updateSlave(created.id, {
      name: 'worker-node-03-renamed',
      host: '192.168.1.101',
      port: 22,
      user: 'shipit-runner',
      sshCredentialId: 1
    })

    expect(updated.name).toBe('worker-node-03-renamed')
    expect(updated.host).toBe('192.168.1.101')
  })

  it('handles 409 conflict when deleting a slave used by a pipeline', async () => {
    const store = useConfigSlavesStore()
    await store.fetchSlaves()

    await expect(store.deleteSlave(1)).rejects.toThrow()
    expect(store.error).toBe('Không thể xoá máy build đang được pipeline sử dụng')
  })
})
