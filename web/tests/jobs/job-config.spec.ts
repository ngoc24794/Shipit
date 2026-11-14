import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount } from '@vue/test-utils'
import JobFormView from '../../src/views/jobs/JobFormView.vue'
import { useJobsStore } from '../../src/stores/jobs'
import { useJobsAllowStore } from '../../src/stores/jobsAllow'
import { useConfigCredentialsStore } from '../../src/stores/configCredentials'
import { useConfigSlavesStore } from '../../src/stores/configSlaves'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'
import { ApiError } from '../../src/api/client'
import type { JobInput } from '../../src/api/types'

describe('Job Form View & Store', () => {
  let mockApi: MockShipitApi

  beforeEach(async () => {
    document.body.innerHTML = ''
    setActivePinia(createPinia())
    mockApi = new MockShipitApi()
    setApi(mockApi)
    await router.push('/jobs/new')
    await router.isReady()
  })

  it('populates dropdowns and ensures envFrom only contains names in allowlist', async () => {
    const allowStore = useJobsAllowStore()
    await allowStore.fetchEntries()

    const wrapper = mount(JobFormView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))

    // Check namespace select options: should only contain namespaces from allowlist ('avina-workspace')
    const namespaceSelect = wrapper.find('select[id*="field"]')
    expect(wrapper.text()).toContain('avina-workspace')

    // Check ConfigMap and Secret allowlist options in task cards
    const selects = wrapper.findAll('select')
    const selectTexts = selects.map((s) => s.text())

    // ConfigMap options must include avina-files-config and avina-users-config
    expect(selectTexts.some((t) => t.includes('avina-files-config'))).toBe(true)
    expect(selectTexts.some((t) => t.includes('avina-users-config'))).toBe(true)

    // Secret options must include avina-files-secret and avina-users-secret
    expect(selectTexts.some((t) => t.includes('avina-files-secret'))).toBe(true)
    expect(selectTexts.some((t) => t.includes('avina-users-secret'))).toBe(true)

    // And must NOT include arbitrary secrets or unallowed names
    expect(selectTexts.some((t) => t.includes('root-secret') || t.includes('unallowed-secret'))).toBe(false)
  })

  it('guarantees NO input exists for entering secret value (không có ô nhập giá trị secret)', async () => {
    const wrapper = mount(JobFormView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))

    // Add env var to the task
    const addEnvBtn = wrapper.findAll('button').find((b) => b.text().includes('Thêm biến môi trường'))
    expect(addEnvBtn).toBeDefined()
    await addEnvBtn!.trigger('click')

    // Switch source to "Lấy từ Secret"
    const sourceSelect = wrapper.findAll('select').find((s) => s.text().includes('Giá trị trực tiếp') && s.text().includes('Lấy từ Secret'))
    expect(sourceSelect).toBeDefined()
    await sourceSelect!.setValue('secret')

    // Look at all input elements rendered in the DOM
    const inputs = wrapper.findAll('input')
    const placeholders = inputs.map((i) => i.attributes('placeholder') || '')
    const ariaLabels = inputs.map((i) => i.attributes('aria-label') || '')

    // Check placeholders and labels: should have "Key", but NO "Giá trị secret" or "Mật khẩu"
    expect(placeholders.some((p) => p.toLowerCase().includes('key'))).toBe(true)
    expect(placeholders.some((p) => p.toLowerCase().includes('mật khẩu') || p.toLowerCase().includes('giá trị secret'))).toBe(false)
    expect(ariaLabels.some((a) => a.toLowerCase().includes('giá trị secret'))).toBe(false)
  })

  it('enforces required timeout validation (timeout bắt buộc và > 0)', async () => {
    const wrapper = mount(JobFormView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))

    // Fill form name and basic inputs
    const nameInput = wrapper.findAll('input').find((i) => i.attributes('placeholder')?.includes('VD: avina-dbmigrator'))
    await nameInput!.setValue('test-job')

    // Clear timeout input on first task
    const timeoutInput = wrapper.findAll('input').find((i) => i.attributes('placeholder') === '1800')
    expect(timeoutInput).toBeDefined()
    await timeoutInput!.setValue('')

    // Click submit
    const submitBtn = wrapper.findAll('button').find((b) => b.text().includes('Tạo Job'))
    await submitBtn!.trigger('click')
    await wrapper.vm.$nextTick()

    // Assert timeout validation error
    expect(wrapper.text()).toContain('Thời gian timeout bắt buộc và phải lớn hơn 0')
  })

  it('creates sample DbMigrator Job calling proper JobInput payload with 2 tasks', async () => {
    const createSpy = vi.spyOn(mockApi, 'createJob')

    const wrapper = mount(JobFormView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))

    // Set Section 1 inputs
    const inputs = wrapper.findAll('input')
    const nameInput = inputs.find((i) => i.attributes('placeholder')?.includes('VD: avina-dbmigrator'))
    await nameInput!.setValue('avina-dbmigrator')

    const gitUrlInput = inputs.find((i) => i.attributes('placeholder')?.includes('http://git.example/AvinaWorkspace'))
    await gitUrlInput!.setValue('http://git.example/AvinaWorkspace')

    const branchInput = inputs.find((i) => i.attributes('placeholder') === 'main')
    await branchInput!.setValue('main')

    const dockerfileInput = inputs.find((i) => i.attributes('placeholder')?.includes('docker/Dockerfile.dbmigrator'))
    await dockerfileInput!.setValue('docker/Dockerfile.dbmigrator')

    const contextInput = inputs.find((i) => i.attributes('placeholder') === '.')
    await contextInput!.setValue('.')

    const imageNameInput = inputs.find((i) => i.attributes('placeholder') === 'avina-dbmigrator')
    await imageNameInput!.setValue('avina-dbmigrator')

    const registryHostInput = inputs.find((i) => i.attributes('placeholder') === '192.168.1.241')
    await registryHostInput!.setValue('192.168.1.241')

    // Set Section 2 inputs
    const containerNameInput = inputs.find((i) => i.attributes('placeholder')?.includes('VD: dbmigrator'))
    await containerNameInput!.setValue('dbmigrator')

    // Selects
    const selects = wrapper.findAll('select')
    const gitCredSelect = selects.find((s) => s.text().includes('gitlab-token'))
    if (gitCredSelect) await gitCredSelect.setValue('2')

    const slaveSelect = selects.find((s) => s.text().includes('slave-staging-01'))
    if (slaveSelect) await slaveSelect.setValue('1')

    const nsSelect = selects.find((s) => s.text().includes('avina-workspace'))
    if (nsSelect) await nsSelect.setValue('avina-workspace')

    // Add ImagePullSecret: registry-secret
    const pullSelect = selects.find((s) => s.text().includes('Chọn pullSecret từ allowlist'))
    if (pullSelect) {
      await pullSelect.setValue('registry-secret')
      await pullSelect.trigger('change')
    }

    // Configure Task 1: label "files"
    const task1Label = wrapper.findAll('input').find((i) => i.attributes('placeholder') === 'VD: migrate-db')
    await task1Label!.setValue('files')

    // Add Task 2
    const addTaskBtn = wrapper.findAll('button').find((b) => b.text().includes('Thêm tác vụ'))
    await addTaskBtn!.trigger('click')
    await wrapper.vm.$nextTick()

    // Configure Task 2: label "users"
    const taskLabels = wrapper.findAll('input').filter((i) => i.attributes('placeholder') === 'VD: migrate-db')
    expect(taskLabels.length).toBe(2)
    await taskLabels[1].setValue('users')

    // Submit form
    const submitBtn = wrapper.findAll('button').find((b) => b.text().includes('Tạo Job'))
    await submitBtn!.trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(createSpy).toHaveBeenCalled()
    const payload = createSpy.mock.calls[0][0] as JobInput
    expect(payload.name).toBe('avina-dbmigrator')
    expect(payload.gitUrl).toBe('http://git.example/AvinaWorkspace')
    expect(payload.dockerfilePath).toBe('docker/Dockerfile.dbmigrator')
    expect(payload.buildContext).toBe('.')
    expect(payload.namespace).toBe('avina-workspace')
    expect(payload.tasks.length).toBe(2)
    expect(payload.tasks[0].label).toBe('files')
    expect(payload.tasks[1].label).toBe('users')
  })

  it('updates position when reordering tasks using keyboard buttons', async () => {
    const wrapper = mount(JobFormView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))

    // Name Task 1: 'first-task'
    const labelInputs = wrapper.findAll('input').filter((i) => i.attributes('placeholder') === 'VD: migrate-db')
    await labelInputs[0].setValue('first-task')

    // Add Task 2: 'second-task'
    const addTaskBtn = wrapper.findAll('button').find((b) => b.text().includes('Thêm tác vụ'))
    await addTaskBtn!.trigger('click')
    await wrapper.vm.$nextTick()

    const labelInputsAfter = wrapper.findAll('input').filter((i) => i.attributes('placeholder') === 'VD: migrate-db')
    await labelInputsAfter[1].setValue('second-task')

    // Click move down button on task 1
    const moveDownBtn = wrapper.find('button[aria-label="Di chuyển tác vụ xuống"]')
    expect(moveDownBtn.exists()).toBe(true)
    await moveDownBtn.trigger('click')
    await wrapper.vm.$nextTick()

    // After moving task 1 down, the order in form.tasks should be swapped: 'second-task', then 'first-task'
    const currentLabels = wrapper.findAll('input').filter((i) => i.attributes('placeholder') === 'VD: migrate-db')
    expect((currentLabels[0].element as HTMLInputElement).value).toBe('second-task')
    expect((currentLabels[1].element as HTMLInputElement).value).toBe('first-task')
  })

  it('duplicates a task properly when clicking "Nhân bản"', async () => {
    const wrapper = mount(JobFormView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))

    const labelInput = wrapper.find('input[placeholder="VD: migrate-db"]')
    await labelInput.setValue('original-task')

    const dupBtn = wrapper.find('button[aria-label="Nhân bản tác vụ"]')
    expect(dupBtn.exists()).toBe(true)
    await dupBtn.trigger('click')
    await wrapper.vm.$nextTick()

    const labelInputs = wrapper.findAll('input').filter((i) => i.attributes('placeholder') === 'VD: migrate-db')
    expect(labelInputs.length).toBe(2)
    expect((labelInputs[1].element as HTMLInputElement).value).toBe('original-task-copy')
  })

  it('maps 400 validation error from API directly onto fields', async () => {
    vi.spyOn(mockApi, 'createJob').mockRejectedValueOnce(
      new ApiError(400, 'Validation failed', {
        title: 'Validation failed',
        errors: {
          name: ['Tên Job không hợp lệ từ máy chủ'],
          gitUrl: ['Git URL không thể kết nối']
        }
      })
    )

    const wrapper = mount(JobFormView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))

    // Fill valid client inputs to pass client validation
    const inputs = wrapper.findAll('input')
    const nameInput = inputs.find((i) => i.attributes('placeholder')?.includes('VD: avina-dbmigrator'))
    await nameInput!.setValue('test-job')
    const gitUrlInput = inputs.find((i) => i.attributes('placeholder')?.includes('http://git.example/AvinaWorkspace'))
    await gitUrlInput!.setValue('http://git.example/test')
    const imageNameInput = inputs.find((i) => i.attributes('placeholder') === 'avina-dbmigrator')
    await imageNameInput!.setValue('test-image')
    const registryHostInput = inputs.find((i) => i.attributes('placeholder') === '192.168.1.241')
    await registryHostInput!.setValue('192.168.1.241')
    const containerNameInput = inputs.find((i) => i.attributes('placeholder')?.includes('VD: dbmigrator'))
    await containerNameInput!.setValue('test-container')

    const selects = wrapper.findAll('select')
    const gitCredSelect = selects.find((s) => s.text().includes('gitlab-token'))
    if (gitCredSelect) await gitCredSelect.setValue('2')
    const slaveSelect = selects.find((s) => s.text().includes('slave-staging-01'))
    if (slaveSelect) await slaveSelect.setValue('1')
    const nsSelect = selects.find((s) => s.text().includes('avina-workspace'))
    if (nsSelect) await nsSelect.setValue('avina-workspace')

    // Submit
    const submitBtn = wrapper.findAll('button').find((b) => b.text().includes('Tạo Job'))
    await submitBtn!.trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 50))

    // Verify mapped 400 error text appears in DOM
    expect(wrapper.text()).toContain('Tên Job không hợp lệ từ máy chủ')
    expect(wrapper.text()).toContain('Git URL không thể kết nối')
  })
})
