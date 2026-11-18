import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount, flushPromises } from '@vue/test-utils'
import PipelinesView from '../../src/views/config/PipelinesView.vue'
import PipelineFormView from '../../src/views/config/PipelineFormView.vue'
import { useConfigPipelinesStore } from '../../src/stores/configPipelines'
import { useConfigCredentialsStore } from '../../src/stores/configCredentials'
import { useConfigSlavesStore } from '../../src/stores/configSlaves'
import { useAuthStore } from '../../src/stores/auth'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'
import { ApiError } from '../../src/api/client'

describe('Pipelines View, Form & Store', () => {
  let mockApi: MockShipitApi
  let pinia: ReturnType<typeof createPinia>

  beforeEach(async () => {
    document.body.innerHTML = ''
    pinia = createPinia()
    setActivePinia(pinia)
    mockApi = new MockShipitApi()
    setApi(mockApi)

    const authStore = useAuthStore()
    authStore.user = { username: 'admin' }
    authStore.initialized = true
  })

  it('navigates from PipelinesView to /pipelines/new and /pipelines/:id/edit', async () => {
    await router.push('/pipelines')
    await router.isReady()
    const pushSpy = vi.spyOn(router, 'push')

    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const addBtn = wrapper.findAll('button').find((b) => b.text().includes('Thêm pipeline'))
    expect(addBtn).toBeDefined()
    await addBtn!.trigger('click')
    expect(pushSpy).toHaveBeenCalledWith('/pipelines/new')

    const editBtn = wrapper.find('button[aria-label="Sửa pipeline"]')
    expect(editBtn.exists()).toBe(true)
    await editBtn.trigger('click')
    expect(pushSpy).toHaveBeenCalledWith('/pipelines/1/edit')
  })

  it('loads pipeline by :id on edit page and shows not-found for unknown id', async () => {
    await router.push('/pipelines/1/edit')
    await router.isReady()

    const wrapper = mount(PipelineFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    expect(wrapper.text()).toContain('Sửa pipeline')
    const nameInput = wrapper.find('input[placeholder="Ví dụ: shipit-demo-pipeline"]')
    expect((nameInput.element as HTMLInputElement).value.length).toBeGreaterThan(0)

    await router.push('/pipelines/99999/edit')
    await router.isReady()

    const notFoundWrapper = mount(PipelineFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    expect(notFoundWrapper.text()).toContain('Không tìm thấy pipeline')
  })

  it('warns on unsaved changes when leaving PipelineFormView', async () => {
    await router.push('/pipelines/new')
    await router.isReady()
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValueOnce(false)

    const wrapper = mount(PipelineFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const nameInput = wrapper.find('input[placeholder="Ví dụ: shipit-demo-pipeline"]')
    await nameInput.setValue('unsaved-pipeline')

    await router.push('/pipelines')
    expect(confirmSpy).toHaveBeenCalled()
    expect(router.currentRoute.value.path).toBe('/pipelines/new')
  })

  it('filters credentials correctly by kind in form selects (select lọc đúng loại credential)', async () => {
    await router.push('/pipelines/new')
    await router.isReady()

    const wrapper = mount(PipelineFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const selects = wrapper.findAll('select').map((s) => s.element as HTMLSelectElement)
    expect(selects.length).toBeGreaterThanOrEqual(3)

    const gitSelect = selects.find((s) => Array.from(s.options).some((o) => o.text.includes('gitlab-token')))
    expect(gitSelect).toBeDefined()
    const gitOptions = Array.from(gitSelect!.options).filter((o) => o.value !== '')
    expect(gitOptions.every((o) => !o.text.includes('ssh-key') && !o.text.includes('registry'))).toBe(true)

    const regSelect = selects.find((s) => Array.from(s.options).some((o) => o.text.includes('registry-secret')))
    expect(regSelect).toBeDefined()
    const regOptions = Array.from(regSelect!.options).filter((o) => o.value !== '')
    expect(regOptions.every((o) => !o.text.includes('ssh-key') && !o.text.includes('gitlab-token'))).toBe(true)
  })

  it('validates client-side inputs for gitUrl, imageName, deployNamespace (validate)', async () => {
    await router.push('/pipelines/new')
    await router.isReady()

    const wrapper = mount(PipelineFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const saveBtn = wrapper.findAll('button').find((b) => b.text().includes('Tạo pipeline'))
    expect(saveBtn).toBeDefined()

    await saveBtn!.trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Vui lòng nhập tên pipeline')
    expect(wrapper.text()).toContain('Vui lòng nhập địa chỉ Git URL')
    expect(wrapper.text()).toContain('Vui lòng nhập tên Image')
    expect(wrapper.text()).toContain('Vui lòng nhập Namespace')
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
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))

    const vm = wrapper.vm as any
    await vm.handleTriggerRun(targetPipeline)

    expect(pushSpy).toHaveBeenCalledWith(expect.stringMatching(/^\/runs\/\d+/))
  })

  it('handles 409 conflict when triggering run on a pipeline that is already running', async () => {
    const store = useConfigPipelinesStore()
    await store.fetchPipelines()

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

    await router.push('/pipelines/new')
    await router.isReady()

    const wrapper = mount(PipelineFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

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

    expect(vm.formErrors.gitUrl).toBe('Git URL không thể truy cập')
    expect(vm.formErrors.imageName).toBe('Tên image đã tồn tại trên registry')
    expect(vm.formErrors.deployNamespace).toBe('Namespace chưa được cấp phép')
  })

  it('creates pipeline with "Không cần đăng nhập" sending registryCredentialId: null without required error', async () => {
    const createSpy = vi.spyOn(mockApi, 'createPipeline')

    await router.push('/pipelines/new')
    await router.isReady()

    const wrapper = mount(PipelineFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const vm = wrapper.vm as any
    vm.form.name = 'pipeline-no-auth'
    vm.form.gitUrl = 'https://git.example.com/repo.git'
    vm.form.gitBranch = 'main'
    vm.form.gitCredentialId = 2
    vm.form.slaveId = 1
    vm.form.dockerfilePath = 'Dockerfile'
    vm.form.imageName = 'my-image'
    vm.form.registryHost = 'registry.example.com'
    vm.form.registryCredentialId = ''
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

  it('edits pipeline with null registryCredentialId, displays "Không cần đăng nhập", switches to credential and back', async () => {
    const existing = await mockApi.createPipeline({
      name: 'edit-test-pipeline',
      gitUrl: 'https://git.example.com/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      dockerfilePath: 'Dockerfile',
      buildContext: null,
      imageName: 'test-app',
      registryHost: 'registry.example.com',
      deployRegistryHost: null,
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'test-deploy',
      deployContainer: 'app'
    })

    const updateSpy = vi.spyOn(mockApi, 'updatePipeline')

    await router.push(`/pipelines/${existing.id}/edit`)
    await router.isReady()

    const wrapper = mount(PipelineFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const vm = wrapper.vm as any
    expect(vm.form.registryCredentialId).toBe('')

    vm.form.registryCredentialId = 3
    await vm.handleSave()

    expect(updateSpy).toHaveBeenCalledWith(
      existing.id,
      expect.objectContaining({
        registryCredentialId: 3
      })
    )
  })

  it('defaults to "Không cần đăng nhập" when no registry credentials exist', async () => {
    const credStore = useConfigCredentialsStore()
    await credStore.fetchCredentials()
    vi.spyOn(mockApi, 'getCredentials').mockResolvedValueOnce(
      credStore.credentials.filter((c) => c.kind !== 'registry')
    )

    await router.push('/pipelines/new')
    await router.isReady()

    const wrapper = mount(PipelineFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const vm = wrapper.vm as any
    expect(vm.form.registryCredentialId).toBe('')
  })

  it('creates pipeline with buildContext "." and validates invalid buildContext (../x, /abs, a b)', async () => {
    const createSpy = vi.spyOn(mockApi, 'createPipeline')

    await router.push('/pipelines/new')
    await router.isReady()

    const wrapper = mount(PipelineFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const vm = wrapper.vm as any
    vm.form.name = 'pipeline-dot'
    vm.form.gitUrl = 'https://git.example.com/repo.git'
    vm.form.gitBranch = 'main'
    vm.form.gitCredentialId = 2
    vm.form.slaveId = 1
    vm.form.dockerfilePath = 'Dockerfile'
    vm.form.imageName = 'dot-app'
    vm.form.registryHost = 'registry.example.com'
    vm.form.registryCredentialId = null
    vm.form.deployNamespace = 'default'
    vm.form.deployDeployment = 'dot-deploy'
    vm.form.deployContainer = 'app'

    for (const invalidVal of ['../x', '/abs', 'a b']) {
      createSpy.mockClear()
      vm.form.buildContext = invalidVal
      await vm.handleSave()
      await wrapper.vm.$nextTick()
      expect(createSpy).not.toHaveBeenCalled()
      expect(vm.formErrors.buildContext).toBeDefined()
    }

    vm.form.buildContext = '.'
    await vm.handleSave()
    await wrapper.vm.$nextTick()

    expect(createSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'pipeline-dot',
        buildContext: '.'
      })
    )
  })

  it('validates invalid deployRegistryHost and sends null when empty', async () => {
    const createSpy = vi.spyOn(mockApi, 'createPipeline')

    await router.push('/pipelines/new')
    await router.isReady()

    const wrapper = mount(PipelineFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const vm = wrapper.vm as any
    vm.form.name = 'pipeline-deploy-host'
    vm.form.gitUrl = 'https://git.example.com/repo.git'
    vm.form.gitBranch = 'main'
    vm.form.gitCredentialId = 2
    vm.form.slaveId = 1
    vm.form.dockerfilePath = 'Dockerfile'
    vm.form.imageName = 'my-image'
    vm.form.registryHost = '192.168.1.241'
    vm.form.registryCredentialId = null
    vm.form.deployNamespace = 'default'
    vm.form.deployDeployment = 'my-deploy'
    vm.form.deployContainer = 'app'

    for (const invalidVal of ['http://x', 'a b', 'x;y']) {
      createSpy.mockClear()
      vm.form.deployRegistryHost = invalidVal
      await vm.handleSave()
      await wrapper.vm.$nextTick()
      expect(createSpy).not.toHaveBeenCalled()
      expect(vm.formErrors.deployRegistryHost).toBeDefined()
    }

    vm.form.deployRegistryHost = ''
    await vm.handleSave()
    await wrapper.vm.$nextTick()

    expect(createSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'pipeline-deploy-host',
        deployRegistryHost: null
      })
    )
  })
})
