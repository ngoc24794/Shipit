import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount } from '@vue/test-utils'
import PipelinesView from '../../src/views/config/PipelinesView.vue'
import { useConfigPipelinesStore } from '../../src/stores/configPipelines'
import { useConfigCredentialsStore } from '../../src/stores/configCredentials'
import { useConfigSlavesStore } from '../../src/stores/configSlaves'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'
import { ApiError } from '../../src/api/client'
import type { Pipeline } from '../../src/api/types'

describe('Pipelines View & Store', () => {
  let mockApi: MockShipitApi

  beforeEach(() => {
    document.body.innerHTML = ''
    setActivePinia(createPinia())
    mockApi = new MockShipitApi()
    setApi(mockApi)
  })

  it('filters credentials correctly by kind in form selects (select lọc đúng loại credential)', async () => {
    const credStore = useConfigCredentialsStore()
    const slaveStore = useConfigSlavesStore()
    await credStore.fetchCredentials()
    await slaveStore.fetchSlaves()

    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    // Open Add Pipeline dialog
    const addBtn = wrapper.findAll('button').find((b) => b.text().includes('Thêm pipeline'))
    expect(addBtn).toBeDefined()
    await addBtn!.trigger('click')

    // Find all select elements in the dialog
    const selects = Array.from(document.body.querySelectorAll('select'))
    expect(selects.length).toBeGreaterThanOrEqual(3)

    // Git credential select should only have git_token options
    const gitSelect = selects.find((s) => {
      const options = Array.from(s.options)
      return options.some((o) => o.text.includes('gitlab-token'))
    })
    expect(gitSelect).toBeDefined()
    const gitOptions = Array.from(gitSelect!.options).filter((o) => o.value !== '')
    expect(gitOptions.every((o) => !o.text.includes('ssh-key') && !o.text.includes('registry'))).toBe(true)

    // Registry credential select should only have registry options
    const regSelect = selects.find((s) => {
      const options = Array.from(s.options)
      return options.some((o) => o.text.includes('registry-secret'))
    })
    expect(regSelect).toBeDefined()
    const regOptions = Array.from(regSelect!.options).filter((o) => o.value !== '')
    expect(regOptions.every((o) => !o.text.includes('ssh-key') && !o.text.includes('gitlab-token'))).toBe(true)
  })

  it('validates client-side inputs for gitUrl, imageName, deployNamespace (validate)', async () => {
    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    // Open Add Pipeline dialog
    const addBtn = wrapper.findAll('button').find((b) => b.text().includes('Thêm pipeline'))
    await addBtn!.trigger('click')

    // Find submit button in dialog
    const saveBtn = Array.from(document.body.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Tạo pipeline')
    )
    expect(saveBtn).toBeDefined()

    // Submit with empty inputs
    await saveBtn!.click()
    await wrapper.vm.$nextTick()

    // Assert validation error messages appear in DOM
    expect(document.body.textContent).toContain('Vui lòng nhập tên pipeline')
    expect(document.body.textContent).toContain('Vui lòng nhập địa chỉ Git URL')
    expect(document.body.textContent).toContain('Vui lòng nhập tên Image')
    expect(document.body.textContent).toContain('Vui lòng nhập Namespace')
  })

  it('submits valid pipeline input to API (submit gọi đúng input)', async () => {
    const store = useConfigPipelinesStore()
    const createSpy = vi.spyOn(mockApi, 'createPipeline')

    const inputData = {
      name: 'new-valid-pipeline',
      gitUrl: 'https://git.example.com/company/repo.git',
      gitBranch: 'develop',
      gitCredentialId: 2,
      slaveId: 1,
      dockerfilePath: 'deploy/Dockerfile',
      imageName: 'my-service',
      registryHost: 'registry.example.com',
      registryCredentialId: 3,
      deployNamespace: 'production',
      deployDeployment: 'my-service-deploy',
      deployContainer: 'my-service-app'
    }

    const created = await store.createPipeline(inputData)

    expect(createSpy).toHaveBeenCalledWith(inputData)
    expect(created.id).toBeDefined()
    expect(created.name).toBe('new-valid-pipeline')
    expect(store.pipelines.some((p) => p.name === 'new-valid-pipeline')).toBe(true)
  })

  it('triggers a run and redirects to run detail page (Chạy ngay chuyển trang)', async () => {
    const store = useConfigPipelinesStore()
    const pushSpy = vi.spyOn(router, 'push')

    // Create a pipeline without any active runs
    const targetPipeline = await store.createPipeline({
      name: 'idle-pipeline',
      gitUrl: 'https://git.example.com/company/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      dockerfilePath: 'Dockerfile',
      imageName: 'idle-service',
      registryHost: '192.168.1.241',
      registryCredentialId: 3,
      deployNamespace: 'default',
      deployDeployment: 'idle-service',
      deployContainer: 'app'
    })

    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    // Call handleTriggerRun in the view component
    const vm = wrapper.vm as any
    await vm.handleTriggerRun(targetPipeline)

    expect(pushSpy).toHaveBeenCalledWith(expect.stringMatching(/^\/runs\/\d+/))
  })

  it('handles 409 conflict when triggering run on a pipeline that is already running', async () => {
    const store = useConfigPipelinesStore()
    await store.fetchPipelines()

    // Pipeline id 1 has an active running run (run id 3) in MockShipitApi
    await expect(store.triggerRun(1)).rejects.toThrow()
    expect(store.error).toBe('Pipeline đang có lượt chạy')
  })

  it('displays 400 Problem.errors on matching fields (lỗi 400 hiện đúng ô)', async () => {
    const errorWithProblem = new ApiError(400, 'Dữ liệu không hợp lệ', {
      title: 'Validation failed',
      errors: {
        gitUrl: ['Git URL không thể truy cập'],
        imageName: ['Tên image đã tồn tại trên registry'],
        deployNamespace: ['Namespace chưa được cấp phép']
      }
    })

    vi.spyOn(mockApi, 'createPipeline').mockRejectedValueOnce(errorWithProblem)

    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    // Open dialog
    const addBtn = wrapper.findAll('button').find((b) => b.text().includes('Thêm pipeline'))
    await addBtn!.trigger('click')

    // Fill valid form inputs so client-side validation passes
    const vm = wrapper.vm as any
    vm.form.name = 'test-pipeline'
    vm.form.gitUrl = 'https://git.example.com/test.git'
    vm.form.gitBranch = 'main'
    vm.form.gitCredentialId = 2
    vm.form.slaveId = 1
    vm.form.dockerfilePath = 'Dockerfile'
    vm.form.imageName = 'valid-name'
    vm.form.registryHost = '192.168.1.241'
    vm.form.registryCredentialId = 3
    vm.form.deployNamespace = 'valid-ns'
    vm.form.deployDeployment = 'valid-deploy'
    vm.form.deployContainer = 'app'

    await vm.handleSave()
    await wrapper.vm.$nextTick()

    // Check that server errors are mapped to form errors
    expect(vm.formErrors.gitUrl).toBe('Git URL không thể truy cập')
    expect(vm.formErrors.imageName).toBe('Tên image đã tồn tại trên registry')
    expect(vm.formErrors.deployNamespace).toBe('Namespace chưa được cấp phép')
  })

  it('creates pipeline with "Không cần đăng nhập" sending registryCredentialId: null without required error', async () => {
    const credStore = useConfigCredentialsStore()
    const slaveStore = useConfigSlavesStore()
    await credStore.fetchCredentials()
    await slaveStore.fetchSlaves()

    const createSpy = vi.spyOn(mockApi, 'createPipeline')

    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    const addBtn = wrapper.findAll('button').find((b) => b.text().includes('Thêm pipeline'))
    await addBtn!.trigger('click')

    const vm = wrapper.vm as any
    vm.form.name = 'pipeline-no-auth'
    vm.form.gitUrl = 'https://git.example.com/repo.git'
    vm.form.gitBranch = 'main'
    vm.form.gitCredentialId = 2
    vm.form.slaveId = 1
    vm.form.dockerfilePath = 'Dockerfile'
    vm.form.imageName = 'my-image'
    vm.form.registryHost = 'registry.example.com'
    vm.form.registryCredentialId = '' // "Không cần đăng nhập"
    vm.form.deployNamespace = 'production'
    vm.form.deployDeployment = 'my-deploy'
    vm.form.deployContainer = 'app'

    await vm.handleSave()
    await wrapper.vm.$nextTick()

    expect(vm.formErrors.registryCredentialId).toBeUndefined()
    expect(createSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'pipeline-no-auth',
        registryCredentialId: null
      })
    )
  })

  it('creates pipeline selecting a registry credential sending valid credential id', async () => {
    const credStore = useConfigCredentialsStore()
    const slaveStore = useConfigSlavesStore()
    await credStore.fetchCredentials()
    await slaveStore.fetchSlaves()

    const createSpy = vi.spyOn(mockApi, 'createPipeline')

    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    const addBtn = wrapper.findAll('button').find((b) => b.text().includes('Thêm pipeline'))
    await addBtn!.trigger('click')

    const vm = wrapper.vm as any
    vm.form.name = 'pipeline-with-auth'
    vm.form.gitUrl = 'https://git.example.com/repo.git'
    vm.form.gitBranch = 'main'
    vm.form.gitCredentialId = 2
    vm.form.slaveId = 1
    vm.form.dockerfilePath = 'Dockerfile'
    vm.form.imageName = 'my-image'
    vm.form.registryHost = 'registry.example.com'
    vm.form.registryCredentialId = 3
    vm.form.deployNamespace = 'production'
    vm.form.deployDeployment = 'my-deploy'
    vm.form.deployContainer = 'app'

    await vm.handleSave()
    await wrapper.vm.$nextTick()

    expect(createSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'pipeline-with-auth',
        registryCredentialId: 3
      })
    )
  })

  it('edits pipeline with null registryCredentialId, displays "Không cần đăng nhập", switches to credential and back', async () => {
    const credStore = useConfigCredentialsStore()
    const slaveStore = useConfigSlavesStore()
    await credStore.fetchCredentials()
    await slaveStore.fetchSlaves()

    const updateSpy = vi.spyOn(mockApi, 'updatePipeline')

    const existingPipeline: Pipeline = {
      id: 99,
      name: 'edit-test-pipeline',
      gitUrl: 'https://git.example.com/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      dockerfilePath: 'Dockerfile',
      buildContext: null,
      imageName: 'test-app',
      registryHost: 'registry.example.com',
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'test-deploy',
      deployContainer: 'app',
      lastRun: null
    }

    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    const vm = wrapper.vm as any
    vm.openEditDialog(existingPipeline)
    await wrapper.vm.$nextTick()

    // Select should show "Không cần đăng nhập" (represented by empty string in select model)
    expect(vm.form.registryCredentialId).toBe('')

    // Find the registry credential select element
    const selects = Array.from(document.body.querySelectorAll('select'))
    const regSelect = selects.find((s) => {
      const options = Array.from(s.options)
      return options.some((o) => o.text.includes('Không cần đăng nhập'))
    })
    expect(regSelect).toBeDefined()
    expect(regSelect!.value).toBe('')

    // Change to credential id 3 and save
    vm.form.registryCredentialId = 3
    await vm.handleSave()

    expect(updateSpy).toHaveBeenCalledWith(
      99,
      expect.objectContaining({
        registryCredentialId: 3
      })
    )

    // Reopen with credential id 3 and change back to "Không cần đăng nhập"
    vm.openEditDialog({ ...existingPipeline, registryCredentialId: 3 })
    await wrapper.vm.$nextTick()
    expect(vm.form.registryCredentialId).toBe(3)

    // Switch back to "Không cần đăng nhập" (value '')
    vm.form.registryCredentialId = ''
    await vm.handleSave()

    expect(updateSpy).toHaveBeenCalledWith(
      99,
      expect.objectContaining({
        registryCredentialId: null
      })
    )
  })

  it('defaults to "Không cần đăng nhập" when no registry credentials exist', async () => {
    const credStore = useConfigCredentialsStore()
    credStore.credentials = credStore.credentials.filter((c) => c.kind !== 'registry')

    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    const vm = wrapper.vm as any
    vm.openAddDialog()
    await wrapper.vm.$nextTick()

    expect(vm.form.registryCredentialId).toBe('')
  })

  it('helper getRegistryCredentialName handles null correctly', () => {
    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })
    const vm = wrapper.vm as any
    expect(vm.getRegistryCredentialName(null)).toBe('Không cần đăng nhập')
    expect(vm.getRegistryCredentialName(undefined)).toBe('Không cần đăng nhập')
  })

  it('creates pipeline leaving buildContext empty sends payload with buildContext: null', async () => {
    const credStore = useConfigCredentialsStore()
    const slaveStore = useConfigSlavesStore()
    await credStore.fetchCredentials()
    await slaveStore.fetchSlaves()

    const createSpy = vi.spyOn(mockApi, 'createPipeline')

    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    const addBtn = wrapper.findAll('button').find((b) => b.text().includes('Thêm pipeline'))
    await addBtn!.trigger('click')

    const vm = wrapper.vm as any
    vm.form.name = 'pipeline-empty-context'
    vm.form.gitUrl = 'https://git.example.com/repo.git'
    vm.form.gitBranch = 'main'
    vm.form.gitCredentialId = 2
    vm.form.slaveId = 1
    vm.form.dockerfilePath = 'Dockerfile'
    vm.form.buildContext = ''
    vm.form.imageName = 'empty-ctx-app'
    vm.form.registryHost = 'registry.example.com'
    vm.form.registryCredentialId = null
    vm.form.deployNamespace = 'default'
    vm.form.deployDeployment = 'empty-ctx-deploy'
    vm.form.deployContainer = 'app'

    await vm.handleSave()
    await wrapper.vm.$nextTick()

    expect(createSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'pipeline-empty-context',
        buildContext: null
      })
    )
  })

  it('creates pipeline with buildContext "." and "docker" sends proper payloads', async () => {
    const credStore = useConfigCredentialsStore()
    const slaveStore = useConfigSlavesStore()
    await credStore.fetchCredentials()
    await slaveStore.fetchSlaves()

    const createSpy = vi.spyOn(mockApi, 'createPipeline')

    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    const addBtn = wrapper.findAll('button').find((b) => b.text().includes('Thêm pipeline'))
    await addBtn!.trigger('click')

    const vm = wrapper.vm as any
    vm.form.name = 'pipeline-dot'
    vm.form.gitUrl = 'https://git.example.com/repo.git'
    vm.form.gitBranch = 'main'
    vm.form.gitCredentialId = 2
    vm.form.slaveId = 1
    vm.form.dockerfilePath = 'Dockerfile'
    vm.form.buildContext = '.'
    vm.form.imageName = 'dot-app'
    vm.form.registryHost = 'registry.example.com'
    vm.form.registryCredentialId = null
    vm.form.deployNamespace = 'default'
    vm.form.deployDeployment = 'dot-deploy'
    vm.form.deployContainer = 'app'

    await vm.handleSave()
    await wrapper.vm.$nextTick()

    expect(createSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'pipeline-dot',
        buildContext: '.'
      })
    )

    // Now test "docker"
    vm.openAddDialog()
    await wrapper.vm.$nextTick()

    vm.form.name = 'pipeline-docker-ctx'
    vm.form.gitUrl = 'https://git.example.com/repo.git'
    vm.form.gitBranch = 'main'
    vm.form.gitCredentialId = 2
    vm.form.slaveId = 1
    vm.form.dockerfilePath = 'Dockerfile'
    vm.form.buildContext = 'docker'
    vm.form.imageName = 'docker-app'
    vm.form.registryHost = 'registry.example.com'
    vm.form.registryCredentialId = null
    vm.form.deployNamespace = 'default'
    vm.form.deployDeployment = 'docker-deploy'
    vm.form.deployContainer = 'app'

    await vm.handleSave()
    await wrapper.vm.$nextTick()

    expect(createSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'pipeline-docker-ctx',
        buildContext: 'docker'
      })
    )
  })

  it('displays error and does not call API when buildContext is invalid (../x, /abs, a b)', async () => {
    const credStore = useConfigCredentialsStore()
    const slaveStore = useConfigSlavesStore()
    await credStore.fetchCredentials()
    await slaveStore.fetchSlaves()

    const createSpy = vi.spyOn(mockApi, 'createPipeline')

    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    const addBtn = wrapper.findAll('button').find((b) => b.text().includes('Thêm pipeline'))
    await addBtn!.trigger('click')

    const vm = wrapper.vm as any
    vm.form.name = 'pipeline-test'
    vm.form.gitUrl = 'https://git.example.com/repo.git'
    vm.form.gitBranch = 'main'
    vm.form.gitCredentialId = 2
    vm.form.slaveId = 1
    vm.form.dockerfilePath = 'Dockerfile'
    vm.form.imageName = 'test-app'
    vm.form.registryHost = 'registry.example.com'
    vm.form.registryCredentialId = null
    vm.form.deployNamespace = 'default'
    vm.form.deployDeployment = 'test-deploy'
    vm.form.deployContainer = 'app'

    const invalidCases = ['../x', '/abs', 'a b']
    for (const invalidVal of invalidCases) {
      createSpy.mockClear()
      vm.form.buildContext = invalidVal

      await vm.handleSave()
      await wrapper.vm.$nextTick()

      // API was NOT called
      expect(createSpy).not.toHaveBeenCalled()
      // Error message exists on formErrors.buildContext and in DOM
      expect(vm.formErrors.buildContext).toBeDefined()
      expect(vm.formErrors.buildContext.length).toBeGreaterThan(0)
      expect(document.body.textContent).toContain(vm.formErrors.buildContext)
    }
  })

  it('edits pipeline with buildContext ".", displays ".", and clearing it saves as null', async () => {
    const credStore = useConfigCredentialsStore()
    const slaveStore = useConfigSlavesStore()
    await credStore.fetchCredentials()
    await slaveStore.fetchSlaves()

    const updateSpy = vi.spyOn(mockApi, 'updatePipeline')

    const existingPipeline: Pipeline = {
      id: 88,
      name: 'edit-context-pipeline',
      gitUrl: 'https://git.example.com/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      dockerfilePath: 'Dockerfile',
      buildContext: '.',
      imageName: 'test-app',
      registryHost: 'registry.example.com',
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'test-deploy',
      deployContainer: 'app',
      lastRun: null
    }

    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    const vm = wrapper.vm as any
    vm.openEditDialog(existingPipeline)
    await wrapper.vm.$nextTick()

    // Form state has "."
    expect(vm.form.buildContext).toBe('.')

    // DOM input with placeholder ". (gốc repo)" has value "."
    const contextInput = document.body.querySelector('input[placeholder=". (gốc repo)"]') as HTMLInputElement
    expect(contextInput).toBeDefined()
    expect(contextInput.value).toBe('.')

    // Clear input and save
    vm.form.buildContext = ''
    await vm.handleSave()
    await wrapper.vm.$nextTick()

    expect(updateSpy).toHaveBeenCalledWith(
      88,
      expect.objectContaining({
        buildContext: null
      })
    )
  })

  it('renders "Thư mục context build" field with description and placeholder', async () => {
    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    const vm = wrapper.vm as any
    vm.openAddDialog()
    await wrapper.vm.$nextTick()

    const input = document.body.querySelector('input[placeholder=". (gốc repo)"]')
    expect(input).toBeDefined()

    expect(document.body.textContent).toContain('Thư mục context build')
    expect(document.body.textContent).toContain('Tuỳ chọn. Đường dẫn trong repo dùng làm context khi build.')
  })
})
