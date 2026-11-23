import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { nextTick } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import { mount as baseMount, flushPromises, type VueWrapper } from '@vue/test-utils'
import PipelineFormView from '../src/views/config/PipelineFormView.vue'
import JobFormView from '../src/views/jobs/JobFormView.vue'
import { useAuthStore } from '../src/stores/auth'
import { useToast } from '../src/composables/useToast'
import { MockShipitApi } from '../src/api/mock'
import { setApi } from '../src/api'
import { router } from '../src/router'

describe('Quick-fill auto-sync in PipelineFormView and JobFormView', () => {
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

  async function mountAt(component: any, path: string) {
    await router.push(path)
    await router.isReady()
    const wrapper = mount(component, {
      global: { plugins: [pinia, router] }
    })
    await new Promise((resolve) => setTimeout(resolve, 120))
    await flushPromises()
    return wrapper
  }

  describe('PipelineFormView', () => {
    it('auto-fills imageName and deployDeployment when typing into name in create mode', async () => {
      const wrapper = await mountAt(PipelineFormView, '/pipelines/new')
      const vm = wrapper.vm as any

      expect(vm.isDirty).toBe(false)

      const nameInput = wrapper.find('input[placeholder="Ví dụ: shipit-demo-pipeline"]')
      expect(nameInput.exists()).toBe(true)
      await nameInput.setValue('billing-api')

      expect(vm.form.name).toBe('billing-api')
      expect(vm.form.imageName).toBe('billing-api')
      expect(vm.form.deployDeployment).toBe('billing-api')
      expect(vm.isDirty).toBe(true)

      // Continuing to edit name keeps all 3 synced
      await nameInput.setValue('billing-api-v2')
      expect(vm.form.imageName).toBe('billing-api-v2')
      expect(vm.form.deployDeployment).toBe('billing-api-v2')

      // Clearing name clears all 3 and returns isDirty to false
      await nameInput.setValue('')
      expect(vm.form.imageName).toBe('')
      expect(vm.form.deployDeployment).toBe('')
      expect(vm.isDirty).toBe(false)
    })

    it('auto-fills the other 2 fields when starting with imageName or deployDeployment', async () => {
      // Start with imageName (Step 3)
      const w1 = await mountAt(PipelineFormView, '/pipelines/new?step=3')
      const vm1 = w1.vm as any
      const imageInput = w1.find('input[placeholder="Ví dụ: shipit-demo"]')
      expect(imageInput.exists()).toBe(true)
      await imageInput.setValue('orders-svc')

      expect(vm1.form.name).toBe('orders-svc')
      expect(vm1.form.imageName).toBe('orders-svc')
      expect(vm1.form.deployDeployment).toBe('orders-svc')

      // Start with deployDeployment (Step 4)
      const w2 = await mountAt(PipelineFormView, '/pipelines/new?step=4')
      const vm2 = w2.vm as any
      const deployInput = w2.find('input[placeholder="shipit-demo"]')
      expect(deployInput.exists()).toBe(true)
      await deployInput.setValue('payments-svc')

      expect(vm2.form.name).toBe('payments-svc')
      expect(vm2.form.imageName).toBe('payments-svc')
      expect(vm2.form.deployDeployment).toBe('payments-svc')
    })

    it('unlinks when editing a second field and re-arms when all 3 are cleared', async () => {
      const wrapper = await mountAt(PipelineFormView, '/pipelines/new')
      const vm = wrapper.vm as any

      vm.form.name = 'analytics'
      expect(vm.form.imageName).toBe('analytics')
      expect(vm.form.deployDeployment).toBe('analytics')

      // Diverge on imageName
      vm.form.imageName = 'analytics-worker'
      expect(vm.form.name).toBe('analytics')
      expect(vm.form.deployDeployment).toBe('analytics')

      // Editing name no longer overwrites the other two
      vm.form.name = 'analytics-prod'
      expect(vm.form.imageName).toBe('analytics-worker')
      expect(vm.form.deployDeployment).toBe('analytics')

      // Clear all 3 to re-arm
      vm.form.name = ''
      vm.form.imageName = ''
      vm.form.deployDeployment = ''

      // Typing into deployDeployment now syncs all 3 again
      vm.form.deployDeployment = 'reporting'
      expect(vm.form.name).toBe('reporting')
      expect(vm.form.imageName).toBe('reporting')
    })

    it('clears validation errors on synced fields when filled via auto-sync', async () => {
      const wrapper = await mountAt(PipelineFormView, '/pipelines/new')
      const vm = wrapper.vm as any

      // Trigger validation on empty form
      await vm.handleSave()
      expect(vm.formErrors.name).toBeTruthy()
      expect(vm.formErrors.imageName).toBeTruthy()
      expect(vm.formErrors.deployDeployment).toBeTruthy()

      // Fill name -> auto-syncs imageName and deployDeployment and clears their errors
      vm.form.name = 'valid-svc'
      await nextTick()

      expect(vm.formErrors.name).toBeUndefined()
      expect(vm.formErrors.imageName).toBeUndefined()
      expect(vm.formErrors.deployDeployment).toBeUndefined()
    })

    it('does not auto-sync on load in edit mode or duplicate mode', async () => {
      // Edit mode
      const editWrapper = await mountAt(PipelineFormView, '/pipelines/1/edit')
      const editVm = editWrapper.vm as any
      const origImage = editVm.form.imageName
      const origDeploy = editVm.form.deployDeployment
      expect(origImage).toBeTruthy()
      expect(origDeploy).toBeTruthy()

      editVm.form.name = 'renamed-pipeline'
      expect(editVm.form.imageName).toBe(origImage)
      expect(editVm.form.deployDeployment).toBe(origDeploy)
      editWrapper.unmount()

      // Duplicate mode
      const dupWrapper = await mountAt(PipelineFormView, '/pipelines/new?duplicateFrom=1')
      expect(dupWrapper.find('[data-testid="duplicate-banner"]').exists()).toBe(true)
      const dupVm = dupWrapper.vm as any
      const dupOrigImage = dupVm.form.imageName
      const dupOrigDeploy = dupVm.form.deployDeployment

      dupVm.form.name = 'duplicated-custom-name'
      expect(dupVm.form.imageName).toBe(dupOrigImage)
      expect(dupVm.form.deployDeployment).toBe(dupOrigDeploy)
    })
  })

  describe('JobFormView', () => {
    it('auto-fills imageName and containerName when typing into name in create mode', async () => {
      const wrapper = await mountAt(JobFormView, '/jobs/new')
      const vm = wrapper.vm as any

      expect(vm.isHydrating).toBe(false)

      const nameInput = wrapper.find('input[placeholder="VD: avina-dbmigrator"]')
      expect(nameInput.exists()).toBe(true)
      await nameInput.setValue('data-sync-job')
      await nextTick()

      expect(vm.form.name).toBe('data-sync-job')
      expect(vm.form.imageName).toBe('data-sync-job')
      expect(vm.form.containerName).toBe('data-sync-job')
      expect(vm.previewDirty).toBe(true)
    })

    it('auto-fills the other 2 fields when starting with imageName or containerName', async () => {
      // Start with imageName (Step 2)
      const w1 = await mountAt(JobFormView, '/jobs/new?step=2')
      const vm1 = w1.vm as any
      const imageInput = w1.find('input[placeholder="avina-dbmigrator"]')
      expect(imageInput.exists()).toBe(true)
      await imageInput.setValue('report-builder')

      expect(vm1.form.name).toBe('report-builder')
      expect(vm1.form.imageName).toBe('report-builder')
      expect(vm1.form.containerName).toBe('report-builder')

      // Start with containerName (Step 3)
      const w2 = await mountAt(JobFormView, '/jobs/new?step=3')
      const vm2 = w2.vm as any
      const containerInput = w2.find('input[placeholder="VD: dbmigrator"]')
      expect(containerInput.exists()).toBe(true)
      await containerInput.setValue('cleanup-runner')

      expect(vm2.form.name).toBe('cleanup-runner')
      expect(vm2.form.imageName).toBe('cleanup-runner')
      expect(vm2.form.containerName).toBe('cleanup-runner')
    })

    it('unlinks when editing a second field and re-arms when all 3 are cleared', async () => {
      const wrapper = await mountAt(JobFormView, '/jobs/new')
      const vm = wrapper.vm as any

      vm.form.name = 'etl-job'
      expect(vm.form.imageName).toBe('etl-job')
      expect(vm.form.containerName).toBe('etl-job')

      // Diverge on containerName
      vm.form.containerName = 'etl-container'
      expect(vm.form.name).toBe('etl-job')
      expect(vm.form.imageName).toBe('etl-job')

      // Editing name no longer overwrites the others
      vm.form.name = 'etl-job-v2'
      expect(vm.form.imageName).toBe('etl-job')
      expect(vm.form.containerName).toBe('etl-container')

      // Clear all 3 to re-arm
      vm.form.name = ''
      vm.form.imageName = ''
      vm.form.containerName = ''

      // Typing into imageName now syncs all 3 again
      vm.form.imageName = 'indexer-job'
      expect(vm.form.name).toBe('indexer-job')
      expect(vm.form.containerName).toBe('indexer-job')
    })

    it('clears validation errors on synced fields when filled via auto-sync', async () => {
      const wrapper = await mountAt(JobFormView, '/jobs/new')
      const vm = wrapper.vm as any

      await vm.handleSave()
      expect(vm.formErrors.name).toBeTruthy()
      expect(vm.formErrors.imageName).toBeTruthy()
      expect(vm.formErrors.containerName).toBeTruthy()

      vm.form.name = 'valid-job'
      await nextTick()

      expect(vm.formErrors.name).toBeUndefined()
      expect(vm.formErrors.imageName).toBeUndefined()
      expect(vm.formErrors.containerName).toBeUndefined()
    })

    it('does not auto-sync on load in edit mode or duplicate mode', async () => {
      // Edit mode
      const editWrapper = await mountAt(JobFormView, '/jobs/1')
      const editVm = editWrapper.vm as any
      const origImage = editVm.form.imageName
      const origContainer = editVm.form.containerName
      expect(origImage).toBeTruthy()
      expect(origContainer).toBeTruthy()

      editVm.form.name = 'renamed-job'
      expect(editVm.form.imageName).toBe(origImage)
      expect(editVm.form.containerName).toBe(origContainer)
      editWrapper.unmount()

      // Duplicate mode
      const dupWrapper = await mountAt(JobFormView, '/jobs/new?duplicateFrom=1')
      expect(dupWrapper.find('[data-testid="duplicate-banner"]').exists()).toBe(true)
      const dupVm = dupWrapper.vm as any
      const dupOrigImage = dupVm.form.imageName
      const dupOrigContainer = dupVm.form.containerName

      dupVm.form.name = 'duplicated-job-name'
      expect(dupVm.form.imageName).toBe(dupOrigImage)
      expect(dupVm.form.containerName).toBe(dupOrigContainer)
    })
  })
})
