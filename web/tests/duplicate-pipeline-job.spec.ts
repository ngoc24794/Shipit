import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount as baseMount, flushPromises, type VueWrapper } from '@vue/test-utils'
import PipelinesView from '../src/views/config/PipelinesView.vue'
import PipelineFormView from '../src/views/config/PipelineFormView.vue'
import JobsView from '../src/views/jobs/JobsView.vue'
import JobFormView from '../src/views/jobs/JobFormView.vue'
import { useConfigPipelinesStore } from '../src/stores/configPipelines'
import { useJobsStore } from '../src/stores/jobs'
import { useAuthStore } from '../src/stores/auth'
import { useToast } from '../src/composables/useToast'
import { MockShipitApi } from '../src/api/mock'
import { setApi } from '../src/api'
import { router } from '../src/router'

describe('Duplicate Pipeline and Job feature', () => {
  let mockApi: MockShipitApi
  let pinia: ReturnType<typeof createPinia>
  const mountedWrappers: VueWrapper[] = []

  const mount: typeof baseMount = ((component: any, options?: any) => {
    const w = baseMount(component, options)
    mountedWrappers.push(w)
    return w
  }) as typeof baseMount

  beforeEach(async () => {
    document.body.innerHTML = ''
    pinia = createPinia()
    setActivePinia(pinia)
    mockApi = new MockShipitApi()
    setApi(mockApi)

    const authStore = useAuthStore()
    authStore.user = { username: 'admin' }
    authStore.initialized = true

    const toast = useToast()
    toast.toasts.value = []
  })

  afterEach(() => {
    while (mountedWrappers.length > 0) {
      mountedWrappers.pop()?.unmount()
    }
    vi.restoreAllMocks()
  })

  describe('Pipelines duplication', () => {
    it('navigates from PipelinesView to /pipelines/new?duplicateFrom=<id> when clicking Duplicate', async () => {
      await router.push('/pipelines')
      await router.isReady()
      const pushSpy = vi.spyOn(router, 'push')

      const wrapper = mount(PipelinesView, {
        global: { plugins: [pinia, router] }
      })

      await new Promise((resolve) => setTimeout(resolve, 80))
      await flushPromises()

      const dupBtn = wrapper.find('button[aria-label="Nhân bản pipeline"]')
      expect(dupBtn.exists()).toBe(true)
      await dupBtn.trigger('click')
      expect(pushSpy).toHaveBeenCalledWith('/pipelines/new?duplicateFrom=1')
    })

    it('pre-fills PipelineFormView from source pipeline (fetching when store is empty) and creates a new pipeline on submit', async () => {
      const source = await mockApi.createPipeline({
        name: 'billing-service',
        gitUrl: 'https://git.example.com/company/billing.git',
        gitBranch: 'release',
        gitCredentialId: 2,
        slaveId: 2,
        dockerfilePath: 'docker/Dockerfile.billing',
        buildContext: 'services/billing',
        imageName: 'billing-svc',
        registryHost: '192.168.1.241:5000',
        deployRegistryHost: 'registry.local:5000',
        registryCredentialId: 3,
        deployNamespace: 'devops',
        deployDeployment: 'shipit-demo',
        deployContainer: 'billing'
      })

      const createSpy = vi.spyOn(mockApi, 'createPipeline')
      const updateSpy = vi.spyOn(mockApi, 'updatePipeline')

      // Direct navigation with empty Pinia store
      await router.push(`/pipelines/new?duplicateFrom=${source.id}`)
      await router.isReady()

      const wrapper = mount(PipelineFormView, {
        global: { plugins: [pinia, router] }
      })

      await new Promise((resolve) => setTimeout(resolve, 120))
      await flushPromises()

      const banner = wrapper.find('[data-testid="duplicate-banner"]')
      expect(banner.exists()).toBe(true)
      expect(banner.text()).toContain('billing-service')
      expect(wrapper.text()).toContain('Nhân bản pipeline')

      const vm = wrapper.vm as any
      expect(vm.form.name).toBe('billing-service-copy')
      expect(vm.form.gitUrl).toBe('https://git.example.com/company/billing.git')
      expect(vm.form.gitBranch).toBe('release')
      expect(vm.form.gitCredentialId).toBe(2)
      expect(vm.form.slaveId).toBe(2)
      expect(vm.form.dockerfilePath).toBe('docker/Dockerfile.billing')
      expect(vm.form.buildContext).toBe('services/billing')
      expect(vm.form.imageName).toBe('billing-svc')
      expect(vm.form.registryHost).toBe('192.168.1.241:5000')
      expect(vm.form.deployRegistryHost).toBe('registry.local:5000')
      expect(vm.form.registryCredentialId).toBe(3)
      expect(vm.form.deployNamespace).toBe('devops')
      expect(vm.form.deployDeployment).toBe('shipit-demo')
      expect(vm.form.deployContainer).toBe('billing')

      await vm.handleSave()
      await flushPromises()

      expect(createSpy).toHaveBeenCalledTimes(1)
      expect(updateSpy).not.toHaveBeenCalled()
      expect(createSpy).toHaveBeenCalledWith(
        expect.objectContaining({
          name: 'billing-service-copy',
          gitUrl: 'https://git.example.com/company/billing.git',
          gitBranch: 'release',
          buildContext: 'services/billing',
          deployRegistryHost: 'registry.local:5000'
        })
      )

      // Original pipeline remains unmodified
      const store = useConfigPipelinesStore()
      const originalAfter = store.pipelines.find((p) => p.id === source.id)
      expect(originalAfter?.name).toBe('billing-service')
    })

    it('handles invalid and missing duplicateFrom IDs gracefully in PipelineFormView', async () => {
      const toast = useToast()

      // Non-numeric ID
      await router.push('/pipelines/new?duplicateFrom=abc')
      await router.isReady()

      const invalidWrapper = mount(PipelineFormView, {
        global: { plugins: [pinia, router] }
      })

      await new Promise((resolve) => setTimeout(resolve, 100))
      await flushPromises()

      expect(invalidWrapper.find('[data-testid="duplicate-banner"]').exists()).toBe(false)
      expect(invalidWrapper.text()).toContain('Thêm pipeline mới')
      expect(invalidWrapper.text()).not.toContain('Không tìm thấy pipeline')
      expect(toast.toasts.value.some((t) => t.type === 'error')).toBe(true)

      // Unknown ID
      toast.toasts.value = []
      await router.push('/pipelines/new?duplicateFrom=99999')
      await router.isReady()

      const missingWrapper = mount(PipelineFormView, {
        global: { plugins: [pinia, router] }
      })

      await new Promise((resolve) => setTimeout(resolve, 100))
      await flushPromises()

      expect(missingWrapper.find('[data-testid="duplicate-banner"]').exists()).toBe(false)
      expect(missingWrapper.text()).toContain('Thêm pipeline mới')
      expect(missingWrapper.text()).not.toContain('Không tìm thấy pipeline')
      expect(toast.toasts.value.some((t) => t.type === 'error')).toBe(true)
    })
  })

  describe('Jobs duplication', () => {
    it('navigates from JobsView to /jobs/new?duplicateFrom=<id> when clicking Duplicate', async () => {
      await router.push('/jobs')
      await router.isReady()
      const pushSpy = vi.spyOn(router, 'push')

      const wrapper = mount(JobsView, {
        global: { plugins: [pinia, router] }
      })

      await new Promise((resolve) => setTimeout(resolve, 80))
      await flushPromises()

      const dupBtn = wrapper.find('button[aria-label="Nhân bản Job"]')
      expect(dupBtn.exists()).toBe(true)
      await dupBtn.trigger('click')
      expect(pushSpy).toHaveBeenCalledWith('/jobs/new?duplicateFrom=1')
    })

    it('pre-fills JobFormView by fetching source job when store is empty and creates a new job on submit', async () => {
      const getJobSpy = vi.spyOn(mockApi, 'getJob')
      const createSpy = vi.spyOn(mockApi, 'createJob')
      const updateSpy = vi.spyOn(mockApi, 'updateJob')

      await router.push('/jobs/new?duplicateFrom=1')
      await router.isReady()

      const wrapper = mount(JobFormView, {
        global: { plugins: [pinia, router] }
      })

      await new Promise((resolve) => setTimeout(resolve, 120))
      await flushPromises()

      expect(getJobSpy).toHaveBeenCalledWith(1)

      const banner = wrapper.find('[data-testid="duplicate-banner"]')
      expect(banner.exists()).toBe(true)
      expect(banner.text()).toContain('avina-dbmigrator')
      expect(wrapper.text()).toContain('Nhân bản Job')

      const vm = wrapper.vm as any
      expect(vm.form.name).toBe('avina-dbmigrator-copy')
      expect(vm.form.gitUrl).toBe('http://git.example/AvinaWorkspace')
      expect(vm.form.namespace).toBe('avina-workspace')
      expect(vm.form.tasks.length).toBe(2)
      expect(vm.form.tasks[0].label).toBe('files')
      expect(vm.form.tasks[1].label).toBe('users')

      await vm.handleSave()
      await flushPromises()

      expect(createSpy).toHaveBeenCalledTimes(1)
      expect(updateSpy).not.toHaveBeenCalled()
      expect(createSpy).toHaveBeenCalledWith(
        expect.objectContaining({
          name: 'avina-dbmigrator-copy',
          namespace: 'avina-workspace'
        })
      )
    })

    it('deep-clones tasks, args, env, and imagePullSecrets without mutating the source job in store', async () => {
      const jobsStore = useJobsStore()
      await jobsStore.fetchJobs()

      const sourceJob = jobsStore.jobs.find((j) => j.id === 1)!
      expect(sourceJob).toBeDefined()
      const originalSnapshot = JSON.stringify(sourceJob)

      await router.push('/jobs/new?duplicateFrom=1')
      await router.isReady()

      const wrapper = mount(JobFormView, {
        global: { plugins: [pinia, router] }
      })

      await new Promise((resolve) => setTimeout(resolve, 100))
      await flushPromises()

      const vm = wrapper.vm as any
      // Mutate cloned arrays and nested objects
      vm.form.imagePullSecrets.push('extra-pull-secret')
      vm.form.tasks[0].label = 'mutated-task-label'
      vm.form.tasks[0].args.push('--extra-arg')
      vm.form.tasks[0].envFromConfigMaps.push('extra-cm')
      vm.form.tasks[0].envFromSecrets.push('extra-secret')
      if (vm.form.tasks[0].env.length > 0) {
        vm.form.tasks[0].env[0].value = 'mutated-value'
      } else {
        vm.form.tasks[0].env.push({ name: 'NEW_ENV', value: '123', secretName: null, secretKey: null })
      }

      // Source job in store must remain completely unchanged
      expect(JSON.stringify(sourceJob)).toBe(originalSnapshot)
    })

    it('truncates long source job names to satisfy DNS-1123 <= 40 char validation', async () => {
      const longJob = await mockApi.createJob({
        name: 'very-long-job-name-that-is-38-chars-01',
        gitUrl: 'http://git.example/AvinaWorkspace',
        gitBranch: 'main',
        gitCredentialId: 2,
        slaveId: 1,
        dockerfilePath: 'Dockerfile',
        buildContext: null,
        imageName: 'test-img',
        registryHost: '192.168.1.241',
        deployRegistryHost: null,
        registryCredentialId: null,
        namespace: 'avina-workspace',
        containerName: 'app',
        serviceAccount: null,
        imagePullSecrets: [],
        tasks: [
          {
            label: 'task-1',
            args: [],
            envFromConfigMaps: [],
            envFromSecrets: [],
            env: [],
            timeoutSeconds: 1800,
            backoffLimit: 0,
            ttlSeconds: 3600
          }
        ]
      })

      await router.push(`/jobs/new?duplicateFrom=${longJob.id}`)
      await router.isReady()

      const wrapper = mount(JobFormView, {
        global: { plugins: [pinia, router] }
      })

      await new Promise((resolve) => setTimeout(resolve, 100))
      await flushPromises()

      const vm = wrapper.vm as any
      expect(vm.form.name.length).toBeLessThanOrEqual(40)
      expect(vm.form.name).toMatch(/^[a-z0-9]([-a-z0-9]*[a-z0-9])?$/)
      expect(vm.form.name.endsWith('-copy')).toBe(true)
      expect(vm.form.name).not.toBe(longJob.name)
    })

    it('handles invalid and missing duplicateFrom IDs gracefully in JobFormView', async () => {
      const toast = useToast()

      // Invalid non-numeric ID
      await router.push('/jobs/new?duplicateFrom=invalid')
      await router.isReady()

      const invalidWrapper = mount(JobFormView, {
        global: { plugins: [pinia, router] }
      })

      await new Promise((resolve) => setTimeout(resolve, 100))
      await flushPromises()

      expect(invalidWrapper.find('[data-testid="duplicate-banner"]').exists()).toBe(false)
      expect(invalidWrapper.text()).toContain('Tạo Job mới')
      expect(toast.toasts.value.some((t) => t.type === 'error')).toBe(true)

      // Unknown ID
      toast.toasts.value = []
      await router.push('/jobs/new?duplicateFrom=99999')
      await router.isReady()

      const missingWrapper = mount(JobFormView, {
        global: { plugins: [pinia, router] }
      })

      await new Promise((resolve) => setTimeout(resolve, 100))
      await flushPromises()

      expect(missingWrapper.find('[data-testid="duplicate-banner"]').exists()).toBe(false)
      expect(missingWrapper.text()).toContain('Tạo Job mới')
      expect(toast.toasts.value.some((t) => t.type === 'error')).toBe(true)
    })
  })
})
