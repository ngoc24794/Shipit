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

describe('Pipelines View & Store', () => {
  let mockApi: MockShipitApi

  beforeEach(() => {
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
})
