import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount } from '@vue/test-utils'
import SlavesView from '../../src/views/config/SlavesView.vue'
import { useConfigSlavesStore } from '../../src/stores/configSlaves'
import { useConfigCredentialsStore } from '../../src/stores/configCredentials'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'

describe('Slaves View & Store', () => {
  let mockApi: MockShipitApi

  beforeEach(() => {
    setActivePinia(createPinia())
    mockApi = new MockShipitApi()
    setApi(mockApi)
  })

  it('filters SSH credential select to strictly kind=ssh_key (select chỉ có ssh_key)', async () => {
    const credStore = useConfigCredentialsStore()
    await credStore.fetchCredentials()

    // Mock API has 3 credentials: 1 ssh_key, 1 git_token, 1 registry
    expect(credStore.credentials.length).toBe(3)
    const sshCreds = credStore.credentials.filter((c) => c.kind === 'ssh_key')
    expect(sshCreds.length).toBe(1)

    const wrapper = mount(SlavesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    // Open add slave dialog
    const addButton = wrapper.findAll('button').find((b) => b.text().includes('Thêm máy build'))
    expect(addButton).toBeDefined()
    await addButton!.trigger('click')

    // Find the select for SSH credentials in document.body
    const selects = document.body.querySelectorAll('select')
    expect(selects.length).toBeGreaterThan(0)
    const sshSelect = selects[0]

    const options = Array.from(sshSelect.options).filter((o) => o.value !== '')
    // Must contain exactly 1 option (the ssh_key)
    expect(options.length).toBe(1)
    expect(options[0].textContent?.trim()).toBe('devops-ssh-key')
  })

  it('tests connection to a slave and displays result (kiểm tra kết nối hiện kết quả)', async () => {
    const store = useConfigSlavesStore()
    const testSpy = vi.spyOn(mockApi, 'testSlaveConnection')

    await store.fetchSlaves()
    expect(store.slaves.length).toBeGreaterThan(0)
    const slaveId = store.slaves[0].id

    // Test connection via store
    const result = await store.testSlave(slaveId)
    expect(testSpy).toHaveBeenCalledWith(slaveId)
    expect(result.ok).toBe(true)
    expect(result.message).toContain('thành công')
    expect(store.testResults[slaveId]).toEqual(result)

    // Test connection in View UI
    const wrapper = mount(SlavesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    // Check that button "Kiểm tra kết nối" exists
    const testBtn = wrapper.findAll('button').find((b) => b.text().includes('Kiểm tra kết nối'))
    expect(testBtn).toBeDefined()

    await testBtn!.trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 150))

    // Status on the row should be updated with success message
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

    // Slave id 1 is used by pipeline in mockApi
    await expect(store.deleteSlave(1)).rejects.toThrow()
    expect(store.error).toBe('Không thể xoá máy build đang được pipeline sử dụng')
  })
})
